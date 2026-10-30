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

    # Common UI vocabulary. Android carries none of these - its equivalents are either absent
    # or welded into longer sentences - so they are authored here and unreviewed. Taken first
    # because they are the highest-frequency words left in the backlog and the least ambiguous
    # to translate: every one of them exists in every desktop OS in every language.
    'label_categories': ('Categories', {
        'sv': 'Kategorier',
        'de': 'Kategorien',
        'fr': 'Catégories',
        'es': 'Categorías',
        'it': 'Categorie',
        'pt-BR': 'Categorias',
        'af': 'Kategorieë',
        'fil': 'Mga kategorya',
        'hi': 'श्रेणियाँ',
        'ur': 'زمرے',
        'zh-Hans': '分类',
    }, "authored here; common UI vocabulary"),
    'label_all': ('All', {
        'sv': 'Alla',
        'de': 'Alle',
        'fr': 'Tout',
        'es': 'Todo',
        'it': 'Tutti',
        'pt-BR': 'Todos',
        'af': 'Alles',
        'fil': 'Lahat',
        'hi': 'सभी',
        'ur': 'تمام',
        'zh-Hans': '全部',
    }, "authored here; common UI vocabulary"),
    'label_details': ('Details', {
        'sv': 'Detaljer',
        'de': 'Details',
        'fr': 'Détails',
        'es': 'Detalles',
        'it': 'Dettagli',
        'pt-BR': 'Detalhes',
        'af': 'Besonderhede',
        'fil': 'Mga detalye',
        'hi': 'विवरण',
        'ur': 'تفصیلات',
        'zh-Hans': '详情',
    }, "authored here; common UI vocabulary"),
    'label_copy': ('Copy', {
        'sv': 'Kopiera',
        'de': 'Kopieren',
        'fr': 'Copier',
        'es': 'Copiar',
        'it': 'Copia',
        'pt-BR': 'Copiar',
        'af': 'Kopieer',
        'fil': 'Kopyahin',
        'hi': 'कॉपी करें',
        'ur': 'کاپی کریں',
        'zh-Hans': '复制',
    }, "authored here; common UI vocabulary"),
    'label_remove': ('Remove', {
        'sv': 'Ta bort',
        'de': 'Entfernen',
        'fr': 'Supprimer',
        'es': 'Quitar',
        'it': 'Rimuovi',
        'pt-BR': 'Remover',
        'af': 'Verwyder',
        'fil': 'Alisin',
        'hi': 'हटाएं',
        'ur': 'ہٹائیں',
        'zh-Hans': '移除',
    }, "authored here; common UI vocabulary"),
    'label_notes': ('Notes', {
        'sv': 'Anteckningar',
        'de': 'Notizen',
        'fr': 'Notes',
        'es': 'Notas',
        'it': 'Note',
        'pt-BR': 'Notas',
        'af': 'Notas',
        'fil': 'Mga tala',
        'hi': 'नोट्स',
        'ur': 'نوٹس',
        'zh-Hans': '笔记',
    }, "authored here; common UI vocabulary"),
    'label_learn_more': ('Learn more', {
        'sv': 'Läs mer',
        'de': 'Mehr erfahren',
        'fr': 'En savoir plus',
        'es': 'Más información',
        'it': 'Scopri di più',
        'pt-BR': 'Saiba mais',
        'af': 'Lees meer',
        'fil': 'Matuto pa',
        'hi': 'और जानें',
        'ur': 'مزید جانیں',
        'zh-Hans': '了解更多',
    }, "authored here; common UI vocabulary"),
    'label_purchase': ('Purchase', {
        'sv': 'Köp',
        'de': 'Kaufen',
        'fr': 'Acheter',
        'es': 'Comprar',
        'it': 'Acquista',
        'pt-BR': 'Comprar',
        'af': 'Koop',
        'fil': 'Bilhin',
        'hi': 'खरीदें',
        'ur': 'خریدیں',
        'zh-Hans': '购买',
    }, "authored here; common UI vocabulary"),
    'label_more_info': ('More info', {
        'sv': 'Mer information',
        'de': 'Mehr Infos',
        'fr': "Plus d'infos",
        'es': 'Más información',
        'it': 'Altre info',
        'pt-BR': 'Mais informações',
        'af': 'Meer inligting',
        'fil': 'Higit pang impormasyon',
        'hi': 'अधिक जानकारी',
        'ur': 'مزید معلومات',
        'zh-Hans': '更多信息',
    }, "authored here; common UI vocabulary"),
    'label_show_info': ('Show info', {
        'sv': 'Visa info',
        'de': 'Info anzeigen',
        'fr': 'Afficher les infos',
        'es': 'Mostrar info',
        'it': 'Mostra info',
        'pt-BR': 'Mostrar informações',
        'af': 'Wys inligting',
        'fil': 'Ipakita ang impormasyon',
        'hi': 'जानकारी दिखाएं',
        'ur': 'معلومات دکھائیں',
        'zh-Hans': '显示信息',
    }, "authored here; common UI vocabulary"),
    'label_hide_info': ('Hide info', {
        'sv': 'Dölj info',
        'de': 'Info ausblenden',
        'fr': 'Masquer les infos',
        'es': 'Ocultar info',
        'it': 'Nascondi info',
        'pt-BR': 'Ocultar informações',
        'af': 'Versteek inligting',
        'fil': 'Itago ang impormasyon',
        'hi': 'जानकारी छिपाएं',
        'ur': 'معلومات چھپائیں',
        'zh-Hans': '隐藏信息',
    }, "authored here; common UI vocabulary"),
    'search_placeholder': ('Search...', {
        'sv': 'Sök...',
        'de': 'Suchen...',
        'fr': 'Rechercher...',
        'es': 'Buscar...',
        'it': 'Cerca...',
        'pt-BR': 'Pesquisar...',
        'af': 'Soek...',
        'fil': 'Maghanap...',
        'hi': 'खोजें...',
        'ur': 'تلاش کریں...',
        'zh-Hans': '搜索...',
    }, "authored here; common UI vocabulary"),
    'enter_compound': ('Enter compound...', {
        'sv': 'Ange förening...',
        'de': 'Verbindung eingeben...',
        'fr': 'Saisir un composé...',
        'es': 'Introduce un compuesto...',
        'it': 'Inserisci un composto...',
        'pt-BR': 'Digite um composto...',
        'af': 'Voer verbinding in...',
        'fil': 'Maglagay ng compound...',
        'hi': 'यौगिक दर्ज करें...',
        'ur': 'مرکب درج کریں...',
        'zh-Hans': '输入化合物...',
    }, "authored here; common UI vocabulary"),
    'enter_value': ('Enter value', {
        'sv': 'Ange värde',
        'de': 'Wert eingeben',
        'fr': 'Saisir une valeur',
        'es': 'Introduce un valor',
        'it': 'Inserisci un valore',
        'pt-BR': 'Digite um valor',
        'af': 'Voer waarde in',
        'fil': 'Maglagay ng halaga',
        'hi': 'मान दर्ज करें',
        'ur': 'قدر درج کریں',
        'zh-Hans': '输入数值',
    }, "authored here; common UI vocabulary"),

    # The PRO page and the flashcard pitch. Marketing copy, so it is voice-dependent and there
    # is nothing on Android to borrow - its store copy lives in Play Console, not strings.xml.
    # Authored here and unreviewed.
    'pro_card_free_title': ('Free Version', {
        'sv': 'Gratisversion',
        'de': 'Kostenlose Version',
        'fr': 'Version gratuite',
        'es': 'Versión gratuita',
        'it': 'Versione gratuita',
        'pt-BR': 'Versão gratuita',
        'af': 'Gratis weergawe',
        'fil': 'Libreng bersyon',
        'hi': 'निःशुल्क संस्करण',
        'ur': 'مفت ورژن',
        'zh-Hans': '免费版',
    }, "authored here; PRO and flashcard copy"),
    'pro_card_free_tagline': ('Perfect for basic access.', {
        'sv': 'Perfekt för grundläggande åtkomst.',
        'de': 'Ideal für den einfachen Zugriff.',
        'fr': 'Parfait pour un accès de base.',
        'es': 'Perfecto para el acceso básico.',
        'it': "Perfetto per l'accesso di base.",
        'pt-BR': 'Ideal para o acesso básico.',
        'af': 'Perfek vir basiese toegang.',
        'fil': 'Perpekto para sa basic na access.',
        'hi': 'बुनियादी पहुँच के लिए उपयुक्त।',
        'ur': 'بنیادی رسائی کے لیے بہترین۔',
        'zh-Hans': '适合基本使用。',
    }, "authored here; PRO and flashcard copy"),
    'pro_card_pro_tagline': ('Unlock advanced data.', {
        'sv': 'Lås upp avancerade data.',
        'de': 'Erweiterte Daten freischalten.',
        'fr': 'Débloquez des données avancées.',
        'es': 'Desbloquea datos avanzados.',
        'it': 'Sblocca dati avanzati.',
        'pt-BR': 'Desbloqueie dados avançados.',
        'af': 'Ontsluit gevorderde data.',
        'fil': 'I-unlock ang advanced na data.',
        'hi': 'उन्नत डेटा अनलॉक करें।',
        'ur': 'جدید ڈیٹا اَن لاک کریں۔',
        'zh-Hans': '解锁进阶数据。',
    }, "authored here; PRO and flashcard copy"),
    'pro_card_proplus_tagline': ('Unlock advanced data and access to all platforms.', {
        'sv': 'Lås upp avancerade data och åtkomst på alla plattformar.',
        'de': 'Erweiterte Daten und Zugriff auf allen Plattformen freischalten.',
        'fr': "Débloquez des données avancées et l'accès sur toutes les plateformes.",
        'es': 'Desbloquea datos avanzados y acceso en todas las plataformas.',
        'it': "Sblocca dati avanzati e l'accesso su tutte le piattaforme.",
        'pt-BR': 'Desbloqueie dados avançados e acesso em todas as plataformas.',
        'af': 'Ontsluit gevorderde data en toegang op alle platforms.',
        'fil': 'I-unlock ang advanced na data at access sa lahat ng platform.',
        'hi': 'उन्नत डेटा और सभी प्लेटफ़ॉर्म पर पहुँच अनलॉक करें।',
        'ur': 'جدید ڈیٹا اور تمام پلیٹ فارمز پر رسائی اَن لاک کریں۔',
        'zh-Hans': '解锁进阶数据，并在所有平台上使用。',
    }, "authored here; PRO and flashcard copy"),
    'pro_most_popular': ('Most popular', {
        'sv': 'Mest populär',
        'de': 'Am beliebtesten',
        'fr': 'Le plus populaire',
        'es': 'Más popular',
        'it': 'Il più popolare',
        'pt-BR': 'Mais popular',
        'af': 'Gewildste',
        'fil': 'Pinakasikat',
        'hi': 'सबसे लोकप्रिय',
        'ur': 'سب سے مقبول',
        'zh-Hans': '最受欢迎',
    }, "authored here; PRO and flashcard copy"),
    'pro_all_free_features': ('All Free features', {
        'sv': 'Alla gratisfunktioner',
        'de': 'Alle Gratis-Funktionen',
        'fr': 'Toutes les fonctions gratuites',
        'es': 'Todas las funciones gratuitas',
        'it': 'Tutte le funzioni gratuite',
        'pt-BR': 'Todos os recursos gratuitos',
        'af': 'Alle gratis funksies',
        'fil': 'Lahat ng libreng feature',
        'hi': 'सभी निःशुल्क सुविधाएँ',
        'ur': 'تمام مفت خصوصیات',
        'zh-Hans': '全部免费功能',
    }, "authored here; PRO and flashcard copy"),
    'pro_all_pro_features': ('All PRO features', {
        'sv': 'Alla PRO-funktioner',
        'de': 'Alle PRO-Funktionen',
        'fr': 'Toutes les fonctions PRO',
        'es': 'Todas las funciones PRO',
        'it': 'Tutte le funzioni PRO',
        'pt-BR': 'Todos os recursos PRO',
        'af': 'Alle PRO-funksies',
        'fil': 'Lahat ng PRO feature',
        'hi': 'सभी PRO सुविधाएँ',
        'ur': 'تمام PRO خصوصیات',
        'zh-Hans': '全部 PRO 功能',
    }, "authored here; PRO and flashcard copy"),
    'pro_additional_features': ('Additional Features', {
        'sv': 'Fler funktioner',
        'de': 'Weitere Funktionen',
        'fr': 'Fonctions supplémentaires',
        'es': 'Funciones adicionales',
        'it': 'Funzioni aggiuntive',
        'pt-BR': 'Recursos adicionais',
        'af': 'Bykomende funksies',
        'fil': 'Karagdagang feature',
        'hi': 'अतिरिक्त सुविधाएँ',
        'ur': 'اضافی خصوصیات',
        'zh-Hans': '更多功能',
    }, "authored here; PRO and flashcard copy"),
    'pro_flashcards_and_lives': ('Additional flashcards and lives', {
        'sv': 'Fler flashcards och liv',
        'de': 'Mehr Lernkarten und Leben',
        'fr': 'Plus de cartes et de vies',
        'es': 'Más tarjetas y vidas',
        'it': 'Più flashcard e vite',
        'pt-BR': 'Mais flashcards e vidas',
        'af': 'Meer flitskaarte en lewens',
        'fil': 'Karagdagang flashcard at buhay',
        'hi': 'अधिक फ़्लैशकार्ड और जीवन',
        'ur': 'مزید فلیش کارڈز اور لائیوز',
        'zh-Hans': '更多闪卡与生命值',
    }, "authored here; PRO and flashcard copy"),
    'pro_compare_title': ('Compare features by versions', {
        'sv': 'Jämför funktioner mellan versioner',
        'de': 'Funktionen der Versionen vergleichen',
        'fr': 'Comparer les fonctions par version',
        'es': 'Compara las funciones por versión',
        'it': 'Confronta le funzioni per versione',
        'pt-BR': 'Compare os recursos por versão',
        'af': 'Vergelyk funksies per weergawe',
        'fil': 'Ihambing ang mga feature ayon sa bersyon',
        'hi': 'संस्करणों की सुविधाओं की तुलना करें',
        'ur': 'ورژنز کی خصوصیات کا موازنہ کریں',
        'zh-Hans': '按版本比较功能',
    }, "authored here; PRO and flashcard copy"),
    'pro_compare_subtitle': ('Easily compare the different features of different versions', {
        'sv': 'Jämför enkelt vad de olika versionerna innehåller',
        'de': 'Vergleiche mühelos, was die Versionen bieten',
        'fr': 'Comparez facilement ce que proposent les différentes versions',
        'es': 'Compara fácilmente lo que ofrece cada versión',
        'it': 'Confronta facilmente cosa offre ogni versione',
        'pt-BR': 'Compare facilmente o que cada versão oferece',
        'af': 'Vergelyk maklik wat elke weergawe bied',
        'fil': 'Madaling ihambing ang inaalok ng bawat bersyon',
        'hi': 'आसानी से देखें कि हर संस्करण में क्या है',
        'ur': 'آسانی سے موازنہ کریں کہ ہر ورژن میں کیا ہے',
        'zh-Hans': '轻松比较各版本的功能差异',
    }, "authored here; PRO and flashcard copy"),
    'feature_element_notes': ('Element Notes', {
        'sv': 'Elementanteckningar',
        'de': 'Element-Notizen',
        'fr': "Notes sur l'élément",
        'es': 'Notas del elemento',
        'it': "Note sull'elemento",
        'pt-BR': 'Notas do elemento',
        'af': 'Elementnotas',
        'fil': 'Mga tala sa elemento',
        'hi': 'तत्व नोट्स',
        'ur': 'عنصر کے نوٹس',
        'zh-Hans': '元素笔记',
    }, "authored here; PRO and flashcard copy"),
    'feature_save_unit_converter': ('Save in Unit Converter', {
        'sv': 'Spara i enhetsomvandlaren',
        'de': 'Im Einheitenrechner speichern',
        'fr': 'Enregistrer dans le convertisseur',
        'es': 'Guardar en el conversor de unidades',
        'it': 'Salva nel convertitore di unità',
        'pt-BR': 'Salvar no conversor de unidades',
        'af': 'Stoor in die eenheidsomskakelaar',
        'fil': 'I-save sa unit converter',
        'hi': 'यूनिट कन्वर्टर में सहेजें',
        'ur': 'یونٹ کنورٹر میں محفوظ کریں',
        'zh-Hans': '在单位换算器中保存',
    }, "authored here; PRO and flashcard copy"),
    'feature_sixty_lives': ('60 lives in flashcards', {
        'sv': '60 liv i flashcards',
        'de': '60 Leben in Lernkarten',
        'fr': '60 vies dans les cartes',
        'es': '60 vidas en las tarjetas',
        'it': '60 vite nelle flashcard',
        'pt-BR': '60 vidas nos flashcards',
        'af': '60 lewens in flitskaarte',
        'fil': '60 buhay sa flashcards',
        'hi': 'फ़्लैशकार्ड में 60 जीवन',
        'ur': 'فلیش کارڈز میں 60 لائیوز',
        'zh-Hans': '闪卡中 60 点生命值',
    }, "authored here; PRO and flashcard copy"),
    'pro_state_current': ('Current Version', {
        'sv': 'Nuvarande version',
        'de': 'Aktuelle Version',
        'fr': 'Version actuelle',
        'es': 'Versión actual',
        'it': 'Versione attuale',
        'pt-BR': 'Versão atual',
        'af': 'Huidige weergawe',
        'fil': 'Kasalukuyang bersyon',
        'hi': 'वर्तमान संस्करण',
        'ur': 'موجودہ ورژن',
        'zh-Hans': '当前版本',
    }, "authored here; PRO and flashcard copy"),
    'pro_state_upgraded': ('Upgraded to PRO+', {
        'sv': 'Uppgraderad till PRO+',
        'de': 'Auf PRO+ aktualisiert',
        'fr': 'Passé à PRO+',
        'es': 'Actualizado a PRO+',
        'it': 'Aggiornato a PRO+',
        'pt-BR': 'Atualizado para PRO+',
        'af': 'Opgegradeer na PRO+',
        'fil': 'Na-upgrade sa PRO+',
        'hi': 'PRO+ में अपग्रेड किया गया',
        'ur': 'PRO+ میں اپ گریڈ ہو گیا',
        'zh-Hans': '已升级到 PRO+',
    }, "authored here; PRO and flashcard copy"),
    'label_lives': ('Lives', {
        'sv': 'Liv',
        'de': 'Leben',
        'fr': 'Vies',
        'es': 'Vidas',
        'it': 'Vite',
        'pt-BR': 'Vidas',
        'af': 'Lewens',
        'fil': 'Buhay',
        'hi': 'जीवन',
        'ur': 'لائیوز',
        'zh-Hans': '生命值',
    }, "authored here; PRO and flashcard copy"),
    'flashcards_quiz_completed': ('Quiz Completed', {
        'sv': 'Quiz slutfört',
        'de': 'Quiz abgeschlossen',
        'fr': 'Quiz terminé',
        'es': 'Cuestionario completado',
        'it': 'Quiz completato',
        'pt-BR': 'Quiz concluído',
        'af': 'Vasvra voltooi',
        'fil': 'Tapos na ang quiz',
        'hi': 'क्विज़ पूरा हुआ',
        'ur': 'کوئز مکمل',
        'zh-Hans': '测验完成',
    }, "authored here; PRO and flashcard copy"),
    'flashcards_start': ('Start Flashcard', {
        'sv': 'Starta flashcard',
        'de': 'Lernkarte starten',
        'fr': 'Commencer les cartes',
        'es': 'Empezar las tarjetas',
        'it': 'Inizia le flashcard',
        'pt-BR': 'Começar os flashcards',
        'af': 'Begin flitskaart',
        'fil': 'Simulan ang flashcard',
        'hi': 'फ़्लैशकार्ड शुरू करें',
        'ur': 'فلیش کارڈ شروع کریں',
        'zh-Hans': '开始闪卡',
    }, "authored here; PRO and flashcard copy"),
    'flashcards_pro_pitch': ("Unlock the full potential of 'Atomic - Periodic Table' with access to all learning games and unlimited lives.", {
        'sv': "Lås upp allt i 'Atomic - Periodiska systemet' med tillgång till alla lärspel och obegränsat med liv.",
        'de': "Schöpfe 'Atomic - Periodic Table' voll aus - mit allen Lernspielen und unbegrenzten Leben.",
        'fr': "Profitez pleinement d'« Atomic - Periodic Table » : tous les jeux d'apprentissage et des vies illimitées.",
        'es': "Aprovecha al máximo 'Atomic - Periodic Table' con todos los juegos de aprendizaje y vidas ilimitadas.",
        'it': "Sfrutta al massimo 'Atomic - Periodic Table' con tutti i giochi didattici e vite illimitate.",
        'pt-BR': "Aproveite todo o potencial do 'Atomic - Periodic Table' com todos os jogos de aprendizagem e vidas ilimitadas.",
        'af': "Ontsluit die volle potensiaal van 'Atomic - Periodic Table' met alle leerspeletjies en onbeperkte lewens.",
        'fil': "I-unlock ang buong potensyal ng 'Atomic - Periodic Table' na may lahat ng learning game at walang limitasyong buhay.",
        'hi': "'Atomic - Periodic Table' की पूरी क्षमता अनलॉक करें - सभी लर्निंग गेम और असीमित जीवन।",
        'ur': "'Atomic - Periodic Table' کی مکمل صلاحیت اَن لاک کریں - تمام لرننگ گیمز اور لامحدود لائیوز۔",
        'zh-Hans': "解锁 'Atomic - Periodic Table' 的全部功能：所有学习游戏与无限生命值。",
    }, "authored here; PRO and flashcard copy"),

    # PRO page rows whose Android strings carry their own list punctuation. Declared rather
    # than used directly: the borrow path strips the leading dash and the trailing colon, and
    # using the Android key straight from the markup does not - which printed
    # "• - Ytterligare tabeller:" behind a bullet the page had already drawn.
    'feature_element_overview': ('Element Overview', 'element_overview', "punctuation stripped by the borrow"),
    'pro_additional_tables': ('Additional Tables', 'additional_tables_item', "punctuation stripped by the borrow"),
    'feature_early_access': ('Early access to DEV features', 'early_access_features', "punctuation stripped by the borrow"),
    'label_features': ('Feature', 'features', "punctuation stripped by the borrow"),
    'feature_save_calculations': ('Save Calculations', 'save_calculations', "punctuation stripped by the borrow"),
    'feature_poisson_sound': ("Poisson's Constant, Sound of Speed", 'poisson_constants_sound_speed', "punctuation stripped by the borrow"),
    'feature_hazard_materials': ('Hazard of Materials', 'hazard_materials', "punctuation stripped by the borrow"),
    'feature_poisson_table': ("Poisson's Ratio Table", 'poisson_ratio_table', "punctuation stripped by the borrow"),
    'feature_young_bulk_shear': ('Young-, Bulk-, Shear Modulus', 'young_bulk_shear_modulus', "punctuation stripped by the borrow"),
    'feature_unlimited_lives': ('Unlimited lives in flashcards', 'flashcards_unlimited_lives', "punctuation stripped by the borrow"),
    'feature_additional_games': ('Additional flashcard games', 'additional_flashcards_games', "punctuation stripped by the borrow"),

    # The tail of the page retrofit: pH descriptions, the solubility legend, the emission
    # header, formula categories and two quiz labels.
    'label_detailed_info': ('Detailed information', 'more_information', "Android: 'More information:'"),
    'solubility_soluble': ('S: Soluble', ('solubility_info_c', 0), 'line 1 of solubility_info_c'),
    'solubility_slightly': ('Ss: Slightly Soluble', ('solubility_info_c', 1), 'line 2 of solubility_info_c'),
    'solubility_insoluble': ('I: Insoluble', ('solubility_info_c', 2), 'line 3 of solubility_info_c'),
    'solubility_decomposed': ('-: Decomposed by water', {
        'sv': '-: Sönderdelas av vatten',
        'de': '-: Zersetzt sich in Wasser',
        'fr': "- : Décomposé par l'eau",
        'es': '-: Descompuesto por el agua',
        'it': "-: Decomposto dall'acqua",
        'pt-BR': '-: Decomposto pela água',
        'af': '-: Ontbind deur water',
        'fil': '-: Nabubulok sa tubig',
        'hi': '-: पानी से अपघटित',
        'ur': '-: پانی سے تحلیل',
        'zh-Hans': '-：遇水分解',
    }, "authored here; page retrofit tail"),
    'ph_universal_indicator': ('Universal Indicator', {
        'sv': 'Universalindikator',
        'de': 'Universalindikator',
        'fr': 'Indicateur universel',
        'es': 'Indicador universal',
        'it': 'Indicatore universale',
        'pt-BR': 'Indicador universal',
        'af': 'Universele indikator',
        'fil': 'Universal na indikator',
        'hi': 'सार्वभौमिक सूचक',
        'ur': 'یونیورسل انڈیکیٹر',
        'zh-Hans': '万能指示剂',
    }, "authored here; page retrofit tail"),
    'ph_universal_desc': ('A solution made of several compounds', {
        'sv': 'En lösning av flera föreningar',
        'de': 'Eine Lösung aus mehreren Verbindungen',
        'fr': 'Une solution composée de plusieurs composés',
        'es': 'Una disolución de varios compuestos',
        'it': 'Una soluzione di più composti',
        'pt-BR': 'Uma solução de vários compostos',
        'af': "'n Oplossing van verskeie verbindings",
        'fil': 'Isang solusyon na gawa sa ilang compound',
        'hi': 'कई यौगिकों से बना एक विलयन',
        'ur': 'کئی مرکبات سے بنا ایک محلول',
        'zh-Hans': '由多种化合物组成的溶液',
    }, "authored here; page retrofit tail"),
    'ph_bromothymol_desc': ('Also known as bromothymol sulfone phthalein and BTB', {
        'sv': 'Kallas även bromtymolsulfonftalein och BTB',
        'de': 'Auch bekannt als Bromthymolsulfonphthalein und BTB',
        'fr': 'Aussi appelé sulfonephtaléine de bromothymol et BTB',
        'es': 'También conocido como bromotimol sulfonftaleína y BTB',
        'it': 'Noto anche come bromotimolsolfonftaleina e BTB',
        'pt-BR': 'Também conhecido como bromotimol sulfonaftaleína e BTB',
        'af': 'Ook bekend as bromotimolsulfoonftaleïen en BTB',
        'fil': 'Kilala rin bilang bromothymol sulfone phthalein at BTB',
        'hi': 'इसे ब्रोमोथाइमॉल सल्फोन थैलीन और BTB भी कहते हैं',
        'ur': 'اسے بروموتھائمول سلفون تھیلین اور BTB بھی کہا جاتا ہے',
        'zh-Hans': '又称溴百里酚磺酞（BTB）',
    }, "authored here; page retrofit tail"),
    'label_most_common_isotope': ('Most Common Isotope:', {
        'sv': 'Vanligaste isotopen:',
        'de': 'Häufigstes Isotop:',
        'fr': 'Isotope le plus courant :',
        'es': 'Isótopo más común:',
        'it': 'Isotopo più comune:',
        'pt-BR': 'Isótopo mais comum:',
        'af': 'Mees algemene isotoop:',
        'fil': 'Pinakakaraniwang isotope:',
        'hi': 'सबसे सामान्य समस्थानिक:',
        'ur': 'سب سے عام آئسوٹوپ:',
        'zh-Hans': '最常见同位素：',
    }, "authored here; page retrofit tail"),
    'label_mass_colon': ('Mass:', {
        'sv': 'Massa:',
        'de': 'Masse:',
        'fr': 'Masse :',
        'es': 'Masa:',
        'it': 'Massa:',
        'pt-BR': 'Massa:',
        'af': 'Massa:',
        'fil': 'Masa:',
        'hi': 'द्रव्यमान:',
        'ur': 'کمیت:',
        'zh-Hans': '质量：',
    }, "authored here; page retrofit tail"),
    'label_your_answer': ('Your answer:', {
        'sv': 'Ditt svar:',
        'de': 'Deine Antwort:',
        'fr': 'Votre réponse :',
        'es': 'Tu respuesta:',
        'it': 'La tua risposta:',
        'pt-BR': 'Sua resposta:',
        'af': 'Jou antwoord:',
        'fil': 'Ang sagot mo:',
        'hi': 'आपका उत्तर:',
        'ur': 'آپ کا جواب:',
        'zh-Hans': '你的答案：',
    }, "authored here; page retrofit tail"),
    'label_correct_answer': ('Correct answer:', {
        'sv': 'Rätt svar:',
        'de': 'Richtige Antwort:',
        'fr': 'Bonne réponse :',
        'es': 'Respuesta correcta:',
        'it': 'Risposta corretta:',
        'pt-BR': 'Resposta correta:',
        'af': 'Korrekte antwoord:',
        'fil': 'Tamang sagot:',
        'hi': 'सही उत्तर:',
        'ur': 'درست جواب:',
        'zh-Hans': '正确答案：',
    }, "authored here; page retrofit tail"),
    'emission_wavelength_range': ('Wavelength range: 380-720 nm', {
        'sv': 'Våglängdsområde: 380-720 nm',
        'de': 'Wellenlängenbereich: 380-720 nm',
        'fr': "Plage de longueurs d'onde : 380-720 nm",
        'es': 'Rango de longitud de onda: 380-720 nm',
        'it': "Intervallo di lunghezze d'onda: 380-720 nm",
        'pt-BR': 'Faixa de comprimento de onda: 380-720 nm',
        'af': 'Golflengtebereik: 380-720 nm',
        'fil': 'Saklaw ng wavelength: 380-720 nm',
        'hi': 'तरंगदैर्ध्य परिसर: 380-720 nm',
        'ur': 'طول موج کی حد: 380-720 nm',
        'zh-Hans': '波长范围：380-720 nm',
    }, "authored here; page retrofit tail"),
    'emission_wavelength_vacuum': ('Wavelength in: Vacuum', {
        'sv': 'Våglängd i: Vakuum',
        'de': 'Wellenlänge in: Vakuum',
        'fr': "Longueur d'onde dans : le vide",
        'es': 'Longitud de onda en: vacío',
        'it': "Lunghezza d'onda in: vuoto",
        'pt-BR': 'Comprimento de onda em: vácuo',
        'af': 'Golflengte in: vakuum',
        'fil': 'Wavelength sa: vacuum',
        'hi': 'तरंगदैर्ध्य माध्यम: निर्वात',
        'ur': 'طول موج بمقام: خلا',
        'zh-Hans': '波长介质：真空',
    }, "authored here; page retrofit tail"),
    'label_detail_view': ('Detail View', {
        'sv': 'Detaljvy',
        'de': 'Detailansicht',
        'fr': 'Vue détaillée',
        'es': 'Vista detallada',
        'it': 'Vista dettagliata',
        'pt-BR': 'Visualização detalhada',
        'af': 'Detailaansig',
        'fil': 'Detalyadong view',
        'hi': 'विस्तृत दृश्य',
        'ur': 'تفصیلی منظر',
        'zh-Hans': '详细视图',
    }, "authored here; page retrofit tail"),
    'nuclide_table_info': ('Nuclide Table Info', {
        'sv': 'Om nuklidtabellen',
        'de': 'Über die Nuklidkarte',
        'fr': 'À propos du tableau des nucléides',
        'es': 'Sobre la tabla de nucleidos',
        'it': 'Informazioni sulla tabella dei nuclidi',
        'pt-BR': 'Sobre a tabela de nuclídeos',
        'af': 'Oor die nukliedtabel',
        'fil': 'Tungkol sa nuclide table',
        'hi': 'न्यूक्लाइड तालिका के बारे में',
        'ur': 'نیوکلائیڈ ٹیبل کے بارے میں',
        'zh-Hans': '核素表说明',
    }, "authored here; page retrofit tail"),
    'chip_mechanics': ('Mechanics', {
        'sv': 'Mekanik',
        'de': 'Mechanik',
        'fr': 'Mécanique',
        'es': 'Mecánica',
        'it': 'Meccanica',
        'pt-BR': 'Mecânica',
        'af': 'Meganika',
        'fil': 'Mekanika',
        'hi': 'यांत्रिकी',
        'ur': 'میکانیات',
        'zh-Hans': '力学',
    }, "authored here; page retrofit tail"),
    'chip_wavelengths': ('Wavelengths', {
        'sv': 'Våglängder',
        'de': 'Wellenlängen',
        'fr': "Longueurs d'onde",
        'es': 'Longitudes de onda',
        'it': "Lunghezze d'onda",
        'pt-BR': 'Comprimentos de onda',
        'af': 'Golflengtes',
        'fil': 'Mga wavelength',
        'hi': 'तरंगदैर्ध्य',
        'ur': 'طول موج',
        'zh-Hans': '波长',
    }, "authored here; page retrofit tail"),
    'chip_electricity': ('Electricity', {
        'sv': 'Elektricitet',
        'de': 'Elektrizität',
        'fr': 'Électricité',
        'es': 'Electricidad',
        'it': 'Elettricità',
        'pt-BR': 'Eletricidade',
        'af': 'Elektrisiteit',
        'fil': 'Kuryente',
        'hi': 'विद्युत',
        'ur': 'بجلی',
        'zh-Hans': '电学',
    }, "authored here; page retrofit tail"),
    'chip_magnetism': ('Magnetism and Induction', {
        'sv': 'Magnetism och induktion',
        'de': 'Magnetismus und Induktion',
        'fr': 'Magnétisme et induction',
        'es': 'Magnetismo e inducción',
        'it': 'Magnetismo e induzione',
        'pt-BR': 'Magnetismo e indução',
        'af': 'Magnetisme en induksie',
        'fil': 'Magnetismo at induction',
        'hi': 'चुंबकत्व और प्रेरण',
        'ur': 'مقناطیسیت اور القا',
        'zh-Hans': '磁学与感应',
    }, "authored here; page retrofit tail"),
    'chip_nuclear_physics': ('Nuclear Physics', {
        'sv': 'Kärnfysik',
        'de': 'Kernphysik',
        'fr': 'Physique nucléaire',
        'es': 'Física nuclear',
        'it': 'Fisica nucleare',
        'pt-BR': 'Física nuclear',
        'af': 'Kernfisika',
        'fil': 'Nuclear physics',
        'hi': 'नाभिकीय भौतिकी',
        'ur': 'جوہری طبیعیات',
        'zh-Hans': '核物理',
    }, "authored here; page retrofit tail"),

    # Shown instead of "Ultimate" when the reduced upgrade is what is on offer, so the card says
    # which of the two products the price belongs to - the PRO+ card already does this.
    "ultimate_upgrade_title": ("Ultimate Upgrade", {
        "sv": "Ultimate-uppgradering", "de": "Ultimate-Upgrade", "fr": "Mise \u00e0 niveau Ultimate",
        "es": "Mejora a Ultimate", "it": "Upgrade a Ultimate", "pt-BR": "Upgrade para Ultimate",
        "af": "Ultimate-opgradering", "fil": "Ultimate upgrade",
        "hi": "Ultimate \u0905\u092a\u0917\u094d\u0930\u0947\u0921",
        "ur": "Ultimate \u0627\u067e \u06af\u0631\u06cc\u0688",
        "zh-Hans": "Ultimate \u5347\u7ea7",
    }, "authored here; new product"),

    # Headers the code builds around a value. Declared as formats with positional
    # placeholders rather than concatenated in C#, because word order moves: Swedish puts
    # the element after the noun and Hindi before it, and string addition cannot.
    'element_notes_title': ('%1$s Notes', {
        'sv': 'Anteckningar för %1$s',
        'de': 'Notizen zu %1$s',
        'fr': 'Notes sur %1$s',
        'es': 'Notas de %1$s',
        'it': 'Note su %1$s',
        'pt-BR': 'Notas de %1$s',
        'af': 'Notas oor %1$s',
        'fil': 'Mga tala sa %1$s',
        'hi': '%1$s के नोट्स',
        'ur': '%1$s کے نوٹس',
        'zh-Hans': '%1$s 笔记',
    }, "authored here; format string"),
    'poisson_ratio_for': ("Poisson's Ratio for %1$s:", {
        'sv': 'Poissons tal för %1$s:',
        'de': 'Poissonzahl für %1$s:',
        'fr': 'Coefficient de Poisson de %1$s :',
        'es': 'Coeficiente de Poisson de %1$s:',
        'it': 'Coefficiente di Poisson di %1$s:',
        'pt-BR': 'Coeficiente de Poisson de %1$s:',
        'af': 'Poisson se verhouding vir %1$s:',
        'fil': "Poisson's ratio ng %1$s:",
        'hi': '%1$s का पॉइसन अनुपात:',
        'ur': '%1$s کا پوئسون تناسب:',
        'zh-Hans': '%1$s 的泊松比：',
    }, "authored here; format string"),
    'value_range': ('Value range: %1$s - %2$s', {
        'sv': 'Värdeintervall: %1$s - %2$s',
        'de': 'Wertebereich: %1$s - %2$s',
        'fr': 'Plage de valeurs : %1$s - %2$s',
        'es': 'Rango de valores: %1$s - %2$s',
        'it': 'Intervallo di valori: %1$s - %2$s',
        'pt-BR': 'Faixa de valores: %1$s - %2$s',
        'af': 'Waardebereik: %1$s - %2$s',
        'fil': 'Saklaw ng halaga: %1$s - %2$s',
        'hi': 'मान परिसर: %1$s - %2$s',
        'ur': 'قدر کی حد: %1$s - %2$s',
        'zh-Hans': '取值范围：%1$s - %2$s',
    }, "authored here; format string"),
    'emission_spectrum_of': ('Emission Spectrum of %1$s:', {
        'sv': 'Emissionsspektrum för %1$s:',
        'de': 'Emissionsspektrum von %1$s:',
        'fr': "Spectre d'émission de %1$s :",
        'es': 'Espectro de emisión de %1$s:',
        'it': 'Spettro di emissione di %1$s:',
        'pt-BR': 'Espectro de emissão de %1$s:',
        'af': 'Emissiespektrum van %1$s:',
        'fil': 'Emission spectrum ng %1$s:',
        'hi': '%1$s का उत्सर्जन स्पेक्ट्रम:',
        'ur': '%1$s کا اخراجی طیف:',
        'zh-Hans': '%1$s 的发射光谱：',
    }, "authored here; format string"),
    'info_about': ('Info about %1$s:', {
        'sv': 'Om %1$s:',
        'de': 'Über %1$s:',
        'fr': 'À propos de %1$s :',
        'es': 'Acerca de %1$s:',
        'it': 'Informazioni su %1$s:',
        'pt-BR': 'Sobre %1$s:',
        'af': 'Oor %1$s:',
        'fil': 'Tungkol sa %1$s:',
        'hi': '%1$s के बारे में:',
        'ur': '%1$s کے بارے میں:',
        'zh-Hans': '关于 %1$s：',
    }, "authored here; format string"),
    'ionization_for': ('Ionization Energies for %1$s', {
        'sv': 'Joniseringsenergier för %1$s',
        'de': 'Ionisierungsenergien von %1$s',
        'fr': "Énergies d'ionisation de %1$s",
        'es': 'Energías de ionización de %1$s',
        'it': 'Energie di ionizzazione di %1$s',
        'pt-BR': 'Energias de ionização de %1$s',
        'af': 'Ionisasie-energieë van %1$s',
        'fil': 'Ionization energies ng %1$s',
        'hi': '%1$s की आयनन ऊर्जाएँ',
        'ur': '%1$s کی آئنائزیشن توانائیاں',
        'zh-Hans': '%1$s 的电离能',
    }, "authored here; format string"),
    'loading_progress': ('Loading: %1$s/%2$s', {
        'sv': 'Laddar: %1$s/%2$s',
        'de': 'Lädt: %1$s/%2$s',
        'fr': 'Chargement : %1$s/%2$s',
        'es': 'Cargando: %1$s/%2$s',
        'it': 'Caricamento: %1$s/%2$s',
        'pt-BR': 'Carregando: %1$s/%2$s',
        'af': 'Laai: %1$s/%2$s',
        'fil': 'Naglo-load: %1$s/%2$s',
        'hi': 'लोड हो रहा है: %1$s/%2$s',
        'ur': 'لوڈ ہو رہا ہے: %1$s/%2$s',
        'zh-Hans': '加载中：%1$s/%2$s',
    }, "authored here; format string"),

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

    # The history sidebar. English-only on purpose: Android hardcodes its history screen in English
    # ("No history found..."), so there is no translation to borrow, and its two gate messages -
    # ai_history_sign_in, ai_history_pro_only - are already translated and used as they are.
    "chat_history":         ("Chat history", None, "history sidebar title and its button"),
    "chat_history_empty":   ("No conversations yet", None, "history sidebar with nothing saved"),

    # The account page's licences card, for a phone whose tier no sync has read yet. Shown instead
    # of "free", which would claim the phone owns nothing when all we know is that we have not heard.
    "account_tier_unknown": ("Not synced yet", None, "licences card, phone tier unknown"),

    # An empty favourites card, worded as the balancer's (Android's balancer_text) and the
    # calculator's (Android's calculator_text) are. Android has no such text for these two tools.
    "ideal_gas_favorites_text": ("Solve ideal gas problems and save favorites for later use", None,
                                 "ideal gas calculator, empty favourites"),
    "unit_favorites_text":      ("Convert between units and save favorites for later use", None,
                                 "unit converter, empty favourites"),

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


