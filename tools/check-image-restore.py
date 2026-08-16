#!/usr/bin/env python3
"""Every project the image builds must be in the Dockerfile's restore layer.

The restore layer copies manifests one at a time so it survives every change that is
not a dependency change (see the Dockerfile's own comment). That caching is worth
keeping and the hand-written list is the price — but the list is only correct until
somebody adds a project, and the failure is invisible until after merge: CI restores
the whole solution and passes, then `docker build`'s `--no-restore` publish dies on a
missing assets file, and the image never reaches the registry.

That is exactly what happened when MUI.I3 arrived. `main` was green, the publish
workflow failed twice, and production sat on a stale image until somebody read the
logs. This turns that into a PR-time failure with the fix in the message.

It resolves MUI.Web's transitive ProjectReference closure rather than checking a
directory listing, so a new CLI or tool the image does not build needs no allowlist
entry and no thought at all.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ENTRY = ROOT / "src" / "MUI.Web" / "MUI.Web.csproj"
DOCKERFILE = ROOT / "Dockerfile"

REFERENCE = re.compile(r'<ProjectReference\s+Include="([^"]+)"', re.IGNORECASE)


def closure(entry: Path) -> set[Path]:
    """Every .csproj reachable from `entry`, itself included."""
    seen: set[Path] = set()
    queue = [entry.resolve()]

    while queue:
        project = queue.pop()

        if project in seen:
            continue

        seen.add(project)

        for raw in REFERENCE.findall(project.read_text(encoding="utf-8")):
            queue.append((project.parent / raw.replace("\\", "/")).resolve())

    return seen


def main() -> int:
    dockerfile = DOCKERFILE.read_text(encoding="utf-8")
    missing = []

    for project in sorted(closure(ENTRY)):
        relative = project.relative_to(ROOT).as_posix()

        # The COPY is written with padding for alignment, so match the path rather than
        # the line. A manifest copied by any spelling of COPY counts as restored.
        if relative not in dockerfile:
            missing.append(relative)

    if not missing:
        return 0

    print(
        "The Dockerfile's restore layer is missing a project the image builds.\n"
        "Its `dotnet publish --no-restore` will fail with NETSDK1004 — after merge,\n"
        "in the publish workflow rather than here, leaving production on a stale image.\n",
        file=sys.stderr,
    )

    for relative in missing:
        directory = Path(relative).parent.as_posix()
        print(f"  add:  COPY {relative}  {directory}/", file=sys.stderr)

    return 1


if __name__ == "__main__":
    raise SystemExit(main())
