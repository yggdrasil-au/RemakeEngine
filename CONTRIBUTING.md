# Contributing to Remake Engine

First off, thank you for considering contributing to Remake Engine!
This project thrives thanks to people like you.

> ⚠️ **Important Note:**
> This document covers contributions to the Remake Engine's **core source code only**.
> If you are looking to add support for a new game, **do not contribute it directly to this repository**.
> Instead, game modules should be developed independently as their own repositories and added via Git submodules.

---

## 🚀 Getting Started (Core Engine Contributions)

If you're looking to improve the engine itself—fixing bugs, improving UI/UX, adding engine-level features, etc.—you're in the right place.

### 1. Fork the Repository

Fork the main Remake Engine repository to your own GitHub account.

### 2. Clone Your Fork

```pwsh
git clone https://github.com/YOUR_USERNAME/RemakeEngine.git
cd RemakeEngine
```

### 3. Set Up Your Environment

- Python 3.8+ is required. Development is done on Python 3.13.2.
- Install required packages:

    ```pwsh
    pip install -r requirements.txt
    ```

- You may optionally create a `project.json` file at the root for local testing. This file is user-specific and should be ignored via `.gitignore`.

---

## ✅ What You Can Contribute (Core Engine Only)

Examples of valid contributions to the engine:

- Enhancements to the CLI/interactive interface
- New operation types or execution logic in the engine runtime
- Bug fixes and stability improvements
- Improvements to error handling or placeholder resolution
- Better prompts or validation flows
- Unit tests, documentation, or dev tooling

---

## ❌ What Not to Contribute Here

**Game-specific support should not be added to this repository.**

Each game's support module (e.g., configuration, extraction scripts, assets, tools, etc.) should be maintained as a **separate repository**, which can then be included via Git submodule into a user's setup.

Game modules are:
- Version-controlled independently
- Developed by users or teams on their own timeline
- Plugged into Remake Engine via configuration (e.g., `project.json`)

For guidance on creating a game module, see the Game Module Template and related documentation.

---

## 🛠 Coding Guidelines

- Follow [PEP8](https://peps.python.org/pep-0008/) where practical.
- Use `argparse` for any command-line interfaces.
- Prefer modularity and clear separation of concerns.
- Handle exceptions gracefully with informative messages.
- Use `sys.exit(1)` for failures, `sys.exit(0)` for success.
- Write meaningful commit messages (e.g., `fix(engine): Handle missing project.json gracefully`).

---

## 📦 Making Your Changes

### Create a Branch

```pwsh
git checkout -b fix-placeholder-resolution
```

### Commit Your Work

```pwsh
git add .
git commit -m "fix(placeholder): Improve error on invalid nested keys"
```

### Push to Your Fork

```pwsh
git push origin fix-placeholder-resolution
```

### Open a Pull Request

Go to the original repo, and GitHub should prompt you to open a PR from your fork.
Please include a clear description of what the PR changes, and why.

---

## 📄 Contributor License Agreement (CLA)

By submitting code, you agree to the terms outlined in [CLA.md](./CLA.md).
Pull requests that do not comply may be rejected.

---

## 🧑‍⚖️ Code of Conduct

Please follow the project's Code of Conduct in all interactions.

---

## 🙋 Questions?

Feel free to open an issue if you have questions about contributing, the codebase, or anything else.
