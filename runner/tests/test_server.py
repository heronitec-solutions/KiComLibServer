import io
import json
import os
import tarfile
import threading
from http.client import HTTPConnection

from app.server import Handler, ThreadingHTTPServer, parse_multipart


def _start():
    os.environ["RUNNER_TOKEN"] = "secret"
    os.environ["RUNNER_MAX_UPLOAD_MB"] = "200"
    httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=httpd.serve_forever, daemon=True)
    thread.start()
    return httpd


def _request(httpd, method, path, body=None, headers=None, token=True):
    host, port = httpd.server_address
    conn = HTTPConnection(host, port, timeout=10)
    hdrs = dict(headers or {})
    if token:
        hdrs["Authorization"] = "Bearer secret"
    conn.request(method, path, body=body, headers=hdrs)
    response = conn.getresponse()
    payload = response.read()
    conn.close()
    return response.status, payload


def _multipart(fields):
    boundary = "----kicomlibboundary"
    chunks = []
    for name, data, filename in fields:
        header = f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"'
        if filename:
            header += f'; filename="{filename}"'
        chunks.append(header.encode() + b"\r\n\r\n" + data + b"\r\n")
    chunks.append(f"--{boundary}--\r\n".encode())
    body = b"".join(chunks)
    return body, f"multipart/form-data; boundary={boundary}"


def test_parse_multipart_accepts_dotnet_unquoted_names():
    boundary = "da2f087e-d7c7-4456-8254-80d465199167"
    body = (
        f"--{boundary}\r\n"
        "Content-Type: application/x-tar\r\n"
        "Content-Disposition: form-data; name=base; filename=base.tar; filename*=utf-8''base.tar\r\n"
        "\r\n"
    ).encode() + b"tar-bytes" + (
        f"\r\n--{boundary}\r\n"
        "Content-Type: application/json\r\n"
        "Content-Disposition: form-data; name=spec; filename=spec.json; filename*=utf-8''spec.json\r\n"
        "\r\n"
        '{"projectFile": "a.kicad_pro"}\r\n'
        f"--{boundary}--\r\n"
    ).encode()
    parts = parse_multipart(f'multipart/form-data; boundary="{boundary}"', body)
    assert json.loads(parts["spec"]["data"])["projectFile"] == "a.kicad_pro"
    assert parts["base"]["data"] == b"tar-bytes"
    assert parts["spec"]["filename"] == "spec.json"


def test_health_does_not_require_a_token():
    httpd = _start()
    try:
        status, payload = _request(httpd, "GET", "/health", token=False)
        body = json.loads(payload)
        assert status == 200
        assert body["runner"] == "1.0.0"
        assert "kicad" in body
    finally:
        httpd.shutdown()


def test_post_requires_bearer_token():
    httpd = _start()
    try:
        status, payload = _request(httpd, "POST", "/v1/outputs", body=b"", token=False,
                                   headers={"Content-Length": "0"})
        assert status == 401
        assert json.loads(payload)["code"] == "unauthorized"
    finally:
        httpd.shutdown()


def test_unknown_output_is_rejected():
    httpd = _start()
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w"):
        pass
    body, content_type = _multipart([
        ("spec", b'{"projectFile": "flat.kicad_pro", "outputs": ["not_an_output"]}', "spec.json"),
        ("project", buf.getvalue(), "project.tar"),
    ])
    try:
        status, payload = _request(
            httpd, "POST", "/v1/outputs", body=body,
            headers={"Content-Type": content_type, "Content-Length": str(len(body))},
        )
        assert status == 400
        parsed = json.loads(payload)
        assert parsed["code"] == "invalid"
        assert "unknown output" in parsed["error"]
    finally:
        httpd.shutdown()


def test_upload_limit():
    httpd = _start()
    os.environ["RUNNER_MAX_UPLOAD_MB"] = "0"
    try:
        status, payload = _request(
            httpd, "POST", "/v1/analyze", body=b"0123456789",
            headers={"Content-Type": "application/octet-stream", "Content-Length": "10"},
        )
        assert status == 413
        assert json.loads(payload)["code"] == "too_big"
    finally:
        os.environ["RUNNER_MAX_UPLOAD_MB"] = "200"
        httpd.shutdown()


def test_path_traversal_archive_is_rejected():
    httpd = _start()
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w") as archive:
        info = tarfile.TarInfo("../evil.txt")
        data = b"nope"
        info.size = len(data)
        archive.addfile(info, io.BytesIO(data))
    body, content_type = _multipart([
        ("spec", b'{"projectFile": "flat.kicad_pro"}', None),
        ("project", buf.getvalue(), "project.tar"),
    ])
    try:
        status, payload = _request(
            httpd, "POST", "/v1/analyze", body=body,
            headers={"Content-Type": content_type, "Content-Length": str(len(body))},
        )
        assert status == 400
        assert json.loads(payload)["code"] == "invalid"
    finally:
        httpd.shutdown()
