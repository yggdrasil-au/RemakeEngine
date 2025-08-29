# Engine\Core\git_tools.py
import shutil
import subprocess
from pathlib import Path
from Engine.Utils.printer import print, Colours


class GitTools:
    def __init__(self, games_dir: Path):
        self.games_dir = games_dir


    @staticmethod
    def is_git_installed() -> bool:
        return shutil.which('git') is not None


    def clone_module(self, url: str) -> bool:
        if not self.is_git_installed():
            print(colour=Colours.RED, message="Git is not installed or not found in PATH.", prefix="ENGINE", prefix_colour=Colours.GRAY)
            return False
        try:
            repo_name = Path(url).stem
            target = self.games_dir / repo_name
            if target.exists():
                print(colour=Colours.YELLOW, message=f"Directory '{repo_name}' already exists. Skipping download.", prefix="ENGINE", prefix_colour=Colours.GRAY)
                return True
            print(colour=Colours.CYAN, message=f"Downloading '{repo_name}' from '{url}'...", prefix="ENGINE", prefix_colour=Colours.GRAY)
            print(colour=Colours.CYAN, message=f"Target directory: '{target}'", prefix="ENGINE", prefix_colour=Colours.GRAY)
            self.games_dir.mkdir(parents=True, exist_ok=True)
            proc = subprocess.Popen(['git', 'clone', url, str(target)], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8')
            if proc.stdout:
                for line in proc.stdout:
                    if line:
                        print(colour=Colours.BLUE, message=line.strip(), prefix="ENGINE", prefix_colour=Colours.GRAY)
            rc = proc.wait()
            if rc == 0:
                print(colour=Colours.GREEN, message=f"\nSuccessfully downloaded '{repo_name}'.", prefix="ENGINE", prefix_colour=Colours.GRAY)
                return True
            print(colour=Colours.RED, message=f"\nFailed to download '{repo_name}'. Git exited with code {rc}.", prefix="ENGINE", prefix_colour=Colours.GRAY)
            return False
        except Exception as e:
            print(colour=Colours.RED, message=f"An error occurred during download: {e}", prefix="ENGINE", prefix_colour=Colours.GRAY)
            return False
