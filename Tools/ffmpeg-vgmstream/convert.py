"""
Unified and parallelized CLI tool for converting media using FFmpeg or vgmstream-cli.
"""
import argparse
import subprocess
import shutil
from pathlib import Path
import multiprocessing
from concurrent.futures import ProcessPoolExecutor
from functools import partial
import sys
import os
from tqdm import tqdm
import tempfile

sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '.')))
from Engine.Utils.printer import print, Colours, error, verbose, debug, print_debug, print_verbose


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert media files in parallel using FFmpeg or vgmstream-cli."
    )
    # --- Mode Selection ---
    parser.add_argument("--mode", "-m", required=True, choices=["ffmpeg", "vgmstream"], help="Conversion mode.")
    parser.add_argument("--type", required=True, choices=["audio", "video"], help="Conversion type.")

    # --- Common Paths & Extensions ---
    parser.add_argument("--source", "-s", required=True, type=Path, help="Path to the source directory.")
    parser.add_argument("--target", "-t", required=True, type=Path, help="Path to the target directory.")
    parser.add_argument("--input-ext", "-i", required=True, help="Input file extension (e.g., .vp6).")
    parser.add_argument("--output-ext", "-o", required=True, help="Output file extension (e.g., .ogv).")

    # configs
    parser.add_argument("--overwrite", action="store_true", help="Overwrite existing files.")

    # audio specific
    parser.add_argument(
        "--godot-compatible",
        action="store_true",
        help="Split 4-channel audio into two stereo files for Godot 3D audio."
    )

    # --- FFmpeg-specific ---
    parser.add_argument("--ffmpeg-path", "-f", help="Path to FFmpeg executable (auto-detected if in PATH).")
    parser.add_argument("--video-codec", default="libtheora", help="FFmpeg video codec.")
    parser.add_argument("--video-quality", default="10", help="FFmpeg video quality.")
    parser.add_argument("--audio-codec", default="libvorbis", help="FFmpeg audio codec.")
    parser.add_argument("--audio-quality", default="10", help="FFmpeg audio quality.")

    # --- vgmstream-specific ---
    parser.add_argument("--vgmstream-cli", help="Path to vgmstream-cli executable (auto-detected if in PATH).")

    # --- Concurrency & Logging ---
    parser.add_argument("--workers", "-w", type=int, help="Number of parallel workers to use.")
    parser.add_argument("--verbose", "-v", action="store_true", help="Verbose output.")
    parser.add_argument("--debug", "-d", action="store_true", help="Debug output.")

    return parser.parse_args()


def process_file(src_path: Path, args: argparse.Namespace, tool_executable: str) -> tuple[str, str | None]:
    """
    Worker function to convert a single file.
    Returns a tuple of (status, error_message).
    """
    try:
        # Calculate destination path
        relative_path = src_path.relative_to(args.source)
        dest_path = (args.target / relative_path).with_suffix(args.output_ext)
        dest_path.parent.mkdir(parents=True, exist_ok=True)

        if dest_path.exists() and not args.overwrite:
            return "skipped", None
    except Exception as e:
        return "error", str(e)
    try:
        # Build the command based on the mode and operation type
        cmd = []
        if args.mode == "ffmpeg":
            if args.type == "video":
                verbose("Converting video.")
                cmd = [
                    tool_executable,
                    "-y",  # Overwrite flag for FFmpeg
                    "-i", str(src_path),
                    "-c:v", args.video_codec,
                    "-q:v", args.video_quality,
                    str(dest_path),
                ]
            elif args.type == "audio":
                if args.godot_compatible:
                    verbose("Converting audio for Godot compatibility.")
                    # Split quad into stereo pairs
                    base = dest_path.with_suffix("")  # strip extension
                    cmd = [
                        tool_executable, "-y", "-i", str(src_path),
                        "-filter_complex", "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                        "-map", "[FRONT]", str(base) + "_front" + args.output_ext,
                        "-map", "[REAR]", str(base) + "_rear" + args.output_ext,
                    ]
                else:
                    verbose("Converting audio without splitting channels for Godot compatibility.")
                    cmd = [
                        tool_executable,
                        "-y",  # Overwrite flag for FFmpeg
                        "-i", str(src_path),
                        "-c:a", args.audio_codec,
                        "-q:a", args.audio_quality,
                        "-loglevel", "error", # Keep FFmpeg's console output clean
                        str(dest_path),
                    ]
            else:
                verbose("Converting both audio and video.")
                cmd = [
                    tool_executable,
                    "-y",  # Overwrite flag for FFmpeg
                    "-i", str(src_path),
                    "-c:v", args.video_codec,
                    "-q:v", args.video_quality,
                    "-c:a", args.audio_codec,
                    "-q:a", args.audio_quality,
                    "-loglevel", "error", # Keep FFmpeg's console output clean
                    str(dest_path),
                ]
        elif args.mode == "vgmstream":
            if args.type == "audio":
                if args.godot_compatible:
                    verbose("Converting audio for vgmstream.")
                    # Step 1: decode with vgmstream to temp wav
                    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
                        tmp_wav = tmp.name

                    vgm_cmd = [tool_executable, "-o", tmp_wav, str(src_path)]
                    debug(f"Command: {' '.join(vgm_cmd)}")
                    subprocess.run(vgm_cmd, check=True, capture_output=True, text=True)

                    # Step 2: split with ffmpeg
                    ffmpeg_exec = shutil.which("ffmpeg") or shutil.which("ffmpeg.exe")
                    if not ffmpeg_exec:
                        return "error", "ffmpeg not found (required for --godot-compatible with vgmstream)."

                    base = dest_path.parent / dest_path.stem
                    cmd = [
                        ffmpeg_exec, "-y", "-i", tmp_wav,
                        "-filter_complex", "[0:a]channelsplit=channel_layout=quad[FL][FR][BL][BR];[FL][FR]join=inputs=2:channel_layout=stereo[FRONT];[BL][BR]join=inputs=2:channel_layout=stereo[REAR]",
                        "-map", "[FRONT]", str(base) + "_front" + args.output_ext,
                        "-map", "[REAR]", str(base) + "_rear" + args.output_ext,
                    ]
                    subprocess.run(cmd, check=True, capture_output=True, text=True)

                    # Clean up
                    os.remove(tmp_wav)
                    return "success", None
                else:
                    verbose("Converting audio for vgmstream.")
                    cmd = [
                        tool_executable, "-o",
                        str(dest_path),
                        str(src_path)
                    ]
            elif args.type == "video":
                error("vgmstream-cli does not support video conversion.")
                return "error", "vgmstream-cli does not support video conversion."
        else:
            error(f"Unsupported mode: {args.mode}")
            return "error", f"Unsupported mode: {args.mode}"

        # Run the conversion
        debug(f"Command: {' '.join(cmd)}")
        if args.verbose or args.debug:
            # in debug mode dont capture output
            subprocess.run(cmd, check=True)
        else:
            subprocess.run(cmd, check=True, capture_output=True, text=True)
        return "success", None

    except subprocess.CalledProcessError as e:
        # Clean up partially converted file on error
        if dest_path.exists():
            dest_path.unlink(missing_ok=True)
        return "error", e.stderr.strip()
    except Exception as e:
        return "error", str(e)


