"""Tests for the process runner helpers."""

from __future__ import annotations

import asyncio
import subprocess
from unittest.mock import AsyncMock, Mock, patch

import pytest

from Engine.Core.process_runner import run_process, run_process_async


def test_run_process_success() -> None:
    """A successful command returns a completed process."""

    completed = subprocess.CompletedProcess(["cmd"], 0, stdout="ok", stderr="")
    with patch("Engine.Core.process_runner.subprocess.run", return_value=completed) as sp_run:
        result = run_process(["cmd"])

    assert result.returncode == 0
    assert result.stdout == "ok"
    sp_run.assert_called_once_with(
        ["cmd"], capture_output=True, text=True, timeout=None, check=False, env=None
    )


def test_run_process_timeout() -> None:
    """Timeouts from subprocess.run propagate as exceptions."""

    with patch(
        "Engine.Core.process_runner.subprocess.run",
        side_effect=subprocess.TimeoutExpired(cmd=["cmd"], timeout=1),
    ):
        with pytest.raises(subprocess.TimeoutExpired):
            run_process(["cmd"], timeout=1)


def test_run_process_error() -> None:
    """Non-zero exits raise CalledProcessError when check is True."""

    with patch(
        "Engine.Core.process_runner.subprocess.run",
        side_effect=subprocess.CalledProcessError(1, ["cmd"], output="", stderr="err"),
    ):
        with pytest.raises(subprocess.CalledProcessError):
            run_process(["cmd"], check=True)


def test_run_process_async_success() -> None:
    """Async helper returns completed process on success."""

    process = AsyncMock()
    process.communicate = AsyncMock(return_value=(b"async", b""))
    process.returncode = 0

    create = AsyncMock(return_value=process)
    with patch("Engine.Core.process_runner.asyncio.create_subprocess_exec", create):
        result = asyncio.run(run_process_async(["cmd"]))

    assert result.returncode == 0
    assert result.stdout == "async"
    process.communicate.assert_awaited()


def test_run_process_async_timeout() -> None:
    """Timeouts result in asyncio.TimeoutError and terminate the process."""

    process = AsyncMock()
    process.communicate = AsyncMock(side_effect=asyncio.TimeoutError)
    process.kill = Mock()

    create = AsyncMock(return_value=process)
    with patch("Engine.Core.process_runner.asyncio.create_subprocess_exec", create):
        with pytest.raises(asyncio.TimeoutError):
            asyncio.run(run_process_async(["cmd"], timeout=1))

    process.kill.assert_called_once()


def test_run_process_async_error() -> None:
    """Non-zero async exits raise CalledProcessError when check=True."""

    process = AsyncMock()
    process.communicate = AsyncMock(return_value=(b"", b"err"))
    process.returncode = 1

    create = AsyncMock(return_value=process)
    with patch("Engine.Core.process_runner.asyncio.create_subprocess_exec", create):
        with pytest.raises(subprocess.CalledProcessError):
            asyncio.run(run_process_async(["cmd"], check=True))

