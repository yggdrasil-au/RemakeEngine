# Engine/gui.py — Steam-style, user-facing GUI for RemakeEngine
# - Tabs: Library • Store • Installing
# - Store: shows either "Download from Git" (if not downloaded) or "Install"
# - Installing: only shows in-progress installs with progress bars; removes rows when finished
# - Install flow: load init ops then run-all (fallback to first enabled op)

import threading
import tkinter.messagebox as messagebox
import customtkinter as ctk
from pathlib import Path
# Engine UI
from Engine.Interface.GUI.terminal import AnsiColorParser
# Core
from Engine.Interface.Interface import OperationsEngine
# Builtins
import builtins as py

from Engine.Interface.GUI.utils import _console_write, _make_stream_and_prompt_handlers_for, _install_worker

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

        self._console_write = _console_write.__get__(self)
        self._make_stream_and_prompt_handlers_for = _make_stream_and_prompt_handlers_for.__get__(self)
        self._install_worker = _install_worker.__get__(self)

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
        if not Path(path).exists(): # type: ignore
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

    def _render_install_prompt(self, question: str, secret: bool, submit_cb) -> None:
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


def run() -> None:
    app = RemakeEngineGui()
    app.mainloop()

if __name__ == "__main__":
    run()
