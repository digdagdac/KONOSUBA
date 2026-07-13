#!/usr/bin/env python3
"""Build deterministic eight-direction M1 character animation atlases."""

from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from PIL import Image

from process_m1_art import ProcessingError, load_foreground, scaled_width

CELL_SIZE = 128
MAX_FRAMES = 6
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
SOURCE_ROOT = Path("Docs/AI_Usage/sources/m1_animation_v001")
OUTPUT_ROOT = Path("Assets/_Project/Art/M1Production/Characters/Animation")
INDEX_PATH = Path("Docs/AI_Usage/generations/m1_directional_animation_index_v001.json")
FRAME_LETTERS = "abcdef"
MIRRORED_DIAGONALS = {"southwest": "southeast", "northwest": "northeast"}


@dataclass(frozen=True)
class StateSpec:
    name: str
    frames: int
    fps: float
    loop: bool


@dataclass(frozen=True)
class CharacterSpec:
    role: str
    content_height: int
    south_path: Path
    north_source: str
    east_source: str
    states: tuple[StateSpec, ...]


PLAYER_STATES = (
    StateSpec("idle", 4, 4.0, True),
    StateSpec("move", 6, 10.0, True),
    StateSpec("dash", 4, 14.0, False),
    StateSpec("bless_cast", 6, 8.0, True),
    StateSpec("hit", 3, 12.0, False),
    StateSpec("death", 6, 8.0, False),
)
MAJOR_ENEMY_STATES = (
    StateSpec("idle", 4, 4.0, True),
    StateSpec("move", 6, 9.0, True),
    StateSpec("attack_charge", 6, 8.0, True),
    StateSpec("attack_execute", 4, 14.0, False),
    StateSpec("recover", 4, 7.0, False),
    StateSpec("hit", 3, 12.0, False),
    StateSpec("death", 6, 8.0, False),
)
MINION_STATES = (
    StateSpec("idle", 4, 4.0, True),
    StateSpec("move", 6, 10.0, True),
    StateSpec("basic_attack", 4, 12.0, False),
    StateSpec("hit", 3, 12.0, False),
    StateSpec("death", 6, 8.0, False),
)
CHARACTERS = (
    CharacterSpec(
        "player",
        126,
        Path("Assets/_Project/Art/M1Production/Characters/chr_player_idle_south_a_v001.png"),
        "player_idle_north_source.png",
        "player_idle_east_source.png",
        PLAYER_STATES,
    ),
    CharacterSpec(
        "dasher",
        126,
        Path("Assets/_Project/Art/M1Production/Characters/chr_dasher_idle_south_a_v001.png"),
        "dasher_idle_north_source.png",
        "dasher_idle_east_source.png",
        MAJOR_ENEMY_STATES,
    ),
    CharacterSpec(
        "archer",
        126,
        Path("Assets/_Project/Art/M1Production/Characters/chr_archer_idle_south_a_v001.png"),
        "archer_idle_north_source.png",
        "archer_idle_east_source.png",
        MAJOR_ENEMY_STATES,
    ),
    CharacterSpec(
        "minion",
        70,
        Path("Assets/_Project/Art/M1Production/Characters/chr_minion_idle_south_a_v001.png"),
        "minion_idle_north_source.png",
        "minion_idle_east_source.png",
        MINION_STATES,
    ),
)


