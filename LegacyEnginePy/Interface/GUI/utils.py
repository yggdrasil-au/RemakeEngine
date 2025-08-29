
import tkinter.messagebox as messagebox
from typing import Any

# ---------- Shared Helpers ----------
def _console_write(self, text: str) -> None:
	"""Writes text to the console, parsing ANSI color codes."""
	self.console.configure(state="normal")
	# *** MODIFICATION: Use the parser to insert text with color tags ***
	for chunk, tags in self.ansi_parser.parse_text(text):
		self.console.insert("end", chunk, tags)
	self.console.insert("end", "\n") # Add newline separately
	self.console.see("end")
	self.console.configure(state="disabled")

def _make_stream_and_prompt_handlers_for(self, module_name: str):
	"""Handlers that route engine output/events to the GUI and update Installing UI for a given module."""
	import queue as _queue
	send_queue = _queue.Queue(maxsize=1)

	def on_output(line, stream) -> None:
		# *** MODIFICATION: Prepend ANSI-colored prefix for stderr ***
		if stream == "stderr":
			colored_line = f"\x1b[91m[ERR]\x1b[0m {line}"
			self.after(0, lambda: self._console_write(colored_line))
		else:
			self.after(0, lambda: self._console_write(line))

	def on_event(evt: dict) -> None:
		typ = evt.get("event")
		if typ == "warning":
			# Use ANSI for color
			self.after(0, lambda: self._console_write(f"\x1b[93m⚠ {evt.get('message','')}\x1b[0m") )
		elif typ == "error":
			# Use ANSI for color
			self.after(0, lambda: self._console_write(f"\x1b[91m✖ {evt.get('message','')}\x1b[0m") )
		elif typ == "progress":
			label = evt.get("label", module_name)
			current, total = evt.get("current", 0), max(evt.get("total", 0), 1)
			self.after(0, lambda: self._update_install_progress(module_name, label, current, total))
		elif typ == "prompt":
			question = evt.get("message", "Input required")
			secret = bool(evt.get("secret"))
			# Render inline prompt UI within Installing tab and wait for user submission
			def render():
				self._render_install_prompt(question, secret, lambda ans: send_queue.put_nowait(ans or ""))
			self.after(0, render)
		# progress/end are reflected via UI/console

	def stdin_provider() -> Any | None:
		try:
			return send_queue.get(timeout=120)
		except Exception:
			return None

	return on_output, on_event, stdin_provider

# ---------- Install Worker ----------
def _install_worker(self, module_name: str) -> None:
	registry = self.engine.get_registered_modules()
	url = (registry.get(module_name, {}) or {}).get("url")
	if not url:
		self.after(0, lambda: messagebox.showerror("Install", f"No URL found for '{module_name}'."))
		self.after(0, lambda: self._remove_install_row(module_name))
		return

	self.after(0, lambda: self._console_write(f"'{module_name}' installation started."))
	self.after(0, self.refresh_store)

	# Step 2: load operations + Run All
	on_output, on_event, stdin_provider = self._make_stream_and_prompt_handlers_for(module_name)
	try:
		# This call will run 'init' scripts first and then return the user operations.
		ops = self.engine.load_game_operations(
			module_name,
			interactive_pause=False,
			on_output=on_output,
			on_event=on_event,
			stdin_provider=stdin_provider,
		)

		# Now that init is done and we have the operations, check for 'run-all'.
		has_run_all = any(op.get("run-all") and op.get("enabled", True) for op in (ops or []))
		if has_run_all:
			self.after(0, lambda: self._console_write("Starting installation (Run All)…"))
			# This uses the operations loaded into the engine state by the previous call.
			self.engine.execute_run_all()
		else:
			# Fallback: run the first enabled op if no 'run-all' is found
			first = next((op for op in (ops or []) if op.get("enabled", True)), None)
			if not first:
				self.after(0, lambda: messagebox.showerror("Install", "No installable operations were found."))
				self.after(0, lambda: self._remove_install_row(module_name))
				return
			cmd = self.engine.build_command(first, {})
			self.after(0, lambda: self._console_write(f"Running: {first.get('Name','Operation')}"))
			self.engine.execute_command(
				cmd,
				first.get("Name", module_name),
				on_output=on_output,
				on_event=on_event,
				stdin_provider=stdin_provider,
				env_overrides={"TERM": "dumb"},
			)
	except Exception as e:
		self.after(0, lambda: messagebox.showerror("Install", f"Install error: {e}"))
		self.after(0, lambda: self._remove_install_row(module_name))
		return

	# Step 3: cleanup, refresh lists, and remove from Installing (show only in-progress)
	self.after(0, self.refresh_library)
	self.after(0, self.refresh_store)
	self.after(0, lambda: self._remove_install_row(module_name))
	self.after(0, lambda: messagebox.showinfo("Install", f"'{module_name}' installation finished."))
