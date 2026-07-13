#!/usr/bin/env python3
"""Convert M1 reference PNGs into deterministic Unity production sprites."""

from __future__ import annotations

import argparse
import hashlib
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from PIL import Image, UnidentifiedImageError


CANVAS_SIZE = 128
SOURCE_DIRECTORY = Path("Docs/AI_Usage/sources/m1_unity_v001")
OUTPUT_DIRECTORY = Path("Assets/_Project/Art/M1Production")
CORNER_DISTANCE_SQUARED = 64 * 64


class ProcessingError(RuntimeError):
    """Raised when an input or generated sprite does not meet the contract."""


@dataclass(frozen=True)
class AssetSpec:
    source_name: str
    output_relative_path: Path
    content_height: int | None
    bottom_aligned: bool = False
    is_tile: bool = False


ASSETS = (
    AssetSpec(
        "player_idle_south_source.png",
        OUTPUT_DIRECTORY / "Characters/chr_player_idle_south_a_v001.png",
        126,
        bottom_aligned=True,
    ),
    AssetSpec(
        "dasher_idle_south_source.png",
        OUTPUT_DIRECTORY / "Characters/chr_dasher_idle_south_a_v001.png",
        126,
        bottom_aligned=True,
    ),
    AssetSpec(
        "archer_idle_south_source.png",
        OUTPUT_DIRECTORY / "Characters/chr_archer_idle_south_a_v001.png",
        126,
        bottom_aligned=True,
    ),
    AssetSpec(
        "minion_idle_south_source.png",
        OUTPUT_DIRECTORY / "Characters/chr_minion_idle_south_a_v001.png",
        70,
        bottom_aligned=True,
    ),
    AssetSpec(
        "soul_pickup_source.png",
        OUTPUT_DIRECTORY / "Pickups/ui_icon_soul_pickup_a_v001.png",
        48,
    ),
    AssetSpec(
        "exit_closed_source.png",
        OUTPUT_DIRECTORY / "Environment/env_exit_closed_south_a_v001.png",
        106,
    ),
    AssetSpec(
        "blessing_haste_icon_source.png",
        OUTPUT_DIRECTORY / "UI/ui_icon_bless_haste_a_v001.png",
        96,
    ),
    AssetSpec(
        "blessing_giant_icon_source.png",
        OUTPUT_DIRECTORY / "UI/ui_icon_bless_giant_a_v001.png",
        96,
    ),
    AssetSpec(
        "dungeon_floor_tile_source.png",
        OUTPUT_DIRECTORY / "Environment/env_dungeon_floor_tile_a_v001.png",
        None,
        is_tile=True,
    ),
)


@dataclass(frozen=True)
class PreparedAsset:
    spec: AssetSpec
    source_size: tuple[int, int]
    foreground: Image.Image
    foreground_bbox: tuple[int, int, int, int]


