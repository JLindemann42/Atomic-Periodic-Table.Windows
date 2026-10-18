#!/usr/bin/env python3
"""
Correct isotope half-lives against an evaluated reference.

``fix_stable_isotopes.py`` decided which isotopes are stable. This decides what the others'
half-lives are, because that pass exposed that a good part of the column is not data:

* **Placeholder ramps.** Lead 181-201 are all "0.1 ms"; Pb-198 is really 2.4 *hours*. Osmium
  172-185 read 5.5 s, 6.0 s, 6.5 s ... 12 s in steps of exactly half a second.
* **A pasted column.** Dysprosium's values sit at the same mass numbers in holmium and erbium
  (144.4 d at A=159, 3.1 min at A=169, 40 s at A=172), which is how holmium came to list six
  stable isotopes. Most of them are inside a factor of two, so size alone does not find them.
* **Values that parse to nothing.** "2.5x102^-21 (s)" for He-9, "0.178.3 (s)" for Li-9: the app
  shows no half-life at all.
* **Gaps** where a measurement exists - including the slots the stable-isotope pass had to blank
  because they were wrongly marked stable.

The reference is the NUBASE-derived isotope table shipped in the ``mendeleev`` package
(MIT licence), read straight out of the wheel so nothing opaque is committed here:

    python -m pip download mendeleev --no-deps -d <dir>
    python fix_half_lives.py --reference <dir>/mendeleev-*.whl

Rules, each limited to a slot the data is demonstrably wrong about, and each needing the
reference to *have* a half-life for that nuclide:

  unreadable   the string parses to nothing and is not a plain "no data"
  disagrees    more than 2x from the reference. Inside 2x is ordinary evaluation and rounding
               difference (Fe-46: 9 ms against 13 ms) and is not touched.
  pasted       the same value at the same mass number in more than one element, and off by
               more than 10% from the reference.
  copied       the same value at the same mass number in three or more elements, where the
               reference has nothing to check it against. One element can own a value; three
               cannot, and in this data all nine such triples were one column pasted across
               dysprosium, holmium and erbium. Blanked to "---" rather than guessed.
  ramp         four consecutive mass numbers whose half-lives rise or fall in equal steps
               (osmium 168-171: 3.5 s, 4.0 s, 4.5 s, 5.0 s). Nobody measures that; someone counted.
               Replaced when the reference disagrees by more than 10%, blanked when it has nothing.
  missing      the slot says "---" and a measurement exists.

Stable and observationally-stable slots are never touched. Other slots the reference has no
half-life for are left alone, wrong or not, and counted. Edits are line by line.

Run it on the tables as they are committed: the pasted-column rules need to see the copies, and a
first pass removes some of them. A second run on its own output changes nothing.

Usage:
    python fix_half_lives.py --reference <wheel-or-elements.db>              # report only
    python fix_half_lives.py --reference ... --write [--dir <tables>] [--verbose]
"""

from __future__ import annotations

import argparse
import glob
import io
import math
import os
import re
import sqlite3
import sys
import tempfile
import zipfile
from collections import Counter, defaultdict

YEAR = 3.15576e7   # the app's own year, so a written value reads back as the reference's seconds

REF_UNIT_SECONDS = {
    "ysec": 1e-24, "zsec": 1e-21, "asec": 1e-18, "psec": 1e-12, "nsec": 1e-9, "usec": 1e-6,
    "msec": 1e-3, "sec": 1.0, "minute": 60.0, "hour": 3600.0, "day": 86400.0, "year": YEAR,
    "kyear": 1e3 * YEAR, "Myear": 1e6 * YEAR, "Gyear": 1e9 * YEAR, "Tyear": 1e12 * YEAR,
    "Pyear": 1e15 * YEAR, "Eyear": 1e18 * YEAR, "Zyear": 1e21 * YEAR, "Yyear": 1e24 * YEAR,
}

# Mirrors IsotopeParser.UnitSeconds, longest spelling first.
UNITS = sorted([
    ("nanoseconds", 1e-9), ("nanosecond", 1e-9), ("microseconds", 1e-6), ("microsecond", 1e-6),
    ("milliseconds", 1e-3), ("millisecond", 1e-3), ("million years", YEAR * 1e6),
    ("million y", YEAR * 1e6), ("billion years", YEAR * 1e9), ("seconds", 1.0), ("second", 1.0),
    ("minutes", 60.0), ("minute", 60.0), ("hours", 3600.0), ("hour", 3600.0), ("days", 86400.0),
    ("day", 86400.0), ("years", YEAR), ("year", YEAR), ("ps", 1e-12), ("ns", 1e-9),
    ("μs", 1e-6), ("µs", 1e-6), ("us", 1e-6), ("ms", 1e-3), ("min", 60.0),
    ("hr", 3600.0), ("s", 1.0), ("h", 3600.0), ("d", 86400.0), ("y", YEAR), ("a", YEAR),
    ("m", 60.0),
], key=lambda p: -len(p[0]))

