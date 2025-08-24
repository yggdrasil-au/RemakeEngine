import builtins
import Engine.cli as cli


def _make_select(responses):
    iterator = iter(responses)

    def fake_select(*args, **kwargs):
        class Prompt:
            def ask(self_inner):
                return next(iterator)
        return Prompt()
    return fake_select


def _make_text(responses):
    iterator = iter(responses)

    def fake_text(*args, **kwargs):
        class Prompt:
            def ask(self_inner):
                return next(iterator)
        return Prompt()
    return fake_text


def test_run_exits_when_no_selection(monkeypatch):
    class StubEngine:
        def __init__(self, path):
            self.calls = 0
            StubEngine.instance = self

        def get_available_games(self):
            self.calls += 1
            return []

    monkeypatch.setattr(cli, "OperationsEngine", StubEngine)
    monkeypatch.setattr(cli.questionary, "select", _make_select([None]))
    monkeypatch.setattr(builtins, "input", lambda *a, **kw: "")

    cli.run()

    assert StubEngine.instance.calls == 1


def test_run_downloads_custom_module(monkeypatch):
    class StubEngine:
        def __init__(self, path):
            self.downloaded_url = None
            StubEngine.instance = self

        def get_available_games(self):
            return []

        def is_git_installed(self):
            return True

        def get_registered_modules(self):
            return {"Example": {"url": "https://example.com/repo.git"}}

        def download_module(self, url):
            self.downloaded_url = url

    monkeypatch.setattr(cli, "OperationsEngine", StubEngine)
    monkeypatch.setattr(
        cli.questionary,
        "select",
        _make_select([
            "Download new module...",
            "Other (Custom URL)...",
            "Exit",
        ]),
    )
    monkeypatch.setattr(
        cli.questionary, "text", _make_text(["https://custom.com/repo.git"])
    )
    monkeypatch.setattr(builtins, "input", lambda *a, **kw: "")

    cli.run()

    assert StubEngine.instance.downloaded_url == "https://custom.com/repo.git"
