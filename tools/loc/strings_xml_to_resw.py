#!/usr/bin/env python3
"""
Convert the Android string resources into Windows .resw files.

This is not a one-off. It is the mechanism for pulling future Android string changes across, so
it is idempotent and safe to re-run: it regenerates every .resw and S.cs from scratch.

Three things it deliberately does NOT do:

  * It does not rewrite format specifiers. There are 5,751 positional references (%1$s .. %7$s)
    across the twelve languages, and translators have reordered arguments in languages that
    cannot be proofread here. A mechanical rewrite to {0}/{1} that gets one Hindi or Urdu
    reorder wrong produces a plausible-looking sentence with the values swapped, and nothing
    detects it. The values stay byte-identical to strings.xml and Atomic.Core.Text.JavaFormat
    interprets them at runtime. Keeping them identical also makes the next sync a plain diff.

  * It does not translate anything. Coverage gaps are reported, not filled.

  * It does not touch the Android repo.

Usage:
    python strings_xml_to_resw.py            # dry run: report only
    python strings_xml_to_resw.py --write    # regenerate .resw, S.cs and coverage.tsv
"""

from __future__ import annotations

import argparse
import collections
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

HERE = pathlib.Path(__file__).resolve()
WIN_REPO = HERE.parents[2]
AND_RES = WIN_REPO.parents[1] / "Atomic-Periodic-Table.Android" / "app" / "src" / "main" / "res"

STRINGS_OUT = WIN_REPO / "src" / "Atomic.App" / "Strings"
KEYS_OUT = WIN_REPO / "src" / "Atomic.Core" / "Localization" / "S.g.cs"
COVERAGE_OUT = HERE.parent / "coverage.tsv"

# Android resource folder -> Windows BCP-47 folder.
#
# Where a language has both an unregioned folder and regional ones, Android puts shared strings
# in the unregioned folder and only the differences in the regional. Flattening just the regional
# folder would silently drop the shared half, so each target is a list applied in order, later
# entries overriding earlier.
LANGUAGE_MAP = {
    "en-US":   ["values"],
    "af":      ["values-af"],
    "de":      ["values-de"],
    "es":      ["values-es", "values-es-rES"],
    "es-AR":   ["values-es", "values-es-rAR"],
    "es-MX":   ["values-es", "values-es-rMX"],
    "fil":     ["values-b+fil"],
    "fr":      ["values-fr"],
    "hi":      ["values-hi"],
    "it":      ["values-it-rIT"],
    "pt-BR":   ["values-pt-rBR"],
    "sv":      ["values-sv-rSE"],
    "ur":      ["values-ur", "values-ur-rPK"],
    "ur-IN":   ["values-ur", "values-ur-rIN"],
    "zh-Hans": ["values-zh-rCN"],
}

# The twelve the app advertises. The regional extras above ride along for free - MRT falls back
# from es-AR to es per resource - but they are not separate languages for coverage purposes.
BASE_LANGUAGES = ["en-US", "af", "de", "es", "fil", "fr", "hi", "it", "pt-BR", "sv", "ur", "zh-Hans"]

# Android plural quantity -> MRT plural suffix.
PLURAL_SUFFIX = {
    "zero": "Zero", "one": "One", "two": "Two",
    "few": "Few", "many": "Many", "other": "Other",
}

