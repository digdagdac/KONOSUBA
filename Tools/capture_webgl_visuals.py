#!/usr/bin/env python3
"""Capture live gameplay evidence from local Unity Development WebGL builds.

The tool serves each build with ``Tools/serve_webgl.py``, drives headless Chrome
through the DevTools Protocol, sends a trusted pointer gesture plus a short
movement burst, and stores PNG screenshots together with a machine transcript.

Only the Python standard library is used for the protocol work so the capture
adds no third-party runtime dependency. Pillow is optional and is used solely to
quantify how much of each frame changed, which is how the transcript evidences
live rendering instead of a frozen canvas.

Example:
    python Tools/capture_webgl_visuals.py \
        --surface m1=Builds/M1_GuidedValidation_WebGL:8000 \
        --surface m2=Builds/M2_Rooms_WebGL:8001 \
        --viewport 1280x720 --viewport 1920x1080 \
        --output-directory Evidence/Verification \
        --transcript Evidence/Verification/monster-animation-v002-browser-automation.json
"""

from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
import json
import random
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence, Tuple

TRANSCRIPT_SCHEMA = "overbless.browser-automation-evidence/v1"
TRANSCRIPT_SCHEMA_VERSION = 1
CHROME_CANDIDATES = (
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
)
LOADING_BAR_HIDDEN = (
    '(() => { const bar = document.querySelector("#unity-loading-bar");'
    ' return bar !== null && bar.style.display === "none"; })()'
)
CANVAS_GEOMETRY = (
    '(() => { const c = document.querySelector("#unity-canvas"); if (c === null) { return null; }'
    " return [c.clientWidth, c.clientHeight, c.width, c.height, window.devicePixelRatio]; })()"
)
ANIMATION_FRAME_BUCKET = (
    "new Promise((resolve) => { let count = 0; const started = performance.now();"
    " const step = () => { count++; if (performance.now() - started < 1000)"
    " { requestAnimationFrame(step); } else { resolve(count); } };"
    " requestAnimationFrame(step); })"
)
MOVEMENT_KEYS = (
    ("KeyD", "d", 68, 0.7),
    ("KeyW", "w", 87, 0.7),
    ("KeyA", "a", 65, 0.7),
)


class CaptureError(RuntimeError):
    """Raised when the browser capture cannot produce trustworthy evidence."""


