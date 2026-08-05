#!/usr/bin/env python3
"""Pack reviewed Archer v003 sprite-gen runs into Unity motion sheets."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
from pathlib import Path

from PIL import Image


CELL_SIZE = 128
VERSION = "v003"
SOURCE_ROOT = Path("Docs/AI_Usage/sprite_runs/archer_motion_v003")
OUTPUT_ROOT = Path("Assets/_Project/Art/M1Production/Characters/Animation/MotionsV003")
INDEX_PATH = Path("Docs/AI_Usage/generations/archer_motion_v003_index.json")

DIRECTIONS = (
    "south",
    "north",
    "east",
    "west",
    "southeast",
    "southwest",
    "northeast",
    "northwest",
)
DIRECT_DIRECTIONS = ("south", "north", "east", "southeast", "northeast")
MIRROR_SOURCES = {
    "west": "east",
    "southwest": "southeast",
    "northwest": "northeast",
}
STATES = (
    ("idle", 4, 4.0, True),
    ("walk", 4, 6.0, True),
    ("run", 4, 9.0, True),
    ("attack_charge", 6, 8.0, False),
    ("attack_execute", 6, 14.0, False),
    ("recover", 4, 7.0, False),
    ("hit", 3, 12.0, False),
    ("death", 6, 8.0, False),
)


class BuildError(RuntimeError):
    pass


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def encode_png(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=False, compress_level=9)
    return buffer.getvalue()


def load_direction(root: Path, direction: str) -> tuple[Image.Image, dict]:
    run_dir = root / SOURCE_ROOT / direction
    manifest_path = run_dir / "manifest.json"
    atlas_path = run_dir / "sprite-sheet-alpha.png"
    if not manifest_path.is_file() or not atlas_path.is_file():
        raise BuildError(f"Missing reviewed run for direction '{direction}'.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("degraded_static_fallback"):
        raise BuildError(f"Direction '{direction}' used a forbidden static fallback.")
    if manifest.get("cell", {}).get("width") != CELL_SIZE or manifest.get("cell", {}).get("height") != CELL_SIZE:
        raise BuildError(f"Direction '{direction}' is not a {CELL_SIZE}px cell run.")
    with Image.open(atlas_path) as opened:
        atlas = opened.convert("RGBA")
    return atlas, manifest


def extract_frame(atlas: Image.Image, manifest: dict, state: str, frame_index: int) -> Image.Image:
    try:
        rect = manifest["frame_layout"]["rows"][state][frame_index]
    except (KeyError, IndexError) as error:
        raise BuildError(f"Missing frame layout for {state}/{frame_index}.") from error
    x, y, width, height = (rect[key] for key in ("x", "y", "w", "h"))
    if (width, height) != (CELL_SIZE, CELL_SIZE):
        raise BuildError(f"Invalid frame size for {state}/{frame_index}: {width}x{height}.")
    frame = atlas.crop((x, y, x + width, y + height))
    if frame.getbbox() is None:
        raise BuildError(f"Empty frame for {state}/{frame_index}.")
    return frame


def build_outputs(root: Path) -> tuple[dict[str, bytes], dict]:
    loaded = {direction: load_direction(root, direction) for direction in DIRECT_DIRECTIONS}
    outputs: dict[str, bytes] = {}
    records = []
    for state, frames, fps, loop in STATES:
        sheet = Image.new("RGBA", (CELL_SIZE * frames * len(DIRECTIONS), CELL_SIZE), (0, 0, 0, 0))
        for direction_index, direction in enumerate(DIRECTIONS):
            source_direction = MIRROR_SOURCES.get(direction, direction)
            atlas, manifest = loaded[source_direction]
            for frame_index in range(frames):
                frame = extract_frame(atlas, manifest, state, frame_index)
                if direction in MIRROR_SOURCES:
                    frame = frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                x = (direction_index * frames + frame_index) * CELL_SIZE
                sheet.alpha_composite(frame, (x, 0))
        relative_path = OUTPUT_ROOT / f"chr_archer_{state}_motion_{VERSION}.png"
        payload = encode_png(sheet)
        outputs[relative_path.as_posix()] = payload
        records.append({
            "state": state,
            "frames": frames,
            "fps": fps,
            "loop": loop,
            "path": relative_path.as_posix(),
            "width": sheet.width,
            "height": sheet.height,
            "sha256": sha256(payload),
        })
    index = {
        "schema": "overbless.archer-motion-v003-index/v1",
        "role": "archer",
        "version": VERSION,
        "cell": {"width": CELL_SIZE, "height": CELL_SIZE, "pivot": [0.5, 0.0]},
        "direct_directions": list(DIRECT_DIRECTIONS),
        "derived_mirrors": MIRROR_SOURCES,
        "source_runs": [
            {
                "direction": direction,
                "atlas": (SOURCE_ROOT / direction / "sprite-sheet-alpha.png").as_posix(),
                "manifest": (SOURCE_ROOT / direction / "manifest.json").as_posix(),
            }
            for direction in DIRECT_DIRECTIONS
        ],
        "outputs": records,
    }
    return outputs, index


def write(root: Path, outputs: dict[str, bytes], index: dict) -> None:
    for relative_path, payload in outputs.items():
        path = root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
    index_path = root / INDEX_PATH
    index_path.parent.mkdir(parents=True, exist_ok=True)
    index_path.write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def check(root: Path, outputs: dict[str, bytes], index: dict) -> list[str]:
    errors = []
    for relative_path, payload in outputs.items():
        path = root / relative_path
        if not path.is_file():
            errors.append(f"Missing output '{relative_path}'.")
        elif path.read_bytes() != payload:
            errors.append(f"Output '{relative_path}' is not deterministic.")
    expected_index = json.dumps(index, ensure_ascii=False, indent=2) + "\n"
    index_path = root / INDEX_PATH
    if not index_path.is_file():
        errors.append(f"Missing index '{INDEX_PATH.as_posix()}'.")
    elif index_path.read_text(encoding="utf-8") != expected_index:
        errors.append("Archer v003 index is not deterministic.")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default=".", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        outputs, index = build_outputs(root)
        if args.check:
            errors = check(root, outputs, index)
            if errors:
                print("\n".join(errors), file=sys.stderr)
                return 1
            print("Archer motion v003 check passed: 8 motion sheets are deterministic.")
            return 0
        write(root, outputs, index)
        print("Archer motion v003 build completed: 8 motion sheets written.")
        return 0
    except BuildError as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
