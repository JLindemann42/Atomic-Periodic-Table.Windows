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

### D13 — seven accented lexicon entries, five of them dead on Android

`Lexicon.kt` opens by stating its own invariant: entries are matched against text that has already
been lowercased and stripped of Latin diacritics, so accented forms must be written in their folded
shape. Seven entries are written unfolded. **The corpus gate showed they are not all the same
thing**, and the earlier draft of this note got two of them wrong.

**Two are accented on purpose and must stay that way.** `YESNO_OPENERS` holds `è ` and `é `, and
`QueryPlanner.ComparativePlan` tests that list against the **raw** lowercased query as well as the
normalised one, with a comment saying why: the Romance copulas differ from their conjunctions by
nothing but the accent — Italian `è` (is) against `e` (and), Portuguese `é` against `e`. Folding
them makes *"e le loro densità"* ("and their densities") open a yes/no question. That is not a
guess: folding them flipped three conversation rows in `it` and `pt` from `COMPARISON` to
`COMPARATIVE`, which is how the defect was found. `kt2cs_lexicon.py` exempts the declaration by name
and reports the two separately, so nobody folds them again.

**Five really are unreachable and are now folded on the way out:**

| declaration | entry | emitted as | had a folded twin already? |
|---|---|---|---|
| `NEUTRON_WORDS` | `neutrões` | `neutroes` | **no — genuinely lost vocabulary** |
| `DEFER_TO_PERSONALITY` | `berätta nagot intressant` | `beratta nagot intressant` | **no — genuinely lost** |
| `OVERVIEW_WORDS` | `berätta om` | `beratta om` | yes |
| `AMBIGUOUS_DEGREE` | `más` | `mas` | yes |
| `AMBIGUOUS_DEGREE` | `più` | `piu` | yes |

Only the first two changed any behaviour. `neutrões` is European Portuguese for "neutrons", so
neutron questions in that spelling resolved nothing; the Swedish entry is half-folded in the source
already (`nagot` folded, `berätta` not), which is what a hand-maintained file drifts into. The other
three had their folded twin sitting in the same list, so folding produced a duplicate and nothing
else — **the earlier claim here that `berätta om` meant Swedish "tell me about" never took the
overview path was wrong**: `beratta om` is the very next entry.

Folding happens in the generator rather than in `Lexicon.kt`, so the port stays a pure function of
the Kotlin and a future Android sync picks the fix up again instead of silently reverting it.

**`ClauseSplitter.kt` has the same defect in its own two tables**, which the generator does not see
because they are declared in that file rather than in `Lexicon.kt`. Both are folded here by hand:

- `CLAUSE_OPENERS` contained `berätta`, with no folded twin — so a Swedish second clause opening
  "berätta om…" genuinely never counted as opening a question. Every other Swedish opener in the
  list *is* folded (`varfor`, `ar`), which is what makes this a slip rather than a convention.
- `PROPERTY_LINKERS` contained `för`, which folds to `for` — already listed one line above, so this
  one was harmless. Folded anyway, which makes it a duplicate.

An eighth entry is dead a different way and is **left alone**:
`COMPARATIVE_ADJECTIVES["conducts electricity better"]` maps to `electrical_conductivity`, which is
not a field in either app's `FieldRegistry` and not a key in the element JSON, where the only
conductivity is thermal. Folding cannot help it; it needs a field that does not exist.

The whole corpus is green with these changes: 2,793 of 2,793. `LexiconTest` pins the two exempt
entries to `YESNO_OPENERS` and asserts the five folded ones are reachable in their folded shape.

Nineteen further entries are duplicate map keys (`SERIES_WORDS["noble gas"]` twice, and so on).
Every one maps to the same value, so collapsing them is precisely what Kotlin's `mapOf` does; the
generator reports any future duplicate that maps to a *different* value as a conflict.

### D14 — the neutron-count branch accepted an impossible nuclide

`QueryPlanner`'s nuclide pattern is deliberately loose (`word gap digits`), so three branches guard
it by requiring the mass number to be at least the atomic number — no nuclide has fewer nucleons
than protons. **On Android, only two of the three do.** `ISOTOPE_COMPARISON` and `ISOTOPES` both
check it; `NUCLIDE_COUNT` does not, so "how many neutrons in uranium-3" is claimed and reaches the
executor with mass 3 and Z 92, where the subtraction gives −89.

**Fixed here, with corpus evidence rather than on inspection.** The guard was left off while the
corpus was being ported, because tightening a guard can move rows and the corpus is the
specification for Phase 4. With P4h green the experiment is cheap: adding it leaves the score at
2,793 of 2,793, so no row depended on the loose branch. Pinned by
`QueryPlannerSupportTest.EveryNuclideBranchRejectsAnImpossibleMassNumber`. Still live on Android.

### D15 — the "what is known" list could offer properties that are also missing