def parse_arguments(argv: Iterable[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Process M1 reference art into Unity-ready 128px sprites."
    )
    parser.add_argument(
        "repo_root",
        nargs="?",
        type=Path,
        default=Path.cwd(),
        help="repository root (defaults to the current directory)",
    )
    return parser.parse_args(argv)


def sampled_corner_colour(image: Image.Image) -> tuple[int, int, int]:
    """Return a robust RGB reference from all corners of an RGB(A) image."""
    width, height = image.size
    corners = (
        image.getpixel((0, 0)),
        image.getpixel((width - 1, 0)),
        image.getpixel((0, height - 1)),
        image.getpixel((width - 1, height - 1)),
    )
    return tuple(
        (ordered[1] + ordered[2]) // 2
        for ordered in (sorted(colour[channel] for colour in corners) for channel in range(3))
    )


def is_magenta_background(
    red: int, green: int, blue: int, corner_colour: tuple[int, int, int]
) -> bool:
    """Classify the supplied RGB pixel as the magenta chroma background."""
    strong_magenta = (
        red >= 180
        and blue >= 180
        and green <= 120
        and red - green >= 100
        and blue - green >= 100
    )
    red_delta = red - corner_colour[0]
    green_delta = green - corner_colour[1]
    blue_delta = blue - corner_colour[2]
    near_corner_colour = (
        red_delta * red_delta
        + green_delta * green_delta
        + blue_delta * blue_delta
        <= CORNER_DISTANCE_SQUARED
    )
    return strong_magenta or near_corner_colour


def load_foreground(source_path: Path) -> tuple[tuple[int, int], Image.Image, tuple[int, int, int, int]]:
    """Load a PNG and convert its magenta background to binary transparency."""
    if not source_path.is_file():
        raise ProcessingError(f"missing source PNG: {source_path}")

    try:
        with Image.open(source_path) as source:
            if source.format != "PNG":
                raise ProcessingError(
                    f"wrong source format for {source_path}: expected PNG, got {source.format!r}"
                )
            if source.mode not in {"RGB", "RGBA"}:
                raise ProcessingError(
                    f"wrong source pixel format for {source_path}: expected RGB or RGBA, got {source.mode!r}"
                )
            if getattr(source, "n_frames", 1) != 1:
                raise ProcessingError(f"animated source PNG is not supported: {source_path}")
            source.load()
            rgba = source.convert("RGBA")
    except UnidentifiedImageError as error:
        raise ProcessingError(f"invalid PNG source: {source_path}") from error

    width, height = rgba.size
    if width < 1 or height < 1:
        raise ProcessingError(f"invalid source dimensions for {source_path}: {width}x{height}")

    corner_colour = sampled_corner_colour(rgba)
    foreground_bytes = bytearray(width * height * 4)
    rgba_bytes = rgba.tobytes()
    for destination in range(0, len(rgba_bytes), 4):
        red, green, blue, alpha = rgba_bytes[destination : destination + 4]
        if alpha and not is_magenta_background(red, green, blue, corner_colour):
            foreground_bytes[destination : destination + 4] = bytes((red, green, blue, 255))

    foreground = Image.frombytes("RGBA", rgba.size, bytes(foreground_bytes))
    foreground_bbox = foreground.getchannel("A").getbbox()
    if foreground_bbox is None:
        raise ProcessingError(f"empty foreground after chroma removal: {source_path}")
    return (width, height), foreground, foreground_bbox


def scaled_width(width: int, height: int, target_height: int, source_path: Path) -> int:
    """Scale proportionally with integer half-up rounding for repeatable sizing."""
    result = (width * target_height + height // 2) // height
    if not 1 <= result <= CANVAS_SIZE:
        raise ProcessingError(
            f"scaled foreground from {source_path} is {result}x{target_height}, which does not fit "
            f"a {CANVAS_SIZE}x{CANVAS_SIZE} canvas"
        )
    return result


def render_sprite(prepared: PreparedAsset) -> Image.Image:
    """Render a single character, icon, pickup, or exit on a transparent canvas."""
    spec = prepared.spec
    if spec.content_height is None:
        raise ProcessingError(f"missing content height for non-tile source: {spec.source_name}")

    left, top, right, bottom = prepared.foreground_bbox
    crop = prepared.foreground.crop((left, top, right, bottom))
    crop_width, crop_height = crop.size
    width = scaled_width(crop_width, crop_height, spec.content_height, Path(spec.source_name))
    resized = crop.resize((width, spec.content_height), Image.Resampling.NEAREST)

    x = (CANVAS_SIZE - width) // 2
    y = CANVAS_SIZE - spec.content_height if spec.bottom_aligned else (CANVAS_SIZE - spec.content_height) // 2
    canvas = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (0, 0, 0, 0))
    canvas.alpha_composite(resized, (x, y))
    return canvas


def synchronize_tile_edges(tile: Image.Image) -> None:
    """Copy left/top edge values onto right/bottom edges for exact repeat seams."""
    pixels = tile.load()
    for y in range(CANVAS_SIZE):
        pixels[CANVAS_SIZE - 1, y] = pixels[0, y]
    for x in range(CANVAS_SIZE):
        pixels[x, CANVAS_SIZE - 1] = pixels[x, 0]


def render_tile(prepared: PreparedAsset) -> Image.Image:
    """Crop a floor tile, scale it to fill the canvas, and make it opaque/tileable."""
    left, top, right, bottom = prepared.foreground_bbox
    crop = prepared.foreground.crop((left, top, right, bottom))
    tile = crop.resize((CANVAS_SIZE, CANVAS_SIZE), Image.Resampling.NEAREST).convert("RGB").convert("RGBA")
    synchronize_tile_edges(tile)
    return tile


def binary_alpha(image: Image.Image) -> bool:
    return set(image.getchannel("A").tobytes()).issubset({0, 255})


def validate_rendered_image(image: Image.Image, spec: AssetSpec) -> tuple[int, int, int, int]:
    """Validate required output dimensions, alpha, placement, and seam invariants."""
    if image.mode != "RGBA" or image.size != (CANVAS_SIZE, CANVAS_SIZE):
        raise ProcessingError(
            f"invalid generated image for {spec.output_relative_path}: expected 128x128 RGBA, "
            f"got {image.size} {image.mode}"
        )

    alpha = image.getchannel("A")
    bbox = alpha.getbbox()
    if bbox is None:
        raise ProcessingError(f"generated image is empty: {spec.output_relative_path}")

    if spec.is_tile:
        if alpha.getextrema() != (255, 255):
            raise ProcessingError(f"generated tile is not fully opaque: {spec.output_relative_path}")
        pixels = image.load()
        for y in range(CANVAS_SIZE):
            if pixels[0, y] != pixels[CANVAS_SIZE - 1, y]:
                raise ProcessingError(f"generated tile has a horizontal seam: {spec.output_relative_path}")
        for x in range(CANVAS_SIZE):
            if pixels[x, 0] != pixels[x, CANVAS_SIZE - 1]:
                raise ProcessingError(f"generated tile has a vertical seam: {spec.output_relative_path}")
        return bbox

    if not binary_alpha(image):
        raise ProcessingError(f"generated alpha is not binary: {spec.output_relative_path}")
    if spec.content_height is None or bbox[3] - bbox[1] != spec.content_height:
        raise ProcessingError(
            f"generated content height is invalid for {spec.output_relative_path}: "
            f"expected {spec.content_height}, got {bbox[3] - bbox[1]}"
        )
    if spec.bottom_aligned and bbox[3] != CANVAS_SIZE:
        raise ProcessingError(f"character is not bottom-aligned: {spec.output_relative_path}")
    return bbox


def validate_written_png(path: Path, spec: AssetSpec) -> tuple[int, int, int, int]:
    """Re-open a temporary output to detect malformed or incorrectly encoded PNGs."""
    try:
        with Image.open(path) as output:
            if output.format != "PNG":
                raise ProcessingError(f"invalid output format for {path}: {output.format!r}")
            output.verify()
        with Image.open(path) as output:
            output.load()
            return validate_rendered_image(output.convert("RGBA"), spec)
    except UnidentifiedImageError as error:
        raise ProcessingError(f"invalid generated PNG: {path}") from error


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as output:
        for block in iter(lambda: output.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def prepare_assets(repository_root: Path) -> tuple[PreparedAsset, ...]:
    prepared = []
    source_directory = repository_root / SOURCE_DIRECTORY
    for spec in ASSETS:
        source_size, foreground, foreground_bbox = load_foreground(source_directory / spec.source_name)
        prepared.append(PreparedAsset(spec, source_size, foreground, foreground_bbox))
    return tuple(prepared)


def main(argv: Iterable[str] | None = None) -> int:
    args = parse_arguments(argv)
    repository_root = args.repo_root.resolve()
    if not repository_root.is_dir():
        raise ProcessingError(f"repository root does not exist or is not a directory: {repository_root}")

    prepared_assets = prepare_assets(repository_root)
    rendered = []
    for prepared in prepared_assets:
        image = render_tile(prepared) if prepared.spec.is_tile else render_sprite(prepared)
        bbox = validate_rendered_image(image, prepared.spec)
        rendered.append((prepared, image, bbox))

    temporary_outputs: list[Path] = []
    try:
        for prepared, image, _ in rendered:
            output_path = repository_root / prepared.spec.output_relative_path
            output_path.parent.mkdir(parents=True, exist_ok=True)
            temporary_path = output_path.with_name(f".{output_path.name}.tmp")
            temporary_outputs.append(temporary_path)
            image.save(temporary_path, format="PNG", optimize=False, compress_level=9)
            validate_written_png(temporary_path, prepared.spec)

        for prepared, _, _ in rendered:
            output_path = repository_root / prepared.spec.output_relative_path
            temporary_path = output_path.with_name(f".{output_path.name}.tmp")
            temporary_path.replace(output_path)
            temporary_outputs.remove(temporary_path)
            checksum = sha256(output_path)
            with Image.open(output_path) as output:
                print(
                    f"{prepared.spec.source_name} -> {prepared.spec.output_relative_path.as_posix()}: "
                    f"source={prepared.source_size[0]}x{prepared.source_size[1]} "
                    f"output={output.width}x{output.height} "
                    f"opaque_bbox={output.convert('RGBA').getchannel('A').getbbox()} "
                    f"sha256={checksum}"
                )
    finally:
        for temporary_path in temporary_outputs:
            temporary_path.unlink(missing_ok=True)

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ProcessingError) as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(1)
