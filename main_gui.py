# main_gui.py
import customtkinter as ctk
from operations_engine import OperationsEngine
from pathlib import Path
import threading
import tkinter.messagebox as messagebox

import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), 'Utils')))
from printer import print, Colours, print_error, print_verbose, print_debug, printc

# --- Dialog window for handling interactive prompts ---
class PromptDialog(ctk.CTkToplevel):
    def __init__(self, master, title, prompts):
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

        self._update_visibility() # Set initial layout

        self.grab_set()
        self.wait_window()

    def _update_visibility(self, *args):
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

    def _on_ok(self):
        """Collect answers from variables and close the dialog."""
        self.result = {}
        for name, var in self.prompt_vars.items():
            if isinstance(var, dict):
                self.result[name] = [choice for choice, choice_var in var.items() if choice_var.get()]
            else:
                self.result[name] = var.get()
        self.destroy()

    def _on_cancel(self):
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
        self.master_app = master # Reference to the main App

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

        self.on_selection_change(self.module_selector.get()) # Set initial state

        self.grab_set() # Modal
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
            self.after(0, self.destroy) # Close dialog on success
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
        self.geometry("700x500") # Increased width slightly

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

        # --- NEW: Download button ---
        self.download_button = ctk.CTkButton(top_frame, text="Download Module", command=self.open_download_dialog)
        self.download_button.pack(side="left", padx=(0, 5), pady=5)
        # --- END NEW ---

        if not available_games:
            self.game_selector.set("No games found. Download a module to begin.")
            self.game_selector.configure(state="disabled")
        else:
            self.game_selector.set("Select a Game...")

        self.op_list_frame = ctk.CTkScrollableFrame(self, label_text="Operations")
        self.op_list_frame.grid(row=1, column=0, padx=10, pady=(5, 10), sticky="nsew")

    # --- NEW: Method to open the download dialog ---
    def open_download_dialog(self):
        if not self.engine.is_git_installed():
            messagebox.showerror("Git Not Found", "Git is required for this feature but it could not be found in your system's PATH.")
            return
        DownloadDialog(self, self.engine)

    # --- NEW: Method to refresh the main game selector ---
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

    def on_game_selected(self, selected_game: str):
        """Callback when a game is chosen from the dropdown."""
        operations = self.engine.load_game_operations(selected_game, interactive_pause=False)

        # Clear previous operation widgets
        for widget in self.op_list_frame.winfo_children():
            widget.destroy()

        # Add "Run All" button if applicable
        if any(op.get("run-all") for op in operations):
            run_all_button = ctk.CTkButton(
                self.op_list_frame,
                text="Run All",
                command=self.run_all_operations,
                fg_color="#006400"
            )
            run_all_button.pack(fill="x", padx=5, pady=(5, 10))

        # Create buttons for individual operations
        for op in operations:
            op_name = op.get("Name")
            if op_name:
                button = ctk.CTkButton(
                    self.op_list_frame,
                    text=op_name,
                    command=lambda op_config=op: self.run_operation(op_config)
                )
                button.pack(fill="x", padx=5, pady=2)

    def run_all_operations(self):
        """Run all 'run-all' operations in a separate thread."""
        thread = threading.Thread(target=self.engine.execute_run_all)
        thread.start()

    def run_operation(self, op_config: dict):
        """Prepare and run a selected operation, showing a prompt dialog if needed."""
        prompts = op_config.get("prompts", [])
        prompt_answers = {}

        if prompts:
            dialog = PromptDialog(self, title=op_config.get("Name"), prompts=prompts)
            prompt_answers = dialog.get_answers()

            if prompt_answers is None:
                return

        command = self.engine.build_command(op_config, prompt_answers)

        thread = threading.Thread(
            target=self.engine.execute_command,
            args=(command, op_config.get("Name"))
        )
        thread.start()

if __name__ == "__main__":
    app = App()
    app.mainloop()