`AnswerComposer.noData` exists to turn a dead end into somewhere to go: after "tungsten has no
recorded Curie point" it names the neighbouring thermal properties that *are* recorded. It selects
them with

```kotlin
.filter { element.value(it.id) != null }
```

and `KnowledgeStore.value` is `values[fieldId] ?: FieldValue.Missing` — it never returns null. The
filter is always true, so the list is the first four fields of the category regardless of whether
the element has them, which is the opposite of what the paragraph above it promises.

**Fixed rather than preserved**, because C# cannot express it: `Value()` returns a non-nullable
`FieldValue`, so `!= null` is not a compilable check. The port uses `!element.Value(s.Id).IsMissing`,
which is the evident intent. Pinned by
`AnswerComposerTest.TheAlternativesOfferedAreOnesTheElementActuallyHas`.

This was the one place the port changed user-visible behaviour without the corpus having ruled on
it. **P4h has now ruled**: the corpus is green, and since the change can only ever *remove* a
suggestion, a row that asserted the old text would have failed loudly. Settled.

### D8 — widgets: three of five

Windows has no equivalent of Android home-screen widgets; the nearest surface is the Widgets
Board, which is a separate technology (provider COM server + Adaptive Cards) rather than a
port. Element of the Day, Science Daily and Quick Ask are implemented. Quick Nav and Short
Command are dropped — both are search shortcuts, better served on Windows by the existing
`AutoSuggestBox` and a taskbar jump list.

---

### D16 — the corpus is 2,793 questions, not 3,162

The plan and several earlier notes quote the corpus as "3,162 rows". That is its **line** count
across the 31 TSVs. Subtract 338 `#` comment lines and 31 headers and the corpus holds **2,793
questions**, every one of which the harness evaluates — `QuestionCorpus` treats a malformed row as a
hard error rather than skipping it, precisely so a pass rate is always taken over a set of known
size.

Two further departures from `CorpusTest.kt`, both deliberate:

- **One xUnit case per replayable unit, not one per file.** A session replays as a unit because its
  turns share a `DialogueState`; standalone rows are units of one. A failure then names the row in
  the test explorer — `Properties_Pass(key: "prop-z-1")` — and can be re-run alone, instead of
  arriving buried in a 40-line assertion message. The Kotlin's scoreboard survives as
  `CorpusTest.Scoreboard`, which evaluates everything in one pass and reports a pass rate per file;
  that is the number to watch when a change moves the corpus, and the per-row theories are where an
  individual row is diagnosed.
- **The corpus names Kotlin's enum members.** `expect_intent` reads `PROPERTY_LOOKUP` and
  `expect_card` reads `NFPA_DIAMOND`, while the C# enums are PascalCase. `CorpusTest.NameMatches`
  drops underscores and compares case-insensitively, so the TSVs stay byte-identical to Android's
  and the next sync is a file copy. `EnumNamesSurviveTheSnakeCaseFold` proves the fold is injective
  over both enums, so it can never quietly accept the wrong member.

Where Android skips the corpus when its assets are unreachable, the port fails: here the TSVs, the
element tables and the `.resw` files are all copied into the test output by the csproj, so
unreachable means broken — and a silently skipped 2,793-row gate is exactly the vacuous pass the
harness exists to prevent.

### D17 — Win2D is pinned to 1.3.2, and the pin is load-bearing

The PRO card gate needs a real Gaussian blur, which means an `IGraphicsEffect` implementation, which
means Win2D — WinUI ships no effect types of its own. **Win2D 1.4.0 depends on WindowsAppSDK 1.8**
and pulling it in fails the restore outright (`NU1605`, a downgrade of
`Microsoft.Windows.SDK.BuildTools` from 10.0.26100.4654 to .1742), because this app is pinned to
WindowsAppSDK 1.7.

`Microsoft.Graphics.Win2D` **1.3.2** is the newest release that restores against 1.7, and the blur
is verified working on it. Do not let a tool "update" this package without moving the app to
WindowsAppSDK 1.8 deliberately and re-testing the whole shell.

The gate keeps Android's numbers from `ProCardGate.kt` — blur radius 28, fallback content opacity
0.35, scrim opacity 0.92 — and, importantly, its *structure*: blur and scrim are **alternatives, not
layers**. Android applies the blur alone on API 31+ (with `alpha = 1f`) and the scrim alone below
it. `ProGate` does the same, falling back only when the effect cannot be created — a remote session,
a basic display adapter, or a driver that refuses it. The failure is remembered so the throwing call
is not repeated on every recycle of a virtualized list row.

Android's other decision is carried over verbatim and matters more than the visuals: **the content
behind the gate stays bound**, so unlocking is one property change with no re-bind and no reload.

### D18 — a shared element name is not evidence of a language

