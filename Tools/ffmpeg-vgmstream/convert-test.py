"""
Unified and parallelized CLI tool for converting media using FFmpeg or vgmstream-cli.
"""
import argparse
import subprocess
import shutil
from pathlib import Path
from concurrent.futures import ProcessPoolExecutor
from functools import partial
import sys
import os
from tqdm import tqdm

# Assuming your printer utility is available
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'Utils')))
from printer import print, Colours, print_error, print_verbose, print_debug, printc


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert media files in parallel using FFmpeg or vgmstream-cli."
    )
    # --- Mode Selection ---
    parser.add_argument("--mode", "-m", required=True, choices=["ffmpeg", "vgmstream"], help="Conversion mode.")

    # --- Common Paths & Extensions ---
    parser.add_argument("--source", "-s", required=True, type=Path, help="Path to the source directory.")
    parser.add_argument("--target", "-t", required=True, type=Path, help="Path to the target directory.")
    parser.add_argument("--input-ext", "-i", required=True, help="Input file extension (e.g., .vp6).")
    parser.add_argument("--output-ext", "-o", required=True, help="Output file extension (e.g., .ogv).")

    # --- FFmpeg-specific ---
    parser.add_argument("--ffmpeg-path", "-f", help="Path to FFmpeg executable (auto-detected if in PATH).")
    parser.add_argument("--overwrite", action="store_true", help="Overwrite existing files.")
    parser.add_argument("--video-codec", default="libtheora", help="FFmpeg video codec.")
    parser.add_argument("--video-quality", default="10", help="FFmpeg video quality.")
    parser.add_argument("--audio-codec", default="libvorbis", help="FFmpeg audio codec.")
    parser.add_argument("--audio-quality", default="10", help="FFmpeg audio quality.")

    # --- vgmstream-specific ---
    parser.add_argument("--vgmstream-cli", help="Path to vgmstream-cli executable (auto-detected if in PATH).")

    # --- Concurrency & Logging ---
    parser.add_argument("--workers", "-w", type=int, default=os.cpu_count(), help="Number of parallel workers to use.")
    parser.add_argument("--verbose", "-v", action="store_true", help="Verbose output.")
    parser.add_argument("--debug", "-d", action="store_true", help="Debug output.")
    # --- New Debug Mode ---
    parser.add_argument(
        "--interactive-debug",
        action="store_true",
        help="Run conversions one-by-one and show the tool's live output. Disables parallelism and the progress bar."
    )

    return parser.parse_args()


def process_file(src_path: Path, args: argparse.Namespace, tool_executable: str, interactive: bool = False) -> tuple[str, str | None]:
    """
    Worker function to convert a single file.
    Can run in normal (captured output) or interactive (streaming output) mode.
    Returns a tuple of (status, error_message).
    """
    try:
        # Calculate destination path
        relative_path = src_path.relative_to(args.source)
        dest_path = (args.target / relative_path).with_suffix(args.output_ext)
        dest_path.parent.mkdir(parents=True, exist_ok=True)

        if dest_path.exists() and not args.overwrite:
            return "skipped", None

        # Build the command based on the mode
        cmd = []
        if args.mode == "ffmpeg":
            # For interactive mode, don't suppress loglevel
            log_level = ["-loglevel", "error"] if not interactive else []
            cmd = [
                tool_executable,
                "-y",  # Overwrite flag for FFmpeg
                "-i", str(src_path),
                "-c:v", args.video_codec,
                "-q:v", args.video_quality,
                "-c:a", args.audio_codec,
                "-q:a", args.audio_quality,
                *log_level,
                str(dest_path),
            ]
        elif args.mode == "vgmstream":
            cmd = [tool_executable, "-o", str(dest_path), str(src_path)]

        # Run the conversion
        if interactive:
            print_debug(f"Command: {' '.join(cmd)}")
            # Stream output directly to the console
            subprocess.run(cmd, check=True)
        else:
            print_debug(f"Command: {' '.join(cmd)}")
            # Capture output to keep the main console clean
            subprocess.run(cmd, check=True, capture_output=True, text=True)

        return "success", None

    except subprocess.CalledProcessError as e:
        # Clean up partially converted file on error
        if dest_path.exists():
            dest_path.unlink(missing_ok=True)
        # In interactive mode, the error is already on screen. Otherwise, return stderr.
        error_msg = f"Process failed with exit code {e.returncode}. See console output above." if interactive else e.stderr.strip()
        return "error", error_msg
    except Exception as e:
        return "error", str(e)


