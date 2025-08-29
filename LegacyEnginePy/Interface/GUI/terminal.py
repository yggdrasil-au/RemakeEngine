import customtkinter as ctk
import re # Import regex module

class AnsiColorParser:
    """Parses strings with ANSI escape codes and maps them to Tkinter text tags."""

    def __init__(self, widget: ctk.CTkTextbox) -> None:
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
