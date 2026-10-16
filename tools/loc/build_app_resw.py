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

    # Settings. Windows words these its own way, but Android has translated every one of the
    # concepts already - under different English labels - so the borrow column carries them.
    #
    # system_default is borrowed by both the language and the theme picker: their first item is
    # the same idea, and Android translated it once.
    "settings_language":         ("App language", "language_title", "Android: 'Languages'"),
    "settings_language_desc":    ("Choose the language used for the interface", "language_content",
                                  "Android: 'Select Language'"),
    "settings_language_system":  ("Use system setting", "system_default", "Android: 'System Default'"),

    "settings_general":          ("General", "favorite_settings_general", "section heading"),
    "settings_theme_title":      ("App theme", "settings_themes_title", "Android: 'Themes'"),
    "settings_theme_desc":       ("Select which app theme to display", "settings_themes_content",
                                  "Android: 'Select Theme'"),

    # The row picks the unit for every temperature the app shows, not the main table's units -
    # so settings_unit_content ("Customize the units in the main table") is the wrong borrow
    # however close it looks.
    "settings_temperature_title": ("Default temperature unit", "favorite_settings_temperatures",
                                   "Android: 'Temperatures'"),
    "settings_temperature_desc":  ("Unit used wherever a temperature is shown", {
        "sv": "Enhet som används överallt där en temperatur visas",
        "de": "Einheit, die überall dort verwendet wird, wo eine Temperatur angezeigt wird",
        "fr": "Unité utilisée partout où une température est affichée",
        "es": "Unidad utilizada siempre que se muestra una temperatura",
        "it": "Unità usata ovunque venga mostrata una temperatura",
        "pt-BR": "Unidade usada sempre que uma temperatura é exibida",
        "af": "Eenheid wat gebruik word oral waar 'n temperatuur gewys word",
        "fil": "Yunit na ginagamit saanman may ipinapakitang temperatura",
        "hi": "तापमान दिखाए जाने पर उपयोग की जाने वाली इकाई",
        "ur": "وہ اکائی جو ہر جگہ درجہ حرارت دکھانے کے لیے استعمال ہوتی ہے",
        "zh-Hans": "显示温度时使用的单位",
    }, "authored here; no Android equivalent"),

    # The About cards. Android's win_title/win_text are the mirror image - its card sends users
    # *to* the desktop app - so borrowing them would render "Skaffa Desktop-app" on the desktop.
    # git_text is direction-neutral and borrows cleanly.
    "settings_android_title": ("Get Atomic for Android", {
        "sv": "Skaffa Atomic för Android",
        "de": "Atomic für Android holen",
        "fr": "Obtenir Atomic pour Android",
        "es": "Obtén Atomic para Android",
        "it": "Ottieni Atomic per Android",
        "pt-BR": "Obtenha o Atomic para Android",
        "af": "Kry Atomic vir Android",
        "fil": "Kunin ang Atomic para sa Android",
        "hi": "Android के लिए Atomic पाएं",
        "ur": "اینڈرائڈ کے لیے Atomic حاصل کریں",
        "zh-Hans": "获取 Android 版 Atomic",
    }, "authored here; Android's win_title points the other way"),
    "settings_android_desc": ("Download the app for your mobile devices to access all data everywhere", {
        "sv": "Ladda ner appen till dina mobila enheter för att nå all data överallt",
        "de": "Lade die App für deine Mobilgeräte herunter, um überall auf alle Daten zuzugreifen",
        "fr": "Téléchargez l'application sur vos appareils mobiles pour accéder à toutes les données partout",
        "es": "Descarga la aplicación en tus dispositivos móviles para acceder a todos los datos en cualquier lugar",
        "it": "Scarica l'app sui tuoi dispositivi mobili per accedere a tutti i dati ovunque",
        "pt-BR": "Baixe o aplicativo para seus dispositivos móveis e acesse todos os dados em qualquer lugar",
        "af": "Laai die app af vir jou mobiele toestelle om oral toegang tot alle data te kry",
        "fil": "I-download ang app para sa iyong mga mobile device upang ma-access ang lahat ng data kahit saan",
        "hi": "अपने मोबाइल उपकरणों के लिए ऐप डाउनलोड करें और हर जगह सभी डेटा देखें",
        "ur": "اپنے موبائل آلات کے لیے ایپ ڈاؤن لوڈ کریں اور ہر جگہ تمام ڈیٹا تک رسائی حاصل کریں",
        "zh-Hans": "在移动设备上下载应用，随时随地查看全部数据",
    }, "authored here; Android's win_text points the other way"),
    "settings_android_link": ("Download for Android", {
        "sv": "Ladda ner för Android",
        "de": "Für Android herunterladen",
        "fr": "Télécharger pour Android",
        "es": "Descargar para Android",
        "it": "Scarica per Android",
        "pt-BR": "Baixar para Android",
        "af": "Laai af vir Android",
        "fil": "I-download para sa Android",
        "hi": "Android के लिए डाउनलोड करें",
        "ur": "اینڈرائڈ کے لیے ڈاؤن لوڈ کریں",
        "zh-Hans": "下载 Android 版",
    }, "authored here; Android's win_text points the other way"),
    "settings_github_desc": ("Open-source repository for the Windows app", "git_text",
                             "Android: 'Atomics open-source GitHub repo'"),

    # The proton/neutron/electron counter on the element page. Android labels the first two with
    # ai_label_protons and ai_label_neutrons and has no electrons member of that set, so the label
    # is borrowed from element_electrons - whose trailing colon the borrow strips, which is the
    # whole point: "Elektroner:" beside "Protoner" and "Neutroner" reads like a mistake.
    "label_electrons":      ("Electrons", "element_electrons", "Android: 'Electrons:'"),

    # Ultimate: PRO+ on every platform, bought once. A Windows-first product with no Android
    # string to borrow, so these four are authored here and unreviewed. The card's title is the
    # product name and stays "Ultimate" in every language, so it is not a resource at all.
    "ultimate_description": ("PRO+ on Windows and Android, bought once.", {
        'sv': 'PRO+ på Windows och Android, köpt en gång.',
        'de': 'PRO+ auf Windows und Android, einmal gekauft.',
        'fr': 'PRO+ sur Windows et Android, acheté une seule fois.',
        'es': 'PRO+ en Windows y Android, con una sola compra.',
        'it': 'PRO+ su Windows e Android, con un solo acquisto.',
        'pt-BR': 'PRO+ no Windows e no Android, com uma única compra.',
        'af': 'PRO+ op Windows en Android, een keer gekoop.',
        'fil': 'PRO+ sa Windows at Android, isang beses lang bilhin.',
        'hi': 'Windows और Android पर PRO+, एक ही बार खरीदें।',
        'ur': 'ونڈوز اور اینڈرائڈ پر PRO+، ایک بار خریدیں۔',
        'zh-Hans': '在 Windows 和 Android 上享有 PRO+，一次购买。',
    },
                             "authored here; new product"),
    "ultimate_bullets": ('• Everything in PRO+\n• Every platform you sign in on\n• One purchase, no upgrade needed', {
        'sv': '• Allt i PRO+\n• Alla plattformar du loggar in på\n• Ett köp, ingen uppgradering behövs',
        'de': '• Alles aus PRO+\n• Jede Plattform, auf der du dich anmeldest\n• Ein Kauf, kein Upgrade nötig',
        'fr': '• Tout le contenu de PRO+\n• Toutes les plateformes où vous vous connectez\n• Un seul achat, aucune mise à niveau',
        'es': '• Todo lo de PRO+\n• Todas las plataformas donde inicies sesión\n• Una compra, sin mejoras necesarias',
        'it': '• Tutto ciò che include PRO+\n• Ogni piattaforma su cui accedi\n• Un acquisto, nessun upgrade necessario',
        'pt-BR': '• Tudo do PRO+\n• Todas as plataformas em que você entrar\n• Uma compra, sem precisar de upgrade',
        'af': '• Alles in PRO+\n• Elke platform waarop jy aanmeld\n• Een aankoop, geen opgradering nodig',
        'fil': '• Lahat ng nasa PRO+\n• Bawat platform na sinasalihan mo\n• Isang bili, walang kailangang i-upgrade',
        'hi': '• PRO+ की सभी सुविधाएँ\n• हर वह प्लेटफ़ॉर्म जहाँ आप साइन इन करते हैं\n• एक खरीद, अपग्रेड की ज़रूरत नहीं',
        'ur': '• PRO+ کی تمام خصوصیات\n• ہر وہ پلیٹ فارم جہاں آپ سائن اِن کرتے ہیں\n• ایک خریداری، اپ گریڈ کی ضرورت نہیں',
        'zh-Hans': '• PRO+ 的全部功能\n• 你登录的每个平台\n• 一次购买，无需升级',
    }, "authored here; new product"),
    "ultimate_badge": ("All platforms", {
        'sv': 'Alla plattformar',
        'de': 'Alle Plattformen',
        'fr': 'Toutes les plateformes',
        'es': 'Todas las plataformas',
        'it': 'Tutte le piattaforme',
        'pt-BR': 'Todas as plataformas',
        'af': 'Alle platforms',
        'fil': 'Lahat ng platform',
        'hi': 'सभी प्लेटफ़ॉर्म',
        'ur': 'تمام پلیٹ فارمز',
        'zh-Hans': '全部平台',
    }, "authored here; new product"),
    # Shown on the Ultimate card until the add-on exists in the Store catalogue. The card is
    # visible from the start - a tier the user cannot see is a tier they cannot plan to buy - so
    # it needs something honest to say in the meantime.
    "ultimate_coming_soon": ("Coming soon", {
        'sv': 'Kommer snart',
        'de': 'Demnächst',
        'fr': 'Bientôt disponible',
        'es': 'Próximamente',
        'it': 'Presto disponibile',
        'pt-BR': 'Em breve',
        'af': 'Binnekort',
        'fil': 'Malapit nang dumating',
        'hi': 'जल्द आ रहा है',
        'ur': 'جلد آ رہا ہے',
        'zh-Hans': '即将推出',
    }, "authored here; new product"),

    "ultimate_buy": ("Buy Ultimate", {
        'sv': 'Köp Ultimate',
        'de': 'Ultimate kaufen',
        'fr': 'Acheter Ultimate',
        'es': 'Comprar Ultimate',
        'it': 'Acquista Ultimate',
        'pt-BR': 'Comprar o Ultimate',
        'af': 'Koop Ultimate',
        'fil': 'Bilhin ang Ultimate',
        'hi': 'Ultimate खरीदें',
        'ur': 'Ultimate خریدیں',
        'zh-Hans': '购买 Ultimate',
    }, "authored here; new product"),

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

    # Scheduled toasts. Android hardcodes its notification copy in English inside
    # NotificationHelper.kt rather than putting it in strings.xml, so there is nothing to borrow -
    # these are English-only on both platforms until someone translates them.
    "notif_lives_title":    ("Lives refilled", None, "toast when lives come back"),
    "notif_lives_body":     ("Your lives have been refilled - come back and play!", None, ""),
    "notif_streak_title":   ("Keep your streak", None, "evening reminder at a streak of 3+"),
    "notif_streak_body":    ("You have a %1$s day streak going. A quick round keeps it alive.", None, ""),
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
    authored_total = 0
    for lang in languages:
        resources = load_resources(lang)
        entries: dict[str, str] = {}
        borrowed = 0
        authored = 0
        for key, (english, borrow, _note) in DECLARATIONS.items():
            if lang == "en-US":
                entries[key] = english
            elif isinstance(borrow, dict):
                # Authored here because no Android key carries the concept. Regional variants
                # fall back to their base language before falling back to English, so es-AR
                # reads Spanish rather than jumping to en-US.
                text = borrow.get(lang) or borrow.get(lang.split("-")[0])
                if text:
                    entries[key] = text
                    authored += 1
            elif borrow and resources.get(borrow):
                entries[key] = resources[borrow].rstrip(":").strip()
                borrowed += 1
            # else: omitted, so MRT falls back to en-US
        borrowed_total += borrowed
        authored_total += authored
        if args.write:
            gen.write_resw(STRINGS / lang / "App.resw", entries)
        print(f"  {lang:8s} {len(entries):3d} entries "
              f"({borrowed} borrowed from Android, {authored} authored here)")

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