class WebSocketClient:
    """Minimal RFC 6455 text client good enough for a localhost CDP endpoint."""

    def __init__(self, url: str, timeout: float = 180.0) -> None:
        if not url.startswith("ws://"):
            raise CaptureError(f"Only unencrypted localhost DevTools endpoints are supported: {url}")

        remainder = url[len("ws://") :]
        authority, _, path = remainder.partition("/")
        host, _, port_text = authority.partition(":")
        self._path = "/" + path
        self._socket = socket.create_connection((host, int(port_text or "80")), timeout=timeout)
        self._socket.settimeout(timeout)
        self._buffer = b""
        self._handshake(host, port_text or "80")

    def _handshake(self, host: str, port: str) -> None:
        key = base64.b64encode(bytes(random.getrandbits(8) for _ in range(16))).decode("ascii")
        request = (
            f"GET {self._path} HTTP/1.1\r\n"
            f"Host: {host}:{port}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n\r\n"
        )
        self._socket.sendall(request.encode("ascii"))
        header = self._read_until(b"\r\n\r\n")
        if b" 101 " not in header.split(b"\r\n", 1)[0]:
            raise CaptureError(f"DevTools refused the WebSocket upgrade: {header!r}")

    def _read_until(self, terminator: bytes) -> bytes:
        while terminator not in self._buffer:
            chunk = self._socket.recv(65536)
            if not chunk:
                raise CaptureError("DevTools closed the connection during the handshake.")
            self._buffer += chunk

        head, _, self._buffer = self._buffer.partition(terminator)
        return head + terminator

    def _read_exactly(self, count: int) -> bytes:
        while len(self._buffer) < count:
            chunk = self._socket.recv(max(65536, count - len(self._buffer)))
            if not chunk:
                raise CaptureError("DevTools closed the connection mid-frame.")
            self._buffer += chunk

        payload, self._buffer = self._buffer[:count], self._buffer[count:]
        return payload

    def send_text(self, text: str) -> None:
        payload = text.encode("utf-8")
        header = bytearray([0x81])
        length = len(payload)
        if length < 126:
            header.append(0x80 | length)
        elif length < 65536:
            header.append(0x80 | 126)
            header += struct.pack(">H", length)
        else:
            header.append(0x80 | 127)
            header += struct.pack(">Q", length)

        mask = bytes(random.getrandbits(8) for _ in range(4))
        header += mask
        masked = bytes(byte ^ mask[index % 4] for index, byte in enumerate(payload))
        self._socket.sendall(bytes(header) + masked)

    def receive_text(self) -> str:
        while True:
            opcode, payload = self._receive_frame()
            if opcode == 0x1:
                return payload.decode("utf-8")
            if opcode == 0x8:
                raise CaptureError("DevTools closed the WebSocket.")
            if opcode == 0x9:
                self._send_control(0xA, payload)

    def _receive_frame(self) -> Tuple[int, bytes]:
        opcode = 0
        payload = b""
        while True:
            first, second = self._read_exactly(2)
            final = bool(first & 0x80)
            frame_opcode = first & 0x0F
            length = second & 0x7F
            if length == 126:
                length = struct.unpack(">H", self._read_exactly(2))[0]
            elif length == 127:
                length = struct.unpack(">Q", self._read_exactly(8))[0]

            if second & 0x80:
                raise CaptureError("DevTools sent a masked frame, which is not allowed.")

            payload += self._read_exactly(length)
            if frame_opcode != 0x0:
                opcode = frame_opcode
            if final:
                return opcode, payload

    def _send_control(self, opcode: int, payload: bytes) -> None:
        mask = bytes(random.getrandbits(8) for _ in range(4))
        masked = bytes(byte ^ mask[index % 4] for index, byte in enumerate(payload))
        self._socket.sendall(bytes([0x80 | opcode, 0x80 | len(payload)]) + mask + masked)

    def close(self) -> None:
        try:
            self._send_control(0x8, b"")
        except OSError:
            pass
        finally:
            self._socket.close()


class DevToolsClient:
    """Sequential CDP client. Events are buffered and never block a call."""

    def __init__(self, websocket_url: str) -> None:
        self._socket = WebSocketClient(websocket_url)
        self._next_id = 0
        self.events: List[Dict[str, Any]] = []

    def call(
        self,
        method: str,
        params: Optional[Dict[str, Any]] = None,
        session_id: Optional[str] = None,
        timeout: float = 180.0,
    ) -> Dict[str, Any]:
        self._next_id += 1
        message: Dict[str, Any] = {"id": self._next_id, "method": method}
        if params:
            message["params"] = params
        if session_id:
            message["sessionId"] = session_id

        self._socket.send_text(json.dumps(message))
        deadline = time.monotonic() + timeout
        while True:
            if time.monotonic() > deadline:
                raise CaptureError(f"DevTools call timed out: {method}")

            payload = json.loads(self._socket.receive_text())
            if payload.get("id") != message["id"]:
                self.events.append(payload)
                if len(self.events) > 2000:
                    del self.events[:1000]
                continue

            if "error" in payload:
                raise CaptureError(f"DevTools call failed: {method}: {payload['error']}")

            return payload.get("result", {})

    def close(self) -> None:
        self._socket.close()


@dataclass
class Surface:
    label: str
    directory: Path
    port: int
    server: Optional[subprocess.Popen] = None
    runs: List[Dict[str, Any]] = field(default_factory=list)

    @property
    def url(self) -> str:
        return f"http://127.0.0.1:{self.port}/"