def main():
    args = parse_args()

    # Enable optional logging
    if args.debug: print_debug.enable()
    if args.verbose or args.debug: print_verbose.enable()

    # --- 1. Setup and Validation ---
    print(Colours.CYAN, f"--- Starting {args.mode.upper()} Conversion ---")
    tool_executable = None
    if args.mode == "ffmpeg":
        tool_executable = args.ffmpeg_path or shutil.which("ffmpeg") or shutil.which("ffmpeg.exe")
    elif args.mode == "vgmstream":
        tool_executable = args.vgmstream_cli or shutil.which("vgmstream-cli") or shutil.which("vgmstream-cli.exe")

    if not tool_executable:
        print_error(f"Could not find executable for mode '{args.mode}'. Please specify the path or add it to your PATH.")
        sys.exit(1)

    print_verbose(f"Using executable: {tool_executable}")
    args.source = args.source.resolve()
    args.target = args.target.resolve()

    if not args.source.is_dir():
        print_error(f"Source directory not found: {args.source}")
        sys.exit(1)

    # --- 2. File Discovery ---
    files_to_process = list(args.source.rglob(f"*{args.input_ext}"))
    if not files_to_process:
        print(Colours.YELLOW, f"No '{args.input_ext}' files found in {args.source}.")
        return

    # --- 3. Processing (Two Modes: Interactive or Parallel) ---
    success_count, skipped_count, error_count = 0, 0, 0
    errors = []

    if args.interactive_debug:
        print(Colours.MAGENTA, "\n--- Running in Interactive Debug Mode ---")
        print(Colours.MAGENTA, "Processing files one-by-one to show live tool output.")
        
        for i, file_path in enumerate(files_to_process):
            printc(Colours.CYAN, f"\n[{i+1}/{len(files_to_process)}] Processing: {file_path.name}")
            status, msg = process_file(file_path, args, tool_executable, interactive=True)
            
            if status == "success":
                printc(Colours.GREEN, "-> Success")
                success_count += 1
            elif status == "skipped":
                printc(Colours.YELLOW, "-> Skipped")
                skipped_count += 1
            elif status == "error":
                # The detailed error was already printed by the tool itself
                printc(Colours.RED, f"-> Error: {msg}")
                error_count += 1
                errors.append((file_path.name, msg))
    else:
        # Original parallel processing logic
        print(Colours.CYAN, f"Found {len(files_to_process)} files to process with {args.workers} workers.")
        worker_func = partial(process_file, args=args, tool_executable=tool_executable, interactive=False)

        with ProcessPoolExecutor(max_workers=args.workers) as executor:
            results = list(tqdm(
                executor.map(worker_func, files_to_process),
                total=len(files_to_process),
                desc="Converting Files",
                unit="file"
            ))

        for i, (status, msg) in enumerate(results):
            if status == "success": success_count += 1
            elif status == "skipped": skipped_count += 1
            elif status == "error":
                error_count += 1
                errors.append((files_to_process[i].name, msg))

    # --- 4. Tally and Report Results ---
    print(Colours.CYAN, "\n--- Conversion Completed ---")
    print(Colours.GREEN, f"Success: {success_count}")
    print(Colours.YELLOW, f"Skipped: {skipped_count}")
    print(Colours.RED, f"Errors: {error_count}")

    if errors:
        print_error("\nEncountered the following errors:")
        for filename, error_msg in errors:
            print(Colours.RED, f"  - File: {filename}\n    Reason: {error_msg}")


if __name__ == "__main__":
    main()