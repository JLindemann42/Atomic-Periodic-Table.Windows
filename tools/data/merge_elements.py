#!/usr/bin/env python3
"""
Merge the Windows per-element JSON files into the Android elements_{lang}.json set.

The two apps drifted from a shared ancestor. Neither side is a superset:

  * Android added 14 properties in v5 (thermal_conductivity, molar_volume, curie_point, ...)
    on all 118 elements, and holds real values where Windows is blank for electrical_type
    (37 elements), element_electrons / element_protons (12 each), poisson_ratio and
    magnetic_type (10 each).

  * Windows kept cas_number (30), eg_number (19), oxidation_state_pos/_neg (9), the NFPA
    triple for 5 elements Android omits, special (2), note_text (1), and a good many extra
    isotope slots on ten transuranics (berkelium +16, einsteinium +15, ...).

So the merge is bidirectional. Android wins any genuine disagreement - it is the actively
maintained dataset - but Windows-only fields are carried over, and Android blanks are filled
from Windows where Windows actually has something.

The trap: the two sides spell "no data" differently. Android normalises to "---". Windows uses
"---", "--", "empty", "unknown", "???", "N/A", "" and even "--- (ms)". Treating those as data
would reintroduce junk over good Android values, so they are normalised before comparison.

Output is written to BOTH repos, so the apps stop diverging, plus merge_report.tsv listing
every field where the two sides disagreed on a real value - that file is for human review
before the result is committed.

Usage:
    python merge_elements.py                      # dry run, writes only the report
    python merge_elements.py --write              # write merged files into the Windows repo
    python merge_elements.py --write --android    # ...and into the Android repo too

--android is opt-in on purpose. The merged data is a strict improvement for both apps, but the
Android repo is a separate shipping product; pushing twelve regenerated data files into whatever
branch it happens to be on is the maintainer's call, not this script's. Run it deliberately, on
a clean branch, and re-run scripts/verify_element_jsons.py there afterwards.
"""

from __future__ import annotations

import argparse
import collections
import re
import json
import pathlib
import sys

# --- locations ---------------------------------------------------------------------------

HERE = pathlib.Path(__file__).resolve()
WIN_REPO = HERE.parents[2]
AND_REPO = WIN_REPO.parents[1] / "Atomic-Periodic-Table.Android"

WIN_ELEMENTS = WIN_REPO / "src" / "Atomic.App" / "Elements"
AND_ASSETS = AND_REPO / "app" / "src" / "main" / "assets"
WIN_OUT = WIN_REPO / "src" / "Atomic.App" / "Assets" / "Elements"
REPORT = HERE.parent / "merge_report.tsv"

LANGUAGES = ["af", "de", "en", "es", "fil", "fr", "hi", "it", "pt", "sv", "ur", "zh"]

# --- rules -------------------------------------------------------------------------------

# Android keys caesium the British way; Windows filed it as cesium.json. The merged data uses
# the Android spelling. "cesium" survives as an entity-resolver alias so the American spelling
# still resolves in chat - that lives in the engine, not here.
KEY_ALIASES = {"cesium": "caesium"}

# Every spelling of "no data" seen across the two datasets. Compared case-insensitively after
# stripping. Anything matching is treated as absent rather than as a value.
BLANK_MARKERS = {
    "", "-", "--", "---", "----", "n/a", "na", "none", "null",
    "empty", "unknown", "?", "??", "???",
}

# Windows also writes dashes with the unit still attached - "--- (pm)", "--- (kJ/mol)",
# "--- (°C)" and so on, 307 occurrences across twelve distinct units. Enumerating them is
# hopeless; match the shape instead.
BLANK_WITH_UNIT = re.compile(r"^-{1,4}\s*\([^)]*\)$")

# Fields Android does not carry that must survive the merge.
WINDOWS_ONLY_KEEP = {
    "cas_number", "eg_number",
    "oxidation_state_pos", "oxidation_state_neg",
    "health", "flammability", "instability",
    "special",
}

# Windows-only fields deliberately dropped.
WINDOWS_ONLY_DROP = {
    "note_text",  # a stray "test" string on hydrogen
}

# Fields never back-filled from Windows, even where Android is blank.
#
# link/wikilink are element image and article URLs. Android's v5 pass curated them and records
# "---" where it genuinely has none; the older Windows table has at least one mis-paste -
# technetium points at a photo of gadolinite, a gadolinium/yttrium mineral. Importing that would
# put the wrong picture on the element page. There is exactly one element where this rule loses
# anything, and that one is wrong.
NEVER_FILL_FROM_WINDOWS = {"link", "wikilink"}

# The 14 properties Android gained in v5. Listed for the summary only.
V5_FIELDS = [
    "curie_point", "electron_affinity", "electronegativity_allen", "human_body",
    "meteorites", "molar_heat_capacity", "molar_volume", "neel_point",
    "refractive_index", "space_group_name", "space_group_number",
    "thermal_conductivity", "thermal_expansion", "work_function",
]

