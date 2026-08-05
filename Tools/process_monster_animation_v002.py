#!/usr/bin/env python3
"""Build deterministic v002 directional monster animation atlases from authored sheets."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from PIL import Image, UnidentifiedImageError

from process_m1_art import ProcessingError, is_magenta_background, sampled_corner_colour


CELL_SIZE = 128
SOURCE_SHEET_SIZE = (1536, 1024)
SOURCE_GRID_COLUMNS = 8
SOURCE_GRID_ROWS = 5
ATLAS_MAX_FRAMES = 8
V001_MAX_FRAMES = 6
FRAME_LETTERS = "abcdefgh"
CREATED_UTC = "2026-07-30T00:00:00Z"
SOURCE_ROOT = Path("Docs/AI_Usage/sources/monster_animation_v002")
OUTPUT_ROOT = Path("Assets/_Project/Art/M1Production/Characters/Animation")
INDEX_PATH = Path("Docs/AI_Usage/generations/monster_directional_animation_index_v002.json")

DIRECT_DIRECTIONS = ("south", "north", "east", "southeast", "northeast")
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
MIRROR_SOURCES = {
    "west": "east",
    "southwest": "southeast",
    "northwest": "northeast",
}
# The original east and southeast bow poses overlap the nominal 192px source columns:
# their bow tips reach into the following column while the following pose starts
# after a small magenta gap.  Shift only the affected extraction windows right
# by 20px so each pose keeps its own bow instead of inheriting a neighbour tip.
SOURCE_RECT_OVERRIDES = {
    ("archer", direction, "attack_execute", frame_index):
        (20 + frame_index * 192, 614, 212 + frame_index * 192, 819)
    for direction in ("east", "southeast")
    for frame_index in range(3)
}

EXPECTED_AUTHORED_FRAME_COUNT = 360
EXPECTED_DERIVED_FRAME_COUNT = 216
EXPECTED_INHERITED_FRAME_COUNT = 312


@dataclass(frozen=True)
class StateSpec:
    name: str
    frames: int
    fps: float
    loop: bool
    source_row: int | None


@dataclass(frozen=True)
class RoleSpec:
    role: str
    content_height: int
    v001_atlas_size: tuple[int, int]
    v001_inherited_rows: tuple[tuple[str, int], ...]


@dataclass(frozen=True)
class LoadedSheet:
    path: Path
    relative_path: Path
    image: Image.Image
    source_mode: str
    source_sha256: str
    corner_colour: tuple[int, int, int]


ANIMATION_STATES = (
    StateSpec("idle", 4, 4.0, True, None),
    # The Walk row contains a coherent four-pose character cycle. The source
    # Run row changes each role's silhouette and palette, so it cannot represent
    # the same character in motion. Reuse Walk at 1.5x cadence for Run until a
    # coherent authored Run row is approved.
    StateSpec("walk", 4, 6.0, True, 0),
    StateSpec("run", 4, 9.0, True, 0),
    StateSpec("attack_charge", 6, 8.0, False, 2),
    StateSpec("attack_execute", 6, 14.0, False, 3),
    StateSpec("recover", 4, 7.0, False, 4),
    StateSpec("hit", 3, 12.0, False, None),
    StateSpec("death", 6, 8.0, False, None),
)
STATE_BY_NAME = {state.name: state for state in ANIMATION_STATES}

ROLE_SPECS = (
    RoleSpec(
        "dasher",
        126,
        (CELL_SIZE * V001_MAX_FRAMES * len(DIRECTIONS), CELL_SIZE * 7),
        (("idle", 0), ("hit", 5), ("death", 6)),
    ),
    RoleSpec(
        "archer",
        126,
        (CELL_SIZE * V001_MAX_FRAMES * len(DIRECTIONS), CELL_SIZE * 7),
        (("idle", 0), ("hit", 5), ("death", 6)),
    ),
    RoleSpec(
        "minion",
        70,
        (CELL_SIZE * V001_MAX_FRAMES * len(DIRECTIONS), CELL_SIZE * 5),
        (("idle", 0), ("hit", 3), ("death", 4)),
    ),
)


MODIFICATION_ALLOWLIST = {
    "permitted_operations": [
        "equal_grid_cell_partition",
        "pure_or_near_chroma_magenta_to_binary_alpha",
        "tight_alpha_bounds_crop",
        "proportional_nearest_neighbor_normalization",
        "bottom_center_placement_on_128px_canvas",
        "exact_horizontal_pixel_mirror_for_west_southwest_and_northwest_only",
        "exact_active_frame_pixel_copy_from_declared_v001_idle_hit_and_death_rows_only",
        "approved_source_window_override_for_archer_east_and_southeast_attack_execute_frames_a_to_c_only",
        "transparent_canvas_atlas_packing",
        "deterministic_png_encoding",
    ],
    "forbidden_operations": [
        "derive_a_direct_direction",
        "derive_a_non_declared_direction",
        "idle_transform",
        "diagonal_composite",
        "interpolation",
        "rotation",
        "translation_after_bottom_center_placement",
        "scale_after_normalization",
        "palette_or_tint_adjustment",
        "blur_or_antialiasing",
        "synthesize_or_repeat_missing_active_frames",
        "use_v001_move_or_attack_rows",
    ],
}


def fps_for_role(role_spec: RoleSpec, state: StateSpec) -> float:
    if role_spec.role == "minion" and state.name == "attack_execute":
        return 24.0
    return state.fps


def parse_arguments(argv: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "repo_root",
        nargs="?",
        type=Path,
        default=Path.cwd(),
        help="repository root (defaults to the current directory)",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="rebuild expected outputs in memory and verify byte-for-byte drift without writing",
    )
    return parser.parse_args(argv)


def sha256_bytes(content: bytes) -> str:
    return hashlib.sha256(content).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def canonical_json(document: dict) -> bytes:
    return (json.dumps(document, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def encode_png(image: Image.Image) -> bytes:
    encoded = io.BytesIO()
    image.save(encoded, format="PNG", optimize=False, compress_level=9)
    return encoded.getvalue()


def source_sheet_relative_path(role: str, direction: str) -> Path:
    return SOURCE_ROOT / f"{role}_{direction}_motion_sheet_source.png"


def v001_atlas_relative_path(role: str) -> Path:
    return OUTPUT_ROOT / f"chr_{role}_animation_atlas_v001.png"


def v002_atlas_relative_path(role: str) -> Path:
    return OUTPUT_ROOT / f"chr_{role}_animation_atlas_v002.png"


def frame_name(role: str, state: str, direction: str, frame_index: int) -> str:
    return f"chr_{role}_{state}_{direction}_{FRAME_LETTERS[frame_index]}_v002"


def validate_source_inventory(repository_root: Path) -> None:
    source_root = repository_root / SOURCE_ROOT
    if not source_root.is_dir():
        raise ProcessingError(f"missing monster animation v002 source directory: {source_root}")

    expected_names = {
        source_sheet_relative_path(role.role, direction).name
        for role in ROLE_SPECS
        for direction in DIRECT_DIRECTIONS
    }
    actual_names = {
        path.name
        for path in source_root.iterdir()
        if path.is_file() and path.suffix.lower() == ".png"
    }
    missing_names = sorted(expected_names - actual_names)
    unexpected_names = sorted(actual_names - expected_names)
    if missing_names:
        raise ProcessingError(
            "missing required monster animation v002 source sheets: " + ", ".join(missing_names)
        )
    if unexpected_names:
        raise ProcessingError(
            "unexpected monster animation v002 source sheets: " + ", ".join(unexpected_names)
        )


def load_png(path: Path, label: str, require_rgba: bool = False) -> tuple[Image.Image, str]:
    if not path.is_file():
        raise ProcessingError(f"missing PNG for {label}: {path}")

    try:
        with Image.open(path) as opened:
            if opened.format != "PNG":
                raise ProcessingError(f"{label} must be a PNG: {path}")
            if opened.mode not in {"RGB", "RGBA"}:
                raise ProcessingError(f"{label} must use RGB or RGBA pixels: {path}")
            if require_rgba and opened.mode != "RGBA":
                raise ProcessingError(f"{label} must be encoded as RGBA: {path}")
            if getattr(opened, "n_frames", 1) != 1:
                raise ProcessingError(f"{label} must be a static PNG: {path}")
            if opened.width < 1 or opened.height < 1:
                raise ProcessingError(f"{label} has invalid dimensions: {path}")
            source_mode = opened.mode
            opened.verify()
        with Image.open(path) as opened:
            opened.load()
            return opened.convert("RGBA"), source_mode
    except UnidentifiedImageError as error:
        raise ProcessingError(f"invalid PNG for {label}: {path}") from error
    except OSError as error:
        raise ProcessingError(f"invalid PNG for {label}: {path}") from error


def load_source_sheet(repository_root: Path, role: str, direction: str) -> LoadedSheet:
    relative_path = source_sheet_relative_path(role, direction)
    path = repository_root / relative_path
    image, source_mode = load_png(path, f"{role}/{direction} source sheet")
    if image.size != SOURCE_SHEET_SIZE:
        raise ProcessingError(
            f"{role}/{direction} source sheet must be {SOURCE_SHEET_SIZE[0]}x{SOURCE_SHEET_SIZE[1]}, "
            f"got {image.width}x{image.height}: {path}"
        )
    return LoadedSheet(
        path,
        relative_path,
        image,
        source_mode,
        sha256_file(path),
        sampled_corner_colour(image),
    )


def grid_rect(column: int, row: int) -> tuple[int, int, int, int]:
    if not 0 <= column < SOURCE_GRID_COLUMNS or not 0 <= row < SOURCE_GRID_ROWS:
        raise ProcessingError(f"source grid cell is outside 8x5 bounds: column={column}, row={row}")
    width, height = SOURCE_SHEET_SIZE
    return (
        width * column // SOURCE_GRID_COLUMNS,
        height * row // SOURCE_GRID_ROWS,
        width * (column + 1) // SOURCE_GRID_COLUMNS,
        height * (row + 1) // SOURCE_GRID_ROWS,
    )


def authored_source_rect(
    role: str, direction: str, state: StateSpec, frame_index: int
) -> tuple[int, int, int, int]:
    override = SOURCE_RECT_OVERRIDES.get((role, direction, state.name, frame_index))
    return override if override is not None else grid_rect(frame_index, state.source_row)


def source_window_override_records() -> list[dict]:
    return [
        {
            "role": role,
            "direction": direction,
            "state": state,
            "frame": frame_index,
            "source_rect": list(rect),
        }
        for (role, direction, state, frame_index), rect in sorted(SOURCE_RECT_OVERRIDES.items())
    ]


def remove_chroma_magenta(
    image: Image.Image, corner_colour: tuple[int, int, int], label: str
) -> Image.Image:
    source = image.tobytes()
    foreground = bytearray(len(source))
    for offset in range(0, len(source), 4):
        red, green, blue, alpha = source[offset : offset + 4]
        if alpha and not is_magenta_background(red, green, blue, corner_colour):
            foreground[offset : offset + 4] = bytes((red, green, blue, 255))

    result = Image.frombytes("RGBA", image.size, bytes(foreground))
    if result.getchannel("A").getbbox() is None:
        raise ProcessingError(f"empty active source cell after chroma removal: {label}")
    return result


def normalize_bottom_center(
    foreground: Image.Image, content_height: int, label: str
) -> Image.Image:
    bounds = foreground.getchannel("A").getbbox()
    if bounds is None:
        raise ProcessingError(f"cannot normalize empty frame: {label}")
    left, top, right, bottom = bounds
    crop = foreground.crop((left, top, right, bottom))
    normalized_height = content_height
    normalized_width = (
        crop.width * normalized_height + crop.height // 2
    ) // crop.height
    if normalized_width > CELL_SIZE:
        normalized_width = CELL_SIZE
        normalized_height = max(
            1,
            (crop.height * normalized_width + crop.width // 2) // crop.width,
        )
    if not 1 <= normalized_width <= CELL_SIZE or not 1 <= normalized_height <= CELL_SIZE:
        raise ProcessingError(
            f"normalized {label} would be {normalized_width}x{normalized_height}, "
            f"outside {CELL_SIZE}x{CELL_SIZE}"
        )
    resized = crop.resize(
        (normalized_width, normalized_height),
        Image.Resampling.NEAREST,
    )
    resized_bounds = resized.getchannel("A").getbbox()
    if resized_bounds is None:
        raise ProcessingError(f"normalized frame became empty: {label}")
    resized = resized.crop(resized_bounds)
    canvas = Image.new("RGBA", (CELL_SIZE, CELL_SIZE), (0, 0, 0, 0))
    canvas.alpha_composite(
        resized,
        ((CELL_SIZE - resized.width) // 2, CELL_SIZE - resized.height),
    )
    return canvas


def validate_frame(
    image: Image.Image,
    label: str,
    *,
    require_bottom_aligned: bool = True,
) -> list[int]:
    if image.mode != "RGBA" or image.size != (CELL_SIZE, CELL_SIZE):
        raise ProcessingError(f"{label} must be a {CELL_SIZE}x{CELL_SIZE} RGBA frame")
    alpha = image.getchannel("A")
    if not set(alpha.tobytes()).issubset({0, 255}):
        raise ProcessingError(f"{label} alpha is not binary")
    bounds = alpha.getbbox()
    if bounds is None:
        raise ProcessingError(f"{label} is empty")
    if not (0 <= bounds[0] < bounds[2] <= CELL_SIZE and 0 <= bounds[1] < bounds[3] <= CELL_SIZE):
        raise ProcessingError(f"{label} opaque bounds are outside the frame: {bounds}")
    if require_bottom_aligned and bounds[3] != CELL_SIZE:
        raise ProcessingError(f"{label} is not bottom-aligned: opaque foot is {bounds[3]}")
    return list(bounds)


def validate_frame_variation(frames: list[Image.Image], label: str) -> None:
    if len({frame.tobytes() for frame in frames}) < 2:
        raise ProcessingError(f"{label} has no visible frame variation")


def validate_loop_seam(frames: list[Image.Image], label: str) -> None:
    first_bounds = frames[0].getchannel("A").getbbox()
    last_bounds = frames[-1].getchannel("A").getbbox()
    if first_bounds is None or last_bounds is None:
        raise ProcessingError(f"{label} loop seam has an empty endpoint")
    overlap_left = max(first_bounds[0], last_bounds[0])
    overlap_right = min(first_bounds[2], last_bounds[2])
    if overlap_left >= overlap_right:
        raise ProcessingError(f"{label} loop seam endpoints do not overlap horizontally")
    first_center_twice = first_bounds[0] + first_bounds[2]
    last_center_twice = last_bounds[0] + last_bounds[2]
    if abs(first_center_twice - last_center_twice) > CELL_SIZE // 2:
        raise ProcessingError(f"{label} loop seam endpoints shift more than 32 pixels")


def validate_loop_cycle_has_no_held_duplicate(frames: list[Image.Image], label: str) -> None:
    if len({frame.tobytes() for frame in frames}) != len(frames):
        raise ProcessingError(f"{label} repeats a frame inside an active loop")


def validate_mirror(source: Image.Image, mirrored: Image.Image, label: str) -> None:
    expected = source.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    if mirrored.tobytes() != expected.tobytes():
        raise ProcessingError(f"{label} is not an exact horizontal pixel mirror")


def validate_atlas(image: Image.Image, label: str) -> None:
    expected_size = (CELL_SIZE * ATLAS_MAX_FRAMES * len(DIRECTIONS), CELL_SIZE * len(ANIMATION_STATES))
    if image.mode != "RGBA" or image.size != expected_size:
        raise ProcessingError(f"{label} must be a {expected_size[0]}x{expected_size[1]} RGBA atlas")
    if not set(image.getchannel("A").tobytes()).issubset({0, 255}):
        raise ProcessingError(f"{label} atlas alpha is not binary")
    if image.getchannel("A").getbbox() is None:
        raise ProcessingError(f"{label} atlas is empty")


def extract_authored_frame(
    sheet: LoadedSheet, role_spec: RoleSpec, state: StateSpec, frame_index: int, direction: str
) -> tuple[Image.Image, dict]:
    if state.source_row is None:
        raise ProcessingError(f"{state.name} is not an authored source-sheet state")
    rect = authored_source_rect(role_spec.role, direction, state, frame_index)
    label = f"{role_spec.role}/{direction}/{state.name}/{FRAME_LETTERS[frame_index]}"
    cell = sheet.image.crop(rect)
    foreground = remove_chroma_magenta(cell, sheet.corner_colour, label)
    cell_bounds = foreground.getchannel("A").getbbox()
    if cell_bounds is None:
        raise ProcessingError(f"empty active source cell: {label}")
    frame = normalize_bottom_center(foreground, role_spec.content_height, label)
    opaque_bounds = validate_frame(frame, label)
    source_bounds = [
        rect[0] + cell_bounds[0],
        rect[1] + cell_bounds[1],
        rect[0] + cell_bounds[2],
        rect[1] + cell_bounds[3],
    ]
    return frame, {
        "source_path": sheet.relative_path.as_posix(),
        "source_sha256": sheet.source_sha256,
        "source_mode": sheet.source_mode,
        "source_grid": [SOURCE_GRID_COLUMNS, SOURCE_GRID_ROWS],
        "source_cell": [frame_index, state.source_row],
        "source_rect": list(rect),
        "source_window_override": (role_spec.role, direction, state.name, frame_index) in SOURCE_RECT_OVERRIDES,
        "source_opaque_bounds": source_bounds,
        "opaque_bounds": opaque_bounds,
    }


def load_inherited_frames(repository_root: Path, role_spec: RoleSpec) -> tuple[dict, dict]:
    relative_path = v001_atlas_relative_path(role_spec.role)
    path = repository_root / relative_path
    atlas, source_mode = load_png(path, f"{role_spec.role} v001 inherited atlas", require_rgba=True)
    if atlas.size != role_spec.v001_atlas_size:
        raise ProcessingError(
            f"{role_spec.role} v001 atlas must be {role_spec.v001_atlas_size[0]}x"
            f"{role_spec.v001_atlas_size[1]}, got {atlas.width}x{atlas.height}: {path}"
        )
    if not set(atlas.getchannel("A").tobytes()).issubset({0, 255}):
        raise ProcessingError(f"{role_spec.role} v001 atlas alpha is not binary: {path}")

    source_sha256 = sha256_file(path)
    inherited_frames: dict[str, dict[str, list[tuple[Image.Image, dict]]]] = {}
    for state_name, source_row in role_spec.v001_inherited_rows:
        state = STATE_BY_NAME[state_name]
        by_direction: dict[str, list[tuple[Image.Image, dict]]] = {}
        for direction_index, direction in enumerate(DIRECTIONS):
            frames = []
            images = []
            for frame_index in range(state.frames):
                source_rect = [
                    (direction_index * V001_MAX_FRAMES + frame_index) * CELL_SIZE,
                    source_row * CELL_SIZE,
                    CELL_SIZE,
                    CELL_SIZE,
                ]
                frame = atlas.crop(
                    (
                        source_rect[0],
                        source_rect[1],
                        source_rect[0] + CELL_SIZE,
                        source_rect[1] + CELL_SIZE,
                    )
                ).copy()
                opaque_bounds = validate_frame(
                    frame,
                    f"{role_spec.role} v001 {state_name}/{direction}/{FRAME_LETTERS[frame_index]}",
                    require_bottom_aligned=False,
                )
                images.append(frame)
                frames.append(
                    (
                        frame,
                        {
                            "atlas_path": relative_path.as_posix(),
                            "atlas_sha256": source_sha256,
                            "atlas_mode": source_mode,
                            "state": state_name,
                            "direction": direction,
                            "frame": frame_index,
                            "rect": source_rect,
                            "opaque_bounds": opaque_bounds,
                            "pixel_sha256": sha256_bytes(frame.tobytes()),
                        },
                    )
                )
            validate_frame_variation(images, f"{role_spec.role} inherited {state_name}/{direction}")
            if state.loop:
                validate_loop_seam(images, f"{role_spec.role} inherited {state_name}/{direction}")
            by_direction[direction] = frames
        inherited_frames[state_name] = by_direction

    source_record = {
        "path": relative_path.as_posix(),
        "sha256": source_sha256,
        "mode": source_mode,
        "size": list(role_spec.v001_atlas_size),
        "topology": {
            "cell_size": CELL_SIZE,
            "max_frames_per_direction": V001_MAX_FRAMES,
            "directions": list(DIRECTIONS),
            "rows_used": [
                {"state": state_name, "source_row": source_row}
                for state_name, source_row in role_spec.v001_inherited_rows
            ],
            "rows_forbidden": ["move", "attack_charge", "attack_execute", "recover", "basic_attack"],
        },
    }
    return inherited_frames, source_record


def build_authored_frames(
    repository_root: Path, role_spec: RoleSpec, sheets: dict[str, LoadedSheet]
) -> dict[str, dict[str, list[tuple[Image.Image, dict]]]]:
    authored: dict[str, dict[str, list[tuple[Image.Image, dict]]]] = {}
    for direction in DIRECT_DIRECTIONS:
        by_state: dict[str, list[tuple[Image.Image, dict]]] = {}
        sheet = sheets[direction]
        for state in ANIMATION_STATES:
            if state.source_row is None:
                continue
            frames = [
                extract_authored_frame(sheet, role_spec, state, frame_index, direction)
                for frame_index in range(state.frames)
            ]
            images = [frame for frame, _ in frames]
            validate_frame_variation(images, f"{role_spec.role} authored {state.name}/{direction}")
            if state.loop:
                validate_loop_seam(images, f"{role_spec.role} authored {state.name}/{direction}")
                validate_loop_cycle_has_no_held_duplicate(
                    images,
                    f"{role_spec.role} authored {state.name}/{direction}",
                )
            by_state[state.name] = frames
        authored[direction] = by_state
    return authored


def atlas_rect(state_index: int, direction_index: int, frame_index: int) -> list[int]:
    return [
        (direction_index * ATLAS_MAX_FRAMES + frame_index) * CELL_SIZE,
        state_index * CELL_SIZE,
        CELL_SIZE,
        CELL_SIZE,
    ]


def build_character(
    repository_root: Path, role_spec: RoleSpec, sheets: dict[str, LoadedSheet]
) -> tuple[Path, bytes, dict, list[dict], dict]:
    authored_frames = build_authored_frames(repository_root, role_spec, sheets)
    inherited_frames, inherited_source = load_inherited_frames(repository_root, role_spec)
    atlas = Image.new(
        "RGBA",
        (CELL_SIZE * ATLAS_MAX_FRAMES * len(DIRECTIONS), CELL_SIZE * len(ANIMATION_STATES)),
        (0, 0, 0, 0),
    )
    records: list[dict] = []

    for state_index, state in enumerate(ANIMATION_STATES):
        for direction_index, direction in enumerate(DIRECTIONS):
            for frame_index in range(state.frames):
                name = frame_name(role_spec.role, state.name, direction, frame_index)
                rect = atlas_rect(state_index, direction_index, frame_index)
                if state.source_row is not None:
                    if direction in DIRECT_DIRECTIONS:
                        frame, source_record = authored_frames[direction][state.name][frame_index]
                        classification = "authored"
                        mirror_source = None
                        lineage = {"authored_source": source_record}
                    else:
                        source_direction = MIRROR_SOURCES[direction]
                        source_frame, source_record = authored_frames[source_direction][state.name][frame_index]
                        frame = source_frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                        validate_mirror(
                            source_frame,
                            frame,
                            f"{role_spec.role}/{state.name}/{direction}/{FRAME_LETTERS[frame_index]}",
                        )
                        classification = "derived"
                        mirror_source = frame_name(
                            role_spec.role, state.name, source_direction, frame_index
                        )
                        lineage = {
                            "authored_source": source_record,
                            "derivation": "exact-horizontal-pixel-mirror",
                        }
                else:
                    frame, inherited_record = inherited_frames[state.name][direction][frame_index]
                    classification = "inherited"
                    mirror_source = None
                    lineage = {"inherited_v001": inherited_record}

                opaque_bounds = validate_frame(
                    frame,
                    name,
                    require_bottom_aligned=classification != "inherited",
                )
                atlas.alpha_composite(frame, (rect[0], rect[1]))
                records.append(
                    {
                        "name": name,
                        "role": role_spec.role,
                        "state": state.name,
                        "direction": direction,
                        "frame": frame_index,
                        "frame_letter": FRAME_LETTERS[frame_index],
                        "fps": fps_for_role(role_spec, state),
                        "loop": state.loop,
                        "frame_count": state.frames,
                        "rect": rect,
                        "opaque_bounds": opaque_bounds,
                        "opaque_foot_y": opaque_bounds[3],
                        "alpha": "binary",
                        "pixel_sha256": sha256_bytes(frame.tobytes()),
                        "classification": classification,
                        "mirror_source": mirror_source,
                        "lineage": lineage,
                    }
                )

    validate_atlas(atlas, f"{role_spec.role} v002 atlas")
    for record in records:
        x, y, width, height = record["rect"]
        packed_frame = atlas.crop((x, y, x + width, y + height))
        if sha256_bytes(packed_frame.tobytes()) != record["pixel_sha256"]:
            raise ProcessingError(f"atlas packing changed frame pixels: {record['name']}")

    output_relative_path = v002_atlas_relative_path(role_spec.role)
    encoded_atlas = encode_png(atlas)
    character_record = {
        "role": role_spec.role,
        "content_height": role_spec.content_height,
        "atlas_path": output_relative_path.as_posix(),
        "atlas_size": list(atlas.size),
        "atlas_sha256": sha256_bytes(encoded_atlas),
        "inherited_v001_source": inherited_source,
        "states": [
            {
                "name": state.name,
                "frames": state.frames,
                "fps": fps_for_role(role_spec, state),
                "loop": state.loop,
            }
            for state in ANIMATION_STATES
        ],
        "frame_counts": {
            "authored": sum(record["classification"] == "authored" for record in records),
            "derived": sum(record["classification"] == "derived" for record in records),
            "inherited": sum(record["classification"] == "inherited" for record in records),
        },
    }
    return output_relative_path, encoded_atlas, character_record, records, inherited_source


def validate_classification_counts(frame_records: list[dict]) -> dict[str, int]:
    counts = {
        classification: sum(record["classification"] == classification for record in frame_records)
        for classification in ("authored", "derived", "inherited")
    }
    expected = {
        "authored": EXPECTED_AUTHORED_FRAME_COUNT,
        "derived": EXPECTED_DERIVED_FRAME_COUNT,
        "inherited": EXPECTED_INHERITED_FRAME_COUNT,
    }
    if counts != expected:
        raise ProcessingError(f"v002 frame classification count mismatch: expected {expected}, got {counts}")
    return counts


def build_expected(repository_root: Path) -> tuple[list[tuple[Path, bytes]], dict]:
    validate_source_inventory(repository_root)
    loaded_sheets: dict[str, dict[str, LoadedSheet]] = {}
    source_records = []
    for role_spec in ROLE_SPECS:
        sheets = {}
        for direction in DIRECT_DIRECTIONS:
            sheet = load_source_sheet(repository_root, role_spec.role, direction)
            sheets[direction] = sheet
            source_records.append(
                {
                    "role": role_spec.role,
                    "direction": direction,
                    "path": sheet.relative_path.as_posix(),
                    "sha256": sheet.source_sha256,
                    "mode": sheet.source_mode,
                    "size": list(sheet.image.size),
                    "grid": [SOURCE_GRID_COLUMNS, SOURCE_GRID_ROWS],
                }
            )
        loaded_sheets[role_spec.role] = sheets

    outputs: list[tuple[Path, bytes]] = []
    characters = []
    all_frame_records: list[dict] = []
    inherited_sources = []
    for role_spec in ROLE_SPECS:
        output_path, output_bytes, character_record, records, inherited_source = build_character(
            repository_root, role_spec, loaded_sheets[role_spec.role]
        )
        outputs.append((output_path, output_bytes))
        characters.append(character_record)
        all_frame_records.extend(records)
        inherited_sources.append(inherited_source)

    classification_counts = validate_classification_counts(all_frame_records)
    index = {
        "schema": "overbless.monster-directional-animation-index/v2",
        "version": "v002",
        "created_utc": CREATED_UTC,
        "source_contract": {
            "source_root": SOURCE_ROOT.as_posix(),
            "source_sheet_count": len(ROLE_SPECS) * len(DIRECT_DIRECTIONS),
            "source_sheet_naming": "{role}_{direction}_motion_sheet_source.png",
            "roles": [spec.role for spec in ROLE_SPECS],
            "direct_directions": list(DIRECT_DIRECTIONS),
            "source_sheet_size": list(SOURCE_SHEET_SIZE),
            "source_sheet_mode": ["RGB", "RGBA"],
            "source_grid": [SOURCE_GRID_COLUMNS, SOURCE_GRID_ROWS],
            "source_rows": ["walk", "run", "attack_charge", "attack_execute", "recover"],
        },
        "atlas_contract": {
            "atlas_size": [
                CELL_SIZE * ATLAS_MAX_FRAMES * len(DIRECTIONS),
                CELL_SIZE * len(ANIMATION_STATES),
            ],
            "cell_size": [CELL_SIZE, CELL_SIZE],
            "max_frames_per_direction": ATLAS_MAX_FRAMES,
            "directions": list(DIRECTIONS),
            "states": [
                {
                    "name": state.name,
                    "frames": state.frames,
                    "fps": (
                        {"default": state.fps, "minion": 24.0}
                        if state.name == "attack_execute"
                        else state.fps
                    ),
                    "loop": state.loop,
                    "source_row": state.source_row,
                }
                for state in ANIMATION_STATES
            ],
        },
        "direction_derivation": {
            "authored_direct_directions": list(DIRECT_DIRECTIONS),
            "derived_directions": MIRROR_SOURCES,
            "method": "exact horizontal pixel mirror after permitted normalization and bottom-center placement",
        },
        "modification_allowlist": MODIFICATION_ALLOWLIST,
        "source_sheets": source_records,
        "source_window_overrides": source_window_override_records(),
        "inherited_v001_sources": inherited_sources,
        "characters": characters,
        "frame_classification_counts": classification_counts,
        "frames": all_frame_records,
    }
    return outputs, index


def validate_output_atlas(path: Path, expected_bytes: bytes) -> None:
    image, _ = load_png(path, "v002 output atlas", require_rgba=True)
    validate_atlas(image, path.as_posix())
    actual_bytes = path.read_bytes()
    if actual_bytes != expected_bytes:
        raise ProcessingError(f"output hash or deterministic PNG encoding drift: {path}")


def check_outputs(repository_root: Path, outputs: list[tuple[Path, bytes]], expected_index: dict) -> None:
    for relative_path, expected_bytes in outputs:
        output_path = repository_root / relative_path
        if not output_path.is_file():
            raise ProcessingError(f"missing v002 output atlas: {output_path}")
        validate_output_atlas(output_path, expected_bytes)

    index_path = repository_root / INDEX_PATH
    if not index_path.is_file():
        raise ProcessingError(f"missing monster animation v002 index: {index_path}")
    try:
        actual_index = json.loads(index_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ProcessingError(f"invalid monster animation v002 index: {index_path}") from error
    if actual_index != expected_index:
        raise ProcessingError(f"monster animation v002 index metadata drift: {index_path}")
    if index_path.read_bytes() != canonical_json(expected_index):
        raise ProcessingError(f"monster animation v002 index deterministic encoding drift: {index_path}")


def write_outputs_atomically(
    repository_root: Path, outputs: list[tuple[Path, bytes]], index: dict
) -> None:
    temporary_paths: list[Path] = []
    try:
        for relative_path, content in outputs:
            output_path = repository_root / relative_path
            output_path.parent.mkdir(parents=True, exist_ok=True)
            temporary_path = output_path.with_name(f".{output_path.name}.tmp")
            temporary_path.write_bytes(content)
            temporary_paths.append(temporary_path)

        index_path = repository_root / INDEX_PATH
        index_path.parent.mkdir(parents=True, exist_ok=True)
        index_temporary_path = index_path.with_name(f".{index_path.name}.tmp")
        index_temporary_path.write_bytes(canonical_json(index))
        temporary_paths.append(index_temporary_path)

        for relative_path, _ in outputs:
            output_path = repository_root / relative_path
            temporary_path = output_path.with_name(f".{output_path.name}.tmp")
            temporary_path.replace(output_path)
            temporary_paths.remove(temporary_path)
        index_temporary_path.replace(index_path)
        temporary_paths.remove(index_temporary_path)
    finally:
        for temporary_path in temporary_paths:
            temporary_path.unlink(missing_ok=True)


def main(argv: Iterable[str] | None = None) -> int:
    arguments = parse_arguments(argv)
    repository_root = arguments.repo_root.resolve()
    if not repository_root.is_dir():
        raise ProcessingError(f"repository root does not exist or is not a directory: {repository_root}")

    outputs, index = build_expected(repository_root)
    if arguments.check:
        check_outputs(repository_root, outputs, index)
        print("Monster animation v002 check passed: 3 atlases and index are deterministic.")
        return 0

    write_outputs_atomically(repository_root, outputs, index)
    for relative_path, content in outputs:
        print(f"{relative_path.as_posix()} {sha256_bytes(content)}")
    print(f"{INDEX_PATH.as_posix()} {sha256_bytes(canonical_json(index))}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ProcessingError) as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