def main() -> None:
    try:
        args = parse_args()

        # Enable optional logging
        if args.debug:
            print_debug.enable()
        if args.verbose or args.debug:
            print_verbose.enable()

        # avoid too many workers if not specified
        if args.workers is None:
            # Calculate 75% of CPU cores, ensuring it's a whole number and at least 1.
            args.workers = max(1, int(multiprocessing.cpu_count() * 0.75))
            #args.workers = int(multiprocessing.cpu_count())


        # --- 1. Setup and Validation ---
        print(colour=Colours.CYAN, message=f"--- Starting {args.mode.upper()} Conversion ---")
        tool_executable = None
        if args.mode == "ffmpeg":
            tool_executable = args.ffmpeg_path or shutil.which("ffmpeg") or shutil.which("ffmpeg.exe")
        elif args.mode == "vgmstream":
            tool_executable = args.vgmstream_cli or shutil.which("vgmstream-cli") or shutil.which("vgmstream-cli.exe")

        if not tool_executable:
            error(f"Could not find executable for mode '{args.mode}'. Please specify the path or add it to your PATH.")
            sys.exit(1)

        verbose(f"Using executable: {tool_executable}")
        args.source = args.source.resolve()
        args.target = args.target.resolve()

        if not args.source.is_dir():
            error(f"Source directory not found: {args.source}")
            sys.exit(1)

        # --- 2. File Discovery ---
        # Use a generator expression for memory efficiency, then convert to list for tqdm
        files_to_process = list(args.source.rglob(f"*{args.input_ext}"))
        if not files_to_process:
            print(colour=Colours.YELLOW, message=f"No '{args.input_ext}' files found in {args.source}.")
            return

        print(colour=Colours.CYAN, message=f"Found {len(files_to_process)} files to process with {args.workers} workers.")

        # --- 3. Parallel Processing ---
        success_count, skipped_count, error_count = 0, 0, 0
        # Use partial to "pre-load" the worker function with fixed arguments
        worker_func = partial(process_file, args=args, tool_executable=tool_executable)

        with ProcessPoolExecutor(max_workers=args.workers) as executor:
            # Use tqdm to create a progress bar
            results = list(tqdm(
                executor.map(worker_func, files_to_process),
                total=len(files_to_process),
                desc="Converting Files",
                unit="file"
            ))

        # --- 4. Tally and Report Results ---
        errors = []
        for i, (status, msg) in enumerate(results):
            if status == "success":
                success_count += 1
            elif status == "skipped":
                skipped_count += 1
            elif status == "error":
                error_count += 1
                errors.append((files_to_process[i].name, msg))

        print(colour=Colours.CYAN, message="\n--- Conversion Completed ---")
        print(colour=Colours.GREEN, message=f"Success: {success_count}")
        print(colour=Colours.YELLOW, message=f"Skipped: {skipped_count}")
        print(colour=Colours.RED, message=f"Errors: {error_count}")

        if errors:
            error("\nEncountered the following errors:")
            for filename, error_msg in errors:
                print(colour=Colours.RED, message=f"  - File: {filename}\n    Reason: {error_msg}")
    except AttributeError as e:
        error(f"Attribute error: {e}. This may be due to an incorrect or missing argument.")
        sys.exit(1)
    except Exception as e:
        error(f"An unexpected error occurred: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()