def parse_arguments(argv: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repo_root", nargs="?", type=Path, default=Path.cwd())
    return parser.parse_args(argv)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_transparent(path: Path) -> Image.Image:
    if not path.is_file():
        raise ProcessingError(f"missing approved production sprite: {path}")
    with Image.open(path) as source:
        source.load()
        rgba = source.convert("RGBA")
    if rgba.size != (CELL_SIZE, CELL_SIZE):
        raise ProcessingError(f"production key pose must be 128x128: {path}")
    if rgba.getchannel("A").getbbox() is None:
        raise ProcessingError(f"production key pose is empty: {path}")
    return rgba


def normalize_generated(path: Path, content_height: int) -> Image.Image:
    _, foreground, bbox = load_foreground(path)
    left, top, right, bottom = bbox
    crop = foreground.crop((left, top, right, bottom))
    width = scaled_width(crop.width, crop.height, content_height, path)
    resized = crop.resize((width, content_height), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (CELL_SIZE, CELL_SIZE), (0, 0, 0, 0))
    canvas.alpha_composite(resized, ((CELL_SIZE - width) // 2, CELL_SIZE - content_height))
    return canvas


def translate(image: Image.Image, x: int, y: int) -> Image.Image:
    canvas = Image.new("RGBA", image.size, (0, 0, 0, 0))
    canvas.alpha_composite(image, (x, y))
    return canvas


def compose_right_diagonal(vertical: Image.Image, east: Image.Image) -> Image.Image:
    """Combine cardinal poses through integer shifts and opaque pixel compositing only."""
    diagonal = translate(vertical, -2, 1)
    diagonal.alpha_composite(translate(east, 2, -1))
    return diagonal


def validate_diagonal_bases(bases: dict[str, Image.Image]) -> None:
    """Ensure diagonal poses are distinct from cardinals and exact horizontal mirrors."""
    cardinal_frames = {bases[direction].tobytes() for direction in DIRECTIONS[:4]}
    for direction in DIRECTIONS[4:]:
        if bases[direction].tobytes() in cardinal_frames:
            raise ProcessingError(f"diagonal key pose duplicates a cardinal source: {direction}")

    for mirrored, source in MIRRORED_DIAGONALS.items():
        expected = bases[source].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        if bases[mirrored].tobytes() != expected.tobytes():
            raise ProcessingError(f"diagonal key poses are not symmetric: {source}/{mirrored}")


def resize_bottom_center(image: Image.Image, width_scale: float, height_scale: float) -> Image.Image:
    bbox = image.getchannel("A").getbbox()
    if bbox is None:
        raise ProcessingError("cannot resize an empty animation frame")
    crop = image.crop(bbox)
    width = max(1, min(CELL_SIZE, round(crop.width * width_scale)))
    height = max(1, min(CELL_SIZE, round(crop.height * height_scale)))
    resized = crop.resize((width, height), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", image.size, (0, 0, 0, 0))
    canvas.alpha_composite(resized, ((CELL_SIZE - width) // 2, CELL_SIZE - height))
    return canvas


def tint(image: Image.Image, red_add: int, green_add: int, blue_add: int) -> Image.Image:
    pixels = bytearray(image.tobytes())
    for index in range(0, len(pixels), 4):
        if pixels[index + 3] == 0:
            continue
        pixels[index] = min(255, pixels[index] + red_add)
        pixels[index + 1] = min(255, pixels[index + 1] + green_add)
        pixels[index + 2] = min(255, pixels[index + 2] + blue_add)
    return Image.frombytes("RGBA", image.size, bytes(pixels))


def rotate_death(image: Image.Image, angle: float, direction: str) -> Image.Image:
    compact = resize_bottom_center(image, 0.86, 0.86)
    signed_angle = -angle if direction in {"south", "east"} else angle
    rotated = compact.rotate(
        signed_angle,
        resample=Image.Resampling.NEAREST,
        expand=False,
        center=(CELL_SIZE // 2, CELL_SIZE - 8),
        fillcolor=(0, 0, 0, 0),
    )
    return translate(rotated, 0, min(0, -round(angle / 30.0)))


def render_frame(base: Image.Image, state: str, frame: int, count: int, direction: str) -> Image.Image:
    if state == "idle":
        return translate(base, 0, (0, -1, -2, -1)[frame])
    if state == "move":
        x_offsets = (-1, 0, 1, 1, 0, -1)
        y_offsets = (0, -2, -1, 0, -2, -1)
        return translate(base, x_offsets[frame], y_offsets[frame])
    if state == "dash":
        scales = ((1.00, 1.00), (1.06, 0.96), (1.10, 0.93), (1.04, 0.97))
        return resize_bottom_center(base, *scales[frame])
    if state == "bless_cast":
        strengths = (0, 10, 24, 32, 18, 6)
        lifted = translate(base, 0, (0, -1, -2, -3, -2, -1)[frame])
        return tint(lifted, 0, strengths[frame], strengths[frame])
    if state == "attack_charge":
        width = (1.00, 1.02, 1.04, 1.06, 1.04, 1.02)[frame]
        charged = resize_bottom_center(base, width, 1.0 - (width - 1.0) * 0.5)
        return tint(charged, frame * 3, frame, 0)
    if state in {"attack_execute", "basic_attack"}:
        scales = ((1.00, 1.00), (1.08, 0.95), (1.12, 0.92), (1.04, 0.98))
        return resize_bottom_center(base, *scales[frame])
    if state == "recover":
        scales = ((1.08, 0.94), (1.04, 0.97), (0.98, 1.02), (1.00, 1.00))
        return resize_bottom_center(base, *scales[frame])
    if state == "hit":
        offsets = (-2, 2, -1)
        return tint(translate(base, offsets[frame], -1), 55, 0, 0)
    if state == "death":
        return rotate_death(base, (0, 12, 25, 40, 58, 75)[frame], direction)
    raise ProcessingError(f"unsupported animation state: {state}")


def validate_frame(image: Image.Image, label: str) -> tuple[int, int, int, int]:
    if image.mode != "RGBA" or image.size != (CELL_SIZE, CELL_SIZE):
        raise ProcessingError(f"invalid frame shape for {label}")
    if not set(image.getchannel("A").tobytes()).issubset({0, 255}):
        raise ProcessingError(f"non-binary alpha in {label}")
    bbox = image.getchannel("A").getbbox()
    if bbox is None:
        raise ProcessingError(f"empty frame: {label}")
    return bbox


def build_character(repo_root: Path, spec: CharacterSpec) -> dict:
    south_path = repo_root / spec.south_path
    north_path = repo_root / SOURCE_ROOT / spec.north_source
    east_path = repo_root / SOURCE_ROOT / spec.east_source
    bases = {
        "south": load_transparent(south_path),
        "north": normalize_generated(north_path, spec.content_height),
        "east": normalize_generated(east_path, spec.content_height),
    }
    bases["west"] = bases["east"].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    bases["southeast"] = compose_right_diagonal(bases["south"], bases["east"])
    bases["southwest"] = bases["southeast"].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    bases["northeast"] = compose_right_diagonal(bases["north"], bases["east"])
    bases["northwest"] = bases["northeast"].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    validate_diagonal_bases(bases)
    atlas_width = CELL_SIZE * MAX_FRAMES * len(DIRECTIONS)
    atlas_height = CELL_SIZE * len(spec.states)
    atlas = Image.new("RGBA", (atlas_width, atlas_height), (0, 0, 0, 0))
    state_records = []
    for state_index, state in enumerate(spec.states):
        direction_records = {}
        for direction_index, direction in enumerate(DIRECTIONS):
            frames = []
            unique_frames = set()
            for frame_index in range(state.frames):
                sprite_name = (
                    f"chr_{spec.role}_{state.name}_{direction}_{FRAME_LETTERS[frame_index]}_v001"
                )
                source_direction = MIRRORED_DIAGONALS.get(direction, direction)
                frame = render_frame(
                    bases[source_direction],
                    state.name,
                    frame_index,
                    state.frames,
                    source_direction,
                )
                if source_direction != direction:
                    frame = frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                bbox = validate_frame(frame, sprite_name)
                x = (direction_index * MAX_FRAMES + frame_index) * CELL_SIZE
                y = state_index * CELL_SIZE
                atlas.alpha_composite(frame, (x, y))
                frame_hash = hashlib.sha256(frame.tobytes()).hexdigest()
                unique_frames.add(frame_hash)
                frames.append(
                    {
                        "name": sprite_name,
                        "frame": frame_index,
                        "rect": [x, y, CELL_SIZE, CELL_SIZE],
                        "opaque_bbox": list(bbox),
                        "pixel_sha256": frame_hash,
                    }
                )
            if len(unique_frames) < 2:
                raise ProcessingError(f"animation has no visible motion: {spec.role}/{state.name}/{direction}")
            direction_records[direction] = frames
        state_records.append(
            {
                "name": state.name,
                "fps": state.fps,
                "loop": state.loop,
                "directions": direction_records,
            }
        )

    output_relative = OUTPUT_ROOT / f"chr_{spec.role}_animation_atlas_v001.png"
    output_path = repo_root / output_relative
    output_path.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(output_path, format="PNG", optimize=False, compress_level=9)
    if atlas.getchannel("A").getbbox() is None:
        raise ProcessingError(f"empty atlas: {output_relative}")
    return {
        "role": spec.role,
        "content_height": spec.content_height,
        "atlas_file": output_relative.as_posix(),
        "atlas_size": [atlas_width, atlas_height],
        "atlas_sha256": sha256(output_path),
        "source_files": {
            "south": spec.south_path.as_posix(),
            "north": (SOURCE_ROOT / spec.north_source).as_posix(),
            "east": (SOURCE_ROOT / spec.east_source).as_posix(),
            "west": "derived:horizontal-mirror-east",
            "southeast": "derived:pixel-composite(south@-2,+1;east@+2,-1)",
            "southwest": "derived:horizontal-mirror-southeast",
            "northeast": "derived:pixel-composite(north@-2,+1;east@+2,-1)",
            "northwest": "derived:horizontal-mirror-northeast",
        },
        "source_sha256": {
            "south": sha256(south_path),
            "north": sha256(north_path),
            "east": sha256(east_path),
        },
        "states": state_records,
    }


def main(argv: Iterable[str] | None = None) -> int:
    arguments = parse_arguments(argv)
    repo_root = arguments.repo_root.resolve()
    records = [build_character(repo_root, spec) for spec in CHARACTERS]
    index = {
        "schema": "overbless.m1-directional-animation-index/v1",
        "created_at": "2026-07-13",
        "cell_size": CELL_SIZE,
        "max_frames_per_direction": MAX_FRAMES,
        "directions": list(DIRECTIONS),
        "characters": records,
    }
    index_path = repo_root / INDEX_PATH
    index_path.parent.mkdir(parents=True, exist_ok=True)
    index_path.write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for record in records:
        print(f"{record['atlas_file']} {record['atlas_sha256']}")
    print(f"{INDEX_PATH.as_posix()} {sha256(index_path)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
