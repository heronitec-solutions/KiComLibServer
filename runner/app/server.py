"""HTTP API for the kicad-runner. Stdlib ThreadingHTTPServer only."""

from __future__ import annotations

import json
import os
import re
import shutil
import tempfile
import threading
import uuid
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

from app import RUNNER_VERSION
from app.analyze import analyze
from app.archive import ArchiveError, safe_extract
from app.cli import job_env, kicad_version, write_empty_tables
from app.errors import RunnerError, invalid, timed_out
from app.outputs import produce
from app.render import render
from app.visual_diff import visual_diff

_SEM: threading.Semaphore | None = None
_SEM_SIZE: int | None = None
_SEM_LOCK = threading.Lock()


def max_parallel() -> int:
    try:
        return max(1, int(os.environ.get("RUNNER_MAX_PARALLEL", "2")))
    except ValueError:
        return 2


def job_timeout() -> float:
    try:
        return float(os.environ.get("RUNNER_JOB_TIMEOUT_SECONDS", "600"))
    except ValueError:
        return 600.0


def max_upload_bytes() -> int:
    try:
        mb = int(os.environ.get("RUNNER_MAX_UPLOAD_MB", "200"))
    except ValueError:
        mb = 200
    return mb * 1024 * 1024


def _semaphore() -> threading.Semaphore:
    global _SEM, _SEM_SIZE
    size = max_parallel()
    with _SEM_LOCK:
        if _SEM is None or _SEM_SIZE != size:
            _SEM = threading.Semaphore(size)
            _SEM_SIZE = size
        return _SEM


def work_root() -> Path:
    preferred = Path(os.environ.get("RUNNER_WORK_DIR", "/work"))
    try:
        preferred.mkdir(parents=True, exist_ok=True)
        probe = preferred / ".write-probe"
        probe.write_text("ok", encoding="utf-8")
        probe.unlink()
        return preferred
    except OSError:
        fallback = Path(tempfile.gettempdir()) / "kicomlib-runner"
        fallback.mkdir(parents=True, exist_ok=True)
        return fallback


@contextmanager
def workspace():
    """Fresh work directory and KICAD_CONFIG_HOME, deleted afterwards."""
    job = work_root() / uuid.uuid4().hex
    job.mkdir(parents=True)
    config = job / "kicad-config"
    config.mkdir()
    (job / "cache").mkdir()
    write_empty_tables(config)
    env = job_env(config, job)
    try:
        yield job, env
    finally:
        shutil.rmtree(job, ignore_errors=True)


def _disposition_value(header: str, param: str) -> str | None:
    match = re.search(
        rf'(?:^|[;\s]){param}\s*=\s*(?:"([^"]*)"|([^";\s]+))',
        header,
        re.IGNORECASE,
    )
    if not match:
        return None
    return match.group(1) if match.group(1) is not None else match.group(2)


def parse_multipart(content_type: str, body: bytes) -> dict[str, dict]:
    match = re.search(r'boundary="?([^";]+)"?', content_type or "")
    if not match:
        raise invalid("expected multipart/form-data")
    boundary = match.group(1).encode("utf-8", errors="replace")
    delimiter = b"--" + boundary
    parts = body.split(delimiter)
    found: dict[str, dict] = {}
    for part in parts:
        if not part or part in {b"--", b"--\r\n"} or part.strip() in {b"", b"--"}:
            continue
        if part.startswith(b"\r\n"):
            part = part[2:]
        if part.endswith(b"\r\n"):
            part = part[:-2]
        if part.endswith(b"--"):
            part = part[:-2]
            if part.endswith(b"\r\n"):
                part = part[:-2]
        header_blob, sep, data = part.partition(b"\r\n\r\n")
        if not sep:
            continue
        header = header_blob.decode("utf-8", errors="replace")
        # .NET MultipartFormDataContent sends name=spec without quotes.
        # Browsers and the runner tests send name="spec".
        name = _disposition_value(header, "name")
        if not name:
            continue
        found[name] = {
            "data": data,
            "filename": _disposition_value(header, "filename"),
        }
    return found


