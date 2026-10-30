#!/usr/bin/env python3
"""
Convert Android's static reference tables (model/*Model.kt) into C# for Atomic.Core.

These eleven datasets are what DatasetIndex makes searchable, and the AI corpus asks questions
whose answers come out of them. That makes them part of the engine's contract, not decoration:
if the Windows dictionary has 81 entries and Android's has 151, seventy corpus rows fail.

Like the localization converter this is not a one-off. It is the mechanism for pulling future
Android data changes across, so it is idempotent and regenerates every file from scratch.

Two deliberate non-goals:

  * It does not translate. These tables are English-only on Android and stay English-only here;
    the answer composer flags that rather than pretending otherwise.

  * It does not merge. Where the Windows pages carry rows Android lacks, that is a data question
    for compare_datasets.py to surface and a human to settle - exactly as the element merge in
    Phase 1 worked. Silently preferring either side is how the transposed cerium melting point
    survived four years.

Usage:
    python kt2cs_model.py            # report
    python kt2cs_model.py --write
"""

from __future__ import annotations

import argparse
import os
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve()
WIN_REPO = HERE.parents[2]
# The Android checkout these convert from. A sibling folder by default, as on the development
# machine; CI checks Android out elsewhere and says where with ANDROID_REPO (roadmap 0.4).
ANDROID_REPO = (pathlib.Path(os.environ["ANDROID_REPO"]) if os.environ.get("ANDROID_REPO")
                else WIN_REPO.parents[1] / "Atomic-Periodic-Table.Android")
AND_MODEL = ANDROID_REPO / "app" / "src" / "main" / \
    "java" / "com" / "jlindemann" / "science" / "model"
OUT_DIR = WIN_REPO / "src" / "Atomic.Core" / "Models"

# Kotlin data class -> the C# record it maps onto, and the property names in declaration order.
#
# The names are not always the Kotlin ones. "cristal" is a misspelling of "crystal" that would be
# copied into new C# code for no reason, and "short" is a C# keyword. Renaming here rather than at
# every call site keeps the divergence in one place; DIVERGENCES records it.
SCHEMAS: dict[str, dict] = {
    "Dictionary": {
        "source": "DictionaryModel.kt",
        "fields": [("Category", "string"), ("Heading", "string"),
                   ("Text", "string"), ("Wiki", "string")],
        "summary": "A glossary entry: a term, the field it belongs to, and its definition.",
    },
    "Equation": {
        "source": "EquationModel.kt",
        # R.drawable.e_density is an Android resource id. Windows ships the same PNGs under
        # Equations/, named identically, so the drawable name is the portable form of the
        # reference and the app appends the extension.
        "fields": [("Title", "string"), ("Category", "string"),
                   ("Image", "string"), ("Description", "string")],
        "summary": "A formula: its name, the image that renders it, and what the symbols mean.",
    },
    "Constant": {
        "source": "ConstantsModel.kt",
        "kotlin_type": "Constants",
        "fields": [("Name", "string"), ("Value", "string"), ("Unit", "string"),
                   ("Info", "string"), ("Category", "string")],
        "summary": "A physical or mathematical constant. Value is text because several carry "
                   "an exponent or a tolerance that no double can hold.",
    },
    "Poisson": {
        "source": "PoissonModel.kt",
        "fields": [("Name", "string"), ("Start", "double"),
                   ("End", "double"), ("Type", "string")],
        "summary": "A material's Poisson's ratio, as the range it is quoted over.",
    },
    "Geology": {
        "source": "GeologyModel.kt",
        "fields": [("Name", "string"), ("Type", "string"), ("Group", "string"),
                   ("Color", "string"), ("Streak", "string"), ("Crystal", "string"),
                   ("Hardness", "string"), ("Luster", "string"), ("Density", "string"),
                   ("Magnetism", "string"), ("Hydrochloride", "string")],
        "summary": "A mineral or rock, with the properties a hand-sample identification uses.",
    },
    "Ion": {
        "source": "IonModel.kt",
        "fields": [("Name", "string"), ("Symbol", "string"), ("Count", "int")],
        "summary": "How many ionization energies are known for an element.",
    },
    "Indicator": {
        "source": "IndicatorModel.kt",
        "fields": [("Name", "string"), ("Acid", "string"), ("AcidColor", "string"),
                   ("Neutral", "string"), ("NeutralColor", "string"),
                   ("Alkali", "string"), ("AlkaliColor", "string")],
        "summary": "A pH indicator and the colours it shows across its transition range.",
    },
}

# Columns Windows has and Android does not, harvested from the Windows page so regenerating from
# Android cannot silently drop them. compare_datasets.py is what identifies these.
#
# dataset -> (property, type, default, Windows file, regex capturing (key, value), why)
EXTRA_COLUMNS: dict[str, tuple] = {
    "Poisson": (
        "Symbol", "string", '""',
        "Tables/PoissonPage.xaml.cs",
        r'new Poisson \{ Symbol = "((?:[^"\\]|\\.)*)", Name = "((?:[^"\\]|\\.)*)"',
        "the two-letter chart label; Android's band chart has no legend and never needed one",
    ),
}