RESW_HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
"""

# Android's own escaping. \n is a real newline; the rest are literal characters that had to be
# escaped because of Android's parser, not because they mean anything.
ANDROID_ESCAPES = {
    "n": "\n", "t": "\t", "'": "'", '"': '"',
    "@": "@", "?": "?", "\\": "\\", " ": " ",
}


def unescape(text: str) -> str:
    out, i = [], 0
    while i < len(text):
        c = text[i]
        if c == "\\" and i + 1 < len(text):
            nxt = text[i + 1]
            out.append(ANDROID_ESCAPES.get(nxt, nxt))
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def inner_text(el: ET.Element) -> str:
    """Flatten an element's text. There is no inline markup in this resource set (verified), so
    this only has to concatenate text and tails."""
    parts = [el.text or ""]
    for child in el:
        parts.append(inner_text(child))
        parts.append(child.tail or "")
    return "".join(parts)


def load_folder(folder: pathlib.Path) -> dict[str, str]:
    """One Android values folder, flattened to resw keys."""
    path = folder / "strings.xml"
    if not path.is_file():
        return {}

    tree = ET.fromstring(path.read_text(encoding="utf-8"))
    out: dict[str, str] = {}

    for el in tree:
        name = el.get("name")
        if not name or el.get("translatable") == "false":
            continue

        if el.tag == "string":
            out[name] = unescape(inner_text(el))

        elif el.tag == "string-array":
            for i, item in enumerate(el.findall("item")):
                out[f"{name}.{i}"] = unescape(inner_text(item))

        elif el.tag == "plurals":
            for item in el.findall("item"):
                suffix = PLURAL_SUFFIX.get(item.get("quantity", ""))
                if suffix:
                    out[f"{name}.{suffix}"] = unescape(inner_text(item))

    return out


def xml_escape(value: str) -> str:
    return (value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"))


def write_resw(path: pathlib.Path, entries: dict[str, str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    body = [RESW_HEADER]
    for key in sorted(entries):
        body.append(
            f'  <data name="{key}" xml:space="preserve">\n'
            f"    <value>{xml_escape(entries[key])}</value>\n"
            f"  </data>\n"
        )
    body.append("</root>\n")
    path.write_text("".join(body), encoding="utf-8")


CSHARP_KEYWORDS = {
    "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
    "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
    "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
    "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
    "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
    "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
    "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
    "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
    "void", "volatile", "while",
}


def identifier(key: str) -> str:
    ident = re.sub(r"[^A-Za-z0-9_]", "_", key)
    if ident[:1].isdigit():
        ident = "_" + ident
    if ident in CSHARP_KEYWORDS:
        ident = "@" + ident
    return ident


def write_keys(path: pathlib.Path, keys: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    seen: dict[str, str] = {}
    lines = [
        "// <auto-generated/>",
        "// Generated by tools/loc/strings_xml_to_resw.py. Do not edit by hand.",
        "//",
        "// Resource keys as compile-time constants. The Kotlin sources reference strings as",
        "// R.string.foo; the ported code says S.foo, which makes that translation a pure regex and",
        "// turns a mistyped key into a build error rather than a string that silently reads",
        '// "str:ai_feild_density" at runtime.',
        "",
        "namespace Atomic.Core.Localization;",
        "",
        "public static class S",
        "{",
    ]
    for key in sorted(keys):
        ident = identifier(key)
        if ident in seen:
            raise SystemExit(f"identifier collision: {key!r} and {seen[ident]!r} both -> {ident}")
        seen[ident] = key
        lines.append(f'    public const string {ident} = "{key}";')
    lines.append("}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    if not AND_RES.is_dir():
        print(f"missing: {AND_RES}", file=sys.stderr)
        return 1

    tables: dict[str, dict[str, str]] = {}
    for lang, folders in LANGUAGE_MAP.items():
        merged: dict[str, str] = {}
        for folder in folders:
            merged.update(load_folder(AND_RES / folder))
        tables[lang] = merged

    english = tables["en-US"]
    print(f"en-US: {len(english)} keys")

    # Translations carry keys English does not - usually because a string was marked
    # translatable="false" in the base but left translated, or was removed from the base and not
    # from the translations. Nothing can reference them: S.g.cs is generated from English, so a
    # key absent there is unreachable from code. Drop them rather than ship dead resources.
    orphans = collections.Counter()
    for lang, entries in tables.items():
        extra = set(entries) - set(english)
        orphans[lang] = len(extra)
        for key in extra:
            del entries[key]
    dropped = sum(orphans.values())
    if dropped:
        worst = ", ".join(f"{l}:{n}" for l, n in orphans.most_common(3) if n)
        print(f"dropped {dropped} keys absent from en-US ({worst})")

    rows = ["language\tkeys\tof_english\tmissing"]
    for lang in BASE_LANGUAGES:
        entries = tables[lang]
        missing = len(set(english) - set(entries))
        pct = 100.0 * len(set(english) & set(entries)) / max(1, len(english))
        rows.append(f"{lang}\t{len(entries)}\t{pct:.1f}%\t{missing}")
        print(f"  {lang:8s} {len(entries):5d} keys   {pct:5.1f}% of en-US   {missing:5d} missing")

    extras = [l for l in LANGUAGE_MAP if l not in BASE_LANGUAGES]
    for lang in extras:
        print(f"  {lang:8s} {len(tables[lang]):5d} keys   (regional variant)")

    if args.write:
        for lang, entries in tables.items():
            write_resw(STRINGS_OUT / lang / "Resources.resw", entries)
        write_keys(KEYS_OUT, list(english))
        COVERAGE_OUT.write_text("\n".join(rows) + "\n", encoding="utf-8")
        print(f"\nwrote {len(tables)} .resw, {KEYS_OUT.name} ({len(english)} keys), coverage.tsv")
    else:
        print("\nDRY RUN - nothing written. Re-run with --write.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
