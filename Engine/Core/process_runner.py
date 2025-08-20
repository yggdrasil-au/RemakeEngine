from __future__ import annotations
import json, os, locale, time, threading
from typing import List, Dict, Any, Optional, Tuple
from queue import Queue, Empty
import subprocess
from Engine.Utils.printer import print, Colours
from Engine.Core.types import OnOutput, OnEvent, StdinProvider, REMAKE_PREFIX

class ProcessRunner:
    def __init__(self, logger):
        self.logger = logger

    def execute(self, command_parts: List[str], op_title: str, *, on_output: OnOutput=None, on_event: OnEvent=None, stdin_provider: StdinProvider=None, env_overrides: Optional[Dict[str, Any]] = None) -> bool:
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
                bufsize=1
            )
            q: Queue[Tuple[str, str]] = Queue(maxsize=1000)

            def _reader(stream, name):
                try:
                    for raw in iter(stream.readline, ""):
                        q.put((name, raw.rstrip("\r\n")))
                finally:
                    try: stream.close()
                    except Exception: pass

            t_out = threading.Thread(target=_reader, args=(proc.stdout, "stdout"), daemon=True)
            t_err = threading.Thread(target=_reader, args=(proc.stderr, "stderr"), daemon=True)
            t_out.start(); t_err.start()

            awaiting = False
            last_prompt = None

            while True:
                if proc.poll() is not None:
                    try:
                        while True:
                            name, line = q.get_nowait()
                            self._handle_line(line, name, on_output, on_event, lambda: (awaiting := True), lambda m: (last_prompt := m))
                    except Empty:
                        pass
                    break
                try:
                    name, line = q.get(timeout=0.1)
                except Empty:
                    pass
                else:
                    self._handle_line(line, name, on_output, on_event, lambda: (awaiting := True), lambda m: (last_prompt := m))

                if awaiting and proc.poll() is None and proc.stdin:
                    try:
                        ans = stdin_provider() if stdin_provider else input((last_prompt or "Input required") + " ")
                    except Exception:
                        ans = None
                    if isinstance(ans, str):
                        try:
                            proc.stdin.write(ans + "\n"); proc.stdin.flush()
                        except Exception:
                            pass
                    awaiting = False

            rc = proc.wait()
            dur = time.monotonic() - start
            log_msg = f"Operation '{op_title}' completed in {dur:.2f}s with exit code {rc}."
            self.logger.info(log_msg)

            if rc == 0:
                print(colour=Colours.GREEN, message=f"\nOperation '{op_title}' completed successfully in {dur:.2f} seconds.")
                if on_event: on_event({"event": "end", "success": True, "exit_code": 0})
                return True
            else:
                if on_event: on_event({"event": "end", "success": False, "exit_code": rc})
                print(colour=Colours.RED, message=f"\nOperation '{op_title}' failed with exit code {rc} after {dur:.2f} seconds.")
                return False
        except FileNotFoundError:
            msg = f"Operation '{op_title}' failed: command or script not found."
            self.logger.error(msg)
            if on_event: on_event({"event": "error", "kind": "FileNotFoundError", "message": msg})
            print(colour=Colours.RED, message=f"\nError: {msg}")
            return False
        except PermissionError:
            msg = "Operation failed: Permission denied for command or script."
            self.logger.error(msg)
            if on_event: on_event({"event": "error", "kind": "PermissionError", "message": msg})
            print(colour=Colours.RED, message=f"\nError: {msg}")
            return False
        except Exception as e:
            dur = time.monotonic() - start
            msg = f"Operation '{op_title}' failed after {dur:.2f}s with an exception: {e}"
            self.logger.error(msg)
            if on_event: on_event({"event": "error", "kind": "Exception", "message": str(e)})
            print(colour=Colours.RED, message=f"\nError running operation '{op_title}': {e}")
            return False

    def _handle_line(self, line: str, stream_name: str, on_output: OnOutput, on_event: OnEvent, set_awaiting, set_prompt_msg):
        if line.startswith(REMAKE_PREFIX):
            payload = line[len(REMAKE_PREFIX):].strip()
            try:
                evt = json.loads(payload)
                if on_event: on_event(evt)
                if evt.get("event") == "prompt":
                    set_awaiting(); set_prompt_msg(evt.get("message", "Input required"))
            except Exception:
                if on_output: on_output(line, stream_name)
                else: print(colour=Colours.RED, message=line)
        else:
            if on_output: on_output(line, stream_name)
            else: print(colour=Colours.WHITE, message=line)
