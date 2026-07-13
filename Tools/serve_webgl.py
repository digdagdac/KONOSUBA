#!/usr/bin/env python3
"""Serve a local Unity Development WebGL directory on fixed localhost only."""

from __future__ import annotations

import argparse
import functools
import mimetypes
import os
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Optional
from urllib.parse import unquote, urlsplit

HOST = "127.0.0.1"
DEFAULT_PORT = 8000

MIME_TYPES = {
    ".css": "text/css; charset=utf-8",
    ".data": "application/octet-stream",
    ".html": "text/html; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".wasm": "application/wasm",
    ".xml": "application/xml; charset=utf-8",
}
COMPRESSED_SUFFIXES = {".br": "br", ".gz": "gzip"}


class DevelopmentWebGLHandler(SimpleHTTPRequestHandler):
    """GET/HEAD-only static handler with no-cache responses and no directory listing."""

    def __init__(self, *args: object, root: Path, verbose: bool, **kwargs: object) -> None:
        self._root = root
        self._verbose = verbose
        super().__init__(*args, directory=os.fspath(root), **kwargs)

    def send_head(self) -> Optional[object]:
        candidate = self._resolve_request_path(self.path)
        if candidate is None:
            self.send_error(HTTPStatus.BAD_REQUEST, "Invalid request path")
            return None

        resolved = self._resolve_existing_path(candidate)
        if resolved is None:
            self.send_error(HTTPStatus.NOT_FOUND, "File not found")
            return None

        if resolved.is_dir():
            for index_name in ("index.html", "index.htm"):
                index_path = self._resolve_existing_path(resolved / index_name)
                if index_path is not None and index_path.is_file():
                    return self._send_file(index_path)

            self.send_error(HTTPStatus.NOT_FOUND, "Directory listing is disabled")
            return None

        if not resolved.is_file():
            self.send_error(HTTPStatus.NOT_FOUND, "File not found")
            return None

        return self._send_file(resolved)

    def _resolve_request_path(self, path: str) -> Optional[Path]:
        parsed = urlsplit(path)
        if parsed.scheme or parsed.netloc:
            return None

        try:
            decoded_path = unquote(parsed.path, encoding="utf-8", errors="strict")
        except UnicodeDecodeError:
            return None

        if not decoded_path.startswith("/") or "\x00" in decoded_path or "\\" in decoded_path:
            return None

        relative_path = decoded_path[1:]
        if relative_path:
            parts = relative_path.split("/")
            if any(part in ("", ".", "..") or ":" in part for part in parts):
                return None
        else:
            parts = []

        try:
            candidate = self._root.joinpath(*parts).resolve(strict=False)
        except (OSError, RuntimeError):
            return None

        return candidate if _is_within_root(self._root, candidate) else None

    def _resolve_existing_path(self, candidate: Path) -> Optional[Path]:
        try:
            resolved = candidate.resolve(strict=True)
        except (FileNotFoundError, OSError, RuntimeError):
            return None

        return resolved if _is_within_root(self._root, resolved) else None

    def _send_file(self, path: Path) -> Optional[object]:
        try:
            file = path.open("rb")
            try:
                file_info = os.fstat(file.fileno())
            except OSError:
                file.close()
                raise
        except OSError:
            self.send_error(HTTPStatus.NOT_FOUND, "File not found")
            return None

        self.send_response(HTTPStatus.OK)
        self.send_header("Content-type", self.guess_type(os.fspath(path)))
        self.send_header("Content-Length", str(file_info.st_size))
        content_encoding = COMPRESSED_SUFFIXES.get(path.suffix.lower())
        if content_encoding is not None:
            self.send_header("Content-Encoding", content_encoding)
        self.end_headers()
        return file

    def guess_type(self, path: str) -> str:
        suffixes = Path(path).suffixes
        extension = suffixes[-1].lower() if suffixes else ""
        if extension in COMPRESSED_SUFFIXES:
            extension = suffixes[-2].lower() if len(suffixes) > 1 else ""
        return MIME_TYPES.get(extension, mimetypes.guess_type(path)[0] or "application/octet-stream")

    def end_headers(self) -> None:
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        self.send_header("Pragma", "no-cache")
        self.send_header("Expires", "0")
        super().end_headers()

    def log_message(self, format: str, *args: object) -> None:
        if self._verbose:
            super().log_message(format, *args)


class LocalWebGLServer(ThreadingHTTPServer):
    allow_reuse_address = True


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Serve a Unity Development WebGL output directory on http://127.0.0.1 only."
    )
    parser.add_argument(
        "directory",
        type=Path,
        help="Local Development WebGL output directory containing index.html.",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=DEFAULT_PORT,
        help=f"Local TCP port (1-65535; default: {DEFAULT_PORT}).",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Print HTTP request logs to stderr.",
    )
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("--port must be between 1 and 65535")
    return args


def _is_within_root(root: Path, path: Path) -> bool:
    return path == root or root in path.parents


def resolve_development_directory(directory: Path) -> Path:
    try:
        root = directory.expanduser().resolve(strict=True)
    except FileNotFoundError as error:
        raise ValueError(f"Development WebGL directory does not exist: {directory}") from error
    except (OSError, RuntimeError) as error:
        raise ValueError(f"Development WebGL directory cannot be resolved: {directory}: {error}") from error

    try:
        if not root.is_dir():
            raise ValueError(f"Development WebGL path is not a directory: {root}")

        index_path = (root / "index.html").resolve(strict=True)
        if not _is_within_root(root, index_path) or not index_path.is_file():
            raise ValueError(f"Development WebGL directory must contain an in-root index.html: {root}")
    except FileNotFoundError as error:
        raise ValueError(f"Development WebGL directory must contain index.html: {root}") from error
    except (OSError, RuntimeError) as error:
        raise ValueError(f"Development WebGL directory index cannot be resolved: {root}: {error}") from error

    return root


def main() -> None:
    args = parse_args()
    try:
        root = resolve_development_directory(args.directory)
    except ValueError as error:
        raise SystemExit(str(error))

    handler = functools.partial(DevelopmentWebGLHandler, root=root, verbose=args.verbose)
    with LocalWebGLServer((HOST, args.port), handler) as server:
        print(f"Serving {root} at http://{HOST}:{args.port}/")
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
