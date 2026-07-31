#!/usr/bin/env python3
"""Build and verify the five local-unsealed M2 runtime visual sprites deterministically."""

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


CANVAS_SIZE = 128
SOURCE_ROOT = Path("Docs/AI_Usage/sources")
OUTPUT_ROOT = Path("Assets/_Project/Art/M2Production")
INDEX_PATH = Path("Docs/AI_Usage/generations/m2_runtime_visual_index_v002.json")
RUNTIME_AUTHORIZATION = "local-unsealed-only"
APPROVAL_PATH = "Docs/Decisions/M2_IMPLEMENTATION_APPROVAL.json"
CREATED_UTC = "2026-07-30T09:36:59Z"


@dataclass(frozen=True)
class RuntimeVisualSpec:
    name: str
    source_path: Path
    output_path: Path
    max_content_size: int
    bottom_aligned: bool
    classification: str
    intended_binding: str
    grid_columns: int | None = None
    grid_rows: int | None = None
    grid_column: int | None = None
    grid_row: int | None = None


SPECS = (
    RuntimeVisualSpec(
        "static_world_pillar",
        SOURCE_ROOT / "m2_runtime_visual_v002/static_world_pillar_source.png",
        OUTPUT_ROOT / "Environment/env_static_world_pillar_south_a_v002.png",
        118,
        True,
        "m2-runtime-static-non-damaging-world-cover",
        "M2 WorldPillar SpriteRenderer only",
    ),
    RuntimeVisualSpec(
        "echo_bless_icon",
        SOURCE_ROOT / "m2_assets_v001/echo_vfx_sheet_source.png",
        OUTPUT_ROOT / "UI/ui_icon_bless_echo_a_v002.png",
        96,
        False,
        "m2-runtime-echo-blessing-ui",
        "M2 EchoCard icon only",
        4,
        2,
        0,
        0,
    ),
    RuntimeVisualSpec(
        "echo_status_icon",
        SOURCE_ROOT / "m2_assets_v001/echo_vfx_sheet_source.png",
        OUTPUT_ROOT / "UI/ui_icon_echo_status_a_v002.png",
        48,
        False,
        "m2-runtime-echo-status-ui",
        "M2 enemy Echo status indicator only",
        4,
        2,
        1,
        0,
    ),
    RuntimeVisualSpec(
        "echo_double_silhouette",
        SOURCE_ROOT / "m2_assets_v001/echo_vfx_sheet_source.png",
        OUTPUT_ROOT / "VFX/vfx_echo_double_silhouette_a_v002.png",
        112,
        False,
        "m2-runtime-echo-double-silhouette-vfx",
        "M2 EchoProjectileVisual projectile renderer only",
        4,
        2,
        2,
        0,
    ),
    RuntimeVisualSpec(
        "echo_line_telegraph",
        SOURCE_ROOT / "m2_assets_v001/echo_vfx_sheet_source.png",
        OUTPUT_ROOT / "VFX/vfx_echo_line_telegraph_a_v002.png",
        120,
        False,
        "m2-runtime-echo-line-telegraph-vfx",
        "M2 EchoProjectileVisual PendingLine renderer only",
        4,
        2,
        3,
        0,
    ),
)


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
        help="verify the existing deterministic outputs and index without writing files",
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


def load_static_png(path: Path, label: str, require_rgba: bool = False) -> Image.Image:
    if not path.is_file():
        raise ProcessingError(f"missing PNG: {path}")

    try:
        with Image.open(path) as opened:
            if opened.format != "PNG":
                raise ProcessingError(f"{label} must be a PNG: {path}")
            if opened.mode not in {"RGB", "RGBA"}:
                raise ProcessingError(f"{label} must use RGB or RGBA pixels: {path}")
            if getattr(opened, "n_frames", 1) != 1:
                raise ProcessingError(f"{label} must be a static PNG: {path}")
            if opened.width < 1 or opened.height < 1:
                raise ProcessingError(f"{label} has invalid dimensions: {path}")
            opened.verify()
        with Image.open(path) as opened:
            opened.load()
            if require_rgba and opened.mode != "RGBA":
                raise ProcessingError(f"{label} must be encoded as RGBA: {path}")
            return opened.convert("RGBA")
    except UnidentifiedImageError as error:
        raise ProcessingError(f"invalid PNG: {path}") from error
    except OSError as error:
        raise ProcessingError(f"invalid PNG: {path}") from error


def remove_sampled_magenta(image: Image.Image, label: str) -> Image.Image:
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


