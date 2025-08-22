# Engine/gui.py — Steam-style, user-facing GUI for RemakeEngine
# - Tabs: Library • Store • Installing
# - Store: shows either "Download from Git" (if not downloaded) or "Install"
# - Installing: only shows in-progress installs with progress bars; removes rows when finished
# - Install flow: load init ops then run-all (fallback to first enabled op)

from pathlib import Path
import re # Import regex module
import threading
import builtins as py
import tkinter.messagebox as messagebox
import customtkinter as ctk

# Core
from Engine.Interface.Interface import OperationsEngine
# Utilities
from Engine.Utils.printer import print, Colours


class AnsiColorParser:
    """Parses strings with ANSI escape codes and maps them to Tkinter text tags."""
    
    def __init__(self, widget: ctk.CTkTextbox):
        self.widget = widget
        # Regex to find ANSI escape codes
        self.ansi_pattern = re.compile(r'\x1b\[([0-9;]*)m')
        
        # Standard ANSI color map (code -> (tag_name, hex_color))
        self.colors = {
            '30': ('black_fg', '#000000'), '31': ('red_fg', '#FF5555'),
            '32': ('green_fg', '#50FA7B'), '33': ('yellow_fg', '#F1FA8C'),
            '34': ('blue_fg', '#6272A4'), '35': ('magenta_fg', '#FF79C6'),
            '36': ('cyan_fg', '#8BE9FD'), '37': ('white_fg', '#BFBFBF'),
            '90': ('bright_black_fg', '#4D4D4D'), '91': ('bright_red_fg', '#FF6E67'),
            '92': ('bright_green_fg', '#5AF78E'), '93': ('bright_yellow_fg', '#F4F99D'),
            '94': ('bright_blue_fg', '#728EFA'), '95': ('bright_magenta_fg', '#FF92D0'),
            '96': ('bright_cyan_fg', '#9AEDFE'), '97': ('bright_white_fg', '#F2F2F2'),
        }
        self._configure_tags()

    def _configure_tags(self) -> None:
        """Configures all necessary color and style tags in the target widget."""
        for code, (tag, color) in self.colors.items():
            self.widget.tag_config(tag, foreground=color)
        
        # Configure bold style
        bold_font = ctk.CTkFont(weight="bold")
        #self.widget.tag_config('bold', font=bold_font)

    def parse_text(self, text: str) -> list[tuple[str, list[str]]]:
        """Parses text and yields tuples of (text_chunk, list_of_tags)."""
        segments = []
        last_index = 0
        current_tags = set()

        for match in self.ansi_pattern.finditer(text):
            # Add text before the match with current styling
            start, end = match.span()
            if start > last_index:
                segments.append((text[last_index:start], list(current_tags)))
            
            last_index = end
            
            # Process the ANSI code
            codes = match.group(1).split(';')
            for code in codes:
                if not code:  # Empty code (e.g., from ";") is treated as reset
                    code = '0'
                if code == '0':  # Reset
                    current_tags.clear()
                elif code == '1':  # Bold
                    current_tags.add('bold')
                elif code in self.colors:  # Apply color
                    # Remove other foreground colors before adding a new one
                    current_tags = {tag for tag in current_tags if not tag.endswith('_fg')}
                    current_tags.add(self.colors[code][0])
        
        # Add any remaining text after the last match
        if last_index < len(text):
            segments.append((text[last_index:], list(current_tags)))
            
        return segments


