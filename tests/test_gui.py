from typing import Literal
import Engine.gui as gui
from unittest.mock import MagicMock


def test_run_invokes_mainloop(monkeypatch) -> None:
    fake_app = MagicMock()
    fake_class = MagicMock(return_value=fake_app)
    monkeypatch.setattr(gui, "RemakeEngineGui", fake_class)

    gui.run()

    fake_class.assert_called_once_with()
    fake_app.mainloop.assert_called_once_with()


def _make_app(monkeypatch, engine) -> gui.RemakeEngineGui:
    def fake_init(self) -> None:
        self.engine = engine
        self._console_write = MagicMock()
    monkeypatch.setattr(gui.RemakeEngineGui, "__init__", fake_init)
    return gui.RemakeEngineGui()


def test_download_module_missing_url(monkeypatch) -> None:
    class StubEngine:
        def is_git_installed(self) -> Literal[True]:
            return True
    engine = StubEngine()
    app = _make_app(monkeypatch=monkeypatch, engine=engine)

    show_error = MagicMock()
    monkeypatch.setattr(gui.messagebox, "showerror", show_error)

    app._download_module(url="")

    show_error.assert_called_once()
    assert "No Git URL" in show_error.call_args[0][1]


def test_download_module_requires_git(monkeypatch) -> None:
    class StubEngine:
        def is_git_installed(self) -> Literal[False]:
            return False
    engine = StubEngine()
    app = _make_app(monkeypatch=monkeypatch, engine=engine)

    show_error = MagicMock()
    monkeypatch.setattr(gui.messagebox, "showerror", show_error)

    app._download_module(url="https://github.com/Superposition28/TheSimpsonsGame-PS3.git")

    show_error.assert_called_once()
    assert "Git is not installed" in show_error.call_args[0][1]
