"""
Engine/gui.py
Interactive Graphical User Interface for the RemakeEngine
"""

import customtkinter as ctk
from pathlib import Path
import threading
import tkinter.messagebox as messagebox

# Core
from Engine.Core.operations_engine import OperationsEngine
# Utilities
from Engine.Utils.printer import print, Colours, error, print_verbose, print_debug, printc


# --- Dialog window for handling interactive prompts ---
class PromptDialog(ctk.CTkToplevel):
    def __init__(self, master, title, prompts) -> None:
        super().__init__(master)
        self.title(title)
        self.prompts = prompts
        self.result = None

        self.prompt_vars = {}
        self.prompt_widgets = {}

        # --- Create all widgets first, but DON'T pack them yet ---
        for prompt in self.prompts:
            prompt_name = prompt["Name"]
            frame = ctk.CTkFrame(self)

            if prompt["type"] == "confirm":
                var = ctk.BooleanVar(value=prompt.get("default", False))
                self.prompt_vars[prompt_name] = var

                label = ctk.CTkLabel(frame, text=prompt["message"])
                label.pack(padx=10, pady=(10, 5), anchor="w")

                button_sub_frame = ctk.CTkFrame(frame, fg_color="transparent")
                button_sub_frame.pack(padx=10, pady=(0, 10), anchor="w")

                yes_button = ctk.CTkRadioButton(button_sub_frame, text="Yes", variable=var, value=True)
                no_button = ctk.CTkRadioButton(button_sub_frame, text="No", variable=var, value=False)

                yes_button.pack(side="left", padx=(10, 0))
                no_button.pack(side="left", padx=10)

            elif prompt["type"] == "text":
                var = ctk.StringVar(value=prompt.get("default", ""))
                self.prompt_vars[prompt_name] = var
                label = ctk.CTkLabel(frame, text=prompt["message"])
                label.pack(padx=10, pady=(10, 0))
                entry = ctk.CTkEntry(frame, textvariable=var)
                entry.pack(padx=10, pady=(0, 10), fill="x")

            elif prompt["type"] == "checkbox":
                label = ctk.CTkLabel(frame, text=prompt["message"])
                label.pack(padx=10, pady=(10, 5), anchor="w")

                self.prompt_vars[prompt_name] = {}
                defaults = prompt.get("default", [])
                for choice in prompt.get("choices", []):
                    var = ctk.BooleanVar(value=(choice in defaults))
                    self.prompt_vars[prompt_name][choice] = var
                    cb = ctk.CTkCheckBox(frame, text=choice, variable=var)
                    cb.pack(padx=20, pady=2, anchor="w")

            self.prompt_widgets[prompt_name] = frame

        # --- OK and Cancel buttons ---
        self.button_frame = ctk.CTkFrame(self)
        ok_button = ctk.CTkButton(self.button_frame, text="OK", command=self._on_ok)
        ok_button.pack(side="right", padx=(10, 0))
        cancel_button = ctk.CTkButton(self.button_frame, text="Cancel", command=self._on_cancel, fg_color="gray")
        cancel_button.pack(side="right")

        # --- Handle conditional visibility ---
        for prompt in self.prompts:
            if "condition" in prompt:
                condition_name = prompt["condition"]
                if condition_name in self.prompt_vars:
                    control_var = self.prompt_vars[condition_name]
                    control_var.trace_add("write", self._update_visibility)

        self._update_visibility()  # Set initial layout

        self.grab_set()
        self.wait_window()

    def _update_visibility(self, *args) -> None:
        """Clears and redraws all widgets in the correct order based on visibility."""
        for frame in self.prompt_widgets.values():
            frame.pack_forget()
        self.button_frame.pack_forget()

        for prompt in self.prompts:
            is_visible = True
            if "condition" in prompt:
                condition_name = prompt["condition"]
                control_var = self.prompt_vars.get(condition_name)
                if not control_var or not control_var.get():
                    is_visible = False

            if is_visible:
                widget_frame = self.prompt_widgets.get(prompt["Name"])
                if widget_frame:
                    widget_frame.pack(padx=10, pady=5, fill="x")

        self.button_frame.pack(padx=10, pady=10, fill="x")

    def _on_ok(self) -> None:
        """Collect answers from variables and close the dialog."""
        self.result = {}
        for name, var in self.prompt_vars.items():
            if isinstance(var, dict):
                self.result[name] = [choice for choice, choice_var in var.items() if choice_var.get()]
            else:
                self.result[name] = var.get()
        self.destroy()

    def _on_cancel(self) -> None:
        """Set result to None and close the dialog."""
        self.result = None
        self.destroy()

    def get_answers(self):
        """Public method to retrieve the collected answers."""
        return self.result