def parse_surface(value: str) -> Surface:
    label, _, remainder = value.partition("=")
    directory_text, _, port_text = remainder.rpartition(":")
    if not label or not directory_text or not port_text.isdigit():
        raise argparse.ArgumentTypeError(
            f"--surface expects label=directory:port, received '{value}'."
        )

    directory = Path(directory_text)
    if not (directory / "index.html").is_file():
        raise argparse.ArgumentTypeError(f"No index.html under '{directory}'.")

    return Surface(label=label, directory=directory, port=int(port_text))


def parse_viewport(value: str) -> Tuple[int, int]:
    width_text, _, height_text = value.lower().partition("x")
    if not width_text.isdigit() or not height_text.isdigit():
        raise argparse.ArgumentTypeError(f"--viewport expects WIDTHxHEIGHT, received '{value}'.")

    return int(width_text), int(height_text)


def resolve_chrome(explicit: Optional[str]) -> Path:
    if explicit:
        candidate = Path(explicit)
        if not candidate.is_file():
            raise CaptureError(f"Chrome was not found at '{candidate}'.")
        return candidate

    for candidate_text in CHROME_CANDIDATES:
        candidate = Path(candidate_text)
        if candidate.is_file():
            return candidate

    discovered = shutil.which("chrome") or shutil.which("google-chrome") or shutil.which("chromium")
    if discovered:
        return Path(discovered)

    raise CaptureError("Chrome could not be located. Pass --chrome with an explicit path.")


def wait_for_http(url: str, timeout: float) -> None:
    deadline = time.monotonic() + timeout
    last_error: Optional[Exception] = None
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(url, timeout=5) as response:
                if response.status == 200:
                    return
        except (urllib.error.URLError, OSError) as error:  # pragma: no cover - transient
            last_error = error

        time.sleep(0.25)

    raise CaptureError(f"'{url}' never became available: {last_error}")


def start_server(surface: Surface, repository_root: Path) -> None:
    script = repository_root / "Tools" / "serve_webgl.py"
    surface.server = subprocess.Popen(
        [sys.executable, str(script), str(surface.directory), "--port", str(surface.port)],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        cwd=str(repository_root),
    )
    wait_for_http(surface.url, timeout=30)


def stop_server(surface: Surface) -> None:
    if surface.server is None:
        return

    surface.server.terminate()
    try:
        surface.server.wait(timeout=10)
    except subprocess.TimeoutExpired:  # pragma: no cover - defensive
        surface.server.kill()


def launch_chrome(chrome: Path, debug_port: int, profile: Path, width: int, height: int) -> subprocess.Popen:
    arguments = [
        str(chrome),
        "--headless=new",
        f"--remote-debugging-port={debug_port}",
        f"--user-data-dir={profile}",
        f"--window-size={width},{height}",
        "--use-gl=angle",
        "--use-angle=swiftshader",
        "--enable-unsafe-swiftshader",
        "--hide-scrollbars",
        "--mute-audio",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-extensions",
        "--disable-background-timer-throttling",
        "--disable-backgrounding-occluded-windows",
        "--disable-renderer-backgrounding",
        "about:blank",
    ]
    process = subprocess.Popen(arguments, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    wait_for_http(f"http://127.0.0.1:{debug_port}/json/version", timeout=60)
    return process


def fetch_websocket_url(debug_port: int) -> str:
    with urllib.request.urlopen(f"http://127.0.0.1:{debug_port}/json/version", timeout=10) as response:
        payload = json.loads(response.read().decode("utf-8"))

    websocket_url = payload.get("webSocketDebuggerUrl")
    if not websocket_url:
        raise CaptureError("DevTools did not advertise a browser WebSocket endpoint.")

    return websocket_url


def evaluate(
    client: DevToolsClient,
    session_id: str,
    expression: str,
    await_promise: bool = False,
    timeout: float = 180.0,
) -> Any:
    result = client.call(
        "Runtime.evaluate",
        {
            "expression": expression,
            "returnByValue": True,
            "awaitPromise": await_promise,
        },
        session_id=session_id,
        timeout=timeout,
    )
    if result.get("exceptionDetails"):
        raise CaptureError(f"Page evaluation failed: {result['exceptionDetails']}")

    return result.get("result", {}).get("value")


def dispatch_click(client: DevToolsClient, session_id: str, x: int, y: int) -> None:
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


def dispatch_key(client: DevToolsClient, session_id: str, code: str, key: str, virtual_key: int, hold: float) -> None:
    client.call(
        "Input.dispatchKeyEvent",
        {
            "type": "keyDown",
            "code": code,
            "key": key,
            "text": key,
            "unmodifiedText": key,
            "windowsVirtualKeyCode": virtual_key,
            "nativeVirtualKeyCode": virtual_key,
        },
        session_id=session_id,
    )
    time.sleep(hold)
    client.call(
        "Input.dispatchKeyEvent",
        {
            "type": "keyUp",
            "code": code,
            "key": key,
            "windowsVirtualKeyCode": virtual_key,
            "nativeVirtualKeyCode": virtual_key,
        },
        session_id=session_id,
    )


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)

    return digest.hexdigest()