# Recorded in docs/porting-notes.md; repeated here so the generator is self-explaining.
DIVERGENCES = [
    ("Geology.cristal -> Crystal", "the Kotlin field is a misspelling"),
    ("Ion.short -> Symbol", "'short' is a C# keyword and would need @short everywhere"),
    ("Equation.equation: Int -> Image: string", "R.drawable ids do not exist off Android; both "
     "apps ship the same PNG filenames"),
    ("Constants -> Constant", "the Kotlin type is plural for a single row"),
]


class KotlinScanner:
    """
    Balanced-paren scanner that knows Kotlin string literals.

    A regex cannot do this: the dictionary text contains parentheses, commas, escaped quotes and
    at least one string ending in a backslash-escaped character. Counting depth while skipping
    string bodies is the only way to find where a row actually ends.
    """

    def __init__(self, text: str):
        self.text = text
        self.i = 0

    def skip_string(self) -> str:
        """Consume a "..." literal at the cursor and return its raw body (escapes intact)."""
        assert self.text[self.i] == '"'
        self.i += 1
        start = self.i
        while self.i < len(self.text):
            c = self.text[self.i]
            if c == "\\":
                self.i += 2
                continue
            if c == '"':
                body = self.text[start:self.i]
                self.i += 1
                return body
            self.i += 1
        raise ValueError("unterminated string literal")

    def read_args(self) -> list[str]:
        """At an opening paren: consume through the match, returning top-level argument text."""
        assert self.text[self.i] == "("
        self.i += 1
        depth, args, start = 1, [], self.i
        while self.i < len(self.text):
            c = self.text[self.i]
            if c == '"':
                self.skip_string()
                continue
            if c in "([{":
                depth += 1
            elif c in ")]}":
                depth -= 1
                if depth == 0:
                    args.append(self.text[start:self.i])
                    self.i += 1
                    return args
            elif c == "," and depth == 1:
                args.append(self.text[start:self.i])
                start = self.i + 1
            self.i += 1
        raise ValueError("unterminated argument list")


def kotlin_string_to_csharp(body: str) -> str:
    """
    Re-escape a Kotlin string body for C#.

    The two languages agree on \\n \\t \\" and \\\\. They disagree on \\$, which Kotlin needs
    because $ starts a template and C# does not (outside an interpolated string). Unicode escapes
    are \\uXXXX in both.
    """
    out, i = [], 0
    while i < len(body):
        if body[i] == "\\" and i + 1 < len(body):
            nxt = body[i + 1]
            if nxt == "$":
                out.append("$")
            elif nxt in 'ntr"\\u':
                out.append(body[i:i + 2])
            else:
                # Kotlin allows escapes C# rejects (\', \b). Emit the bare character.
                out.append(nxt)
            i += 2
        else:
            out.append(body[i])
            i += 1
    return "".join(out)


NUMBER = re.compile(r"^[+-]?[\d_]+(?:\.[\d_]+)?(?:[eE][+-]?\d+)?$")


def convert_arg(raw: str, kind: str) -> str:
    """One Kotlin argument -> a C# literal."""
    text = raw.strip()

    if text.startswith('"'):
        # A few rows are written as "a" + "b" + "c" to keep source lines readable. The pieces are
        # concatenated here rather than emitted as a C# expression, so the generated literal is
        # what actually ships and a diff against strings.xml stays meaningful.
        scanner = KotlinScanner(text)
        pieces = [kotlin_string_to_csharp(scanner.skip_string())]
        while scanner.i < len(text):
            rest = text[scanner.i:].lstrip()
            if not rest.startswith("+"):
                raise ValueError(f"trailing text after string: {text!r}")
            scanner.i = len(text) - len(rest) + 1
            while scanner.i < len(text) and text[scanner.i] in " \t\r\n":
                scanner.i += 1
            if scanner.i >= len(text) or text[scanner.i] != '"':
                raise ValueError(f"'+' not followed by a string literal: {text!r}")
            pieces.append(kotlin_string_to_csharp(scanner.skip_string()))
        return '"' + "".join(pieces) + '"'

    if text.startswith("R.drawable."):
        return f'"{text[len("R.drawable."):]}"'

    if NUMBER.match(text):
        # Kotlin's 1_000_000.0 and unary + are both illegal in a C# literal.
        return text.replace("_", "").lstrip("+")

    raise ValueError(f"unhandled argument for {kind}: {text!r}")