class DownloadDialog(ctk.CTkToplevel):
    def __init__(self, master, engine: OperationsEngine):
        super().__init__(master)
        self.engine = engine
        self.master_app = master  # Reference to the main App

        self.title("Download Game Module")
        self.geometry("450x250")

        self.grid_columnconfigure(0, weight=1)

        # --- Widgets ---
        self.label = ctk.CTkLabel(self, text="Select a registered module or choose 'Other' to enter a custom URL.")
        self.label.grid(row=0, column=0, padx=20, pady=(20, 10), sticky="w")

        modules = ["Other (Custom URL)..."] + list(self.engine.get_registered_modules().keys())
        self.module_selector = ctk.CTkComboBox(self, values=modules, command=self.on_selection_change)
        self.module_selector.set(modules[0])
        self.module_selector.grid(row=1, column=0, padx=20, pady=5, sticky="ew")

        self.url_entry = ctk.CTkEntry(self, placeholder_text="Enter Git Repository URL...")
        # Initially hidden, will be shown if "Other" is selected
        self.url_entry.grid(row=2, column=0, padx=20, pady=5, sticky="ew")

        self.download_button = ctk.CTkButton(self, text="Download", command=self.on_download)
        self.download_button.grid(row=3, column=0, padx=20, pady=(20, 10))

        self.on_selection_change(self.module_selector.get())  # Set initial state

        self.grab_set()  # Modal
        self.wait_window()

    def on_selection_change(self, choice: str):
        """Shows or hides the URL entry box based on selection."""
        if choice == "Other (Custom URL)...":
            self.url_entry.grid()
        else:
            self.url_entry.grid_remove()

    def on_download(self):
        """Handles the download button click."""
        selection = self.module_selector.get()
        url_to_download = ""

        if selection == "Other (Custom URL)...":
            url_to_download = self.url_entry.get().strip()
            if not url_to_download.endswith(".git"):
                messagebox.showerror("Invalid URL", "Please enter a valid Git repository URL (e.g., https://.../repo.git)")
                return
        else:
            modules = self.engine.get_registered_modules()
            url_to_download = modules.get(selection, {}).get("url")

        if not url_to_download:
            messagebox.showerror("Error", "Could not determine a valid URL to download.")
            return

        # Disable widgets and run download in a thread
        self.download_button.configure(text="Downloading...", state="disabled")
        self.module_selector.configure(state="disabled")
        self.url_entry.configure(state="disabled")

        thread = threading.Thread(target=self._download_thread, args=(url_to_download,))
        thread.start()

    def _download_thread(self, url: str):
        """Worker thread to run the download without freezing the GUI."""
        success = self.engine.download_module(url)

        # Schedule GUI updates on the main thread
        if success:
            self.master_app.after(0, self.master_app.refresh_game_list)
            self.after(0, self.destroy)  # Close dialog on success
        else:
            # Re-enable widgets on failure so the user can try again
            self.after(0, lambda: self.download_button.configure(text="Download", state="normal"))
            self.after(0, lambda: self.module_selector.configure(state="normal"))
            self.after(0, lambda: self.url_entry.configure(state="normal"))
            messagebox.showerror("Download Failed", "The module could not be downloaded. Check the console for details.")


