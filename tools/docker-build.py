"""Build selected build.yaml targets using the live repository mounted at /config."""

import argparse
from pathlib import Path
import shlex
import shutil
import subprocess
import sys

import yaml


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("targets", nargs="*", default=["corne_left"],
                        help="Shield names from build.yaml, or 'all'")
    parser.add_argument("--dry-run", action="store_true",
                        help="Show build commands without compiling")
    args = parser.parse_args()
    repo = Path("/config")
    if not (repo / "build.yaml").is_file():
        parser.error("Mount the repository at /config and an output folder at /out.")
    if (repo / "config/west.yml").read_bytes().replace(b"\r\n", b"\n") != (
        Path("/work/config/west.yml").read_bytes().replace(b"\r\n", b"\n")
    ):
        parser.error("config/west.yml changed: rebuild the Docker image first.")

    entries = yaml.safe_load((repo / "build.yaml").read_text())["include"]
    targets = args.targets or ["corne_left"]
    known = {entry["shield"] for entry in entries}
    if targets != ["all"] and not set(targets) <= known:
        parser.error("Unknown target. Choose: " + ", ".join(sorted(known)) + ", all")
    selected = [entry for entry in entries
                if targets == ["all"] or entry["shield"] in targets]

    for entry in selected:
        name = entry.get("artifact-name", f'{entry["shield"]}-{entry["board"]}-zmk')
        # Artifact names also become output paths; keep them to a single filename.
        if not name or any(c in name for c in '/\\') or name in (".", ".."):
            parser.error(f"Invalid artifact name: {name!r}")
        build_dir = Path("/work/build") / name
        command = ["west", "build", "-p", "always", "-s", "/work/zmk/app",
                   "-d", str(build_dir), "-b", entry["board"]]
        if entry.get("snippet"):
            command += ["-S", entry["snippet"]]
        command += ["--", "-DZMK_CONFIG=/config/config",
                    "-DZMK_EXTRA_MODULES=/config", f'-DSHIELD={entry["shield"]}']
        command += shlex.split(entry.get("cmake-args", ""))
        print(shlex.join(command), flush=True)
        if args.dry_run:
            continue
        subprocess.run(command, check=True, cwd="/work")
        output = Path("/out")
        output.mkdir(parents=True, exist_ok=True)
        shutil.copy2(build_dir / "zephyr/zmk.uf2", output / f"{name}.uf2")
        # Export the final hardware mapping for checking repair overrides.
        shutil.copy2(build_dir / "zephyr/zephyr.dts", output / f"{name}.dts")
        print(f"Firmware saved to /out/{name}.uf2", flush=True)


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as error:
        sys.exit(error.returncode)