def crop_echo_cell(source: Image.Image, spec: RuntimeVisualSpec) -> tuple[Image.Image, list[int]]:
    if (
        spec.grid_columns is None
        or spec.grid_rows is None
        or spec.grid_column is None
        or spec.grid_row is None
    ):
        raise ProcessingError(f"missing Echo grid declaration: {spec.name}")

    width, height = source.size
    if width % spec.grid_columns != 0 or height % spec.grid_rows != 0:
        raise ProcessingError(
            f"Echo sheet dimensions must divide evenly into {spec.grid_columns}x{spec.grid_rows} cells: {width}x{height}"
        )

    left = width * spec.grid_column // spec.grid_columns
    top = height * spec.grid_row // spec.grid_rows
    right = width * (spec.grid_column + 1) // spec.grid_columns
    bottom = height * (spec.grid_row + 1) // spec.grid_rows
    inset_x = max(1, (right - left) // 12)
    inset_y = max(1, (bottom - top) // 12)
    rect = [left + inset_x, top + inset_y, right - inset_x, bottom - inset_y]
    if rect[0] >= rect[2] or rect[1] >= rect[3]:
        raise ProcessingError(f"Echo source cell inset is empty: {spec.name}")
    return source.crop(tuple(rect)), rect


def fit_to_canvas(foreground: Image.Image, max_content_size: int, bottom_aligned: bool) -> tuple[Image.Image, list[int]]:
    opaque_bounds = foreground.getchannel("A").getbbox()
    if opaque_bounds is None:
        raise ProcessingError("cannot fit empty foreground")

    crop = foreground.crop(opaque_bounds)
    scale = min(max_content_size / crop.width, max_content_size / crop.height)
    width = max(1, min(CANVAS_SIZE, round(crop.width * scale)))
    height = max(1, min(CANVAS_SIZE, round(crop.height * scale)))
    resized = crop.resize((width, height), Image.Resampling.NEAREST)
    resized_bounds = resized.getchannel("A").getbbox()
    if resized_bounds is None:
        raise ProcessingError("nearest-neighbor resize removed the entire foreground")
    resized = resized.crop(resized_bounds)
    width, height = resized.size

    canvas = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (0, 0, 0, 0))
    x = (CANVAS_SIZE - width) // 2
    y = CANVAS_SIZE - height if bottom_aligned else (CANVAS_SIZE - height) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas, [x, y, width, height]


def validate_sprite(image: Image.Image, label: str, bottom_aligned: bool) -> list[int]:
    if image.mode != "RGBA" or image.size != (CANVAS_SIZE, CANVAS_SIZE):
        raise ProcessingError(f"{label} must be a {CANVAS_SIZE}x{CANVAS_SIZE} RGBA sprite")
    alpha = image.getchannel("A")
    opaque_bounds = alpha.getbbox()
    if opaque_bounds is None:
        raise ProcessingError(f"{label} is empty")
    if not set(alpha.tobytes()).issubset({0, 255}):
        raise ProcessingError(f"{label} alpha is not binary")
    if bottom_aligned and opaque_bounds[3] != CANVAS_SIZE:
        raise ProcessingError(f"{label} is not bottom-aligned")
    return list(opaque_bounds)


def encode_png(image: Image.Image) -> bytes:
    encoded = io.BytesIO()
    image.save(encoded, format="PNG", optimize=False, compress_level=9)
    return encoded.getvalue()


def source_bounds_in_sheet(cell_bounds: tuple[int, int, int, int], rect: list[int]) -> list[int]:
    return [
        rect[0] + cell_bounds[0],
        rect[1] + cell_bounds[1],
        rect[0] + cell_bounds[2],
        rect[1] + cell_bounds[3],
    ]


def render_spec(spec: RuntimeVisualSpec, source: Image.Image) -> tuple[Image.Image, dict]:
    source_rect: list[int] | None = None
    if spec.grid_columns is None:
        foreground = remove_sampled_magenta(source, spec.name)
        source_opaque_bounds = list(foreground.getchannel("A").getbbox() or ())
    else:
        cell, source_rect = crop_echo_cell(source, spec)
        foreground = remove_sampled_magenta(cell, spec.name)
        cell_opaque_bounds = foreground.getchannel("A").getbbox()
        if cell_opaque_bounds is None:
            raise ProcessingError(f"empty Echo source cell: {spec.name}")
        source_opaque_bounds = source_bounds_in_sheet(cell_opaque_bounds, source_rect)

    sprite, placement = fit_to_canvas(foreground, spec.max_content_size, spec.bottom_aligned)
    opaque_bounds = validate_sprite(sprite, spec.output_path.as_posix(), spec.bottom_aligned)
    record = {
        "name": spec.name,
        "output_path": spec.output_path.as_posix(),
        "source_path": spec.source_path.as_posix(),
        "source_size": list(source.size),
        "source_opaque_bounds": source_opaque_bounds,
        "placement": placement,
        "opaque_bounds": opaque_bounds,
        "opaque_foot_y": opaque_bounds[3],
        "size": [CANVAS_SIZE, CANVAS_SIZE],
        "alpha": "binary",
        "classification": spec.classification,
        "intended_binding": spec.intended_binding,
        "runtime_authorization": RUNTIME_AUTHORIZATION,
    }
    if source_rect is not None:
        record.update(
            {
                "source_grid": [spec.grid_columns, spec.grid_rows],
                "source_cell": [spec.grid_column, spec.grid_row],
                "source_rect": source_rect,
                "source_cell_inset_divisor": 12,
            }
        )
    return sprite, record


def build_expected(repository_root: Path) -> tuple[list[tuple[RuntimeVisualSpec, bytes]], dict]:
    loaded_sources: dict[Path, Image.Image] = {}
    source_hashes: dict[Path, str] = {}
    rendered: list[tuple[RuntimeVisualSpec, bytes]] = []
    records = []

    for spec in SPECS:
        source = loaded_sources.get(spec.source_path)
        if source is None:
            source_path = repository_root / spec.source_path
            source = load_static_png(source_path, f"source for {spec.name}")
            loaded_sources[spec.source_path] = source
            source_hashes[spec.source_path] = sha256_file(source_path)
        sprite, record = render_spec(spec, source)
        png_bytes = encode_png(sprite)
        record["source_sha256"] = source_hashes[spec.source_path]
        record["output_sha256"] = sha256_bytes(png_bytes)
        rendered.append((spec, png_bytes))
        records.append(record)

    source_records = [
        {
            "path": source_path.as_posix(),
            "sha256": source_hashes[source_path],
        }
        for source_path in sorted(source_hashes, key=lambda path: path.as_posix())
    ]
    index = {
        "schema": "overbless.m2-runtime-visual-index/v2",
        "version": "v002",
        "created_utc": CREATED_UTC,
        "approval": APPROVAL_PATH,
        "runtime_authorization": RUNTIME_AUTHORIZATION,
        "m2_entry_gate_status": "not-evaluated",
        "canvas_size": [CANVAS_SIZE, CANVAS_SIZE],
        "declared_output_paths": [spec.output_path.as_posix() for spec in SPECS],
        "source_files": source_records,
        "sprites": records,
    }
    return rendered, index


def canonical_json(document: dict) -> bytes:
    return (json.dumps(document, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def reject_undeclared_files(repository_root: Path) -> None:
    declared_outputs = {(repository_root / spec.output_path).resolve() for spec in SPECS}
    production_root = repository_root / OUTPUT_ROOT
    if not production_root.exists():
        return
    if not production_root.is_dir():
        raise ProcessingError(f"M2 production root is not a directory: {production_root}")
    for path in sorted(production_root.rglob("*")):
        if path.is_dir() or path.suffix == ".meta":
            continue
        if path.resolve() not in declared_outputs:
            raise ProcessingError(f"undeclared M2 runtime visual output: {path}")


def check_output(path: Path, expected_bytes: bytes, spec: RuntimeVisualSpec) -> None:
    image = load_static_png(path, f"output for {spec.name}", require_rgba=True)
    validate_sprite(image, path.as_posix(), spec.bottom_aligned)
    actual_bytes = path.read_bytes()
    if actual_bytes != expected_bytes:
        raise ProcessingError(f"output hash or deterministic encoding drift: {path}")


def check(repository_root: Path, rendered: list[tuple[RuntimeVisualSpec, bytes]], expected_index: dict) -> None:
    reject_undeclared_files(repository_root)
    for spec, expected_bytes in rendered:
        check_output(repository_root / spec.output_path, expected_bytes, spec)

    index_path = repository_root / INDEX_PATH
    if not index_path.is_file():
        raise ProcessingError(f"missing runtime visual index: {index_path}")
    try:
        actual_index = json.loads(index_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ProcessingError(f"invalid runtime visual index: {index_path}") from error
    if actual_index != expected_index:
        raise ProcessingError(f"runtime visual index metadata drift: {index_path}")
    if index_path.read_bytes() != canonical_json(expected_index):
        raise ProcessingError(f"runtime visual index deterministic encoding drift: {index_path}")


def write_outputs(repository_root: Path, rendered: list[tuple[RuntimeVisualSpec, bytes]], index: dict) -> None:
    reject_undeclared_files(repository_root)
    temporary_paths: list[Path] = []
    try:
        for spec, content in rendered:
            output_path = repository_root / spec.output_path
            output_path.parent.mkdir(parents=True, exist_ok=True)
            temporary_path = output_path.with_name(f".{output_path.name}.tmp")
            temporary_path.write_bytes(content)
            temporary_paths.append(temporary_path)

        index_path = repository_root / INDEX_PATH
        index_path.parent.mkdir(parents=True, exist_ok=True)
        index_temporary_path = index_path.with_name(f".{index_path.name}.tmp")
        index_temporary_path.write_bytes(canonical_json(index))
        temporary_paths.append(index_temporary_path)

        for spec, _ in rendered:
            output_path = repository_root / spec.output_path
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

    rendered, index = build_expected(repository_root)
    if arguments.check:
        check(repository_root, rendered, index)
        print("M2 runtime visual check passed: 5 outputs and index are deterministic.")
        return 0

    write_outputs(repository_root, rendered, index)
    for spec, content in rendered:
        print(f"{spec.output_path.as_posix()} {sha256_bytes(content)}")
    print(f"{INDEX_PATH.as_posix()} {sha256_bytes(canonical_json(index))}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ProcessingError) as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