# --- Main Application Class ---
class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("Operations Manager")
        self.geometry("760x560")  # Slightly larger for comfort

        self.engine = OperationsEngine(Path.cwd())

        # --- Layout ---
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)

        # --- Widgets ---
        top_frame = ctk.CTkFrame(self)
        top_frame.grid(row=0, column=0, padx=10, pady=10, sticky="ew")

        available_games = self.engine.get_available_games()

        self.game_selector = ctk.CTkComboBox(
            top_frame,
            values=available_games,
            command=self.on_game_selected
        )
        self.game_selector.pack(side="left", padx=5, pady=5, expand=True, fill="x")

        self.download_button = ctk.CTkButton(top_frame, text="Download Module", command=self.open_download_dialog)
        self.download_button.pack(side="left", padx=(6, 0), pady=5)

        if not available_games:
            self.game_selector.set("No games found. Download a module to begin.")
            self.game_selector.configure(state="disabled")
        else:
            self.game_selector.set("Select a Game...")

        self.op_list_frame = ctk.CTkScrollableFrame(self, label_text="Operations")
        self.op_list_frame.grid(row=1, column=0, padx=10, pady=(5, 10), sticky="nsew")

        # --- Console + Progress ---
        import queue as _queue
        self._queue = _queue

        self.console = ctk.CTkTextbox(self, height=200)
        self.console.configure(state="disabled")
        self.console.grid(row=2, column=0, padx=10, pady=(0, 8), sticky="nsew")
        self.grid_rowconfigure(2, weight=0)

        # ANSI → Tk tag color palette (tweak to taste)
        self._ansi_tag_styles = {
            "default": {"foreground": None},  # uses CTk default
            "white":   {"foreground": "#FFFFFF"},
            "red":     {"foreground": "#FF6B6B"},
            "green":   {"foreground": "#6BFF95"},
            "yellow":  {"foreground": "#FFE083"},
            "blue":    {"foreground": "#7FB3FF"},
            "magenta": {"foreground": "#FF89FF"},
            "cyan":    {"foreground": "#8AF5FF"},
            "gray":    {"foreground": "#A0A0A0"},
            "darkgreen": {"foreground": "#32CD32"},
            "darkcyan":  {"foreground": "#00CED1"},
            "darkyellow":{"foreground": "#DAA520"},
            "darkred":   {"foreground": "#FF4C4C"},
            # 38;5;240 → a dark gray
            "x_38_5_240": {"foreground": "#585858"},
        }

        # Create tags on the Text widget (CTkTextbox supports tk tags)
        self.console.configure(state="normal")
        for tag, style in self._ansi_tag_styles.items():
            kwargs = {}
            if style["foreground"]:
                kwargs["foreground"] = style["foreground"]
            self.console.tag_config(tag, **kwargs)
        self.console.configure(state="disabled")

        self.progress_frame = ctk.CTkScrollableFrame(self, label_text="Progress")
        self.progress_frame.grid(row=3, column=0, padx=10, pady=(0, 10), sticky="nsew")
        self.grid_rowconfigure(3, weight=1)
        self._progress_widgets = {}  # id -> (label, bar)

        self._pending_stdin_sender = None

        # Status label for init runs
        self.status_label = ctk.CTkLabel(self, text="")
        self.status_label.grid(row=4, column=0, padx=10, pady=(0, 10), sticky="w")

    def _ansi_insert(self, text: str):
        """
        Parse ANSI SGR color codes in `text` and insert with Tk tags.
        Supports common codes: 0 (reset), 90..97 (bright), 30..37 (dark),
        and a special case 38;5;240.
        """
        import re

        # Quick path: no ANSI? insert as-is with default tag
        if "\x1b[" not in text:
            self.console.insert("end", text, ("default",))
            return

        # Map SGR codes → tag names in our palette
        code_to_tag = {
            "0": "default",      # reset
            "97": "white",
            "91": "red",
            "92": "green",
            "93": "yellow",
            "94": "blue",
            "95": "magenta",
            "96": "cyan",
            "90": "gray",
            "32": "darkgreen",
            "36": "darkcyan",
            "33": "darkyellow",
            "31": "darkred",
        }

        # Regex to capture SGR chunks like \x1b[31m or \x1b[38;5;240m
        ansi_re = re.compile(r"\x1b\[([0-9;]+)m")

        pos = 0
        current_tag = "default"
        for m in ansi_re.finditer(text):
            # Insert plain text preceding this escape
            if m.start() > pos:
                self.console.insert("end", text[pos:m.start()], (current_tag,))

            seq = m.group(1)  # e.g. "31" or "38;5;240"
            if seq == "0":
                current_tag = "default"
            elif seq in code_to_tag:
                current_tag = code_to_tag[seq]
            elif seq == "38;5;240":
                current_tag = "x_38_5_240"
            else:
                # Try the last element if multiple, e.g. "1;91" → "91"
                last = seq.split(";")[-1]
                current_tag = code_to_tag.get(last, current_tag)

            pos = m.end()

        # Trailing text after last escape
        if pos < len(text):
            self.console.insert("end", text[pos:], (current_tag,))

    def _console_write_ansi(self, line: str, stream: str):
        """
        Write a line to console using ANSI → tag mapping.
        Adds [ERR] prefix for stderr but keeps colorized content intact.
        """
        self.console.configure(state="normal")
        prefix = "[ERR] " if stream == "stderr" else ""
        if prefix:
            self.console.insert("end", prefix, ("default",))
        self._ansi_insert(line + "\n")
        self.console.see("end")
        self.console.configure(state="disabled")

    # --- Helpers: console/progress/prompt/event ---
    def _ui_append_console(self, text, stream):
        self._console_write_ansi(text, stream)

    def _ui_handle_event(self, evt: dict):
        typ = evt.get("event")
        if typ == "progress":
            pid = evt.get("id", "default")
            current, total = evt.get("current", 0), max(evt.get("total", 0), 1)
            label_txt = evt.get("label", pid)
            if pid not in self._progress_widgets:
                row = ctk.CTkFrame(self.progress_frame)
                name = ctk.CTkLabel(row, text=label_txt)
                bar = ctk.CTkProgressBar(row)
                bar.set(0.0)
                name.pack(side="left", padx=6)
                bar.pack(side="right", fill="x", expand=True, padx=6)
                row.pack(fill="x", padx=6, pady=4)
                self._progress_widgets[pid] = (name, bar)
            name, bar = self._progress_widgets[pid]
            bar.set(float(current) / float(total))
            name.configure(text=f"{label_txt} ({current}/{total})")
        elif typ == "prompt":
            question = evt.get("message", "Input required")
            ans = self._prompt_user(question, bool(evt.get("secret")))
            if self._pending_stdin_sender:
                try:
                    self._pending_stdin_sender(ans)
                except Exception:
                    pass
        elif typ in ("warning", "error"):
            messagebox.showwarning("Module warning" if typ == "warning" else "Module error",
                                   evt.get("message", ""))
        elif typ == "end":
            # no modal here; we'll handle final state after thread finishes
            pass

    def _prompt_user(self, message, secret=False):
        d = ctk.CTkInputDialog(text=message, title="Input required")
        return d.get_input() or ""

    def open_download_dialog(self):
        if not self.engine.is_git_installed():
            messagebox.showerror("Git Not Found", "Git is required for this feature but it could not be found in your system's PATH.")
            return
        DownloadDialog(self, self.engine)

    def refresh_game_list(self):
        """Refreshes the game selector combobox with the latest list of games."""
        print(colour=Colours.CYAN, message="GUI: Refreshing game list...")
        available_games = self.engine.get_available_games()
        if available_games:
            self.game_selector.configure(values=available_games, state="normal")
            self.game_selector.set("Select a Game...")
        else:
            self.game_selector.set("No games found. Download a module to begin.")
            self.game_selector.configure(values=[""], state="disabled")

        # Clear operations from view
        for widget in self.op_list_frame.winfo_children():
            widget.destroy()

    # Create shared handlers & stdin queue for a single engine call
    def _make_stream_and_prompt_handlers(self):
        def on_output(line, stream):
            self.after(0, lambda: self._ui_append_console(line, stream))

        def on_event(evt):
            self.after(0, lambda: self._ui_handle_event(evt))

        send_queue = self._queue.Queue(maxsize=1)
        self._pending_stdin_sender = lambda s: send_queue.put_nowait(s)

        def stdin_provider():
            try:
                return send_queue.get(timeout=60)
            except self._queue.Empty:
                return None

        def cleanup():
            self._pending_stdin_sender = None

        return on_output, on_event, stdin_provider, cleanup

    # --- Init runner (THREADED) ---
    def on_game_selected(self, selected_game: str):
        """Kick off init (if any) in a worker thread, then populate operation buttons."""
        # Reset UI
        for widget in self.op_list_frame.winfo_children():
            widget.destroy()
        self.console.configure(state="normal")
        self.console.delete("1.0", "end")
        self.console.configure(state="disabled")
        self._progress_widgets = {}
        self.status_label.configure(text=f"Initializing '{selected_game}'...")

        on_output, on_event, stdin_provider, cleanup = self._make_stream_and_prompt_handlers()

        def worker():
            ops = []
            try:
                # Run init in background so Tk loop stays responsive
                try:
                    ops = self.engine.load_game_operations(
                        selected_game,
                        interactive_pause=False,
                        on_output=on_output,
                        on_event=on_event,
                        stdin_provider=stdin_provider
                    )
                except TypeError:
                    # Engine without extended signature (fallback)
                    ops = self.engine.load_game_operations(selected_game, interactive_pause=False)
            finally:
                cleanup()
                self.after(0, lambda: self._post_init_operations(selected_game, ops))

        threading.Thread(target=worker, daemon=True).start()

    def _post_init_operations(self, selected_game: str, operations: list):
        """Populate UI with operations after init completes."""
        self.status_label.configure(text=f"Ready • {selected_game}")

        # "Run All" button (disabled if none enabled)
        run_all_ops = [op for op in operations if op.get("run-all")]
        if run_all_ops:
            is_any_enabled = any(op.get("enabled", False) for op in run_all_ops)
            run_all_button = ctk.CTkButton(
                self.op_list_frame,
                text="Run All",
                command=self.run_all_operations,
                fg_color="#006400"
            )
            if not is_any_enabled:
                run_all_button.configure(state="disabled", fg_color="gray50", text="Run All (No operations enabled)")
            run_all_button.pack(fill="x", padx=5, pady=(5, 10))

        # Individual operations
        for op in operations:
            op_name = op.get("Name")
            if not op_name:
                continue
            is_enabled = op.get("enabled", True)
            button = ctk.CTkButton(
                self.op_list_frame,
                text=op_name,
                command=lambda op_config=op: self.run_operation(op_config)
            )
            if not is_enabled:
                button.configure(state="disabled", fg_color="gray50")
            button.pack(fill="x", padx=5, pady=2)

    # --- Run All remains threaded as before ---
    def run_all_operations(self):
        """Run all 'run-all' operations in a separate thread."""
        thread = threading.Thread(target=self.engine.execute_run_all)
        thread.start()

    def run_operation(self, op_config: dict):
        """Prepare and run a selected operation, showing a prompt dialog if needed."""
        prompts = op_config.get("prompts", [])
        prompt_answers = {}

        if not op_config.get("enabled", True):
            messagebox.showwarning(
                "Operation Disabled",
                op_config.get("warning", "This operation cannot be run.")
            )
            return

        if prompts:
            dialog = PromptDialog(self, title=op_config.get("Name"), prompts=prompts)
            prompt_answers = dialog.get_answers()
            if prompt_answers is None:
                return

        command = self.engine.build_command(op_config, prompt_answers)

        on_output, on_event, stdin_provider, cleanup = self._make_stream_and_prompt_handlers()

        def worker():
            env = {"TERM": "dumb"}
            try:
                self.engine.execute_command(
                    command, op_config.get("Name"),
                    on_output=on_output,
                    on_event=on_event,
                    stdin_provider=stdin_provider,
                    env_overrides=env
                )
            finally:
                cleanup()

        threading.Thread(target=worker, daemon=True).start()



def run() -> None:
    app = App()
    app.mainloop()

if __name__ == "__main__":
    run()




