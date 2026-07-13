#!/usr/bin/env python3
"""Build the approved offline M2 pixel-art resource package deterministically."""

from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from PIL import Image, UnidentifiedImageError

from process_m1_art import ProcessingError, is_magenta_background, sampled_corner_colour
from process_m1_animation import (
    CELL_SIZE,
    DIRECTIONS,
    FRAME_LETTERS,
    MAJOR_ENEMY_STATES,
    MAX_FRAMES,
    render_frame,
)

SOURCE_ROOT = Path("Docs/AI_Usage/sources/m2_assets_v001")
OUTPUT_ROOT = Path("Assets/_Project/Art/M2Preproduction")
INDEX_PATH = Path("Docs/AI_Usage/generations/m2_image_resource_index_v001.json")
MIRROR_SOURCES = {
    "west": "east",
    "southwest": "southeast",
    "northwest": "northeast",
}


@dataclass(frozen=True)
class GridAssetSpec:
    source_name: str
    columns: int
    rows: int
    column: int
    row: int
    output_relative_path: Path
    max_content_size: int = 112
    bottom_aligned: bool = False


GRID_ASSETS = (
    GridAssetSpec("echo_vfx_sheet_source.png", 4, 2, 0, 0, Path("UI/ui_icon_bless_echo_a_v001.png"), 96),
    GridAssetSpec("echo_vfx_sheet_source.png", 4, 2, 1, 0, Path("UI/ui_icon_echo_status_a_v001.png"), 48),
    GridAssetSpec("echo_vfx_sheet_source.png", 4, 2, 2, 0, Path("VFX/vfx_echo_double_silhouette_a_v001.png"), 112),
    GridAssetSpec("echo_vfx_sheet_source.png", 4, 2, 3, 0, Path("VFX/vfx_echo_line_telegraph_a_v001.png"), 120),
    GridAssetSpec("echo_vfx_sheet_source.png", 4, 2, 0, 1, Path("VFX/vfx_echo_ring_telegraph_a_v001.png"), 120),
    GridAssetSpec("echo_vfx_sheet_source.png", 4, 2, 1, 1, Path("VFX/vfx_echo_apply_burst_a_v001.png"), 112),
    GridAssetSpec("echo_vfx_sheet_source.png", 4, 2, 2, 1, Path("VFX/vfx_resonance_aura_a_v001.png"), 120),
    GridAssetSpec("echo_vfx_sheet_source.png", 4, 2, 3, 1, Path("VFX/vfx_resonance_arrival_sigil_a_v001.png"), 120),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 0, 0, Path("Environment/env_cliff_edge_tile_a_v001.png"), 128),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 1, 0, Path("Environment/env_cliff_inner_corner_tile_a_v001.png"), 128),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 2, 0, Path("Environment/env_destructible_pillar_intact_a_v001.png"), 118, True),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 3, 0, Path("Environment/env_destructible_pillar_damaged_a_v001.png"), 118, True),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 0, 1, Path("Environment/env_destructible_pillar_rubble_a_v001.png"), 112, True),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 1, 1, Path("Environment/env_spike_trap_inactive_a_v001.png"), 112, True),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 2, 1, Path("Environment/env_spike_trap_warning_a_v001.png"), 112, True),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 3, 1, Path("Environment/env_spike_trap_active_a_v001.png"), 118, True),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 0, 2, Path("Environment/env_broken_wall_rubble_a_v001.png"), 116, True),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 1, 2, Path("Environment/env_fractured_floor_tile_a_v001.png"), 128),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 2, 2, Path("Environment/env_final_room_floor_tile_a_v001.png"), 128),
    GridAssetSpec("environment_mechanics_sheet_source.png", 4, 3, 3, 2, Path("Environment/env_exit_open_pedestal_a_v001.png"), 116, True),
    GridAssetSpec("final_room_sheet_source.png", 4, 2, 0, 0, Path("Environment/env_final_portal_closed_a_v001.png"), 116, True),
    GridAssetSpec("final_room_sheet_source.png", 4, 2, 1, 0, Path("Environment/env_final_portal_opening_a_v001.png"), 116, True),
    GridAssetSpec("final_room_sheet_source.png", 4, 2, 2, 0, Path("Environment/env_final_portal_open_a_v001.png"), 116, True),
    GridAssetSpec("final_room_sheet_source.png", 4, 2, 3, 0, Path("UI/ui_icon_resonance_enemy_badge_a_v001.png"), 72),
    GridAssetSpec("final_room_sheet_source.png", 4, 2, 0, 1, Path("VFX/vfx_resonance_spawn_burst_a_v001.png"), 120),
    GridAssetSpec("final_room_sheet_source.png", 4, 2, 1, 1, Path("UI/ui_icon_final_objective_crest_a_v001.png"), 96),
    GridAssetSpec("final_room_sheet_source.png", 4, 2, 2, 1, Path("UI/ui_icon_run_victory_crest_a_v001.png"), 96),
    GridAssetSpec("final_room_sheet_source.png", 4, 2, 3, 1, Path("UI/ui_icon_run_defeat_crest_a_v001.png"), 96),
)

