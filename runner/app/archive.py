"""Safe tar extraction. Rejects paths that escape the job directory."""

from __future__ import annotations

import io
import tarfile
from pathlib import Path, PurePosixPath

from app.errors import invalid


class ArchiveError(Exception):
    """Path escaped the work directory or the archive is not a tar."""

    def __init__(self, message: str) -> None:
        super().__init__(message)
        self.message = message

    def as_runner_error(self):
        return invalid(self.message)


def _within(parent: Path, child: Path) -> bool:
    try:
        child.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def _member_target(dest: Path, name: str) -> Path:
    return dest / name.replace("\\", "/")


def safe_extract(data: bytes, dest: Path) -> None:
    """Unpack ``data`` into ``dest`` using ``tarfile`` ``filter="data"``.

    Members whose names are absolute, contain ``..``, or whose link targets
    leave ``dest`` are rejected before extraction.
    """
    dest.mkdir(parents=True, exist_ok=True)
    try:
        tar = tarfile.open(fileobj=io.BytesIO(data), mode="r:*")
    except tarfile.TarError as exc:
        raise ArchiveError(f"invalid tar archive: {exc}") from exc

    with tar:
        for member in tar.getmembers():
            name = member.name.replace("\\", "/")
            if not name or name.startswith("/") or name.startswith("\\"):
                raise ArchiveError("archive path escapes the work directory")
            if len(name) >= 2 and name[1] == ":":
                raise ArchiveError("archive path escapes the work directory")
            parts = PurePosixPath(name).parts
            if any(part == ".." for part in parts):
                raise ArchiveError("archive path escapes the work directory")
            target = _member_target(dest, name)
            if not _within(dest, target):
                raise ArchiveError("archive path escapes the work directory")
            if member.issym() or member.islnk():
                link_name = member.linkname.replace("\\", "/")
                if link_name.startswith("/") or (len(link_name) >= 2 and link_name[1] == ":"):
                    raise ArchiveError("archive path escapes the work directory")
                link_target = (target.parent / link_name)
                if not _within(dest, link_target):
                    raise ArchiveError("archive path escapes the work directory")
        try:
            tar.extractall(dest, filter="data")
        except (tarfile.FilterError, tarfile.AbsolutePathError, tarfile.OutsideDestinationError,
                tarfile.SpecialFileError, tarfile.AbsoluteLinkError, tarfile.LinkOutsideDestinationError) as exc:
            raise ArchiveError("archive path escapes the work directory") from exc
        except TypeError as exc:
            # filter="data" is required (Python 3.11.4+ / 3.12+).
            raise ArchiveError("tarfile filter='data' is not available") from exc
        except tarfile.TarError as exc:
            raise ArchiveError(f"invalid tar archive: {exc}") from exc