def parse_rows(source: pathlib.Path, kotlin_type: str, arity: int) -> list[list[str]]:
    text = source.read_text(encoding="utf-8")
    scanner = KotlinScanner(text)
    marker = kotlin_type + "("
    rows: list[list[str]] = []

    while True:
        # Find the next constructor call that is not inside a string.
        found = None
        probe = KotlinScanner(text)
        probe.i = scanner.i
        while probe.i < len(text):
            if text[probe.i] == '"':
                probe.skip_string()
                continue
            # A commented-out row is not a row. Without this, DictionaryModel.kt's
            # `//dictionary.add(Dictionary("category", "headingText", ...))` template line shipped
            # as a real dictionary entry on Windows (porting notes, D56).
            if text.startswith("//", probe.i):
                end = text.find("\n", probe.i)
                probe.i = len(text) if end < 0 else end + 1
                continue
            if text.startswith("/*", probe.i):
                end = text.find("*/", probe.i + 2)
                probe.i = len(text) if end < 0 else end + 2
                continue
            if text.startswith(marker, probe.i):
                before = text[probe.i - 1] if probe.i else " "
                if not (before.isalnum() or before == "_"):
                    found = probe.i
                    break
            probe.i += 1
        if found is None:
            return rows

        scanner.i = found + len(kotlin_type)
        args = scanner.read_args()
        if len(args) != arity:
            raise ValueError(
                f"{source.name}: {kotlin_type} row has {len(args)} args, expected {arity}\n"
                f"  {args}")
        rows.append(args)


HEADER = """// <auto-generated/>
// Generated by tools/port/kt2cs_model.py from {source}. Do not edit by hand.
//
// {summary}
//
// English only, on both platforms. The answer composer says so rather than implying the text
// was translated and merely happens to look English.
"""


def windows_extras(dataset: str) -> tuple[dict[str, str], str, str, str, str]:
    """The Windows-only column for a dataset, as {row key -> C# literal}."""
    prop, kind, default, rel_path, pattern, why = EXTRA_COLUMNS[dataset]
    path = WIN_REPO / "src" / "Atomic.App" / rel_path
    values: dict[str, str] = {}
    if path.is_file():
        for value, key in re.findall(pattern, path.read_text(encoding="utf-8-sig")):
            values.setdefault(key.lower(), f'"{value}"')
    return values, prop, kind, default, why


def emit(name: str, schema: dict, rows: list[list[str]]) -> tuple[str, list[str]]:
    fields = list(schema["fields"])
    notes: list[str] = []

    extras: dict[str, str] = {}
    extra_default = ""
    if name in EXTRA_COLUMNS:
        extras, prop, kind, extra_default, why = windows_extras(name)
        fields.append((prop, kind))
        notes.append(f"{name}.{prop} carried over from the Windows page ({len(extras)} values): {why}")

    # Convert first, then drop rows identical in every field. An exact duplicate carries no
    # information and makes DatasetIndex.Row return one of two indistinguishable rows; rows that
    # merely share a *name* are kept, because those are genuinely different measurements.
    converted: list[list[str]] = []
    seen: set[tuple[str, ...]] = set()
    for args in rows:
        values = [convert_arg(raw, f"{name}.{fields[i][0]}") for i, raw in enumerate(args)]
        signature = tuple(values)
        if signature in seen:
            notes.append(f"dropped an exact duplicate row: {values[0]} "
                         f"(identical in every field; a copy-paste in {schema['source']})")
            continue
        seen.add(signature)
        if extras:
            key = values[0].strip('"').lower()
            values.append(extras.get(key, extra_default))
        converted.append(values)

    lines = [HEADER.format(source=schema["source"], summary=schema["summary"])]
    lines.append("using System.Collections.Generic;")
    lines.append("")
    lines.append("namespace Atomic.Core.Models;")
    lines.append("")
    lines.append(f"public sealed record {name}(")
    lines.append(",\n".join(f"    {kind} {prop}" for prop, kind in fields) + ");")
    lines.append("")
    lines.append(f"public static class {name}Model")
    lines.append("{")
    lines.append(f"    /// <summary>All {len(converted)} rows, in Android's authoring order.</summary>")
    lines.append(f"    public static readonly IReadOnlyList<{name}> All =")
    lines.append("    [")
    for values in converted:
        lines.append(f"        new({', '.join(values)}),")
    lines.append("    ];")
    lines.append("}")
    return "\n".join(lines) + "\n", notes


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    if not AND_MODEL.is_dir():
        print(f"Android model directory not found: {AND_MODEL}")
        return 1

    total = 0
    for name, schema in SCHEMAS.items():
        source = AND_MODEL / schema["source"]
        kotlin_type = schema.get("kotlin_type", name)
        try:
            rows = parse_rows(source, kotlin_type, len(schema["fields"]))
        except ValueError as e:
            print(f"  {name:12s} FAILED: {e}")
            return 1

        code, notes = emit(name, schema, rows)
        emitted = code.count("\n        new(")
        total += emitted
        print(f"  {name:12s} {emitted:4d} rows  <- {schema['source']}")
        for note in notes:
            print(f"               {note}")

        if args.write:
            (OUT_DIR / f"{name}Model.g.cs").write_text(code, encoding="utf-8")

    print(f"\n{total} rows across {len(SCHEMAS)} datasets")
    print("\nIntentional renames:")
    for change, why in DIVERGENCES:
        print(f"  {change:44s} {why}")
    if not args.write:
        print("\nDRY RUN - nothing written.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
