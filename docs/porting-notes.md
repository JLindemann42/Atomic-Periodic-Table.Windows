# Kotlin → C# porting notes

Working notes for the Android v5 → Windows port. Two jobs:

1. **The translation rules.** Follow them without exception, so that a reviewer comparing a
   `.kt` file against its `.cs` counterpart can diff them line by line.
2. **The register of intentional divergences.** Anything the Windows port does differently
   from Android, and why. Read this before syncing new Android changes across — a divergence
   recorded here is a decision; a divergence not recorded here is a bug.

Source of truth for the Android side:
`C:\Users\jonat\OneDrive\Dokument\TARDIS_Github\Atomic-Periodic-Table.Android`

---

## Translation rules

| Kotlin | C# |
|---|---|
| `sealed class X { data class A(...) : X() }` | `public abstract record X { public sealed record A(...) : X; }` (nested) |
| `when (x) { is A -> …; is B -> … }` | switch expression with type patterns, **plus a `_ => throw new UnreachableException()` arm** |
| `object X` | `public static class X` |
| `companion object` | `static` members on the type |
| `data class` | `record` (positional where Kotlin is positional) |
| `.copy(a = 1)` | `with { A = 1 }` |
| `List<T>` / `Map<K,V>` (read-only) | `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>` |
| `runCatching { … }.getOrNull()` | `Try.OrNull(() => …)` — one helper, never a scattered empty `catch` |
| `String.format(...)` | `JavaFormat.Format(...)` |
| `Regex("…")` | `JavaRegex.Compile("…")` |
| `.lowercase()` / `.uppercase()` | `.ToLowerInvariant()` / `.ToUpperInvariant()` — **never** `ToLower()` |
| `Normalizer.normalize(s, NFKC)` | `s.Normalize(NormalizationForm.FormKC)` |
| `Character.getType(c) == NON_SPACING_MARK` | `CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark` |
| `Character.UnicodeScript.of(c.code)` | `UnicodeScripts.Of(c)` |
| `@StringRes id: Int` | `string key` |
| `R.string.foo` | `S.foo` |

### Why the `UnreachableException` arm

Kotlin's `when` over a sealed class is exhaustive and the compiler enforces it. C# does not
enforce exhaustiveness on a switch over a record hierarchy — a missing variant silently falls
through. Adding an explicit throwing default converts that into a loud failure the test suite
catches. Do not replace it with a "sensible" fallback value.

### Why three shims exist (`Atomic.Core/Text/`)

These are the entire silent-failure surface of the port. Each, done naively, compiles and
returns wrong answers with no exception.

**`UnicodeScripts`** — .NET has no equivalent of Java's `Character.UnicodeScript`, which
`Tokenizer.kt` depends on to decide script-conditional folding. Implemented as a range table
matching Java's behaviour, including returning UNKNOWN for a lone surrogate. Do **not**
"improve" it by combining surrogate pairs: that changes tokenization and silently diverges
from the index Android built.

**`JavaRegex`** — Kotlin `Regex` is `java.util.regex`, where `\d \w \s \b` are **ASCII-only**.
.NET's are Unicode-aware. Consequences, both silent:

- `\d` in .NET matches `٥` (Arabic-Indic five) and `५` (Devanagari five). `ValueParser`,
  `IsotopeParser` and `QuantityScanner` all use `\d` and all run against Hindi and Urdu text.
- `ChemistryMath.MOLE_QUANTITY` uses `mol(?:e|es|i|s)?\b`. For input `5 molé`: Java treats `é`
  as a non-word character, so there is a boundary and `mol` matches. .NET treats `é` as a word
  character, so there is no boundary and **nothing matches, with no error**.

`JavaRegex.Compile` rewrites these classes outside character classes and always adds
`CultureInvariant | Compiled`. Every `Regex` construction ported from `ai/` must go through it.

**`JavaFormat`** — `.resw` values are kept byte-identical to Android's `strings.xml`, including
`%1$s` / `%2$d` positional specifiers. See the divergence register below.

---

## Divergence register

