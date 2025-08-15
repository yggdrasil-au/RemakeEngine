# main_gui.py
import customtkinter as ctk
from operations_engine import OperationsEngine
from pathlib import Path
import threading

import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), 'Utils')))
from printer import print, Colours, print_error, print_verbose, print_debug, printc

# --- NEW: Dialog window for handling interactive prompts ---
# --- CORRECTED: Dialog window for handling interactive prompts ---
class PromptDialog(ctk.CTkToplevel):
    def __init__(self, master, title, prompts):
        super().__init__(master)
        self.title(title)
        self.prompts = prompts
        self.result = None

        self.prompt_vars = {}
        self.prompt_widgets = {}

        # --- MODIFIED: Create all widgets first, but DON'T pack them yet ---
        for prompt in self.prompts:
            prompt_name = prompt["Name"]
            frame = ctk.CTkFrame(self)

            if prompt["type"] == "confirm":
                # The underlying variable is still a BooleanVar
                var = ctk.BooleanVar(value=prompt.get("default", False))
                self.prompt_vars[prompt_name] = var

                # Create a label for the question
                label = ctk.CTkLabel(frame, text=prompt["message"])
                label.pack(padx=10, pady=(10, 5), anchor="w")

                # Create a sub-frame to hold the buttons side-by-side
                button_sub_frame = ctk.CTkFrame(frame, fg_color="transparent")
                button_sub_frame.pack(padx=10, pady=(0, 10), anchor="w")

                # Create "Yes" and "No" radio buttons
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
        self.button_frame = ctk.CTkFrame(self) # MODIFIED: Saved to instance
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
        """
        MODIFIED: This function now completely handles the layout.
        It clears and redraws all widgets in the correct order.
        """
        # Unpack all prompt frames to start fresh
        for frame in self.prompt_widgets.values():
            frame.pack_forget()
        self.button_frame.pack_forget()

        # Iterate and pack only the visible widgets in order
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

        # Always pack the button frame at the end
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

# --- MODIFIED: Main Application Class ---
class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("Operations Manager")
        self.geometry("600x500") # Increased height for more operations

        self.engine = OperationsEngine(Path.cwd())

        # --- Layout ---
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)

        # --- Widgets ---
        top_frame = ctk.CTkFrame(self)
        top_frame.grid(row=0, column=0, padx=10, pady=10, sticky="ew")

        self.game_selector = ctk.CTkComboBox(
            top_frame,
            values=self.engine.get_available_games(),
            command=self.on_game_selected
        )
        self.game_selector.pack(side="left", padx=5)
        self.game_selector.set("Select a Game...")

        self.op_list_frame = ctk.CTkScrollableFrame(self, label_text="Operations")
        self.op_list_frame.grid(row=1, column=0, padx=10, pady=(5, 10), sticky="nsew")

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
                text="Run All Marked Operations",
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

        # If there are prompts, open the dialog to get answers
        if prompts:
            dialog = PromptDialog(self, title=op_config.get("Name"), prompts=prompts)
            prompt_answers = dialog.get_answers()

            # If the user cancelled the dialog, do nothing
            if prompt_answers is None:
                return

        # If there were no prompts, the prompt_answers dict is empty, which is fine.

        command = self.engine.build_command(op_config, prompt_answers)

        # Run the command in a separate thread to keep the GUI responsive
        thread = threading.Thread(
            target=self.engine.execute_command,
            args=(command, op_config.get("Name"))
        )
        thread.start()

if __name__ == "__main__":
    app = App()
    app.mainloop()



