#!/usr/bin/env python3
"""Pack reviewed south-facing component-row runs into Unity v003 motion sheets.

The new Player, Dasher and Minion art is authored as independent state rows.
The game has eight logical facings; this first replacement locks the newly
approved south identity across those facings and mirrors the left-facing cells.
It deliberately never falls back to a legacy atlas or a static one-frame pose.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
from dataclasses import dataclass
from pathlib import Path

from PIL import Image


CELL_SIZE = 128
VERSION = "v003"
SOURCE_ROOT = Path("Docs/AI_Usage/sprite_runs/character_motion_v003")
OUTPUT_ROOT = Path("Assets/_Project/Art/M1Production/Characters/Animation/MotionsV003")
INDEX_ROOT = Path("Docs/AI_Usage/generations")
DIRECTIONS = ("south", "north", "east", "west", "southeast", "southwest", "northeast", "northwest")
MIRRORED_DIRECTIONS = {"west", "southwest", "northwest"}


@dataclass(frozen=True)
class StateSpec:
    name: str
    frames: int
    fps: float
    loop: bool


@dataclass(frozen=True)
class RoleSpec:
    role: str
    states: tuple[StateSpec, ...]


PLAYER_STATES = (
    StateSpec("idle", 4, 4.0, True),
    StateSpec("move", 4, 8.0, True),
    StateSpec("dash", 4, 14.0, False),
    StateSpec("bless_cast", 4, 8.0, True),
    StateSpec("hit", 3, 12.0, False),
    StateSpec("death", 6, 8.0, False),
)
ENEMY_STATES = (
    StateSpec("idle", 4, 4.0, True),
    StateSpec("walk", 4, 6.0, True),
    StateSpec("run", 4, 9.0, True),
    StateSpec("attack_charge", 6, 8.0, False),
    StateSpec("attack_execute", 6, 14.0, False),
    StateSpec("recover", 4, 7.0, False),
    StateSpec("hit", 3, 12.0, False),
    StateSpec("death", 6, 8.0, False),
)
MINION_STATES = (
    StateSpec("idle", 4, 4.0, True),
    StateSpec("walk", 4, 6.0, True),
    StateSpec("run", 4, 9.0, True),
    StateSpec("attack_charge", 6, 8.0, False),
    StateSpec("attack_execute", 6, 24.0, False),
    StateSpec("recover", 4, 7.0, False),
    StateSpec("hit", 3, 12.0, False),
    StateSpec("death", 6, 8.0, False),
)
ROLES = (
    RoleSpec("player", PLAYER_STATES),
    RoleSpec("dasher", ENEMY_STATES),
    RoleSpec("minion", MINION_STATES),
)


class BuildError(RuntimeError):
    pass


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def encode_png(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=False, compress_level=9)
    return buffer.getvalue()


def load_run(root: Path, role: RoleSpec) -> tuple[Image.Image, dict]:
    run_dir = root / SOURCE_ROOT / role.role / "south"
    manifest_path = run_dir / "manifest.json"
    atlas_path = run_dir / "sprite-sheet-alpha.png"
    if not manifest_path.is_file() or not atlas_path.is_file():
        raise BuildError(f"Missing reviewed south run for {role.role}.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("degraded_static_fallback"):
        raise BuildError(f"{role.role} used a forbidden static fallback.")
    cell = manifest.get("cell", {})
    if cell.get("width") != CELL_SIZE or cell.get("height") != CELL_SIZE:
        raise BuildError(f"{role.role} is not a {CELL_SIZE}px cell run.")
    with Image.open(atlas_path) as opened:
        return opened.convert("RGBA"), manifest


def extract_frame(
    atlas: Image.Image, manifest: dict, state: StateSpec, frame_index: int, source_state: str | None = None
) -> Image.Image:
    source_state = source_state or state.name
    try:
        rect = manifest["frame_layout"]["rows"][source_state][frame_index]
    except (KeyError, IndexError) as error:
        raise BuildError(f"Missing frame layout for {source_state}/{frame_index}.") from error
    x, y, width, height = (rect[key] for key in ("x", "y", "w", "h"))
    if (width, height) != (CELL_SIZE, CELL_SIZE):
        raise BuildError(f"Invalid frame size for {state.name}/{frame_index}: {width}x{height}.")
    frame = atlas.crop((x, y, x + width, y + height))
    if frame.getbbox() is None:
        raise BuildError(f"Empty frame for {state.name}/{frame_index}.")
    return frame


def build_role(root: Path, role: RoleSpec) -> tuple[dict[str, bytes], dict]:
    atlas, manifest = load_run(root, role)
    outputs: dict[str, bytes] = {}
    records = []
    for state in role.states:
        sheet = Image.new("RGBA", (CELL_SIZE * state.frames * len(DIRECTIONS), CELL_SIZE), (0, 0, 0, 0))
        for direction_index, direction in enumerate(DIRECTIONS):
            for frame_index in range(state.frames):
                source_state = "walk" if role.role == "player" and state.name == "move" else state.name
                frame = extract_frame(atlas, manifest, state, frame_index, source_state)
                if direction in MIRRORED_DIRECTIONS:
                    frame = frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                sheet.alpha_composite(frame, ((direction_index * state.frames + frame_index) * CELL_SIZE, 0))
        relative_path = OUTPUT_ROOT / f"chr_{role.role}_{state.name}_motion_{VERSION}.png"
        payload = encode_png(sheet)
        outputs[relative_path.as_posix()] = payload
        records.append({
            "state": state.name,
            "frames": state.frames,
            "fps": state.fps,
            "loop": state.loop,
            "path": relative_path.as_posix(),
            "width": sheet.width,
            "height": sheet.height,
            "sha256": sha256(payload),
        })
    index = {
        "schema": "overbless.character-motion-v003-index/v1",
        "role": role.role,
        "version": VERSION,
        "cell": {"width": CELL_SIZE, "height": CELL_SIZE, "pivot": [0.5, 0.0]},
        "authored_directions": ["south"],
        "derived_directions": {direction: "south" for direction in DIRECTIONS if direction != "south"},
        "mirrored_directions": sorted(MIRRORED_DIRECTIONS),
        "source_run": {
            "atlas": (SOURCE_ROOT / role.role / "south" / "sprite-sheet-alpha.png").as_posix(),
            "manifest": (SOURCE_ROOT / role.role / "south" / "manifest.json").as_posix(),
        },
        "outputs": records,
    }
    return outputs, index


def write_role(root: Path, role: RoleSpec, outputs: dict[str, bytes], index: dict) -> None:
    for relative_path, payload in outputs.items():
        path = root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
    index_path = root / INDEX_ROOT / f"{role.role}_motion_v003_index.json"
    index_path.parent.mkdir(parents=True, exist_ok=True)
    index_path.write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def check_role(root: Path, role: RoleSpec, outputs: dict[str, bytes], index: dict) -> list[str]:
    errors = []
    for relative_path, payload in outputs.items():
        path = root / relative_path
        if not path.is_file() or path.read_bytes() != payload:
            errors.append(f"Output '{relative_path}' is not deterministic.")
    index_path = root / INDEX_ROOT / f"{role.role}_motion_v003_index.json"
    expected = json.dumps(index, ensure_ascii=False, indent=2) + "\n"
    if not index_path.is_file() or index_path.read_text(encoding="utf-8") != expected:
        errors.append(f"{role.role} v003 index is not deterministic.")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default=".", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    errors = []
    try:
        for role in ROLES:
            outputs, index = build_role(root, role)
            if args.check:
                errors.extend(check_role(root, role, outputs, index))
            else:
                write_role(root, role, outputs, index)
        if errors:
            print("\n".join(errors), file=sys.stderr)
            return 1
        print("Character motion v003 check passed." if args.check else "Character motion v003 build completed.")
        return 0
    except (BuildError, OSError, json.JSONDecodeError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