def measure_frame_change(previous: Optional[Path], current: Path) -> Optional[float]:
    try:
        from PIL import Image  # type: ignore
    except ImportError:  # pragma: no cover - optional dependency
        return None

    if previous is None:
        return None

    with Image.open(previous) as left_image, Image.open(current) as right_image:
        left = left_image.convert("L").resize((320, 180))
        right = right_image.convert("L").resize((320, 180))
        left_bytes = left.tobytes()
        right_bytes = right.tobytes()

    total = sum(abs(a - b) for a, b in zip(left_bytes, right_bytes))
    return round(total / (len(left_bytes) * 255.0), 6)


def measure_frame_detail(path: Path) -> Optional[Dict[str, Any]]:
    try:
        from PIL import Image  # type: ignore
    except ImportError:  # pragma: no cover - optional dependency
        return None

    with Image.open(path) as image:
        width, height = image.size
        sampled = image.convert("RGB").resize((320, 180))
        colors = sampled.getcolors(maxcolors=320 * 180)

    distinct = len(colors) if colors else 0
    dominant = max((count for count, _ in colors), default=0) if colors else 0
    return {
        "pixelSize": [width, height],
        "distinctSampledColors": distinct,
        "dominantSampledShare": round(dominant / float(320 * 180), 6),
    }


