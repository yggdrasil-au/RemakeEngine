# main_gui.py
import customtkinter as ctk
from operations_engine import OperationsEngine
from pathlib import Path
import threading

import os
import sys
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), 'Utils')))
from printer import print, Colours, print_error, print_verbose, print_debug, printc


class App(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("Operations Manager")
        self.geometry("600x400")

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

        # --- NEW: Add "Run All" button if applicable ---
        if any(op.get("run-all") for op in operations):
            run_all_button = ctk.CTkButton(
                self.op_list_frame,
                text="Run All Marked Operations",
                command=self.run_all_operations,
                fg_color="#006400" # A different color to stand out
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
        """Prepare and run a selected operation in a separate thread."""

        prompt_answers = {}
        for prompt in op_config.get("prompts", []):
            prompt_answers[prompt["Name"]] = prompt.get("default")

        command = self.engine.build_command(op_config, prompt_answers)

        thread = threading.Thread(
			target=self.engine.execute_command,
			args=(command, op_config.get("Name"))
        )
        thread.start()


if __name__ == "__main__":
    app = App()
    app.mainloop()


