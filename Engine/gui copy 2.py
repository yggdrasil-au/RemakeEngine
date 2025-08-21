"""
Engine/gui.py
Steam-style, user-facing GUI for RemakeEngine
- Focus: Library (installed games), Store (registry), Install (guided flow)
- CLI remains developer-focused and unchanged
"""

from pathlib import Path
import threading
import tkinter.filedialog as filedialog
import tkinter.messagebox as messagebox
import customtkinter as ctk

# Core
from Engine.Core.operations_engine import OperationsEngine
# Utilities
from Engine.Utils.printer import print, Colours


class SteamLikeApp(ctk.CTk):
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

        # Tabs (Library / Store / Install)
        self.tabs = ctk.CTkTabview(self)
        self.tabs.grid(row=0, column=0, padx=12, pady=12, sticky="nsew")

        self.tab_library = self.tabs.add("Library")
        self.tab_store = self.tabs.add("Store")
        self.tab_install = self.tabs.add("Install")

        # Build pages
        self._build_library_tab()
        self._build_store_tab()
        self._build_install_tab()

        # Console output (read-only)
        self.console = ctk.CTkTextbox(self, height=180)
        self.console.configure(state="disabled")
        self.console.grid(row=1, column=0, padx=12, pady=(0, 12), sticky="nsew")

        # Initial data
        self.refresh_library()
        self.refresh_store()

    # ---------- Shared Helpers ----------
    def _console_write(self, text: str) -> None:
        self.console.configure(state="normal")
        self.console.insert("end", text + "\n")
        self.console.see("end")
        self.console.configure(state="disabled")

    def _make_stream_and_prompt_handlers(self):
        """Create handlers that route engine output/events to the GUI.
        - on_output: stream stdout/stderr to console
        - on_event: show warnings and handle interactive prompts
        - stdin_provider: queue-based bridge to feed answers back
        """
        import queue as _queue
        send_queue = _queue.Queue(maxsize=1)

        def on_output(line, stream):
            prefix = "[ERR] " if stream == "stderr" else ""
            self.after(0, lambda: self._console_write(prefix + line))

        def on_event(evt: dict):
            typ = evt.get("event")
            if typ == "warning":
                self.after(0, lambda: messagebox.showwarning("Module warning", evt.get("message", "")))
            elif typ == "error":
                self.after(0, lambda: messagebox.showerror("Module error", evt.get("message", "")))
            elif typ == "prompt":
                # Ask user for input (e.g., original game path) – return empty if cancelled
                q = evt.get("message", "Input required")
                def ask_and_send():
                    ans = ctk.CTkInputDialog(text=q, title="Input required").get_input() or ""
                    try:
                        send_queue.put_nowait(ans)
                    except Exception:
                        pass
                self.after(0, ask_and_send)
            # progress/end events are optional here – console logs suffice

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

        # Header row
        header = ctk.CTkFrame(self.tab_library)
        header.grid(row=0, column=0, padx=8, pady=(8, 4), sticky="ew")
        header.grid_columnconfigure(0, weight=1)
        ctk.CTkLabel(header, text="Installed Games", font=("", 18, "bold")).grid(row=0, column=0, sticky="w")
        ctk.CTkButton(header, text="Refresh", command=self.refresh_library).grid(row=0, column=1, padx=6)

        # Scrollable grid of game cards
        self.lib_list = ctk.CTkScrollableFrame(self.tab_library)
        self.lib_list.grid(row=1, column=0, padx=8, pady=(0, 8), sticky="nsew")

    def refresh_library(self) -> None:
        for w in self.lib_list.winfo_children():
            w.destroy()

        games = self.engine.get_available_games()
        if not games:
            ctk.CTkLabel(self.lib_list, text="No games installed yet. Go to the Store or Install tab to get started.").pack(pady=12)
            return

        for name in games:
            row = ctk.CTkFrame(self.lib_list)
            row.pack(fill="x", padx=6, pady=6)

            ctk.CTkLabel(row, text=name, font=("", 14, "bold")).pack(side="left", padx=6)
            ctk.CTkButton(row, text="Play", command=lambda n=name: self._play_game(n)).pack(side="right", padx=4)
            ctk.CTkButton(row, text="Open Folder", fg_color="gray", command=lambda n=name: self._open_game_folder(n)).pack(side="right", padx=4)

    def _play_game(self, game_name: str) -> None:
        # If the engine exposes a launcher, try it; otherwise open folder as fallback
        try:
            launcher = getattr(self.engine, "launch_game", None)
            if callable(launcher):
                threading.Thread(target=lambda: launcher(game_name), daemon=True).start()
                return
        except Exception:
            pass
        self._open_game_folder(game_name)

    def _open_game_folder(self, game_name: str) -> None:
        # Heuristic: many modules create a folder under ./Games/<name> or similar.
        # Fall back to cwd if unknown.
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
            ctk.CTkLabel(row, text=name, font=("", 14, "bold")).pack(side="left", padx=6)
            ctk.CTkLabel(row, text=url or "", text_color="gray70").pack(side="left")
            ctk.CTkButton(row, text="Install", command=lambda n=name: self._goto_install_with(n)).pack(side="right", padx=4)

    def _goto_install_with(self, module_name: str) -> None:
        # Pre-select module in Install tab and switch to it
        try:
            self.install_module_selector.set(module_name)
        except Exception:
            pass
        self.tabs.set("Install")

    # ---------- Install Tab ----------
    def _build_install_tab(self) -> None:
        self.tab_install.grid_columnconfigure(0, weight=1)
        self.tab_install.grid_rowconfigure(3, weight=1)

        title = ctk.CTkLabel(self.tab_install, text="Install a Game", font=("", 18, "bold"))
        title.grid(row=0, column=0, padx=8, pady=(8, 4), sticky="w")

        # Module selection
        row1 = ctk.CTkFrame(self.tab_install)
        row1.grid(row=1, column=0, padx=8, pady=6, sticky="ew")
        row1.grid_columnconfigure(1, weight=1)
        ctk.CTkLabel(row1, text="Game (from Store/Registry)").grid(row=0, column=0, padx=6, pady=6, sticky="w")
        self.install_module_selector = ctk.CTkComboBox(row1, values=list(self.engine.get_registered_modules().keys()))
        if self.install_module_selector.cget("values"):
            self.install_module_selector.set(self.install_module_selector.cget("values")[0])
        self.install_module_selector.grid(row=0, column=1, padx=6, pady=6, sticky="ew")

        # Original files path
        row2 = ctk.CTkFrame(self.tab_install)
        row2.grid(row=2, column=0, padx=8, pady=6, sticky="ew")
        row2.grid_columnconfigure(1, weight=1)
        ctk.CTkLabel(row2, text="Original game files path").grid(row=0, column=0, padx=6, pady=6, sticky="w")
        self.path_entry = ctk.CTkEntry(row2, placeholder_text="Choose the folder containing the original game files…")
        self.path_entry.grid(row=0, column=1, padx=6, pady=6, sticky="ew")
        ctk.CTkButton(row2, text="Browse", command=self._browse_original_path).grid(row=0, column=2, padx=6)

        # Action buttons
        row3 = ctk.CTkFrame(self.tab_install)
        row3.grid(row=3, column=0, padx=8, pady=6, sticky="ew")
        ctk.CTkButton(row3, text="Install", command=self._begin_install).pack(side="left", padx=6)
        ctk.CTkLabel(row3, text="You may be prompted for additional details by the module.", text_color="gray70").pack(side="left", padx=6)

    def _browse_original_path(self) -> None:
        directory = filedialog.askdirectory(title="Select original game directory")
        if directory:
            self.path_entry.delete(0, "end")
            self.path_entry.insert(0, directory)

    def _begin_install(self) -> None:
        module_name = self.install_module_selector.get().strip()
        if not module_name:
            messagebox.showerror("Install", "Please select a game from the Store/Registry.")
            return

        # Optional: provide the path upfront – most modules will ask via prompt anyway
        user_path = self.path_entry.get().strip()
        if not user_path:
            if not messagebox.askyesno("No path provided", "Proceed without setting the original files path now? The installer may prompt you later."):
                return

        # Launch install in background
        threading.Thread(target=self._install_worker, args=(module_name,), daemon=True).start()

    def _install_worker(self, module_name: str) -> None:
        registry = self.engine.get_registered_modules()
        url = (registry.get(module_name, {}) or {}).get("url")
        if not url:
            self.after(0, lambda: messagebox.showerror("Install", f"No URL found for '{module_name}'."))
            return

        # Step 1: download module if needed
        if not self.engine.is_git_installed():
            self.after(0, lambda: messagebox.showerror("Git required", "Git is not installed or not in PATH."))
            return

        self.after(0, lambda: self._console_write(f"Downloading module for '{module_name}'…"))
        try:
            ok = self.engine.download_module(url)
            if not ok:
                self.after(0, lambda: messagebox.showerror("Install", "Download failed. See console for details."))
                return
        except Exception as e:
            self.after(0, lambda: messagebox.showerror("Install", f"Download error: {e}"))
            return

        # Step 2: load operations + Run All (Steam-like one-click install)
        on_output, on_event, stdin_provider = self._make_stream_and_prompt_handlers()
        try:
            ops = self.engine.load_game_operations(
                module_name,
                interactive_pause=False,
                on_output=on_output,
                on_event=on_event,
                stdin_provider=stdin_provider,
            )
            has_run_all = any(op.get("run-all") and op.get("enabled", False) for op in (ops or []))
            if has_run_all:
                self.after(0, lambda: self._console_write("Starting installation (Run All)…"))
                self.engine.execute_run_all()
            else:
                # Fallback: run the first enabled op
                first = next((op for op in (ops or []) if op.get("enabled", True)), None)
                if not first:
                    self.after(0, lambda: messagebox.showerror("Install", "No installable operations were found."))
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
        except TypeError:
            # Engine without extended signature
            ops = self.engine.load_game_operations(module_name, interactive_pause=False)
            if ops:
                self.engine.execute_run_all()
        except Exception as e:
            self.after(0, lambda: messagebox.showerror("Install", f"Install error: {e}"))
            return

        # Step 3: refresh library after install
        self.after(0, self.refresh_library)
        self.after(0, lambda: messagebox.showinfo("Install", f"'{module_name}' installation finished."))


# Entrypoint

def run() -> None:
    app = SteamLikeApp()
    app.mainloop()


if __name__ == "__main__":
    run()
