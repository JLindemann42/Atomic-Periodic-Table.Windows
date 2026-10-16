# tools

Build- and port-time helpers. Not shipped with the app.

| Directory | Purpose |
|---|---|
| `data/` | Element-table maintenance. `merge_elements.py` merges the Windows and Android element data into the shared `elements_{lang}.json` set and emits `merge_report.tsv` for review. |
| `loc/` | Localization pipeline. `strings_xml_to_resw.py` converts the Android `res/values*/strings.xml` files into `.resw`, and generates the `S` key constants. Re-runnable: it is how future Android string changes are pulled across. |
| `port/` | Kotlin-to-C# conversion helpers, e.g. `kt2cs_lexicon.py` for regenerating `Lexicon.g.cs`. |

See `../docs/porting-notes.md` for the translation rules these scripts implement.