### D1 — `AIAgentManager.kt` is not ported

**Android:** `ai/AIAgentManager.kt`, 2,664 lines — a legacy keyword router that catches
queries the structured engine declines.

**Windows:** replaced by a much smaller `Atomic.Core/Ai/ChatOrchestrator.cs`.

**Why:** the 3,162-row acceptance corpus (`app/src/test/resources/ai/corpus/`) is driven by
`CorpusHarness.kt`, which wires `AiEngine` directly. The legacy router is never on the
acceptance path, so porting it would add 2,664 lines with zero test coverage. What the
non-engine path actually owes the user is defined by `must_defer.tsv` and `capabilities.tsv`:
greetings, "what can you do", and small talk in 12 languages — a personality responder, not a
keyword cascade.

**Accepted loss:** the legacy-only reactivity comparison ("which is more reactive, sodium or
potassium"). Recovered by porting `getReactivityScore` alone (a pure function of group and Z)
into `Ai/Data/ReactivityScale.cs`.

**Also skipped:** `AILearningManager.kt` (SharedPreferences interest telemetry).

### D2 — format specifiers are not converted

`%1$s` and friends are kept verbatim in `.resw` and interpreted at runtime by `JavaFormat`,
rather than rewritten to `{0}`.

**Why:** 406 `ai_*` strings across 12 languages, and translators have already reordered
positional arguments in languages that cannot be proofread here. A mechanical rewrite that
gets one Hindi or Urdu reorder wrong produces a plausible-looking sentence with swapped values,
and nothing detects it. Byte-identical `.resw` values also keep future string syncs a straight
diff against `strings.xml`.

### D3 — generator tests assert properties, not RNG sequences

C# `Random(seed)` produces a different sequence than Kotlin's `Random(seed)`, so tests that
pinned an exact draw cannot port. They are replaced by property assertions over many seeds
(fairness margins 0.25/0.12/0.05 by difficulty, range exclusion, distractor bands 0.4×–2.5×,
β⁺ and electron capture never co-shown).

### D4 — Android's XP curve is adopted, replacing the Windows one

**Windows was:** `4575 + 800·(1.10ⁿ⁻²¹ − 1)/0.10`
**Now:** `4575 + 600·(1.02ⁿ⁻²¹ − 1)/0.02`, matching `utils/XpManager.kt`.

**Why:** not a balance preference. `ProgressSyncManager` merges `xp` and `level` take-the-max
across devices; two different curves plus max-merge is a data-consistency bug — a Windows
client would push an `xp` that Android reads as a higher level, then take back a `level`
disagreeing with its own local `xp`. Migration is free and strictly favourable: XP is what is
persisted, level is derived, so every existing user's level rises and nobody loses anything.

### D5 — no background refill worker

**Android:** `LivesRefillWorker.kt`, a WorkManager job every 15 minutes.

**Windows:** none. Lives are computed from elapsed intervals on read, which already yields the
correct count whenever the app is open. The worker exists only to fire the "below 10 lives"
nudge, which becomes a `ScheduledToastNotification` queued at the computed refill time. Same
user-visible behaviour without the `BackgroundTaskBuilder` / COM-server apparatus.

### D6 — element data merge is bidirectional

The shared `elements_{lang}.json` files are **not** simply Android's copied over. Windows held
fields Android lacks: `cas_number` (30 elements), `eg_number` (19), `oxidation_state_pos`/`_neg`
(9), the NFPA triple `health`/`flammability`/`instability` for 5 elements Android omits,
`special` (2), `note_text` (1), and extra isotope slots on 10 elements — 16 additional on
berkelium, 15 on einsteinium, 12–14 each on seven other transuranics, 1 on antimony. The merge
preserves both sides; see `tools/data/merge_elements.py` and the review artifact
`tools/data/merge_report.tsv`.

Android is the better source almost everywhere else, and materially so: it has real values where
Windows is blank for `electrical_type` on 37 elements, `element_electrons` and `element_protons`
on 12 each, `poisson_ratio` and `magnetic_type` on 10 each.

**Blank markers differ and this is the trap in the merge.** Android normalises absence to `---`.
Windows uses at least `---`, `--`, `empty`, `unknown`, `???`, `N/A`, `""` and even `--- (ms)`. A
naive "keep the non-blank side" rule reads those as data and reintroduces junk over good Android
values. The merge normalises Windows' markers first; the set lives in `BLANK_MARKERS` in the
script.

**Correction to an earlier claim:** `emission_spectrum` was recorded here and in the plan as a
Windows-only field that `EmissionPage` depended on. It is not. The field is present on **0 of 118
records on both sides** — it exists only as an unused `[JsonPropertyName]` on the `Element` model,
and `EmissionPage` builds its own table in code and never reads element JSON. The merge is still
bidirectional, for the fields listed above.

Related: Android keys caesium as `caesium`, Windows filed it as `cesium.json`. The merged data
uses `caesium`, with `cesium` kept as an entity-resolver alias so the American spelling still
resolves in chat.

### D7 — favourites storage format

Android stores balancer and ideal-gas favourites as a `StringSet` with `\u001F`-separated
fields. Windows uses a JSON array in `LocalFolder`, matching the existing `favorites.json` and
`unit_favorites.json` convention. The unit-separator encoding is not replicated.

### D9 — hydrate formulas parse correctly

**Android:** `ChemistryMath.normalise` deletes the hydrate separator (`·` or `*`) before parsing.
That turns `CuSO4·5H2O` into `CuSO45H2O`, where the sulfate's `O4` and the hydrate's `5` fuse into
a single `O45`. Copper(II) sulfate pentahydrate comes back with **46 oxygens instead of 9** and a
molar mass of roughly 1,420 g/mol instead of 249.68 — about six times too large.

**Windows:** the separator is kept, and the formula is split on it with each segment's leading
integer applied as a multiplier for that segment.

**Why diverge here rather than record and preserve:** nothing depends on the broken behaviour. The
3,162-row acceptance corpus contains no hydrate (its only `·` is inside the unit `W/(m·K)`), and
the quiz generator's ~100-compound corpus has none either. So this costs no parity, and the
alternative is shipping a molar-mass calculator that is silently wrong for an entire class of
common compounds.

**This bug is live in the Android app.** The same fix belongs in
`ai/data/ChemistryMath.kt`: stop stripping `·` in `normalise`, and split on it in `countAtoms`.

### D10 — reference tables: the flow runs both ways

The eleven datasets behind `DatasetIndex` existed separately in both apps and had drifted.
`tools/port/compare_datasets.py` is the review gate, the same role `merge_elements.py` played in
Phase 1; `tools/port/kt2cs_model.py` regenerates the C# from Android's `model/*Model.kt` and is
re-runnable for future syncs.

What the comparison found:

| dataset | Windows | Android | outcome |
|---|---|---|---|
| Dictionary | 81 | 151 | Android adopted; Windows was a strict subset |
| Equation | 77 | 77 | identical |
| Constant | 58 | 59 | Android adopted, minus one duplicate row (below) |
| Poisson | 50 | 52 | Android adopted, plus the Windows-only `Symbol` column |
| Geology | 25 | 25 | identical |
| Unit catalogue | 65 | 65 | identical, all 14 categories, factors equal to 1e-12 |
| Electrode | 33 | 18 | **Windows adopted** — a strict superset with identical shared values |
| Solubility | 13×12 | 13×12 | **unreconciled**, see below |

Three things worth keeping:

- **The electrochemical series flows Windows → Android.** Android's `SeriesModel` has 18 couples;
  the Windows page had 33, including both halogens, and every shared row agreed to the last
  digit. `Models/SeriesModel.cs` is therefore hand-written and is *not* regenerated by
  `kt2cs_model.py` — regenerating it would delete fifteen elements. It is also re-sorted strictly
  by potential, which the Windows list was not (lithium sat below erbium), and it keys rows by
  `caesium` rather than the page's `Cesium` so the deep link resolves against the element table.

- **`Poisson.Symbol` is Windows-only** — the two-letter chart label, which Android's band chart
  never needed. `kt2cs_model.py` harvests it back out of `PoissonPage.xaml.cs` on every run
  (`EXTRA_COLUMNS`), so regenerating from Android cannot silently drop it.

- **The solubility tables genuinely disagree and are left unreconciled.** Windows has an H⁺ row
  and SO₃²⁻ / SiO₃²⁻ columns; Android has a Zn²⁺ row and CrO₄²⁻ / CH₃CO₂⁻ columns. Merging means
  authoring ~50 new cells, which is chemistry rather than a merge. Android's table ships in Core
  because the corpus was written against it; the gap is recorded here rather than papered over.

### D11 — duplicate and empty rows in the source tables

Three defects the row-uniqueness and non-empty tests surfaced. `DatasetIndex.Row` returns the
first match, so a duplicate id makes the second row uncitable:

- **`Elementary charge` appears twice in Android's `ConstantsModel.kt`** (lines 22 and 26),
  identical in all five fields. `kt2cs_model.py` drops rows identical in every field and says so.
  **Live on Android**, where the constants table shows it twice.

- **`Basalt` appears twice in the Poisson table on both platforms**, quoted as 0.10–0.35 and
  0.10–0.33. Those are two real literature ranges, so both rows are kept; the Windows
  `DatasetIndex` disambiguates the *id* by appending the range, where Android keys on the name
  alone and makes the second row unreachable. The corpus asserts intent, not ids, so this is free.

- **`Equivalent Resistance (Series)` has an empty description in both apps.** On the formulas page
  the rendered image carries the meaning; as a dataset row it would answer a question with an
  empty string, so the title and category stand in. The underlying data gap is still worth filling
  upstream.

### D12 — two of the seven localized element fields are not actually translated

`FieldSpec.localized` marks seven fields as differing per language, and `LocalizedView` overlays
exactly those. The mechanism is correct; the **data** is not. Measured across all eleven non-English
tables, as the percentage of populated English values that differ in that language:

| field | worst | best | verdict |
|---|---|---|---|
| `description` | af 32% | 100% | fine except Afrikaans |
| `element_group` | fr 87% | 100% | fine |
| `element_phase` | 90% | 100% | fine |
| `magnetic_type` | de 75% | 82% | fine |
| **`element_appearance`** | **af/it/sv 0%** | ur 82% | **untranslated** |
| **`electrical_type`** | **de/sv 5%** | 31% | **untranslated** |

`element` sits at 34–55% for German, French, Swedish and Afrikaans, which is *not* a gap — those
languages genuinely share most element names with English ("Gold", "Argon", "Neon").

So a Swedish question about gold's appearance is answered "Metallic yellow": the overlay reads the
Swedish file correctly, and the Swedish file holds the English string. Nothing in the port can fix
this — it needs translation, on both platforms, since the JSON is now shared. Pinned by
`RetrievalServiceTest.AppearanceIsUntranslatedInTheDataAndThatIsKnown` so it is not rediscovered as
a bug.

### D13 — eight dead entries in the lexicon, live on Android

`Lexicon.kt` opens by stating its own invariant: entries are matched against text that has already
been lowercased and stripped of Latin diacritics, so accented forms must be written in their folded
shape. Seven entries break that rule and are therefore **unreachable — they can never match any
input**, on either platform:

| declaration | entry | would have to be |
|---|---|---|
| `OVERVIEW_WORDS` | `berätta om` | `beratta om` |
| `DEFER_TO_PERSONALITY` | `berätta nagot intressant` | `beratta nagot intressant` |
| `NEUTRON_WORDS` | `neutrões` | `neutroes` |
| `YESNO_OPENERS` | `è ` | `e ` |
| `YESNO_OPENERS` | `é ` | `e ` |
| `AMBIGUOUS_DEGREE` | `más` | `mas` |
| `AMBIGUOUS_DEGREE` | `più` | `piu` |

`berätta om` is the costly one: it is Swedish for "tell me about", the trigger for the overview
path, so "berätta om guld" has never taken it. The second entry is half-folded already — `nagot` is
folded, `berätta` is not — which is what a hand-maintained file drifts into.

An eighth entry is dead a different way: `COMPARATIVE_ADJECTIVES["conducts electricity better"]`
maps to `electrical_conductivity`, which is not a field in either app's `FieldRegistry` and not a
key in the element JSON, where the only conductivity is thermal.

**Preserved verbatim rather than fixed.** Unlike the hydrate bug (D9), folding these *adds*
vocabulary that has never been live, which could change corpus outcomes — and the corpus is the
spec for Phase 4. The decision belongs after that gate, not before it. `kt2cs_lexicon.py` reports
all of them on every run, and `LexiconTest` pins the exact set so a ninth cannot join unnoticed.

Nineteen further entries are duplicate map keys (`SERIES_WORDS["noble gas"]` twice, and so on).
Every one maps to the same value, so collapsing them is precisely what Kotlin's `mapOf` does; the
generator reports any future duplicate that maps to a *different* value as a conflict.

### D8 — widgets: three of five

Windows has no equivalent of Android home-screen widgets; the nearest surface is the Widgets
Board, which is a separate technology (provider COM server + Adaptive Cards) rather than a
port. Element of the Day, Science Daily and Quick Ask are implemented. Quick Nav and Short
Command are dropped — both are search shortcuts, better served on Windows by the existing
`AutoSuggestBox` and a taskbar jump list.

---

## Environment facts worth not rediscovering

- **Firebase project:** `atomic---periodic-table-41532`, project number `315118082769`. Every
  OAuth client in `google-services.json` (2× Android type-1, 1× web type-3) belongs to that
  same project number, so a Windows "Desktop app" OAuth client created in the same GCP project
  will produce the **same Firebase `localId`/uid as Android**. This is the precondition that
  makes cross-device sync work at all — verify it again if the client ID ever changes.
  The `AIza…` key in `google-services.json` is the *Android-restricted* key; the desktop client
  needs the Firebase **Web** API key for `identitytoolkit` calls.
- **The app is packaged and full-trust** (`runFullTrust` in `Package.appxmanifest`), so it is
  not in an AppContainer and the loopback network-isolation restriction does not apply. That is
  what makes the RFC 8252 loopback OAuth flow viable here.
- **`PublishTrimmed` is true for every non-Debug configuration.** Anything deserialized by
  reflection must have a `JsonSerializerContext`, or Release builds return an object with all
  properties null and no error. Debug builds will not reproduce it.

## Pre-existing issues found at baseline (not introduced by the port)

- The MSIX signing certificate (thumbprint `31ABC53F…`) **has expired** — `APPX0108`.
- `<PublishProfile>win-$(Platform).pubxml</PublishProfile>` refers to profiles that do not
  exist in the repo — `NETSDK1198`.
- **`PublishTrimmed=true` produced a blank periodic table.** Fixed by turning trimming off; see
  below.

## Trimming

`PublishTrimmed` is **off**, deliberately, and there is a comment in `Atomic.App.csproj` saying
why. The short version: the app binds with classic `{Binding}`, which resolves by reflection at
runtime, so the trimmer strips the properties it cannot see being used and every binding silently
yields nothing. A published trimmed build showed all 118 periodic-table tiles empty — no symbol,
no name, no atomic number — and clicking one did nothing.

Confirmed by publishing the identical Release build twice, once with trimming and once without.
`dotnet build` never reproduces it: `PublishTrimmed` only applies on `dotnet publish`.

Two consequences for the port:

1. **Anything reflection-based needs the same scrutiny**, not just JSON. If trimming is ever
   re-enabled, the blocker is the XAML, and the fix is converting `{Binding}` to `{x:Bind}`,
   which is compile-time and trim-safe.
2. **`ElementJsonContext` stays regardless.** Source-generated `System.Text.Json` metadata is the
   right thing whether or not the trimmer runs — it removes a reflection dependency, it is
   faster, and it is a precondition for ever adopting AOT. It covers `Element`, the two favourite
   record types and `List<string>`; the trim analyser reported those five call sites and reports
   none now.