def capture_surface(
    client: DevToolsClient,
    session_id: str,
    surface: Surface,
    viewport: Tuple[int, int],
    output_directory: Path,
    frames: int,
    interval: float,
    actions: List[Dict[str, Any]],
) -> Dict[str, Any]:
    width, height = viewport
    client.call(
        "Emulation.setDeviceMetricsOverride",
        {"width": width, "height": height, "deviceScaleFactor": 1, "mobile": False},
        session_id=session_id,
    )
    client.call("Page.navigate", {"url": surface.url}, session_id=session_id)
    actions.append(
        {
            "type": "open",
            "timestamp": utc_now(),
            "selector": surface.url,
            "target": f"{surface.label.upper()} WebGL {width}x{height}",
            "result": "passed",
        }
    )

    deadline = time.monotonic() + 240.0
    loaded = False
    while time.monotonic() < deadline:
        if evaluate(client, session_id, LOADING_BAR_HIDDEN) is True:
            loaded = True
            break

        time.sleep(0.5)

    if not loaded:
        raise CaptureError(f"{surface.label} never finished loading at {width}x{height}.")

    geometry = evaluate(client, session_id, CANVAS_GEOMETRY)
    if geometry is None:
        raise CaptureError(f"{surface.label} exposed no unity canvas at {width}x{height}.")

    actions.append(
        {
            "type": "observe",
            "timestamp": utc_now(),
            "selector": "#unity-canvas",
            "target": f"{surface.label.upper()} canvas {geometry[0]}x{geometry[1]} css / {geometry[2]}x{geometry[3]} backing",
            "result": "passed",
        }
    )

    prefix = f"monster-animation-v002-{surface.label}-{width}x{height}"
    before_gesture = output_directory / f"{prefix}-00-before-gesture.png"
    write_screenshot(client, session_id, before_gesture)

    dispatch_click(client, session_id, max(1, width // 2), max(1, height // 2))
    actions.append(
        {
            "type": "click",
            "timestamp": utc_now(),
            "selector": "#unity-canvas",
            "target": "trusted pointer gesture at canvas centre",
            "result": "passed",
        }
    )
    time.sleep(1.0)

    captures: List[Dict[str, Any]] = []
    previous_path: Optional[Path] = None
    key_index = 0
    for frame_index in range(frames):
        if key_index < len(MOVEMENT_KEYS):
            code, key, virtual_key, hold = MOVEMENT_KEYS[key_index]
            dispatch_key(client, session_id, code, key, virtual_key, hold)
            actions.append(
                {
                    "type": "press",
                    "timestamp": utc_now(),
                    "selector": "#unity-canvas",
                    "target": f"hold {code} for {hold:g}s",
                    "result": "passed",
                }
            )
            key_index += 1
        else:
            time.sleep(interval)

        path = output_directory / f"{prefix}-{frame_index + 1:02d}.png"
        write_screenshot(client, session_id, path)
        entry: Dict[str, Any] = {
            "file": path.name,
            "capturedUtc": utc_now(),
            "sha256": sha256_of(path),
            "bytes": path.stat().st_size,
            "changeFromPreviousFrame": measure_frame_change(previous_path, path),
        }
        detail = measure_frame_detail(path)
        if detail:
            entry.update(detail)

        captures.append(entry)
        previous_path = path

    animation_frames = [
        evaluate(client, session_id, ANIMATION_FRAME_BUCKET, await_promise=True),
        evaluate(client, session_id, ANIMATION_FRAME_BUCKET, await_promise=True),
    ]
    changes = [item["changeFromPreviousFrame"] for item in captures if item["changeFromPreviousFrame"] is not None]
    return {
        "surface": f"{surface.label.upper()} Development WebGL",
        "url": surface.url,
        "viewport": [width, height],
        "canvasCss": [geometry[0], geometry[1]],
        "canvasBacking": [geometry[2], geometry[3]],
        "devicePixelRatio": geometry[4],
        "requestAnimationFrameBuckets": animation_frames,
        "beforeGestureScreenshot": {
            "file": before_gesture.name,
            "sha256": sha256_of(before_gesture),
            "bytes": before_gesture.stat().st_size,
        },
        "captures": captures,
        "maximumFrameChange": max(changes) if changes else None,
        "distinctScreenshotHashes": len({item["sha256"] for item in captures}),
    }


def write_screenshot(client: DevToolsClient, session_id: str, path: Path) -> None:
    result = client.call("Page.captureScreenshot", {"format": "png"}, session_id=session_id)
    data = result.get("data")
    if not data:
        raise CaptureError(f"DevTools returned no screenshot payload for '{path.name}'.")

    try:
        decoded = base64.b64decode(data, validate=True)
    except binascii.Error as error:
        raise CaptureError(f"DevTools screenshot payload was not valid base64: {error}") from error

    path.write_bytes(decoded)


def utc_now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def build_transcript(
    chrome: Path,
    chrome_version: str,
    surfaces: Sequence[Surface],
    actions: List[Dict[str, Any]],
) -> Dict[str, Any]:
    runs: List[Dict[str, Any]] = []
    for surface in surfaces:
        runs.extend(surface.runs)

    return {
        "schema": TRANSCRIPT_SCHEMA,
        "schemaVersion": TRANSCRIPT_SCHEMA_VERSION,
        "tool": "Tools/capture_webgl_visuals.py",
        "browser": chrome_version,
        "browserExecutable": str(chrome),
        "transport": "HTTP + Chrome DevTools Protocol over localhost WebSocket",
        "capturedUtc": utc_now(),
        "actions": actions,
        "runs": runs,
        "limitations": [
            "requestAnimationFrame counts are a browser responsiveness signal, not Unity Profiler frame-time measurements",
            "screenshot change ratios prove the canvas keeps rendering new frames; they do not classify which animation state a frame shows",
            "headless Chrome renders WebGL through SwiftShader, so absolute frame pacing is slower than a GPU session",
            "human gameplay-scale visual approval remains pending",
        ],
        "runtime_authorization": "local-unsealed-only",
        "m2_entry_gate_status": "not-evaluated",
    }


def read_chrome_version(debug_port: int) -> str:
    with urllib.request.urlopen(f"http://127.0.0.1:{debug_port}/json/version", timeout=10) as response:
        payload = json.loads(response.read().decode("utf-8"))

    return payload.get("Browser", "unknown")


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--surface", action="append", required=True, type=parse_surface)
    parser.add_argument("--viewport", action="append", required=True, type=parse_viewport)
    parser.add_argument("--output-directory", required=True, type=Path)
    parser.add_argument("--transcript", required=True, type=Path)
    parser.add_argument("--frames", type=int, default=6)
    parser.add_argument("--interval", type=float, default=0.75)
    parser.add_argument("--debug-port", type=int, default=9333)
    parser.add_argument("--chrome")
    arguments = parser.parse_args(argv)

    if arguments.frames < 1:
        parser.error("--frames must be positive")

    repository_root = Path(__file__).resolve().parent.parent
    output_directory = arguments.output_directory
    output_directory.mkdir(parents=True, exist_ok=True)
    chrome = resolve_chrome(arguments.chrome)

    widest = max(width for width, _ in arguments.viewport)
    tallest = max(height for _, height in arguments.viewport)
    profile = Path(tempfile.mkdtemp(prefix="overbless-capture-"))
    browser: Optional[subprocess.Popen] = None
    client: Optional[DevToolsClient] = None
    actions: List[Dict[str, Any]] = []
    try:
        for surface in arguments.surface:
            start_server(surface, repository_root)

        browser = launch_chrome(chrome, arguments.debug_port, profile, widest, tallest)
        chrome_version = read_chrome_version(arguments.debug_port)
        client = DevToolsClient(fetch_websocket_url(arguments.debug_port))
        target = client.call("Target.createTarget", {"url": "about:blank"})
        session = client.call(
            "Target.attachToTarget",
            {"targetId": target["targetId"], "flatten": True},
        )
        session_id = session["sessionId"]
        client.call("Page.enable", session_id=session_id)
        client.call("Runtime.enable", session_id=session_id)

        for surface in arguments.surface:
            for viewport in arguments.viewport:
                run = capture_surface(
                    client,
                    session_id,
                    surface,
                    viewport,
                    output_directory,
                    arguments.frames,
                    arguments.interval,
                    actions,
                )
                surface.runs.append(run)
                print(
                    f"captured {surface.label} {viewport[0]}x{viewport[1]}: "
                    f"{len(run['captures'])} frames, "
                    f"maxChange={run['maximumFrameChange']}, "
                    f"rAF={run['requestAnimationFrameBuckets']}"
                )

        transcript = build_transcript(chrome, chrome_version, arguments.surface, actions)
        arguments.transcript.parent.mkdir(parents=True, exist_ok=True)
        arguments.transcript.write_text(
            json.dumps(transcript, indent=2, sort_keys=False) + "\n", encoding="utf-8"
        )
        print(f"transcript written: {arguments.transcript}")
        return 0
    except CaptureError as error:
        print(f"capture failed: {error}", file=sys.stderr)
        return 1
    finally:
        if client is not None:
            try:
                client.close()
            except OSError:
                pass
        if browser is not None:
            browser.terminate()
            try:
                browser.wait(timeout=15)
            except subprocess.TimeoutExpired:  # pragma: no cover - defensive
                browser.kill()
        for surface in arguments.surface:
            stop_server(surface)
        shutil.rmtree(profile, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
