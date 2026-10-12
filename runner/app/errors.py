"""Runner errors mapped to the HTTP API."""

from __future__ import annotations


class RunnerError(Exception):
    def __init__(self, message: str, code: str, status: int) -> None:
        super().__init__(message)
        self.message = message
        self.code = code
        self.status = status


def invalid(message: str) -> RunnerError:
    return RunnerError(message, "invalid", 400)


def too_big(message: str) -> RunnerError:
    return RunnerError(message, "too_big", 413)


def unsupported_version(message: str) -> RunnerError:
    return RunnerError(message, "unsupported_version", 422)


def missing_files(message: str) -> RunnerError:
    return RunnerError(message, "missing_files", 422)


def kicad_failed(message: str) -> RunnerError:
    return RunnerError(message, "kicad_cli_failed", 500)


def timed_out(message: str) -> RunnerError:
    return RunnerError(message, "timeout", 504)
