"""Drives the submission build in a real browser and records what a reviewer would see.

The evidence capture tool for the monster animation review is byte-hashed by
``Docs/AI_Usage/edits/monster_directional_animation_live_review_v002.json``, so this script
imports its browser plumbing instead of editing it.

What it verifies, in order:

1. The published player loads from a plain static server.
2. The title screen renders and advances on one trusted click.
3. The first room renders its start prompt and begins on the next click.
4. Movement input reaches the game, so the frames keep changing.

Usage::

    python Tools/verify_submission_run.py --build Builds/Overbless_Web --port 8100 \
        --output-directory Evidence/Verification/submission-run

Exits non-zero with a message when any step fails.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional

TOOLS_DIRECTORY = Path(__file__).resolve().parent
if str(TOOLS_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIRECTORY))

import capture_webgl_visuals as browser  # noqa: E402  (path shim above is required)

STEP_HOLD_SECONDS = 2.0
LOAD_TIMEOUT_SECONDS = 240.0


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Verify the submission WebGL build in a browser.")
    parser.add_argument("--build", default="Builds/Overbless_Web", type=Path)
    parser.add_argument("--port", default=8100, type=int)
    parser.add_argument("--viewport", default="1920x1080", type=browser.parse_viewport)
    parser.add_argument("--output-directory", default=Path("Evidence/Verification/submission-run"), type=Path)
    parser.add_argument("--debug-port", default=9345, type=int)
    parser.add_argument("--chrome")
    return parser.parse_args()


def wait_for_player(client: browser.DevToolsClient, session_id: str, label: str) -> None:
    deadline = time.monotonic() + LOAD_TIMEOUT_SECONDS
    while time.monotonic() < deadline:
        if browser.evaluate(client, session_id, browser.LOADING_BAR_HIDDEN) is True:
            return

        time.sleep(0.5)

    raise browser.CaptureError(f"{label} never finished loading.")


def hold_click(client: browser.DevToolsClient, session_id: str, x: int, y: int, hold: float = 0.18) -> None:
    """Presses, waits, then releases, so at least one rendered frame observes the button down."""
    for event_type in ("mousePressed", "mouseReleased"):
        client.call(
            "Input.dispatchMouseEvent",
            {
                "type": event_type,
                "x": x,
                "y": y,
                "button": "left",
                "buttons": 1 if event_type == "mousePressed" else 0,
                "clickCount": 1,
            },
            session_id=session_id,
        )
        if event_type == "mousePressed":
            time.sleep(hold)


def main() -> int:
    arguments = parse_arguments()
    repository_root = TOOLS_DIRECTORY.parent
    build_directory = (repository_root / arguments.build).resolve()
    if not (build_directory / "index.html").is_file():
        print(f"error: '{arguments.build}' holds no built player.", file=sys.stderr)
        return 2

    width, height = arguments.viewport
    output_directory = (repository_root / arguments.output_directory).resolve()
    output_directory.mkdir(parents=True, exist_ok=True)

    surface = browser.Surface(label="submission", directory=build_directory, port=arguments.port)
    chrome_path = browser.resolve_chrome(arguments.chrome)
    profile = output_directory / "_chrome-profile"
    steps: List[Dict[str, Any]] = []
    frames: List[Dict[str, Any]] = []
    chrome: Optional[Any] = None

    browser.start_server(surface, repository_root)
    try:
        chrome = browser.launch_chrome(chrome_path, arguments.debug_port, profile, width, height)
        websocket_url = browser.fetch_websocket_url(arguments.debug_port)
        client = browser.DevToolsClient(websocket_url)
        try:
            target = client.call("Target.createTarget", {"url": "about:blank"})
            session = client.call(
                "Target.attachToTarget",
                {"targetId": target["targetId"], "flatten": True},
            )
            session_id = session["sessionId"]
            client.call(
                "Emulation.setDeviceMetricsOverride",
                {"width": width, "height": height, "deviceScaleFactor": 1, "mobile": False},
                session_id=session_id,
            )
            client.call("Page.navigate", {"url": surface.url}, session_id=session_id)
            wait_for_player(client, session_id, "The submission player")

            geometry = browser.evaluate(client, session_id, browser.CANVAS_GEOMETRY)
            if geometry is None:
                raise browser.CaptureError("The submission player exposed no unity canvas.")

            steps.append(
                {
                    "step": "load",
                    "detail": f"canvas {geometry[0]}x{geometry[1]} css / {geometry[2]}x{geometry[3]} backing",
                }
            )

            centre_x = max(1, width // 2)
            centre_y = max(1, height // 2)
            plan = [
                ("01-title", "the title screen before any input", None),
                ("02-first-room-gate", "the first room waiting for its trusted gesture", "click"),
                ("03-first-room-started", "the first room after the gesture", "click"),
                ("04-after-movement", "the first room after a movement burst", "keys"),
            ]

            previous: Optional[Path] = None
            for name, description, action in plan:
                if action == "click":
                    hold_click(client, session_id, centre_x, centre_y)
                elif action == "keys":
                    for code, key, virtual_key, hold in browser.MOVEMENT_KEYS:
                        browser.dispatch_key(client, session_id, code, key, virtual_key, hold)

                time.sleep(STEP_HOLD_SECONDS)
                path = output_directory / f"submission-{width}x{height}-{name}.png"
                browser.write_screenshot(client, session_id, path)
                detail = browser.measure_frame_detail(path) or {}
                frames.append(
                    {
                        "name": name,
                        "description": description,
                        "path": str(path.relative_to(repository_root)).replace("\\", "/"),
                        "bytes": path.stat().st_size,
                        "sha256": browser.sha256_of(path),
                        "changeFromPreviousFrame": browser.measure_frame_change(previous, path),
                        "detail": detail,
                    }
                )
                previous = path

            transcript = {
                "schema": "overbless.submission-run-verification/v1",
                "recordedUtc": browser.utc_now(),
                "chrome": browser.read_chrome_version(arguments.debug_port),
                "url": surface.url,
                "viewport": f"{width}x{height}",
                "steps": steps,
                "frames": frames,
            }
            transcript_path = output_directory / "submission-run.json"
            transcript_path.write_text(json.dumps(transcript, indent=2, sort_keys=True) + "\n", encoding="utf-8")

            distinct = {frame["sha256"] for frame in frames}
            print(f"captured {len(frames)} frames, {len(distinct)} distinct")
            for frame in frames:
                change = frame["changeFromPreviousFrame"]
                change_text = "n/a" if change is None else f"{change:.4f}"
                print(f"  {frame['name']:<22} change={change_text:<8} {frame['sha256'][:16]}")

            if len(distinct) != len(frames):
                print("error: two captured steps are byte-identical, so a transition did not happen.", file=sys.stderr)
                return 1

            print(f"transcript: {transcript_path.relative_to(repository_root)}")
            return 0
        finally:
            client.close()
    finally:
        if chrome is not None:
            chrome.terminate()
            try:
                chrome.wait(timeout=15)
            except Exception:
                chrome.kill()

        browser.stop_server(surface)


if __name__ == "__main__":
    sys.exit(main())