SCIENTIFIC = re.compile(r"([\d.]+)\s*[×xX*]\s*10\s*\^?\s*([+\-−–]?\d+)")
LEADING = re.compile(r"^\s*([\d.]+)")
GLUED = re.compile(r"(\d)\((\d+)\)")
DIGITS = re.compile(r"[\d.,]")

SLOT = re.compile(r'^(\s*)"iso_half_(\d+)": "(.*)"(,?)(\r?\n?)$')
ELEMENT = re.compile(r'^  "([a-z]+)": \{\r?\n?$')
KEYVAL = re.compile(r'^\s*"(\w+)": "(.*?)",?\r?\n?$')


def to_float(text: str):
    try:
        return float(text)
    except ValueError:
        return None


def unit_factor(tail: str):
    cleaned = tail.lower().replace("(", " ").replace(")", " ").replace("^", " ")
    cleaned = DIGITS.sub(" ", cleaned).strip()
    if not cleaned:
        return None
    for token, seconds in UNITS:
        if cleaned == token or cleaned.startswith(token + " "):
            return seconds
    first = cleaned.split(" ")[0]
    for token, seconds in UNITS:
        if token == first:
            return seconds
    return None


def half_life_seconds(raw: str):
    """The app's reading of a half-life string; None when stable, absent or unreadable."""
    s = raw.strip()
    if not s or "table" in s.lower():
        return None
    s = re.sub("minu-Tes", "minutes", s, flags=re.I)
    s = GLUED.sub(r"\1", s)
    s = s.replace("−", "-").replace("–", "-")
    s = s.lstrip("~≈<> ")

    sci = SCIENTIFIC.search(s)
    if sci:
        mantissa = to_float(sci.group(1))
        if mantissa is None:
            return None
        try:
            magnitude = mantissa * 10.0 ** int(sci.group(2).replace("+", ""))
        except (ValueError, OverflowError):
            return None
        tail = s[sci.end():]
    else:
        m = LEADING.match(s)
        if not m:
            return None
        value = to_float(m.group(1))
        if value is None:
            return None
        magnitude, tail = value, s[m.end():]

    factor = unit_factor(tail)
    return None if factor is None else magnitude * factor


def is_no_data(raw: str) -> bool:
    s = raw.strip().lower()
    return s in ("", "---", "--", "n/a", "na", "unknown", "empty", "???") or s.startswith("---")


def compact(x: float) -> str:
    text = "%.7g" % x
    if "e" in text:
        return text
    return text.rstrip("0").rstrip(".") if "." in text else text


def write_half_life(seconds: float, unit: str, value: float) -> str:
    """The reference's own number and unit, in the spelling the app already reads."""
    plain = {"msec": "ms", "sec": "s", "minute": "min", "hour": "h", "day": "d",
             "nsec": "ns", "psec": "ps", "usec": "µs"}
    if unit in plain:
        return f"{compact(value)} {plain[unit]}"

    # Years up to 99,999 are written out; everything longer, and every time shorter than a
    # picosecond, is scientific.
    in_years = unit.endswith("year")
    total = seconds / YEAR if in_years else seconds
    if in_years and total < 1e5:
        return f"{compact(round(total, 6))} y"
    exponent = int(math.floor(math.log10(total)))
    mantissa = total / 10.0 ** exponent
    suffix = "y" if in_years else "(s)"
    return f"{compact(round(mantissa, 6))}×10^{exponent} {suffix}"


def load_reference(path: str):
    if path.endswith(".whl"):
        tmp = tempfile.mkdtemp(prefix="halflife-ref-")
        with zipfile.ZipFile(path) as zf:
            member = next(n for n in zf.namelist() if n.endswith("mendeleev/elements.db"))
            zf.extract(member, tmp)
            path = os.path.join(tmp, member)

    ref = {}
    db = sqlite3.connect(path)
    try:
        for z, a, half, unit in db.execute(
                "select atomic_number, mass_number, half_life, half_life_unit from isotopes"):
            if half is not None and unit in REF_UNIT_SECONDS:
                ref[(z, a)] = (half * REF_UNIT_SECONDS[unit], unit, half)
    finally:
        db.close()
    return ref


