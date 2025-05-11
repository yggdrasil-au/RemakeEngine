from . import init
from .Tools.process import init_textures
from .Tools.process import copy


def main() -> None:
    """Main function to execute init and copy processes."""

    print("Running init")
    init.main()

    print("Running init_textures")
    init_textures.main()

    print("Running copy")
    copy.main("move")
