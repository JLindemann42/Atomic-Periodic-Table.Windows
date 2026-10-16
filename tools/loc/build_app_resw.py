#!/usr/bin/env python3
"""
Generate Strings/<lang>/App.resw - the Windows-specific strings.

Two resource files ship per language and they have different owners:

  Resources.resw  generated from Android's strings.xml by strings_xml_to_resw.py.
                  Never hand-edited; regenerating it must stay safe.

  App.resw        this file's output. Labels that exist only on Windows, or where
                  Windows deliberately words something differently from Android.

The declarations below hold the English text explicitly, and may name an Android key to borrow
the other eleven translations from. That combination is what lets English keep the Windows
wording while Swedish still reads "Formler" rather than falling back to "Equations" - Android
already translated the concept, just under a different English label.

Where no borrow key is given the string is English-only and MRT falls back to en-US. That is
honest: nobody has translated it yet, and inventing a translation here would be worse.

Usage:
    python build_app_resw.py            # report
    python build_app_resw.py --write
"""

from __future__ import annotations

import argparse
import pathlib
import xml.etree.ElementTree as ET

HERE = pathlib.Path(__file__).resolve()
WIN_REPO = HERE.parents[2]
STRINGS = WIN_REPO / "src" / "Atomic.App" / "Strings"
KEYS_OUT = WIN_REPO / "src" / "Atomic.Core" / "Localization" / "S.App.g.cs"

# key -> (english, borrow_from_android_key_or_None, note)
DECLARATIONS: dict[str, tuple[str, str | None, str]] = {
    # Navigation pane. Android's home is a bottom tab, so it has no equivalent label at all.
    "nav_periodic_table":  ("Periodic Table", None, "Android has no equivalent; its home is a tab"),
    "nav_equations":       ("Equations", "activity_equations_title", "Android calls these Formulas"),
    "nav_solubility":      ("Solubility", "table_solubility", "Android: 'Solubility Table'"),
    "nav_constants":       ("Constants", "constants_tite", "Android: 'Constants Table'"),
    "nav_emission":        ("Emission Spectrums", None, "Android's key carries a trailing colon"),

    # Breadcrumb leaves for detail pages.
    "nav_element_details":     ("Element Details", "element_info_title", "Android: 'Element Information'"),
    "nav_isotope_details":     ("Isotope Details", None, ""),
    "nav_ionization_details":  ("Ionization Energies Details", None, ""),
    "nav_poisson_details":     ("Poisson's Ratio Details", None, ""),
    "nav_geology_details":     ("Geology Table Details", None, ""),
    "nav_equations_details":   ("Equations Details", None, ""),
    "nav_emission_details":    ("Emission Spectrum Table Details", None, ""),
    "nav_blog":                ("Blog", None, ""),

    # Settings: the language picker is a Windows-only surface.
    "settings_language":         ("App language", None, ""),
    "settings_language_desc":    ("Choose the language used for the interface", None, ""),
    "settings_language_system":  ("Use system setting", None, ""),

    # Alloy table. Android's bottom sheet labels the values by position rather than with
    # captions, so there is nothing to borrow for these three. The dialog's two buttons are not
    # here at all - open_base_element and ok are Android keys, already translated.
    "alloy_search":         ("Search alloys", None, ""),
    "alloy_base_metal":     ("Base metal", None, ""),
    "alloy_composition":    ("Composition", None, ""),

    # Ask Atomic chrome. Android's ai_chat_panel.xml drives these four with icon-only ImageButtons
    # carrying no contentDescription, so there is nothing to borrow - it has no string for them in
    # any language. On Windows they are tooltips and AutomationProperties.Name values, which a
    # screen reader reads aloud, so leaving them unset is not an option the way it is there.
    #
    # Everything the user actually *reads* in the panel comes from translated Android keys:
    # ask_atomic (title), ai_hint (input), ai_greeting (empty state), ai_beta_badge,
    # ai_card_pro_locked. These four are the whole English-only surface.
    "chat_send":            ("Send", None, "icon button; Android has no string"),
    "chat_close":           ("Close", None, "icon button; Android has no string"),
    "chat_new":             ("New conversation", None, "icon button; Android has no string"),
    "chat_thinking":        ("Thinking", None, "read aloud while the engine runs"),

    # Flashcard level bands. Android draws its boxes with the range baked into a layout string per
    # box; the Windows screen renders them from FlashcardCatalog, so it needs one format instead.
    #
    # NOT "level_range_format" - that is an existing Android key meaning a single level ("Level
    # %1$d"), and declaring it here shadowed it with a two-argument version. The generator warns
    # about shadowing and StringResourceTest fails on the argument-count mismatch, which is how
    # this was caught.
    "flashcard_level_band": ("Level %1$s - %2$s", None, "flashcard box heading; a range, not a level"),
}

RESW_HEADER_PATH_HINT = "see strings_xml_to_resw.py; the schema block is identical"


def load_resources(lang: str) -> dict[str, str]:
    path = STRINGS / lang / "Resources.resw"
    if not path.is_file():
        return {}
    root = ET.fromstring(path.read_text(encoding="utf-8"))
    return {d.get("name"): (d.find("value").text or "") for d in root.findall("data")}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    # Reuse the generated header so both files are structurally identical.
    import strings_xml_to_resw as gen  # noqa: E402

    languages = sorted(d.name for d in STRINGS.iterdir() if (d / "Resources.resw").is_file())
    if not languages:
        print("no Strings/<lang>/Resources.resw found - run strings_xml_to_resw.py first")
        return 1

    borrowed_total = 0
    for lang in languages:
        resources = load_resources(lang)
        entries: dict[str, str] = {}
        borrowed = 0
        for key, (english, borrow, _note) in DECLARATIONS.items():
            if lang == "en-US":
                entries[key] = english
            elif borrow and resources.get(borrow):
                entries[key] = resources[borrow].rstrip(":").strip()
                borrowed += 1
            # else: omitted, so MRT falls back to en-US
        borrowed_total += borrowed
        if args.write:
            gen.write_resw(STRINGS / lang / "App.resw", entries)
        print(f"  {lang:8s} {len(entries):3d} entries ({borrowed} borrowed from Android)")

    # The keys join the same partial S class the Android-sourced generator emits, so a
    # Windows-only key is as compile-checked as a ported one. Any key that also exists in
    # Resources.resw is already declared there; emitting it twice would be a duplicate const.
    android_keys = set(load_resources("en-US"))
    shadowed = sorted(k for k in DECLARATIONS if k in android_keys)
    if shadowed:
        print(f"\nWARNING: {len(shadowed)} key(s) shadow an Android key - App.resw wins at "
              f"runtime, and the const comes from S.g.cs: {', '.join(shadowed)}")

    if args.write:
        gen.write_keys(
            KEYS_OUT,
            [k for k in DECLARATIONS if k not in android_keys],
            header="// Generated by tools/loc/build_app_resw.py. Do not edit by hand.",
        )
        print(f"wrote {KEYS_OUT.name}")

    english_only = [k for k, (_e, b, _n) in DECLARATIONS.items() if b is None]
    print(f"\n{len(DECLARATIONS)} declarations, {borrowed_total} borrowed values total")
    print(f"English-only (fall back to en-US in other languages): {', '.join(english_only)}")
    if not args.write:
        print("\nDRY RUN - nothing written.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