def process(path: str, ref, write: bool):
    with io.open(path, encoding="utf-8", newline="") as fh:
        lines = fh.read().splitlines(keepends=True)

    element, z_of, a_of = None, {}, {}
    slots = []                                         # (line index, element, n, current string)
    for i, line in enumerate(lines):
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
        s = SLOT.match(line)
        if s and element:
            slots.append((i, element, s.group(2), s.group(3)))

    # The same string at the same mass number in several elements is a pasted column.
    seen = defaultdict(set)
    for _, el, n, val in slots:
        a = a_of.get((el, n))
        if a is not None and not is_no_data(val) and "table" not in val.lower():
            seen[(a, val)].add(el)
    shared = {k: els for k, els in seen.items() if len(els) >= 2}

    # Four consecutive mass numbers stepping by a constant amount are a counted sequence.
    by_element = defaultdict(list)
    for _, el, n, val in slots:
        a, secs = a_of.get((el, n)), half_life_seconds(val)
        if a is not None and secs is not None:
            by_element[el].append((a, n, secs))
    ramp_members = set()
    for el, run in by_element.items():
        run.sort()
        for k in range(len(run) - 3):
            w = run[k:k + 4]
            if w[3][0] - w[0][0] != 3:
                continue
            steps = [w[j + 1][2] - w[j][2] for j in range(3)]
            if steps[0] != 0 and all(abs(d - steps[0]) < 1e-9 * abs(steps[0]) for d in steps):
                ramp_members.update((el, x[1]) for x in w)

    tally, changes = Counter(), []
    for i, el, n, val in slots:
        z, a = z_of.get(el), a_of.get((el, n))
        if z is None or a is None or "table" in val.lower():
            continue
        group = shared.get((a, val), ())
        in_ramp = (el, n) in ramp_members

        if (z, a) not in ref:
            if len(group) >= 3 or in_ramp:
                # Three elements cannot all have this half-life, and a counted sequence is not a
                # measurement; nothing here can say what the real value is.
                new = "---"
                kind = "copied" if len(group) >= 3 else "ramp"
                tally[kind] += 1
                m = SLOT.match(lines[i])
                lines[i] = f'{m.group(1)}"iso_half_{n}": "{new}"{m.group(4)}{m.group(5)}'
                changes.append((kind, el, a, val, new))
            elif not is_no_data(val):
                tally["left alone: no reference value"] += 1
            continue

        ref_s, unit, ref_v = ref[(z, a)]
        mine = half_life_seconds(val)

        if mine is None:
            kind = "missing" if is_no_data(val) else "unreadable"
        else:
            ratio = mine / ref_s
            if not 0.5 <= ratio <= 2.0:
                kind = "disagrees"
            elif (len(group) >= 2 or in_ramp) and not 0.9 <= ratio <= 1.1:
                kind = "pasted" if len(group) >= 2 else "ramp"
            else:
                continue

        new = write_half_life(ref_s, unit, ref_v)
        back = half_life_seconds(new)
        if back is None or abs(back / ref_s - 1) > 1e-6:
            print(f"  SKIPPED {el} A={a}: {new!r} does not read back as the reference",
                  file=sys.stderr)
            continue

        m = SLOT.match(lines[i])
        lines[i] = f'{m.group(1)}"iso_half_{n}": "{new}"{m.group(4)}{m.group(5)}'
        tally[kind] += 1
        changes.append((kind, el, a, val, new))

    if write and changes:
        with io.open(path, "w", encoding="utf-8", newline="") as fh:
            fh.write("".join(lines))
    return tally, changes


def main() -> int:
    # Half-life strings carry × and −; a legacy console code page cannot print them.
    sys.stdout.reconfigure(encoding="utf-8")
    here = os.path.dirname(os.path.abspath(__file__))
    default = os.path.normpath(
        os.path.join(here, "..", "..", "src", "Atomic.App", "Assets", "Elements"))

    ap = argparse.ArgumentParser()
    ap.add_argument("--reference", required=True, help="mendeleev wheel, or its elements.db")
    ap.add_argument("--dir", default=default)
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    ref = load_reference(args.reference)
    files = sorted(glob.glob(os.path.join(args.dir, "elements_*.json")))
    if not files:
        print("no element tables under", args.dir, file=sys.stderr)
        return 2

    shapes, first = set(), None
    for path in files:
        tally, changes = process(path, ref, args.write)
        if first is None:
            first = changes
        shapes.add(tuple(sorted(tally.items())))
        print(f"{os.path.basename(path):20} {len(changes):4} slots  " +
              ", ".join(f"{k}: {v}" for k, v in sorted(tally.items())))

    if args.verbose and first:
        print()
        for kind, el, a, old, new in sorted(first, key=lambda c: (c[1], c[2])):
            print(f"  {kind:10} {el:13} A={a:<4} {old!r:>26} -> {new!r}")

    if len(shapes) > 1:
        print("WARNING: the language files disagree about which slots need fixing",
              file=sys.stderr)
    print("\nwritten." if args.write else "\nDRY RUN - nothing written.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
