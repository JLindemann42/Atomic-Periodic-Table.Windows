#!/usr/bin/env python3
"""
Correct which isotopes the element tables mark as stable.

The tables carry a half-life per isotope slot (``iso_half_N``). Whether an isotope is stable is
read from that string, and it was wrong in three different ways, none of which a parser can see:

1. **Stable written as "N/A".** Atomic numbers 70-82 (Yb-Pb) use "N/A" where every other element
   uses "Stable", so gold had 40 known isotopes and none of them stable. But "N/A" is not a
   stable marker: the same block also holds Hf-175, Tl-197..200 and Re-187, which are radioactive
   with a half-life nobody filled in. Reading "N/A" as "stable" would have been wrong in seven
   places, so each slot is decided from the nuclide, not the marker.

2. **Stable isotopes carrying a metastable state's half-life.** Ir-191 and Ir-193 hold 4.94 d and
   10.53 d - the half-lives of their isomers, not of the stable ground states.

3. **Radioactive isotopes marked stable.** Dysprosium's half-life column was copied over holmium
   and erbium, so holmium listed six stable isotopes (it has one) and erbium eight (it has six,
   and only two of the eight were among them).

The reference is the table of stable nuclides, by element, in two tiers. STABLE is the nuclides
never seen to decay. OBSERVED is the ones whose decay *has* been seen but is slower than 10^15
years (Ca-48, Kr-78, Xe-124, Os-186...), which the data calls "Observationally Stable" for 25
entries already. A value in the second tier is only replaced when it is plainly wrong - a half-life
of seconds on something that outlives the universe - and a bare "---" is left as "no data",
because promoting it would claim more than anyone has measured.

Edits are made line by line so the files stay byte-identical elsewhere. Re-runnable.

Usage:
    python fix_stable_isotopes.py                 # report only
    python fix_stable_isotopes.py --write
    python fix_stable_isotopes.py --dir <path>    # another copy of the tables
"""

from __future__ import annotations

import argparse
import glob
import io
import json
import os
import re
import sys
from collections import Counter

# Z -> mass numbers that are stable, or observationally stable with no decay ever seen.
STABLE_AND_OBSERVED: dict[int, set[int]] = {
    1: {1, 2}, 2: {3, 4}, 3: {6, 7}, 4: {9}, 5: {10, 11}, 6: {12, 13}, 7: {14, 15},
    8: {16, 17, 18}, 9: {19}, 10: {20, 21, 22}, 11: {23}, 12: {24, 25, 26}, 13: {27},
    14: {28, 29, 30}, 15: {31}, 16: {32, 33, 34, 36}, 17: {35, 37}, 18: {36, 38, 40},
    19: {39, 41}, 20: {40, 42, 43, 44, 46, 48}, 21: {45}, 22: {46, 47, 48, 49, 50}, 23: {50, 51},
    24: {50, 52, 53, 54}, 25: {55}, 26: {54, 56, 57, 58}, 27: {59}, 28: {58, 60, 61, 62, 64},
    29: {63, 65}, 30: {64, 66, 67, 68, 70}, 31: {69, 71}, 32: {70, 72, 73, 74, 76}, 33: {75},
    34: {74, 76, 77, 78, 80, 82}, 35: {79, 81}, 36: {78, 80, 82, 83, 84, 86}, 37: {85},
    38: {84, 86, 87, 88}, 39: {89}, 40: {90, 91, 92, 94, 96}, 41: {93},
    42: {92, 94, 95, 96, 97, 98, 100}, 44: {96, 98, 99, 100, 101, 102, 104}, 45: {103},
    46: {102, 104, 105, 106, 108, 110}, 47: {107, 109}, 48: {106, 108, 110, 111, 112, 114, 116},
    49: {113}, 50: {112, 114, 115, 116, 117, 118, 119, 120, 122, 124}, 51: {121, 123},
    52: {120, 122, 123, 124, 125, 126, 128, 130}, 53: {127},
    54: {124, 126, 128, 129, 130, 131, 132, 134, 136}, 55: {133},
    56: {130, 132, 134, 135, 136, 137, 138}, 57: {139}, 58: {136, 138, 140, 142}, 59: {141},
    60: {142, 143, 145, 146, 148, 150}, 62: {144, 149, 150, 152, 154}, 63: {153},
    64: {154, 155, 156, 157, 158, 160}, 65: {159}, 66: {156, 158, 160, 161, 162, 163, 164},
    67: {165}, 68: {162, 164, 166, 167, 168, 170}, 69: {169},
    70: {168, 170, 171, 172, 173, 174, 176}, 71: {175}, 72: {174, 176, 177, 178, 179, 180},
    73: {181}, 74: {180, 182, 183, 184, 186}, 75: {185}, 76: {184, 186, 187, 188, 189, 190, 192},
    77: {191, 193}, 78: {192, 194, 195, 196, 198}, 79: {197},
    80: {196, 198, 199, 200, 201, 202, 204}, 81: {203, 205}, 82: {204, 206, 207, 208}, 83: {209},
}

# Decay has been observed for these, and is slower than 10^15 years. A subset of the table above.
OBSERVED: dict[int, set[int]] = {
    20: {48}, 23: {50}, 32: {76}, 34: {82}, 36: {78}, 40: {96}, 42: {100}, 48: {113, 116},
    52: {128, 130}, 54: {124, 136}, 56: {130}, 60: {150}, 62: {148}, 64: {152}, 72: {174},
    74: {180}, 76: {186},
}

