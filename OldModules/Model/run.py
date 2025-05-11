
"""
This module orchestrates the execution of init and blend processes.
"""

from .Tools.process import init
from .Tools.process import blend


def main(verbose: bool, debug_sleep: bool, export: set) -> None:
    """Main function to execute init and blend processes."""
    print("Running init")
    init.main()

    print("Running blend")
    blend.main(verbose, debug_sleep, export)
