#!/usr/bin/env python3
"""
Replace hardcoded English in page XAML with {loc:Str Key=...}.

The pages were written before localization existed and carry ~300 literal strings. This matches
each one against the values already in Strings/en-US, so a replacement only happens where a
translated key genuinely exists - it never invents a key, and it never guesses at a translation.

Anything unmatched is reported rather than touched. That list is the honest remainder: strings
nobody has translated yet, which need a decision (borrow an Android key, or declare a Windows-only
one in build_app_resw.py) rather than a rewrite.

Re-runnable: already-converted attributes are skipped, so it can be run again after new keys land.

Usage:
    python retrofit_pages.py            # report
    python retrofit_pages.py --write
"""

from __future__ import annotations

import argparse
import collections
import pathlib
import re
import xml.etree.ElementTree as ET

HERE = pathlib.Path(__file__).resolve()
WIN_REPO = HERE.parents[2]
APP = WIN_REPO / "src" / "Atomic.App"
STRINGS = APP / "Strings"

# The attributes that carry user-visible text on the controls these pages use.
ATTRIBUTES = ("Text", "Content", "Header", "PlaceholderText", "Description")

# Attribute values that are not prose and must never be swapped for a resource.
#
# Content on a Button is often a glyph or a single symbol, and several pages use Text for a unit
# or a formula. A key whose English happens to equal "K" would otherwise capture every one of them.
# Not a raw string: the private-use range has to be resolved by Python, since that is where the
# Segoe icon glyphs live and they must never be treated as text.
NOT_PROSE = re.compile("^(?:[\\W\\d_]+|[A-Za-z]{1,2}|[-]+)$")

ATTR_PATTERN = re.compile(
    r'\b(' + "|".join(ATTRIBUTES) + r')="([^"{}]+)"'
)


def english_values() -> dict[str, list[str]]:
    """English text -> the keys that carry it, from both resource files."""
    index: dict[str, list[str]] = collections.defaultdict(list)
    for name in ("Resources.resw", "App.resw"):
        path = STRINGS / "en-US" / name
        if not path.is_file():
            continue
        root = ET.fromstring(path.read_text(encoding="utf-8"))
        for data in root.findall("data"):
            key = data.get("name")
            value = (data.find("value").text or "").strip()
            if value:
                index[value].append(key)
    return index


def choose(keys: list[str]) -> str:
    """
    Pick a key when several share the same English.

    Shortest name wins, then alphabetical - a stable rule, so a re-run cannot silently swap one
    synonym for another and produce a diff that looks like a change of meaning.
    """
    return sorted(keys, key=lambda k: (len(k), k))[0]


def pages() -> list[pathlib.Path]:
    return sorted(
        p for p in APP.rglob("*.xaml")
        if "bin" not in p.parts and "obj" not in p.parts and p.name != "App.xaml"
    )


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    index = english_values()
    if not index:
        print("no en-US resources found - run strings_xml_to_resw.py first")
        return 1

    converted = 0
    unmatched: dict[str, list[str]] = collections.defaultdict(list)
    touched: list[str] = []

    for page in pages():
        source = page.read_text(encoding="utf-8")
        page_hits = 0

        def replace(match: re.Match[str]) -> str:
            nonlocal page_hits
            attribute, text = match.group(1), match.group(2)
            stripped = text.strip()

            if NOT_PROSE.match(stripped):
                return match.group(0)

            keys = index.get(stripped)
            if not keys:
                # Only report strings that look like sentences or labels, so the remainder is a
                # list someone can act on rather than every stray attribute in the tree.
                if len(stripped) > 2 and any(c.isalpha() for c in stripped):
                    unmatched[stripped].append(page.name)
                return match.group(0)

            page_hits += 1
            return f'{attribute}="{{loc:Str Key={choose(keys)}}}"'

        rewritten = ATTR_PATTERN.sub(replace, source)

        if page_hits == 0:
            continue

        # The namespace has to be declared once per file, on the root element.
        if "xmlns:loc=" not in rewritten:
            rewritten = re.sub(
                r'(\n\s+xmlns:x="http://schemas\.microsoft\.com/winfx/2006/xaml")',
                r'\1\n    xmlns:loc="using:Atomic.App.Localization"',
                rewritten,
                count=1,
            )
            if "xmlns:loc=" not in rewritten:
                print(f"  ! {page.name}: could not add the loc namespace, skipped")
                continue

        converted += page_hits
        touched.append(f"{page_hits:4d}  {page.relative_to(APP)}")
        if args.write:
            page.write_text(rewritten, encoding="utf-8")

    for line in touched:
        print(line)
    print(f"\n{converted} attributes localized across {len(touched)} pages")

    if unmatched:
        print(f"\n{len(unmatched)} strings have no English resource and were left alone:")
        for text, files in sorted(unmatched.items(), key=lambda e: -len(e[1]))[:40]:
            where = ", ".join(sorted(set(files))[:3])
            print(f'    "{text[:56]}"  ({where})')

    if not args.write:
        print("\nDRY RUN - nothing written.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
