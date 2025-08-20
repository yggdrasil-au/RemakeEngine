from __future__ import annotations
import json, os, locale, time, threading, signal
from typing import List, Dict, Any, Optional, Tuple
from queue import Queue, Empty
import subprocess

from Engine.Utils.printer import print, Colours
from Engine.Core.types import OnOutput, OnEvent, StdinProvider, REMAKE_PREFIX


class ProcessRunner:
    def __init__(self, logger):
        self.logger = logger

    def execute(
        self,
        command_parts: List[str],
        op_title: str,
        *,
        on_output: OnOutput = None,
        on_event: OnEvent = None,
        stdin_provider: StdinProvider = None,
        env_overrides: Optional[Dict[str, Any]] = None,
    ) -> bool:
        """
        Run a command, stream its output, handle @@REMAKE@@ events, and support interactive prompts.

        Ctrl+C behavior:
          • If pressed while answering a prompt → sends a blank line to the child (prompt returns "").
          • If pressed outside a prompt → attempts graceful terminate, then kill as last resort.
        """
        if not command_parts or len(command_parts) < 2:
            msg = f"Operation '{op_title}' has no script to execute. Skipping."
            print(colour=Colours.YELLOW, message=msg)
            self.logger.warning(msg)
            return False

        print(colour=Colours.BLUE, message="\nExecuting command:")
        print(colour=Colours.BLUE, message=f"  {' '.join(map(str, command_parts))}")
        start = time.monotonic()

        env = os.environ.copy()
        if env_overrides:
            env.update({k: str(v) for k, v in env_overrides.items()})
        # Make child stdout/err line-buffered & UTF-8 so our reader threads get lines promptly.
        env.setdefault("PYTHONUNBUFFERED", "1")
        env.setdefault("PYTHONIOENCODING", "utf-8")

        try:
            proc = subprocess.Popen(
                command_parts,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                stdin=subprocess.PIPE,
                text=True,
                encoding=env.get("PYTHONIOENCODING", locale.getpreferredencoding(False)),
                env=env,
                bufsize=1,
            )

            # Threaded readers → queue of (stream_name, line)
            q: Queue[Tuple[str, str]] = Queue(maxsize=1000)

            def _reader(stream, name):
                try:
                    for raw in iter(stream.readline, ""):
                        q.put((name, raw.rstrip("\r\n")))
                finally:
                    try:
                        stream.close()
                    except Exception:
                        pass

            t_out = threading.Thread(target=_reader, args=(proc.stdout, "stdout"), daemon=True)
            t_err = threading.Thread(target=_reader, args=(proc.stderr, "stderr"), daemon=True)
            t_out.start()
            t_err.start()

            awaiting_prompt: bool = False
            last_prompt_msg: Optional[str] = None
            suppress_prompt_echo: bool = bool(os.getenv("ENGINE_SUPPRESS_PROMPT_ECHO", "").strip())

            def _handle_line(line: str, stream_name: str) -> Optional[str]:
                """
                Returns a prompt message if this line is a prompt event; otherwise None.
                Also forwards output / events to UI callbacks.
                """
                if line.startswith(REMAKE_PREFIX):
                    payload = line[len(REMAKE_PREFIX):].strip()
                    try:
                        evt = json.loads(payload)

                        # If this is a prompt and the caller wants to suppress echo (e.g., CLI shows its own input),
                        # we can skip forwarding the event to the on_event callback.
                        if evt.get("event") == "prompt":
                            if on_event and not suppress_prompt_echo:
                                on_event(evt)
                            # Always tell the runner loop that a prompt is awaiting.
                            return evt.get("message", "Input required")

                        # Non-prompt structured events always forwarded.
                        if on_event:
                            on_event(evt)

                        return None
                    except Exception:
                        # Malformed structured line → treat as plain output
                        if on_output:
                            on_output(line, stream_name)
                        else:
                            print(colour=Colours.RED, message=line)
                        return None
                else:
                    # Plain output
                    if on_output:
                        on_output(line, stream_name)
                    else:
                        print(colour=Colours.WHITE, message=line)
                    return None

            def _send_to_child(text: str | None) -> None:
                if not proc.stdin:
                    return
                try:
                    # Send newline even for None to unblock child prompt with empty answer.
                    proc.stdin.write((text if isinstance(text, str) else "") + "\n")
                    proc.stdin.flush()
                except Exception:
                    pass

            # Main pump loop
            while True:
                # If process has exited, drain any remaining queued lines then break.
                if proc.poll() is not None:
                    try:
                        while True:
                            name, line = q.get_nowait()
                            prompt_msg = _handle_line(line, name)
                            if prompt_msg is not None:
                                awaiting_prompt = True
                                last_prompt_msg = prompt_msg
                    except Empty:
                        pass
                    break

                try:
                    name, line = q.get(timeout=0.1)
                except Empty:
                    pass
                else:
                    prompt_msg = _handle_line(line, name)
                    if prompt_msg is not None:
                        awaiting_prompt = True
                        last_prompt_msg = prompt_msg

                # If a prompt is active, get input and feed it to child.
                if awaiting_prompt and proc.poll() is None:
                    try:
                        if stdin_provider:
                            ans = stdin_provider()
                        else:
                            # Fallback to basic console input if no provider is given.
                            ans = input((last_prompt_msg or "Input required") + " ")
                    except KeyboardInterrupt:
                        # Treat Ctrl+C during a prompt as "submit blank".
                        ans = ""
                    except Exception:
                        ans = ""

                    _send_to_child(ans if isinstance(ans, str) else "")
                    awaiting_prompt = False
                    last_prompt_msg = None

            # Process finished; compute result
            rc = proc.wait()
            dur = time.monotonic() - start
            log_msg = f"Operation '{op_title}' completed in {dur:.2f}s with exit code {rc}."
            self.logger.info(log_msg)

            if rc == 0:
                print(colour=Colours.GREEN, message=f"\nOperation '{op_title}' completed successfully in {dur:.2f} seconds.")
                if on_event:
                    on_event({"event": "end", "success": True, "exit_code": 0})
                return True
            else:
                if on_event:
                    on_event({"event": "end", "success": False, "exit_code": rc})
                print(colour=Colours.RED, message=f"\nOperation '{op_title}' failed with exit code {rc} after {dur:.2f} seconds.")
                return False

        except KeyboardInterrupt:
            # Ctrl+C outside of an active prompt → attempt graceful shutdown.
            try:
                if 'proc' in locals() and proc and proc.poll() is None:
                    print(colour=Colours.YELLOW, message="\nReceived Ctrl+C — terminating child process…")
                    try:
                        proc.terminate()
                    except Exception:
                        pass
                    try:
                        proc.wait(timeout=5)
                    except Exception:
                        try:
                            proc.kill()
                        except Exception:
                            pass
            finally:
                if on_event:
                    on_event({"event": "end", "success": False, "exit_code": 130})  # 130 is SIGINT convention
                print(colour=Colours.RED, message=f"\nOperation '{op_title}' cancelled by user (Ctrl+C).")
                return False

        except FileNotFoundError:
            msg = f"Operation '{op_title}' failed: command or script not found."
            self.logger.error(msg)
            if on_event:
                on_event({"event": "error", "kind": "FileNotFoundError", "message": msg})
            print(colour=Colours.RED, message=f"\nError: {msg}")
            return False

        except PermissionError:
            msg = "Operation failed: Permission denied for command or script."
            self.logger.error(msg)
            if on_event:
                on_event({"event": "error", "kind": "PermissionError", "message": msg})
            print(colour=Colours.RED, message=f"\nError: {msg}")
            return False

        except Exception as e:
            dur = time.monotonic() - start
            msg = f"Operation '{op_title}' failed after {dur:.2f}s with an exception: {e}"
            self.logger.error(msg)
            if on_event:
                on_event({"event": "error", "kind": "Exception", "message": str(e)})
            print(colour=Colours.RED, message=f"\nError running operation '{op_title}': {e}")
            return False
