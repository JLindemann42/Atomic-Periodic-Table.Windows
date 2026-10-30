#!/usr/bin/env python3
"""
Compare the reference datasets the two apps carry separately, before either side is overwritten.

The Windows pages hold their rows inline in the code-behind; Android holds them in model/*Model.kt.
They have drifted, and not in one direction: Android's dictionary has roughly twice the entries,
while Windows' Poisson table carries a symbol column Android never had.

This is the same review gate merge_elements.py provided in Phase 1, and it exists for the same
reason. That review is what caught cerium's and astatine's transposed melting points, which a
"newer side wins" rule would have preserved.

Output is a report only. Nothing is written, nothing is merged - the decisions belong to a human.

Usage:
    python compare_datasets.py            # summary
    python compare_datasets.py --detail   # list every differing row
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import kt2cs_model as kt  # noqa: E402

WIN_APP = kt.WIN_REPO / "src" / "Atomic.App"

# dataset -> (Windows file, regex capturing the row's key, the Android schema name, key field index)
#
# Each Windows page authors its rows in whichever style that page's author preferred - positional
# constructor for some, object initializer for others - so the key has to be extracted per page.
WINDOWS_SOURCES: dict[str, tuple[str, str, str, int]] = {
    "Dictionary": (
        "DictionaryPage.xaml.cs",
        r'new DictionaryEntry\(\s*"(?:[^"\\]|\\.)*"\s*,\s*"((?:[^"\\]|\\.)*)"',
        "Dictionary", 1),
    "Equation": (
        "Tables/FormulasPage.xaml.cs",
        r'new Equation \{ Name = "((?:[^"\\]|\\.)*)"',
        "Equation", 0),
    "Constant": (
        "Tables/ConstantsPage.xaml.cs",
        r'new ConstantsEntry\(\s*"((?:[^"\\]|\\.)*)"',
        "Constant", 0),
    "Poisson": (
        "Tables/PoissonPage.xaml.cs",
        r'new Poisson \{ Symbol = "(?:[^"\\]|\\.)*", Name = "((?:[^"\\]|\\.)*)"',
        "Poisson", 0),
    "Geology": (
        "Tables/GeologyPage.xaml.cs",
        r'new Geology \{ Name = "((?:[^"\\]|\\.)*)"',
        "Geology", 0),
}

# Fields one side has and the other does not. Losing these is the failure mode this script exists
# to prevent, so they are named explicitly rather than inferred.
EXTRA_FIELDS = {
    "Poisson": ("Windows", "Symbol", "a two-letter abbreviation used as the chart label"),
}


def windows_keys(spec: tuple[str, str, str, int]) -> list[str]:
    path = WIN_APP / spec[0]
    if not path.is_file():
        return []
    return re.findall(spec[1], path.read_text(encoding="utf-8-sig"))


def android_keys(schema_name: str, key_index: int) -> list[str]:
    schema = kt.SCHEMAS[schema_name]
    rows = kt.parse_rows(
        kt.AND_MODEL / schema["source"],
        schema.get("kotlin_type", schema_name),
        len(schema["fields"]))
    return [kt.convert_arg(row[key_index], "key").strip('"') for row in rows]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--detail", action="store_true")
    args = ap.parse_args()

    print(f"{'dataset':12s} {'win':>5s} {'and':>5s} {'shared':>7s} {'win-only':>9s} {'and-only':>9s}")
    print("-" * 52)

    windows_only_total = 0
    for name, spec in WINDOWS_SOURCES.items():
        win = windows_keys(spec)
        and_ = android_keys(spec[2], spec[3])

        # Case-insensitive, because "Ionization energy" and "Ionization Energy" are the same row
        # and reporting them as a divergence would bury the real ones.
        win_set = {k.lower(): k for k in win}
        and_set = {k.lower(): k for k in and_}

        shared = win_set.keys() & and_set.keys()
        win_only = sorted(win_set[k] for k in win_set.keys() - and_set.keys())
        and_only = sorted(and_set[k] for k in and_set.keys() - win_set.keys())
        windows_only_total += len(win_only)

        print(f"{name:12s} {len(win):5d} {len(and_):5d} {len(shared):7d} "
              f"{len(win_only):9d} {len(and_only):9d}")

        # A duplicate key is not cosmetic: DatasetIndex keys rows by this value and returns the
        # first match, so the second row is unreachable to the agent and uncitable.
        for side, keys in (("WINDOWS", win), ("android", and_)):
            seen, dupes = set(), []
            for key in keys:
                if key.lower() in seen:
                    dupes.append(key)
                seen.add(key.lower())
            for key in dupes:
                print(f"    {side} DUPLICATE KEY  {key}")

        if args.detail:
            for key in win_only:
                print(f"    WINDOWS ONLY  {key}")
            for key in and_only:
                print(f"    android only  {key}")

    print()
    for dataset, (side, field, why) in EXTRA_FIELDS.items():
        print(f"{dataset}.{field} exists only on {side}: {why}")

    print(f"\n{windows_only_total} row(s) exist only on Windows and would be lost by adopting "
          f"Android's tables unchanged.")
    if not args.detail:
        print("Re-run with --detail to see them.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
