"""Merges localization fragments into the per-language catalogs.

Usage:  python tools/merge-strings.py
Reads   src/SonosDesk.Core/Localization/fragments/*.{lang}.json
Writes  src/SonosDesk.Core/Localization/Strings/{lang}.json (sorted, UTF-8)
Fails on duplicate keys with different texts.
"""
import glob
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FRAGMENTS = os.path.join(ROOT, "src", "SonosDesk.Core", "Localization", "fragments")
STRINGS = os.path.join(ROOT, "src", "SonosDesk.Core", "Localization", "Strings")


def main() -> int:
    languages = sorted({os.path.basename(p).rsplit(".", 2)[1] for p in glob.glob(os.path.join(FRAGMENTS, "*.*.json"))})
    errors = 0
    for lang in languages:
        merged: dict[str, str] = {}
        for path in sorted(glob.glob(os.path.join(FRAGMENTS, f"*.{lang}.json"))):
            with open(path, encoding="utf-8-sig") as f:
                data = json.load(f)
            for key, text in data.items():
                if key in merged and merged[key] != text:
                    print(f"[{lang}] conflicting key {key!r} in {os.path.basename(path)}", file=sys.stderr)
                    errors += 1
                merged[key] = text
        with open(os.path.join(STRINGS, f"{lang}.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(dict(sorted(merged.items())), f, ensure_ascii=False, indent=2)
            f.write("\n")
        print(f"{lang}: {len(merged)} keys")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
