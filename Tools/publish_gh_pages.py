"""Publishes the submission WebGL build to the ``gh-pages`` branch.

GitHub Pages can serve the repository root, a ``docs`` directory, or a branch. A ``docs``
directory is unusable here because the repository already tracks ``Docs`` and Windows treats
the two names as one directory, so publishing targets a branch instead.

The branch is built in a git worktree, which keeps the working tree untouched. The script
commits but never pushes, so the push stays an explicit decision::

    python Tools/publish_gh_pages.py --build Builds/Overbless_Web
    git push origin gh-pages

Add ``--allow-dirty`` only if you know the uncommitted changes are unrelated.
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import List, Optional

BRANCH = "gh-pages"


def run(arguments: List[str], cwd: Path, capture: bool = True) -> str:
    completed = subprocess.run(
        arguments,
        cwd=str(cwd),
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.STDOUT if capture else None,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if completed.returncode != 0:
        output = (completed.stdout or "").strip()
        raise RuntimeError(f"command failed ({completed.returncode}): {' '.join(arguments)}\n{output}")

    return (completed.stdout or "").strip()


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Publish the submission build to gh-pages.")
    parser.add_argument("--build", default="Builds/Overbless_Web", type=Path)
    parser.add_argument("--message")
    parser.add_argument("--allow-dirty", action="store_true")
    return parser.parse_args()


def branch_exists(repository: Path, name: str) -> bool:
    try:
        run(["git", "rev-parse", "--verify", f"refs/heads/{name}"], repository)
        return True
    except RuntimeError:
        return False


def main() -> int:
    arguments = parse_arguments()
    repository = Path(__file__).resolve().parent.parent
    build_directory = (repository / arguments.build).resolve()
    index = build_directory / "index.html"
    if not index.is_file():
        print(f"error: '{arguments.build}' holds no built player.", file=sys.stderr)
        return 2

    source_commit = run(["git", "rev-parse", "HEAD"], repository)
    source_branch = run(["git", "rev-parse", "--abbrev-ref", "HEAD"], repository)
    if not arguments.allow_dirty:
        dirty = run(["git", "status", "--porcelain", "--untracked-files=no"], repository)
        if dirty:
            print(
                "error: the working tree has uncommitted tracked changes. Commit them first so the "
                "published page can name the commit it came from, or pass --allow-dirty.",
                file=sys.stderr,
            )
            return 2

    worktree: Optional[Path] = None
    try:
        worktree = Path(tempfile.mkdtemp(prefix="overbless-gh-pages-"))
        shutil.rmtree(worktree)

        if branch_exists(repository, BRANCH):
            run(["git", "worktree", "add", str(worktree), BRANCH], repository)
        else:
            # An orphan branch keeps the published tree free of source history.
            run(["git", "worktree", "add", "--detach", str(worktree), source_commit], repository)
            run(["git", "checkout", "--orphan", BRANCH], worktree)
            run(["git", "rm", "-rf", "--quiet", "."], worktree)

        for entry in sorted(worktree.iterdir()):
            if entry.name == ".git":
                continue

            if entry.is_dir():
                shutil.rmtree(entry)
            else:
                entry.unlink()

        for entry in sorted(build_directory.iterdir()):
            destination = worktree / entry.name
            if entry.is_dir():
                shutil.copytree(entry, destination)
            else:
                shutil.copy2(entry, destination)

        (worktree / ".nojekyll").write_text("", encoding="utf-8")

        run(["git", "add", "--all"], worktree)
        staged = run(["git", "status", "--porcelain"], worktree)
        if not staged:
            print("nothing to publish: the branch already matches this build")
            return 0

        message = arguments.message or (
            f"publish submission build from {source_branch} {source_commit[:10]}"
        )
        run(["git", "commit", "--quiet", "-m", message], worktree)
        published = run(["git", "rev-parse", "HEAD"], worktree)
        files = run(["git", "ls-files"], worktree).splitlines()
        total = sum((build_directory / name).stat().st_size for name in files if (build_directory / name).is_file())

        print(f"published {len(files)} files ({total / (1024 * 1024):.2f} MB) to '{BRANCH}' as {published[:10]}")
        print(f"source: {source_branch} {source_commit[:10]}")
        print(f"next:   git push origin {BRANCH}")
        print("then:   repository Settings, Pages, deploy from branch 'gh-pages' at root")
        return 0
    finally:
        if worktree is not None and worktree.exists():
            try:
                run(["git", "worktree", "remove", "--force", str(worktree)], repository)
            except RuntimeError:
                shutil.rmtree(worktree, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