def _json_part(parts: dict[str, dict], name: str) -> dict:
    part = parts.get(name)
    if part is None:
        raise invalid(f"missing multipart field '{name}'")
    try:
        payload = json.loads(part["data"].decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise invalid(f"field '{name}' is not JSON") from exc
    if not isinstance(payload, dict):
        raise invalid(f"field '{name}' must be a JSON object")
    return payload


def _tar_part(parts: dict[str, dict], name: str) -> bytes:
    part = parts.get(name)
    if part is None or not isinstance(part.get("data"), (bytes, bytearray)):
        raise invalid(f"missing multipart field '{name}'")
    return bytes(part["data"])


def _authorized(header: str | None) -> bool:
    import hmac
    token = os.environ.get("RUNNER_TOKEN", "")
    if not token or not header or not header.startswith("Bearer "):
        return False
    return hmac.compare_digest(header[7:].encode("utf-8"), token.encode("utf-8"))


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "kicomlib-kicad-runner/1.0.0"

    def log_message(self, fmt: str, *args) -> None:
        # Avoid writing request lines that might include a token query.
        return

    def do_GET(self) -> None:  # noqa: N802
        path = urlparse(self.path).path
        if path == "/health":
            self._json(200, {"runner": RUNNER_VERSION, "kicad": kicad_version()})
            return
        self._json(404, {"error": "not found", "code": "not_found"})

    def do_POST(self) -> None:  # noqa: N802
        path = urlparse(self.path).path
        if not _authorized(self.headers.get("Authorization")):
            self._json(401, {"error": "unauthorized", "code": "unauthorized"})
            return
        try:
            length = self.headers.get("Content-Length")
            if length is None:
                raise invalid("Content-Length is required")
            size = int(length)
        except RunnerError as exc:
            self._json(exc.status, {"error": exc.message, "code": exc.code})
            return
        except ValueError:
            self._json(400, {"error": "invalid Content-Length", "code": "invalid"})
            return
        if size > max_upload_bytes():
            self._json(413, {"error": "upload exceeds RUNNER_MAX_UPLOAD_MB", "code": "too_big"})
            return
        body = self.rfile.read(size) if size else b""
        if len(body) > max_upload_bytes():
            self._json(413, {"error": "upload exceeds RUNNER_MAX_UPLOAD_MB", "code": "too_big"})
            return
        try:
            if path == "/v1/analyze":
                payload, content_type = self._job(body, self._analyze)
            elif path == "/v1/render":
                payload, content_type = self._job(body, self._render)
            elif path == "/v1/visual-diff":
                payload, content_type = self._job(body, self._visual_diff)
            elif path == "/v1/outputs":
                payload, content_type = self._job(body, self._outputs)
            else:
                self._json(404, {"error": "not found", "code": "not_found"})
                return
        except ArchiveError as exc:
            self._json(400, {"error": exc.message, "code": "invalid"})
            return
        except RunnerError as exc:
            self._json(exc.status, {"error": exc.message, "code": exc.code})
            return
        except Exception as exc:  # noqa: BLE001 — last-resort API error, no traceback to the client
            self._json(500, {"error": str(exc)[:2000] or "internal error", "code": "internal"})
            return
        self._bytes(200, payload, content_type)

    def _job(self, body: bytes, fn):
        sem = _semaphore()
        timeout = job_timeout()
        if not sem.acquire(timeout=timeout):
            raise timed_out("runner is busy")
        try:
            with workspace() as (job, env):
                return fn(body, job, env, timeout)
        finally:
            sem.release()

    def _analyze(self, body: bytes, job: Path, env: dict, timeout: float):
        parts = parse_multipart(self.headers.get("Content-Type", ""), body)
        spec = _json_part(parts, "spec")
        root = job / "project"
        safe_extract(_tar_part(parts, "project"), root)
        result = analyze(root, spec, env=env, timeout=timeout, kicad=kicad_version())
        return json.dumps(result).encode("utf-8"), "application/json; charset=utf-8"

    def _render(self, body: bytes, job: Path, env: dict, timeout: float):
        parts = parse_multipart(self.headers.get("Content-Type", ""), body)
        spec = _json_part(parts, "spec")
        root = job / "project"
        safe_extract(_tar_part(parts, "project"), root)
        return render(root, spec, env=env, timeout=timeout, kicad=kicad_version()), "application/zip"

    def _outputs(self, body: bytes, job: Path, env: dict, timeout: float):
        parts = parse_multipart(self.headers.get("Content-Type", ""), body)
        spec = _json_part(parts, "spec")
        root = job / "project"
        safe_extract(_tar_part(parts, "project"), root)
        models = parts.get("models")
        models_zip = bytes(models["data"]) if models and models.get("data") else None
        blob = produce(
            root, spec, env=env, timeout=timeout, kicad=kicad_version(),
            models_zip=models_zip, work=job / "out",
        )
        return blob, "application/zip"

    def _visual_diff(self, body: bytes, job: Path, env: dict, timeout: float):
        parts = parse_multipart(self.headers.get("Content-Type", ""), body)
        spec = _json_part(parts, "spec")
        base_bytes = _tar_part(parts, "base")
        target_bytes = _tar_part(parts, "target")
        base_root = job / "base"
        target_root = job / "target"
        safe_extract(base_bytes, base_root)
        safe_extract(target_bytes, target_root)
        blob = visual_diff(
            base_root, target_root, spec, env=env, timeout=timeout, kicad=kicad_version(),
            base_bytes=base_bytes, target_bytes=target_bytes, scratch_dir=job,
        )
        return blob, "application/zip"

    def _json(self, status: int, payload: dict) -> None:
        self._bytes(status, json.dumps(payload).encode("utf-8"), "application/json; charset=utf-8")

    def _bytes(self, status: int, payload: bytes, content_type: str) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


def main() -> None:
    host = os.environ.get("RUNNER_HOST", "0.0.0.0")
    port = int(os.environ.get("RUNNER_PORT", "8080"))
    server = ThreadingHTTPServer((host, port), Handler)
    server.serve_forever()


if __name__ == "__main__":
    main()