`EntityResolver.DetectLanguage` is Windows-only (Android's chat detects language differently), so
this is a defect the port introduced in P3h rather than one it inherited.

It scored every language whose alias table claimed a matched surface, then broke ties by preferring
**any non-English leader**, reasoning that "guld" is Swedish evidence even though English also lists
that key. True for a distinctive surface; false for a shared one. Element names are shared across
these twelve languages far more often than not - `gold` is the English, German *and* Afrikaans name,
`argon` and `platinum` are near-universal - so the rule discarded English on exactly the queries
where English was right. **"electron configuration of gold" was answered in German**: correct data,
wrong language, and nothing on screen to explain why.

Now only a surface claimed by **exactly one** language scores. `guld` still selects Swedish, `eisen`
still selects German, and a shared name contributes nothing, so the query falls through to the
active language. Corpus-neutral: 2,793 of 2,793 either way, which is unsurprising - every corpus row
declares its own language, so none of them exercised the tie-break. Pinned by four cases in
`EntityResolverTest`, including the gold one.

### D19 — one card view, not eight data templates

The plan put a `DataTemplate` per `ChatCardKind` on `ChatTemplateSelector`, selected through
`ChatCardCodec.KindOf`. Each would have had to repeat the whole assistant bubble - text, PRO gate,
citation chips - around a single differing child, and eight copies of a bubble is eight places for
them to drift apart. The kind switch lives in `ChatCardView` instead, on the one element that
differs; `KindOf` still does the cheap decode it exists for, just there. The selector keeps the only
distinction a *container* needs: who is speaking.

`CrystalStructureChart` also departs from the plan, which suggested reusing
`CrystalStructureRenderer`. That renderer is right where it is used - a lattice the user can rotate -
but it writes pixels into a `WriteableBitmap`, and a chat card lives in a virtualized list where
every visible bubble would allocate one. The card draws a static isometric wireframe from the same
lattice table using shapes, for the same reason Win2D was kept out of the other charts. The element
page keeps the interactive version.

### D20 — register from bin/AppxManifest.xml, not bin/AppX/AppxManifest.xml

`Add-AppxPackage -Register` was pointed at
`bin\x64\Debug\net8.0-windows10.0.26100.0\**AppX**\AppxManifest.xml` throughout the port. That file
is a leftover from an older packaging run and **the ordinary build does not refresh it** — it was
three days stale when this was found.

It went unnoticed for phases because the binaries, assets and `.resw` files live in the parent
directory and are picked up from there, so code and resource changes appeared to work. Only a
*manifest* change exposes it: the `windows.protocol` extension was declared, the build wrote it to
`bin\…\AppxManifest.xml`, and the shell kept registering the stale copy without it, so
`HKCU:\SOFTWARE\Classes\atomic` was never created and `atomic://` launched nothing.

Register from the manifest **beside the executable**:

```
Add-AppxPackage -Register <abs>\src\Atomic.App\bin\x64\Debug\net8.0-windows10.0.26100.0\AppxManifest.xml
```

A dev-mode package also refuses re-registration with `0x80073CFB` once installed, so a manifest
change needs `Remove-AppxPackage` first — a plain re-register silently leaves the old extensions in
place.

## Environment facts worth not rediscovering

- **Firebase project:** `atomic---periodic-table-41532`, project number `315118082769`. Every
  OAuth client in `google-services.json` (2× Android type-1, 1× web type-3) belongs to that
  same project number, so a Windows "Desktop app" OAuth client created in the same GCP project
  will produce the **same Firebase `localId`/uid as Android**. This is the precondition that
  makes cross-device sync work at all — verify it again if the client ID ever changes.
  The `AIza…` key in `google-services.json` is the *Android-restricted* key; the desktop client
  needs the Firebase **Web** API key for `identitytoolkit` calls.

- **The Web API key is verified.** `AIzaSyDNPscfJNeg6wm1cFW6-V5sQm0sm-kfFa0` resolves through
  `identitytoolkit.googleapis.com/v1/projects?key=…` to project number `315118082769` with
  `atomic---periodic-table-41532.firebaseapp.com` among its authorized domains — the same Firebase
  project Android uses. That is the half of the P0 precondition that holds.

- **The desktop OAuth client supplied on 2026-08-14 does _not_ satisfy it.**
  `945214413416-6i7sk6h30dtmeslo4krgf79lhnbogkk8.apps.googleusercontent.com` carries project number
  **945214413416**, while the Firebase project and all three Android clients are **315118082769**.
  Firebase checks a Google ID token's `aud` against the project's own clients, so this is normally
  rejected outright; and were it accepted, the uid would not be Android's — which is the *silent*
  failure the P0 spike exists to prevent, because sync then appears to work while every Windows user
  writes to a fresh document.

  Fix either by creating the "Desktop app" client inside `atomic---periodic-table-41532`
  (recommended — keeps one project), or by allowlisting the foreign client under that project's
  Google sign-in provider. **Re-run the `?key=` check above against any replacement before
  believing it**; the project number in the client ID prefix is the thing to compare.
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