def strip_list_punctuation(value: str) -> str:
    """
    Drop the punctuation Android welds onto a label, keeping only the words.

    Android writes its list items and captions with the separator baked in - "Element Overview:",
    "- Additional Tables:" - because it renders them as one run of text. Windows draws the bullet
    and the colon itself, so borrowing the raw value gives "- Ytterligare tabeller:" behind a
    bullet the page already drew, and a caption that ends in a colon nothing follows.

    Only the decoration goes. A colon inside the string, as in "Poisson's Constants, Sound of
    Speed:", is left alone apart from the trailing one.
    """
    return value.strip().lstrip("-•").rstrip(":：").strip()


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
            elif isinstance(borrow, tuple):
                # One line of a multi-line Android string. Android packs a whole legend into a
                # single value because it renders it as one TextView; Windows draws each row in its
                # own colour, so it needs them apart. Splitting is still borrowing - these are
                # Android's translations, including its quirks: French writes "S : Soluble" with a
                # space before the colon and Chinese uses a full-width one.
                source, index = borrow
                lines = [l for l in resources.get(source, "").strip('"').split(chr(10)) if l.strip()]
                if index < len(lines):
                    entries[key] = lines[index].strip()
                    borrowed += 1
            elif borrow and resources.get(borrow):
                entries[key] = strip_list_punctuation(resources[borrow])
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
