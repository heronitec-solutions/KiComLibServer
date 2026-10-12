import io
import tarfile
from pathlib import Path

import pytest

from app.archive import ArchiveError, safe_extract


def _tar(members: list[tuple[str, bytes | None]], links: list[tuple[str, str]] | None = None) -> bytes:
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w") as archive:
        for name, data in members:
            info = tarfile.TarInfo(name)
            payload = data or b""
            info.size = len(payload)
            archive.addfile(info, io.BytesIO(payload))
        for name, target in links or []:
            info = tarfile.TarInfo(name)
            info.type = tarfile.SYMTYPE
            info.linkname = target
            archive.addfile(info)
    return buf.getvalue()


def test_extracts_a_normal_member(tmp_path: Path):
    safe_extract(_tar([("proj/flat.kicad_sch", b"(kicad_sch)")]), tmp_path)
    assert (tmp_path / "proj" / "flat.kicad_sch").read_bytes() == b"(kicad_sch)"


def test_rejects_parent_traversal(tmp_path: Path):
    dest = tmp_path / "work"
    with pytest.raises(ArchiveError, match="escapes"):
        safe_extract(_tar([("../evil.txt", b"nope")]), dest)
    assert not (tmp_path / "evil.txt").exists()


def test_rejects_absolute_path(tmp_path: Path):
    with pytest.raises(ArchiveError, match="escapes"):
        safe_extract(_tar([("/tmp/evil.txt", b"nope")]), tmp_path / "work")


def test_rejects_symlink_escape(tmp_path: Path):
    dest = tmp_path / "work"
    with pytest.raises(ArchiveError, match="escapes"):
        safe_extract(_tar([], links=[("escape", "../outside")]), dest)
    assert not (tmp_path / "outside").exists()