class RemakeEngineGui(ctk.CTk):
    def __init__(self) -> None:
        super().__init__()
        self.title("RemakeEngine – Play")
        self.geometry("1024x700")

        # Engine – single instance shared across pages
        self.engine = OperationsEngine(Path.cwd())

        # Layout: top nav + main content + console
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=0)

        # Tabs (Library / Store / Installing)
        self.tabs = ctk.CTkTabview(self)
        self.tabs.grid(row=0, column=0, padx=12, pady=12, sticky="nsew")

        self.tab_library = self.tabs.add("Library")
        self.tab_store = self.tabs.add("Store")
        self.tab_installing = self.tabs.add("Installing")

        # Build pages
        self._build_library_tab()
        self._build_store_tab()
        self._build_installing_tab()

        # Console output (read-only)
        self.console = ctk.CTkTextbox(self, height=180)
        self.console.configure(state="disabled")
        self.console.grid(row=1, column=0, padx=12, pady=(0, 12), sticky="nsew")
        
        # *** MODIFICATION: Initialize the ANSI parser for the console ***
        self.ansi_parser = AnsiColorParser(self.console)

        # Active installs (module_name -> widgets)
        self.install_rows: dict[str, dict] = {}

        # Initial data
        self.refresh_library()
        self.refresh_store()

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

        def on_output(line, stream):
            # *** MODIFICATION: Prepend ANSI-colored prefix for stderr ***
            if stream == "stderr":
                colored_line = f"\x1b[91m[ERR]\x1b[0m {line}"
                self.after(0, lambda: self._console_write(colored_line))
            else:
                self.after(0, lambda: self._console_write(line))

        def on_event(evt: dict):
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

        def stdin_provider():
            try:
                return send_queue.get(timeout=120)
            except Exception:
                return None

        return on_output, on_event, stdin_provider

    # ---------- Library Tab ----------
    def _build_library_tab(self) -> None:
        self.tab_library.grid_columnconfigure(0, weight=1)
        self.tab_library.grid_rowconfigure(1, weight=1)

        header = ctk.CTkFrame(self.tab_library)
        header.grid(row=0, column=0, padx=8, pady=(8, 4), sticky="ew")
        header.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(header, text="Installed Games", font=("", 18, "bold")).grid(row=0, column=0, sticky="w")
        ctk.CTkButton(header, text="Refresh", command=self.refresh_library).grid(row=0, column=1, padx=6)

        self.lib_list = ctk.CTkScrollableFrame(self.tab_library)
        self.lib_list.grid(row=1, column=0, padx=8, pady=(0, 8), sticky="nsew")

    def refresh_library(self) -> None:
        for w in self.lib_list.winfo_children():
            w.destroy()

        games = [g for g in self.engine.get_available_games() if self.engine.is_module_installed(g)]
        if not games:
            ctk.CTkLabel(self.lib_list, text="No games installed yet. Go to the Store to get started.").pack(pady=12)
            return

        for name in games:
            row = ctk.CTkFrame(self.lib_list)
            row.pack(fill="x", padx=6, pady=6)

            ctk.CTkLabel(row, text=name, font=("", 14, "bold")).pack(side="left", padx=6)
            ctk.CTkButton(row, text="Play", command=lambda n=name: self._play_game(n)).pack(side="right", padx=4)
            ctk.CTkButton(row, text="Open Folder", fg_color="gray", command=lambda n=name: self._open_game_folder(n)).pack(side="right", padx=4)

    def _play_game(self, game_name: str) -> None:
        try:
            launcher = getattr(self.engine, "launch_game", None)
            if callable(launcher):
                threading.Thread(target=lambda: launcher(game_name), daemon=True).start()
                return
        except Exception:
            pass
        self._open_game_folder(game_name)

    def _open_game_folder(self, game_name: str) -> None:
        path = None
        try:
            getter = getattr(self.engine, "get_game_path", None)
            if callable(getter):
                path = getter(game_name)
        except Exception:
            path = None
        if not path:
            path = Path.cwd() / game_name
        if not Path(path).exists():
            messagebox.showinfo("Open Folder", f"Couldn't locate a folder for '{game_name}'.")
            return
        try:
            import os, sys
            if sys.platform.startswith("win"): os.startfile(path)  # type: ignore[attr-defined]
            elif sys.platform == "darwin": os.system(f"open '{path}'")
            else: os.system(f"xdg-open '{path}'")
        except Exception as e:
            messagebox.showerror("Open Folder", str(e))

    # ---------- Store Tab ----------
    def _build_store_tab(self) -> None:
        self.tab_store.grid_columnconfigure(0, weight=1)
        self.tab_store.grid_rowconfigure(1, weight=1)

        header = ctk.CTkFrame(self.tab_store)
        header.grid(row=0, column=0, padx=8, pady=(8, 4), sticky="ew")
        header.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(header, text="Store", font=("", 18, "bold")).grid(row=0, column=0, sticky="w")
        ctk.CTkButton(header, text="Refresh", command=self.refresh_store).grid(row=0, column=1, padx=6)

        self.store_list = ctk.CTkScrollableFrame(self.tab_store)
        self.store_list.grid(row=1, column=0, padx=8, pady=(0, 8), sticky="nsew")

    def refresh_store(self) -> None:
        for w in self.store_list.winfo_children():
            w.destroy()

        registry = self.engine.get_registered_modules()  # {name: {url, ...}}
        if not registry:
            ctk.CTkLabel(self.store_list, text="No registry entries found.").pack(pady=12)
            return

        for name, meta in registry.items():
            url = meta.get("url", "")

            row = ctk.CTkFrame(self.store_list)
            row.pack(fill="x", padx=6, pady=6)

            # Left: name + (optional) URL
            left = ctk.CTkFrame(row, fg_color="transparent")
            left.pack(side="left", fill="x", expand=True)
            ctk.CTkLabel(left, text=name, font=("", 14, "bold")).pack(side="left", padx=6)
            if url:
                ctk.CTkLabel(left, text=url, text_color="gray70").pack(side="left")

            # Right: state-aware action
            state = self.engine.get_module_state(name)  # 'not_downloaded' | 'downloaded' | 'installed'

            if state == "not_downloaded":
                ctk.CTkButton(
                    row, text="Download from Git", fg_color="#22577A",
                    command=lambda u=url: self._download_module(u)
                ).pack(side="right", padx=4)
            elif state == "downloaded":
                ctk.CTkButton(
                    row, text="Install",
                    command=lambda n=name: self._start_install(n)
                ).pack(side="right", padx=4)
            else:  # installed
                ctk.CTkButton(row, text="Installed", state="disabled", fg_color="gray40").pack(side="right", padx=4)

    def _download_module(self, url: str) -> None:
        if not url:
            messagebox.showerror("Download", "No Git URL provided for this module.")
            return
        if not self.engine.is_git_installed():
            messagebox.showerror("Git required", "Git is not installed or not in PATH.")
            return
        self._console_write(f"Downloading module from {url}…")
        def worker():
            try:
                ok = self.engine.download_module(url)
                if ok:
                    self.after(0, self.refresh_library)
                    self.after(0, self.refresh_store)
                else:
                    self.after(0, lambda: messagebox.showerror("Download", "Download failed. See console for details."))
            except Exception as e:
                self.after(0, lambda: messagebox.showerror("Download", f"Error: {e}"))
        threading.Thread(target=worker, daemon=True).start()

    def _start_install(self, module_name: str) -> None:
        # Switch to Installing and create row
        self.tabs.set("Installing")
        self._ensure_install_row(module_name)
        # Clear any lingering prompt UI
        for w in self.install_prompt_frame.winfo_children():
            w.destroy()
        self.install_prompt_frame.grid_remove()
        # Launch installer
        threading.Thread(target=self._install_worker, args=(module_name,), daemon=True).start()

    # ---------- Installing Tab ----------
    def _build_installing_tab(self) -> None:
        self.tab_installing.grid_columnconfigure(0, weight=1)
        self.tab_installing.grid_rowconfigure(1, weight=1)

        title = ctk.CTkLabel(self.tab_installing, text="Installing (in progress only)", font=("", 18, "bold"))
        title.grid(row=0, column=0, padx=8, pady=(8, 4), sticky="w")

        self.install_list = ctk.CTkScrollableFrame(self.tab_installing)
        self.install_list.grid(row=1, column=0, padx=8, pady=(0, 8), sticky="nsew")

        # Inline prompt area (shown when a child process requests input)
        self.install_prompt_frame = ctk.CTkFrame(self.tab_installing, fg_color="transparent")
        self.install_prompt_frame.grid(row=2, column=0, padx=0, pady=(0, 8), sticky="ew")
        self.install_prompt_frame.grid_remove()

    def _ensure_install_row(self, module_name: str) -> None:
        if module_name in self.install_rows:
            return
        row = ctk.CTkFrame(self.install_list)
        row.pack(fill="x", padx=6, pady=6)
        name = ctk.CTkLabel(row, text=f"{module_name} — Generating...")
        bar = ctk.CTkProgressBar(row)
        bar.set(0.0)
        name.pack(side="left", padx=6)
        bar.pack(side="right", fill="x", expand=True, padx=6)
        self.install_rows[module_name] = {"frame": row, "label": name, "bar": bar}

    def _update_install_progress(self, module_name: str, label: str, current: int, total: int) -> None:
        self._ensure_install_row(module_name)
        w = self.install_rows[module_name]
        w["label"].configure(text=f"{module_name} — {label} ({current}/{total})")
        try:
            pct = float(current) / float(total) if total else 0.0
        except Exception:
            pct = 0.0
        w["bar"].set(pct)

    def _remove_install_row(self, module_name: str) -> None:
        w = self.install_rows.pop(module_name, None)
        if w:
            try:
                w["frame"].destroy()
            except Exception:
                pass

    def _render_install_prompt(self, question: str, secret: bool, submit_cb):
        """Show a single active prompt inline on the Installing page."""
        self.install_prompt_frame.grid()
        for w in self.install_prompt_frame.winfo_children():
            w.destroy()
        row = ctk.CTkFrame(self.install_prompt_frame)
        row.pack(fill="x", padx=8, pady=8)
        ctk.CTkLabel(row, text=question).pack(anchor="w", padx=6, pady=(6, 3))
        var = ctk.StringVar()
        entry = ctk.CTkEntry(row, textvariable=var, show="*" if secret else None)
        entry.pack(fill="x", padx=6, pady=6)
        def _submit():
            ans = var.get()
            submit_cb(ans)
            entry.configure(state="disabled")
            btn.configure(state="disabled", text="Submitted")
        btn = ctk.CTkButton(row, text="Submit", command=_submit)
        btn.pack(anchor="e", padx=6, pady=(0,6))

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



def run() -> None:
    app = RemakeEngineGui()
    app.mainloop()


if __name__ == "__main__":
    run()