# Only these seven fields are translated per language; everything else is identical across the
# twelve files. Windows-sourced additions are language-neutral (CAS numbers, oxidation states,
# isotope masses) so they replicate verbatim - but never overwrite a localised field.
LOCALISED_FIELDS = {
    "element", "element_group", "description", "element_appearance",
    "element_phase", "electrical_type", "magnetic_type",
}


def is_blank(value) -> bool:
    if value is None:
        return True
    if isinstance(value, str):
        s = value.strip()
        return s.lower() in BLANK_MARKERS or bool(BLANK_WITH_UNIT.match(s))
    return False


def isotope_index(field: str) -> int | None:
    """Slot number for an isotope field such as iso_half_12 or decay_type_3."""
    parts = field.rsplit("_", 1)
    if len(parts) == 2 and parts[1].isdigit():
        if field.startswith(("iso_", "decay_type_")):
            return int(parts[1])
    return None


def load_windows() -> dict[str, dict]:
    out = {}
    for path in sorted(WIN_ELEMENTS.glob("*.json")):
        key = KEY_ALIASES.get(path.stem, path.stem)
        out[key] = json.loads(path.read_text(encoding="utf-8-sig"))
    return out


def load_android(lang: str) -> dict[str, dict]:
    return json.loads((AND_ASSETS / f"elements_{lang}.json").read_text(encoding="utf-8"))


def merge_element(key: str, win: dict, andr: dict, lang: str, report: list) -> dict:
    """Merge one element. Android is the base; Windows fills gaps and adds its own fields."""
    merged = dict(andr)

    for field, wvalue in win.items():
        if field in WINDOWS_ONLY_DROP:
            continue

        # Never let language-neutral Windows data overwrite a translated field.
        if field in LOCALISED_FIELDS and lang != "en":
            continue

        if field not in andr:
            slot = isotope_index(field)
            if slot is not None or field in WINDOWS_ONLY_KEEP:
                if not is_blank(wvalue):
                    merged[field] = wvalue
            # Any other unexpected Windows-only field is dropped, and reported once.
            elif lang == "en":
                report.append((key, field, "dropped-unknown-windows-field", repr(wvalue), ""))
            continue

        avalue = andr[field]
        if wvalue == avalue:
            continue

        if is_blank(avalue) and not is_blank(wvalue):
            if field in NEVER_FILL_FROM_WINDOWS:
                if lang == "en":
                    report.append((key, field, "refused-fill-see-NEVER_FILL", repr(wvalue), repr(avalue)))
                continue
            merged[field] = wvalue
            if lang == "en":
                report.append((key, field, "filled-android-blank", repr(wvalue), repr(avalue)))
        elif is_blank(wvalue):
            pass  # Android has data, Windows does not. Nothing to do.
        elif lang == "en":
            # Genuine disagreement between two real values. Android wins; log for review.
            report.append((key, field, "conflict-android-wins", repr(wvalue), repr(avalue)))

    return merged


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true", help="write merged files into the Windows repo")
    ap.add_argument("--android", action="store_true",
                    help="also write into the Android repo (see module docstring)")
    args = ap.parse_args()

    if not WIN_ELEMENTS.is_dir():
        print(f"missing: {WIN_ELEMENTS}", file=sys.stderr)
        return 1
    if not AND_ASSETS.is_dir():
        print(f"missing: {AND_ASSETS}", file=sys.stderr)
        return 1

    windows = load_windows()
    report: list[tuple] = []
    stats = collections.Counter()

    for lang in LANGUAGES:
        android = load_android(lang)

        missing = set(windows) - set(android)
        if missing:
            print(f"  !! {lang}: elements absent from Android: {sorted(missing)}", file=sys.stderr)

        merged = {}
        for key, avalue in android.items():
            wvalue = windows.get(key)
            merged[key] = merge_element(key, wvalue, avalue, lang, report) if wvalue else dict(avalue)

        added = sum(len(set(merged[k]) - set(android[k])) for k in merged)
        stats[lang] = added

        if args.write:
            targets = [WIN_OUT] + ([AND_ASSETS] if args.android else [])
            for out_dir in targets:
                out_dir.mkdir(parents=True, exist_ok=True)
                (out_dir / f"elements_{lang}.json").write_text(
                    json.dumps(merged, ensure_ascii=False, indent=2, sort_keys=False) + "\n",
                    encoding="utf-8",
                )
        print(f"  {lang}: {len(merged)} elements, +{added} fields from Windows")

    REPORT.write_text(
        "element\tfield\taction\twindows_value\tandroid_value\n"
        + "\n".join("\t".join(r) for r in report)
        + "\n",
        encoding="utf-8",
    )

    kinds = collections.Counter(r[2] for r in report)
    print(f"\nreport: {REPORT}  ({len(report)} rows)")
    for kind, n in kinds.most_common():
        print(f"  {n:5d}  {kind}")

    en = load_android("en")
    print(f"\nv5 fields now present on Windows: {len(V5_FIELDS)} "
          f"(e.g. iron.thermal_conductivity = {en['iron'].get('thermal_conductivity')!r})")

    if not args.write:
        print("\nDRY RUN - nothing written. Review the report, then re-run with --write.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