GOLEM_SOURCES = {
    "south": "golem_idle_south_source.png",
    "north": "golem_idle_north_source.png",
    "east": "golem_idle_east_source.png",
    "southeast": "golem_idle_southeast_source.png",
    "northeast": "golem_idle_northeast_source.png",
}


def parse_arguments(argv: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repo_root", nargs="?", type=Path, default=Path.cwd())
    return parser.parse_args(argv)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_png(path: Path) -> Image.Image:
    if not path.is_file():
        raise ProcessingError(f"missing M2 source PNG: {path}")
    try:
        with Image.open(path) as source:
            if source.format != "PNG" or getattr(source, "n_frames", 1) != 1:
                raise ProcessingError(f"M2 source must be a static PNG: {path}")
            source.load()
            return source.convert("RGBA")
    except UnidentifiedImageError as error:
        raise ProcessingError(f"invalid M2 source PNG: {path}") from error


def remove_chroma(image: Image.Image, label: str) -> Image.Image:
    corner_colour = sampled_corner_colour(image)
    source = image.tobytes()
    output = bytearray(len(source))
    for index in range(0, len(source), 4):
        red, green, blue, alpha = source[index : index + 4]
        if alpha and not is_magenta_background(red, green, blue, corner_colour):
            output[index : index + 4] = bytes((red, green, blue, 255))
    foreground = Image.frombytes("RGBA", image.size, bytes(output))
    if foreground.getchannel("A").getbbox() is None:
        raise ProcessingError(f"empty foreground after chroma removal: {label}")
    return foreground


def crop_grid_cell(source: Image.Image, spec: GridAssetSpec) -> tuple[Image.Image, list[int]]:
    width, height = source.size
    left = width * spec.column // spec.columns
    top = height * spec.row // spec.rows
    right = width * (spec.column + 1) // spec.columns
    bottom = height * (spec.row + 1) // spec.rows
    inset_divisor = 12 if spec.source_name == "echo_vfx_sheet_source.png" else 50
    inset_x = max(1, (right - left) // inset_divisor)
    inset_y = max(1, (bottom - top) // inset_divisor)
    rect = [left + inset_x, top + inset_y, right - inset_x, bottom - inset_y]
    return source.crop(tuple(rect)), rect


def fit_to_canvas(foreground: Image.Image, max_size: int, bottom_aligned: bool) -> tuple[Image.Image, list[int]]:
    bbox = foreground.getchannel("A").getbbox()
    if bbox is None:
        raise ProcessingError("cannot fit an empty M2 asset")
    crop = foreground.crop(bbox)
    scale = min(max_size / crop.width, max_size / crop.height)
    width = max(1, min(CELL_SIZE, round(crop.width * scale)))
    height = max(1, min(CELL_SIZE, round(crop.height * scale)))
    resized = crop.resize((width, height), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (CELL_SIZE, CELL_SIZE), (0, 0, 0, 0))
    x = (CELL_SIZE - width) // 2
    y = CELL_SIZE - height if bottom_aligned else (CELL_SIZE - height) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas, [x, y, width, height]


def validate_sprite(image: Image.Image, label: str) -> None:
    if image.mode != "RGBA" or image.size != (CELL_SIZE, CELL_SIZE):
        raise ProcessingError(f"invalid sprite shape: {label}")
    if not set(image.getchannel("A").tobytes()).issubset({0, 255}):
        raise ProcessingError(f"non-binary alpha: {label}")
    if image.getchannel("A").getbbox() is None:
        raise ProcessingError(f"empty sprite: {label}")


def process_grid_assets(repo_root: Path) -> list[dict]:
    loaded_sources: dict[str, Image.Image] = {}
    records = []
    for spec in GRID_ASSETS:
        source_path = repo_root / SOURCE_ROOT / spec.source_name
        source = loaded_sources.setdefault(spec.source_name, load_png(source_path))
        cell, source_rect = crop_grid_cell(source, spec)
        foreground = remove_chroma(cell, f"{spec.source_name}[{spec.column},{spec.row}]")
        sprite, placement = fit_to_canvas(foreground, spec.max_content_size, spec.bottom_aligned)
        validate_sprite(sprite, spec.output_relative_path.as_posix())
        output_relative = OUTPUT_ROOT / spec.output_relative_path
        output_path = repo_root / output_relative
        output_path.parent.mkdir(parents=True, exist_ok=True)
        sprite.save(output_path, format="PNG", optimize=False, compress_level=9)
        records.append(
            {
                "output_file": output_relative.as_posix(),
                "output_sha256": sha256(output_path),
                "source_file": (SOURCE_ROOT / spec.source_name).as_posix(),
                "source_sha256": sha256(source_path),
                "source_grid": [spec.columns, spec.rows],
                "source_cell": [spec.column, spec.row],
                "source_rect": source_rect,
                "placement": placement,
                "size": [CELL_SIZE, CELL_SIZE],
                "alpha": "binary",
            }
        )
    return records


def normalize_golem_source(path: Path) -> Image.Image:
    source = load_png(path)
    foreground = remove_chroma(source, path.as_posix())
    sprite, _ = fit_to_canvas(foreground, 126, True)
    validate_sprite(sprite, path.as_posix())
    return sprite


def build_golem_atlas(repo_root: Path) -> dict:
    bases = {
        direction: normalize_golem_source(repo_root / SOURCE_ROOT / file_name)
        for direction, file_name in GOLEM_SOURCES.items()
    }
    bases["west"] = bases["east"].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    bases["southwest"] = bases["southeast"].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    bases["northwest"] = bases["northeast"].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    cardinal_bytes = {bases[name].tobytes() for name in ("south", "north", "east", "west")}
    for diagonal in ("southeast", "southwest", "northeast", "northwest"):
        if bases[diagonal].tobytes() in cardinal_bytes:
            raise ProcessingError(f"Golem diagonal duplicates a cardinal pose: {diagonal}")

    atlas_width = CELL_SIZE * MAX_FRAMES * len(DIRECTIONS)
    atlas_height = CELL_SIZE * len(MAJOR_ENEMY_STATES)
    atlas = Image.new("RGBA", (atlas_width, atlas_height), (0, 0, 0, 0))
    state_records = []
    for state_index, state in enumerate(MAJOR_ENEMY_STATES):
        direction_records = {}
        for direction_index, direction in enumerate(DIRECTIONS):
            source_direction = MIRROR_SOURCES.get(direction, direction)
            frames = []
            unique_frames = set()
            render_direction = "east" if source_direction in {"east", "southeast", "northeast"} else source_direction
            for frame_index in range(state.frames):
                name = f"chr_golem_{state.name}_{direction}_{FRAME_LETTERS[frame_index]}_v001"
                frame = render_frame(
                    bases[source_direction],
                    state.name,
                    frame_index,
                    state.frames,
                    render_direction,
                )
                if source_direction != direction:
                    frame = frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
                validate_sprite(frame, name)
                x = (direction_index * MAX_FRAMES + frame_index) * CELL_SIZE
                y = state_index * CELL_SIZE
                atlas.alpha_composite(frame, (x, y))
                pixel_hash = hashlib.sha256(frame.tobytes()).hexdigest()
                unique_frames.add(pixel_hash)
                frames.append(
                    {
                        "name": name,
                        "frame": frame_index,
                        "rect": [x, y, CELL_SIZE, CELL_SIZE],
                        "pixel_sha256": pixel_hash,
                    }
                )
            if len(unique_frames) < 2:
                raise ProcessingError(f"Golem animation has no visible motion: {state.name}/{direction}")
            direction_records[direction] = frames
        state_records.append(
            {
                "name": state.name,
                "fps": state.fps,
                "loop": state.loop,
                "directions": direction_records,
            }
        )

    output_relative = OUTPUT_ROOT / "Characters/Animation/chr_golem_animation_atlas_v001.png"
    output_path = repo_root / output_relative
    output_path.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(output_path, format="PNG", optimize=False, compress_level=9)
    return {
        "role": "golem",
        "atlas_file": output_relative.as_posix(),
        "atlas_size": [atlas_width, atlas_height],
        "atlas_sha256": sha256(output_path),
        "active_sub_sprites": sum(state.frames for state in MAJOR_ENEMY_STATES) * len(DIRECTIONS),
        "directions": list(DIRECTIONS),
        "source_files": {
            direction: (SOURCE_ROOT / file_name).as_posix()
            for direction, file_name in GOLEM_SOURCES.items()
        }
        | {
            "west": "derived:horizontal-mirror-east",
            "southwest": "derived:horizontal-mirror-southeast",
            "northwest": "derived:horizontal-mirror-northeast",
        },
        "source_sha256": {
            direction: sha256(repo_root / SOURCE_ROOT / file_name)
            for direction, file_name in GOLEM_SOURCES.items()
        },
        "states": state_records,
    }


def main(argv: Iterable[str] | None = None) -> int:
    arguments = parse_arguments(argv)
    repo_root = arguments.repo_root.resolve()
    grid_records = process_grid_assets(repo_root)
    golem_record = build_golem_atlas(repo_root)
    index = {
        "schema": "overbless.m2-image-resource-index/v1",
        "created_at": "2026-07-13",
        "approval": "Docs/Decisions/M2_ASSET_PRODUCTION_APPROVAL.json",
        "runtime_binding": False,
        "cell_size": CELL_SIZE,
        "golem_animation": golem_record,
        "sprites": grid_records,
    }
    index_path = repo_root / INDEX_PATH
    index_path.parent.mkdir(parents=True, exist_ok=True)
    index_path.write_text(json.dumps(index, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{golem_record['atlas_file']} {golem_record['atlas_sha256']}")
    print(f"sprites {len(grid_records)}")
    print(f"{INDEX_PATH.as_posix()} {sha256(index_path)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
