#!/usr/bin/env python3
"""Every setting the site reads from the environment must reach the container.

Compose passes only what a service's `environment:` map names. A variable an operator
sets in `.env` and that nothing forwards is configuring nothing — the site never sees
it, the default stands, and the only symptom is a knob that silently does not work.

That is what happened to MUI_ARES_ENABLED: `docs/deploy.md` told operators to set it
to false to turn the pass off with credentials still in place, and compose.yaml never
forwarded it, so the documented off switch did nothing at all. Nothing failed. There
was no error to read. A reviewer caught it.

The check reads the variable names out of CrawlerSettings — where they are already
declared as constants, precisely so they are stated once — and asserts the web
service's environment map mentions each one.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SETTINGS = ROOT / "src" / "MUI.Web" / "Data" / "CrawlerSettings.cs"
COMPOSE = ROOT / "compose.yaml"

# `public const string FooEnvironmentVariable = "MUI_FOO";`
DECLARED = re.compile(
    r'EnvironmentVariable\s*=\s*"(MUI_[A-Z0-9_]+)"'
)


def main() -> int:
    declared = sorted(set(DECLARED.findall(SETTINGS.read_text(encoding="utf-8"))))

    if not declared:
        print(
            f"Found no environment variables declared in {SETTINGS.relative_to(ROOT)}.\n"
            "The pattern this check greps for has moved; fix the check rather than\n"
            "deleting it, or the next unwired setting ships silently.",
            file=sys.stderr,
        )
        return 1

    compose = COMPOSE.read_text(encoding="utf-8")
    missing = [name for name in declared if name not in compose]

    if not missing:
        return 0

    print(
        "compose.yaml does not forward a setting the site reads from the environment.\n"
        "Compose passes only what the `environment:` map names, so an operator setting\n"
        "this in .env would be configuring nothing, with no error anywhere.\n",
        file=sys.stderr,
    )

    for name in missing:
        print(f"  add to the web service's environment:  {name}: ${{{name}:-}}", file=sys.stderr)

    return 1


if __name__ == "__main__":
    raise SystemExit(main())