# A measured half-life at or beyond this is a real measurement of a decay, however slow, and is
# neither promoted to "Stable" nor demoted from it.
OBSERVED_DECAY_YEARS = 1e12

STABLE_LABEL = "Stable"
OBSERVED_LABEL = "Observationally Stable"
UNKNOWN_LABEL = "---"

SLOT = re.compile(r'^(\s*)"iso_half_(\d+)": "(.*)"(,?)(\r?\n?)$')
ELEMENT = re.compile(r'^  "([a-z]+)": \{\r?\n?$')
KEYVAL = re.compile(r'^\s*"(\w+)": "(.*?)",?\r?\n?$')

UNIT_YEARS = {
    "y": 1.0, "year": 1.0, "years": 1.0, "a": 1.0,
    "d": 1 / 365.25, "day": 1 / 365.25, "days": 1 / 365.25,
    "h": 1 / 8766, "hour": 1 / 8766, "hours": 1 / 8766,
    "min": 1 / 525960, "minutes": 1 / 525960, "m": 1 / 525960,
    "s": 1 / 31557600, "sec": 1 / 31557600, "seconds": 1 / 31557600, "(s)": 1 / 31557600,
    "ms": 1 / 3.15576e10,
}


def is_stable_marker(value: str) -> bool:
    return "table" in value.lower()


def years_of(value: str) -> float | None:
    """Best-effort half-life in years; None when it cannot be read as a time."""
    text = value.replace("�", "x").replace("×", "x").strip().lower()
    m = re.match(r"^([\d.]+)(?:\s*x\s*10\^?\s*(-?\d+))?\s*\(?([a-z]+)\)?", text)
    if not m:
        return None
    try:
        number = float(m.group(1)) * (10 ** int(m.group(2)) if m.group(2) else 1)
    except ValueError:
        return None
    unit = UNIT_YEARS.get(m.group(3))
    return None if unit is None else number * unit


def process(path: str, write: bool) -> tuple[Counter, list[tuple]]:
    with io.open(path, encoding="utf-8", newline="") as fh:
        lines = fh.read().splitlines(keepends=True)

    # Pass 1: per element, its Z and each slot's mass number.
    element = None
    z_of: dict[str, int] = {}
    a_of: dict[tuple[str, str], int] = {}
    for line in lines:
        m = ELEMENT.match(line)
        if m:
            element = m.group(1)
            continue
        kv = KEYVAL.match(line)
        if element and kv:
            key, val = kv.group(1), kv.group(2)
            if key == "element_protons" and val.isdigit():
                z_of[element] = int(val)
            elif key.startswith("iso_A_") and val.isdigit():
                a_of[(element, key[6:])] = int(val)

    tally: Counter = Counter()
    report: list[tuple] = []
    element = None
    for i, line in enumerate(lines):
        m = ELEMENT.match(line)
        if m:
            element = m.group(1)
            continue
        slot = SLOT.match(line)
        if not slot or element is None:
            continue

        indent, n, value, comma, eol = slot.groups()
        z = z_of.get(element)
        a = a_of.get((element, n))
        if z is None or a is None or z not in STABLE_AND_OBSERVED:
            continue

        truth = a in STABLE_AND_OBSERVED[z]
        observed_tier = a in OBSERVED.get(z, ())
        flagged = is_stable_marker(value)
        decay_years = None if flagged else years_of(value)
        observed_decay = decay_years is not None and decay_years >= OBSERVED_DECAY_YEARS

        new = None
        if truth and observed_tier and not flagged and not observed_decay:
            # Only a plainly wrong value is replaced; "no data" stays "no data".
            if value in ("---", ""):
                continue
            kind = "observed-decay nuclide with a short half-life"
            new = OBSERVED_LABEL
        elif truth and not flagged and not observed_decay:
            kind = ("N/A written for stable" if value == "N/A"
                    else "no half-life recorded" if value in ("---", "")
                    else "stable carrying a short half-life")
            new = STABLE_LABEL
        elif not truth and flagged:
            kind = "radioactive marked stable"
            new = UNKNOWN_LABEL
        else:
            continue

        tally[kind] += 1
        report.append((kind, element, f"{a}", value, new))
        lines[i] = f'{indent}"iso_half_{n}": "{new}"{comma}{eol}'

    if write and report:
        with io.open(path, "w", encoding="utf-8", newline="") as fh:
            fh.write("".join(lines))
    return tally, report


def main() -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    default = os.path.normpath(os.path.join(here, "..", "..", "shared", "elements"))

    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=default)
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    files = sorted(glob.glob(os.path.join(args.dir, "elements_*.json")))
    if not files:
        print("no element tables under", args.dir, file=sys.stderr)
        return 2

    first = None
    for path in files:
        tally, report = process(path, args.write)
        if first is None:
            first = report
        print(f"{os.path.basename(path):20} {sum(tally.values()):3} slots  " +
              ", ".join(f"{k}: {v}" for k, v in sorted(tally.items())))

    if args.verbose and first:
        print()
        for kind, el, a, old, new in sorted(first):
            print(f"  {kind:36} {el:13} A={a:<4} {old!r:>26} -> {new!r}")

    # Every file must make the same decisions - the isotope block is not translated.
    shapes = {tuple(sorted(Counter(k for k, *_ in process(p, False)[1]).items())) for p in files}
    if not args.write and len(shapes) > 1:
        print("WARNING: the language files disagree about which slots need fixing", file=sys.stderr)

    print("\nwritten." if args.write else "\nDRY RUN - nothing written.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
