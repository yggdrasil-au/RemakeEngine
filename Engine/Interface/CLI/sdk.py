
import questionary
from Engine.Utils.printer import print, Colours
from typing import Any, Literal

from Engine.Interface.CLI.utils import custom_style_fancy

# --- Handlers to bridge SDK events <-> CLI ---
#  - on_output: stream child stdout/stderr into our console
#  - on_event : notice "prompt"/warnings, and remember the latest question
#  - stdin_provider: actually read the user's answer when ProcessRunner asks
import sys as _sys
last_prompt = {"msg": "Input required"}
def on_output(line, stream) -> None:
	"""Stream process output to the console with appropriate coloring."""
	target = _sys.stderr if stream == "stderr" else _sys.stdout
	# Use your coloured printer so ANSI still looks nice in terminals that support it
	print(colour=Colours.WHITE if stream == "stdout" else Colours.RED, message=line, file=target)

def on_event(evt) -> None:
	"""Handle events from the engine, such as prompts and warnings."""
	if evt.get("event") == "prompt":
		last_prompt["msg"] = evt.get("message", "Input required")
		# Echo the question so the user sees it *before* we block for input
		#print(colour=Colours.CYAN, message=f"? {last_prompt['msg']}")
	elif evt.get("event") == "warning":
		print(colour=Colours.YELLOW, message=f"⚠ {evt.get('message','')}")
	elif evt.get("event") == "error":
		print(colour=Colours.RED, message=f"✖ {evt.get('message','')}")

def stdin_provider() -> Any | Literal['']:
	"""Handles stdin requests from the engine by prompting the user for input."""
	try:
		# Flush any pending output before we ask
		import sys as _sys
		_sys.stdout.flush()
		_sys.stderr.flush()

		# Ask using Questionary so the TTY is in a good state
		ans = questionary.text(
			message=last_prompt["msg"],
			qmark="?",
			style=custom_style_fancy
		).ask()

		# Normalise None (Esc/Ctrl+C) to empty string so the child can proceed
		return "" if ans is None else ans
	except KeyboardInterrupt:
		return "None"
