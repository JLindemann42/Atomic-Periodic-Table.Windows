using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Nlu;
using Xunit;
using Xunit.Abstractions;

namespace Atomic.Core.Tests.Corpus
{
    /// <summary>
    /// Runs the training corpus against the real agent stack.
    ///
    /// <para>Deliberately a <b>scoreboard, not a tripwire</b>: every row in a file is evaluated and
    /// the failures are reported together with a pass rate. Failing on the first bad row would make
    /// a 3,000-question corpus useless for the thing it is for - seeing how wide a defect is, and
    /// watching a number move as it gets fixed.</para>
    ///
    /// <para>Three pseudo-intents are recognised in <c>expect_intent</c>, for decisions made before
    /// any intent exists:</para>
    /// <list type="bullet">
    /// <item><c>COMPOUND</c> - the query must split into clauses and the engine must answer it</item>
    /// <item><c>NOSPLIT</c> - the query must <i>not</i> split; the guard on "compare gold and silver"</item>
    /// <item><c>LANGUAGE</c> - the message must be detected as the row's <c>lang</c>, starting from
    /// the language the session's previous turn was answered in (roadmap 1.4)</item>
    /// </list>
    ///
    /// <para><b>Where this departs from Android:</b> the Kotlin runs one JUnit method per file and
    /// accumulates failures into one message. Here each file is a <c>[Theory]</c> over its
    /// replayable units, so a failure names the row in the test explorer rather than being buried in
    /// a 40-line assertion message - and a fix can be re-run against one row instead of 800. The
    /// scoreboard survives as <see cref="Scoreboard"/>, which is what reports the pass rate across
    /// the whole corpus.</para>
    /// </summary>
    public sealed class CorpusTest
    {
        private readonly ITestOutputHelper _output;

        public CorpusTest(ITestOutputHelper output) => _output = output;

        /// <summary>Card expectations are enforced now that the card layer exists.</summary>
        private const bool EnforceCards = true;

        // ---- Per-file theories ----------------------------------------------------------------
        //
        // One method per corpus file rather than a single theory over all 31, because the files are
        // not interchangeable: `regressions` and `reported` are tripwires where every row is a
        // defect a user actually hit, the four calculator files pin where each branch stops, and the
        // conversation files replay whole threads. A failure should name which of those moved.

        public static TheoryData<string> Properties => Units("properties");
        [Theory, MemberData(nameof(Properties))]
        public void Properties_Pass(string key) => RunUnit("properties", key);

        public static TheoryData<string> Categories => Units("categories");
        [Theory, MemberData(nameof(Categories))]
        public void Categories_Pass(string key) => RunUnit("categories", key);

        public static TheoryData<string> SuperlativesFilters => Units("superlatives_filters");
        [Theory, MemberData(nameof(SuperlativesFilters))]
        public void SuperlativesAndFilters_Pass(string key) => RunUnit("superlatives_filters", key);

        public static TheoryData<string> Comparisons => Units("comparisons");
        [Theory, MemberData(nameof(Comparisons))]
        public void Comparisons_Pass(string key) => RunUnit("comparisons", key);

        public static TheoryData<string> Aggregates => Units("aggregates");
        [Theory, MemberData(nameof(Aggregates))]
        public void Aggregates_Pass(string key) => RunUnit("aggregates", key);

        public static TheoryData<string> IsotopesNuclides => Units("isotopes_nuclides");
        [Theory, MemberData(nameof(IsotopesNuclides))]
        public void IsotopesAndNuclides_Pass(string key) => RunUnit("isotopes_nuclides", key);

        public static TheoryData<string> Safety => Units("safety");
        [Theory, MemberData(nameof(Safety))]
        public void Safety_Pass(string key) => RunUnit("safety", key);

        public static TheoryData<string> Calculations => Units("calculations");
        [Theory, MemberData(nameof(Calculations))]
        public void Calculations_Pass(string key) => RunUnit("calculations", key);

        // The four calculators, each with the negative rows that pin where its branch stops. Kept
        // separate from `calculations` because the guard ordering between them is the fragile part:
        // each new branch sits above an older one that would otherwise claim the same query.

        public static TheoryData<string> UnitConvert => Units("unit_convert");
        [Theory, MemberData(nameof(UnitConvert))]
        public void UnitConversion_Pass(string key) => RunUnit("unit_convert", key);

        public static TheoryData<string> BalanceEquations => Units("balance_equations");
        [Theory, MemberData(nameof(BalanceEquations))]
        public void BalanceEquations_Pass(string key) => RunUnit("balance_equations", key);

        public static TheoryData<string> Decay => Units("decay");
        [Theory, MemberData(nameof(Decay))]
        public void Decay_Pass(string key) => RunUnit("decay", key);

        public static TheoryData<string> Solutions => Units("solutions");
        [Theory, MemberData(nameof(Solutions))]
        public void Solutions_Pass(string key) => RunUnit("solutions", key);

        public static TheoryData<string> Compound => Units("compound");
        [Theory, MemberData(nameof(Compound))]
        public void Compound_Pass(string key) => RunUnit("compound", key);

        public static TheoryData<string> Followups => Units("followups");
        [Theory, MemberData(nameof(Followups))]
        public void Followups_Pass(string key) => RunUnit("followups", key);

        /// <summary>Questions needing several mechanisms at once - a subset and a threshold and an ordinal.</summary>
        public static TheoryData<string> Complex => Units("complex");
        [Theory, MemberData(nameof(Complex))]
        public void Complex_Pass(string key) => RunUnit("complex", key);

        /// <summary>
        /// Whole conversations rather than isolated turns.
        ///
        /// <para>A rule that holds for one follow-up can still drift over five, and an interruption
        /// that is correctly declined can still leave the thread broken behind it. Each session
        /// replays against one <c>DialogueState</c>, so a failure part-way through stops that
        /// thread - which is the point: a conversation is only as good as its weakest hop.</para>
        /// </summary>
        public static TheoryData<string> ConversationsSimple => Units("conversations_simple");
        [Theory, MemberData(nameof(ConversationsSimple))]
        public void ConversationsSimple_Pass(string key) => RunUnit("conversations_simple", key);

        public static TheoryData<string> ConversationsComplex => Units("conversations_complex");
        [Theory, MemberData(nameof(ConversationsComplex))]
        public void ConversationsComplex_Pass(string key) => RunUnit("conversations_complex", key);

        public static TheoryData<string> Datasets => Units("datasets");
        [Theory, MemberData(nameof(Datasets))]
        public void Datasets_Pass(string key) => RunUnit("datasets", key);

        public static TheoryData<string> NamedCompounds => Units("named_compounds");
        [Theory, MemberData(nameof(NamedCompounds))]
        public void NamedCompounds_Pass(string key) => RunUnit("named_compounds", key);

        public static TheoryData<string> MustDefer => Units("must_defer");
        [Theory, MemberData(nameof(MustDefer))]
        public void MustDefer_Pass(string key) => RunUnit("must_defer", key);

        /// <summary>
        /// "What can you do", in every language. A question about the assistant, whose answer is the
        /// feature overview the personality layer holds - so the grounded engine has to decline all
        /// of them, or the user is shown a property of an element instead.
        /// </summary>
        public static TheoryData<string> Capabilities => Units("capabilities");
        [Theory, MemberData(nameof(Capabilities))]
        public void Capabilities_Pass(string key) => RunUnit("capabilities", key);

        public static TheoryData<string> Cards => Units("cards");
        [Theory, MemberData(nameof(Cards))]
        public void Cards_Pass(string key) => RunUnit("cards", key);

        public static TheoryData<string> LocalizedSv => Units("localized_sv");
        [Theory, MemberData(nameof(LocalizedSv))]
        public void LocalizedSwedish_Pass(string key) => RunUnit("localized_sv", key);

        public static TheoryData<string> LocalizedOther => Units("localized_other");
        [Theory, MemberData(nameof(LocalizedOther))]
        public void LocalizedOther_Pass(string key) => RunUnit("localized_other", key);

        /// <summary>
        /// The same twenty-two question shapes in each of the eleven non-English languages.
        ///
        /// <para>Uniform by design: a gap shows up as a row of failures down one language rather
        /// than as scattered noise, and each shape compares directly against its English
        /// equivalent.</para>
        /// </summary>
        public static TheoryData<string> Multilingual => Units("multilingual");
        [Theory, MemberData(nameof(Multilingual))]
        public void Multilingual_Pass(string key) => RunUnit("multilingual", key);

        /// <summary>
        /// Thirty <i>further</i> shapes in each of the twelve languages, reaching the half of the
        /// field registry the first multilingual pass never touched - conductivity, hardness,
        /// appearance, phase, the two discovery fields, oxidation states, configuration, group,
        /// period, valence electrons, protons - plus the aggregate and count shapes.
        /// </summary>
        public static TheoryData<string> MultilingualWide => Units("multilingual_wide");
        [Theory, MemberData(nameof(MultilingualWide))]
        public void MultilingualWide_Pass(string key) => RunUnit("multilingual_wide", key);

        /// <summary>
        /// The computational shapes in every language: thresholds, ranges, negation, ranks, subset
        /// superlatives, medians, mole conversions, nuclide comparisons, neighbours and compound
        /// questions. English is the only language where an operator word is never inflected and
        /// never collides with an article, so this is where the operator layer is actually tested.
        /// </summary>
        public static TheoryData<string> MultilingualOperators => Units("multilingual_operators");
        [Theory, MemberData(nameof(MultilingualOperators))]
        public void MultilingualOperators_Pass(string key) => RunUnit("multilingual_operators", key);

        /// <summary>Two whole threads per language, where localized follow-up inheritance breaks.</summary>
        public static TheoryData<string> ConversationsMulti => Units("conversations_multi");
        [Theory, MemberData(nameof(ConversationsMulti))]
        public void ConversationsMultilingual_Pass(string key) => RunUnit("conversations_multi", key);

        /// <summary>
        /// Threads that change what <i>kind</i> of question they are asking while the subject stays
        /// put. The complement to the deep threads, which vary the subject and hold the shape. Each
        /// hop here hands on a different slot than the one before it, which is where a dialogue
        /// state leaks.
        /// </summary>
        public static TheoryData<string> ConversationsRepair => Units("conversations_repair");
        [Theory, MemberData(nameof(ConversationsRepair))]
        public void ConversationsRepair_Pass(string key) => RunUnit("conversations_repair", key);

        /// <summary>
        /// Three six-turn threads per language. Longer than the multilingual threads because drift
        /// is cumulative: a rule that survives one follow-up can still be wrong by the fifth, and a
        /// deferral that quietly resets the dialogue state is only visible in the turn <i>after</i> it.
        /// </summary>
        public static TheoryData<string> ConversationsDeep => Units("conversations_deep");
        [Theory, MemberData(nameof(ConversationsDeep))]
        public void ConversationsDeep_Pass(string key) => RunUnit("conversations_deep", key);

        /// <summary>
        /// The queries from the bug reports. Unlike the other files this one is a tripwire - every
        /// row is a defect a user actually hit, so a single regression here should fail loudly.
        /// </summary>
        public static TheoryData<string> Regressions => Units("regressions");
        [Theory, MemberData(nameof(Regressions))]
        public void Regressions_Pass(string key) => RunUnit("regressions", key);

        /// <summary>
        /// The second round of reported conversations. A tripwire like the regressions, for the same
        /// reason: every row is a turn a user watched go wrong.
        /// </summary>
        public static TheoryData<string> Reported => Units("reported");
        [Theory, MemberData(nameof(Reported))]
        public void Reported_Pass(string key) => RunUnit("reported", key);

        /// <summary>Which language each message is answered in, the one decision every other file presets.</summary>
        public static TheoryData<string> Language => Units("language");
        [Theory, MemberData(nameof(Language))]
        public void Language_Pass(string key) => RunUnit("language", key);

        /// <summary>Compounds and superlative affixes: one word carrying a property and what is asked about it.</summary>
        public static TheoryData<string> MorphologyRows => Units("morphology");
        [Theory, MemberData(nameof(MorphologyRows))]
        public void Morphology_Pass(string key) => RunUnit("morphology", key);

        /// <summary>Misspellings corrected when a question would otherwise be declined, and the words never to touch.</summary>
        public static TheoryData<string> Spelling => Units("spelling");
        [Theory, MemberData(nameof(Spelling))]
        public void Spelling_Pass(string key) => RunUnit("spelling", key);

        /// <summary>Sets read off an element: "what else is in that group", "the same block as iron".</summary>
        public static TheoryData<string> FocusSets => Units("focus_sets");
        [Theory, MemberData(nameof(FocusSets))]
        public void FocusSets_Pass(string key) => RunUnit("focus_sets", key);

        // ---- Scoreboard ------------------------------------------------------------------------

        /// <summary>
        /// The whole corpus in one pass, reported as a pass rate per file.
        ///
        /// <para>This is the scoreboard the Kotlin file is: it evaluates every row and reports them
        /// together, which is what makes a 3,162-question corpus useful for seeing how <i>wide</i> a
        /// defect is. The per-row theories above tell you which query broke; this tells you whether
        /// a change moved the number.</para>
        ///
        /// <para>It asserts only the total, so a single fix does not have to be chased through 31
        /// separate red tests - the per-file theories are where an individual row fails.</para>
        /// </summary>
        [Fact]
        public void Scoreboard()
        {
            Assert.True(CorpusHarness.Available, "real assets/strings not reachable");
            Assert.True(QuestionCorpus.Available, "corpus not reachable");

            var totalEvaluated = 0;
            var totalPassed = 0;
            var totalKnown = 0;
            var report = new List<string>();

            foreach (var file in QuestionCorpus.Files)
            {
                var sessions = QuestionCorpus.Sessions(file);
                // The defect files are never held out, so the held-out split has nothing for them.
                if (sessions.Count == 0 && QuestionCorpus.Selected == QuestionCorpus.Split.HeldOut) continue;
                Assert.True(sessions.Count > 0, $"corpus file {file}.tsv is empty or missing");

                var failures = new List<string>();
                var evaluated = 0;
                var known = 0;

                foreach (var session in sessions)
                {
                    // One state per session, so a follow-up row inherits from the row before it and
                    // a standalone row starts clean.
                    var state = new DialogueState();
                    foreach (var corpusCase in session)
                    {
                        evaluated++;
                        var problem = Describe(corpusCase, state);
                        if (corpusCase.KnownFailure is { } why)
                        {
                            if (problem is null) failures.Add($"{corpusCase.Where} now passes; remove its known_failure ({why})");
                            else known++;
                        }
                        else if (problem is not null)
                        {
                            failures.Add($"{corpusCase.Where} {problem}");
                        }
                    }
                }

                var passed = evaluated - failures.Count - known;
                totalEvaluated += evaluated;
                totalPassed += passed;
                totalKnown += known;

                report.Add(Line(file, passed, evaluated) + (known > 0 ? $", {known} known failures" : string.Empty));
                foreach (var failure in failures) report.Add("    " + failure);
            }

            foreach (var line in report) _output.WriteLine(line);
            _output.WriteLine(Line("TOTAL", totalPassed, totalEvaluated));

            _output.WriteLine($"known failures: {totalKnown}");

            Assert.True(
                totalPassed + totalKnown == totalEvaluated,
                $"corpus: {totalEvaluated - totalPassed - totalKnown} of {totalEvaluated} rows failed\n" +
                string.Join("\n", report));
        }

        private static string Line(string name, int passed, int evaluated)
        {
            var rate = evaluated == 0 ? 0.0 : 100.0 * passed / evaluated;
            return string.Format(CultureInfo.InvariantCulture,
                "[corpus] {0,-26} {1,5}/{2,-5} passed ({3,5:F1}%)", name, passed, evaluated, rate);
        }

        // ---- Runner ----------------------------------------------------------------------------

        /// <summary>
        /// The replayable units of one file, as xUnit theory data.
        ///
        /// <para>A session replays as one unit because its turns share a <c>DialogueState</c>;
        /// standalone rows are units of one, which is the common case. Keys are strings so a failure
        /// in the test explorer reads <c>Properties_Pass(key: "prop_density_fe")</c> rather than an
        /// index into a list.</para>
        /// </summary>
        private static TheoryData<string> Units(string file)
        {
            var data = new TheoryData<string>();
            if (!QuestionCorpus.Available)
            {
                // Theory data may not be empty - xUnit reports that as a failure of its own, which
                // would mask the real problem. One row carrying the diagnosis is clearer.
                data.Add(MissingCorpus);
                return data;
            }

            foreach (var session in QuestionCorpus.Sessions(file))
                data.Add(QuestionCorpus.KeyOf(session));

            // regressions and reported are never held out, and theory data may not be empty.
            if (data.Count == 0 && QuestionCorpus.Selected == QuestionCorpus.Split.HeldOut)
                data.Add(NoneHeldOut);

            return data;
        }

        private const string MissingCorpus = "<corpus not reachable>";
        private const string NoneHeldOut = "<no rows held out of this file>";

        private void RunUnit(string file, string key)
        {
            // Android skips these rows when the assets are unreachable, because its corpus lives
            // outside the module and a developer without it should still be able to run the suite.
            // Here the corpus, the element tables and the .resw files are all copied into the output
            // by the csproj, so unreachable means broken - and a silently skipped 3,162-row gate is
            // precisely the vacuous pass this whole file exists to prevent.
            Assert.False(key == MissingCorpus, "corpus not reachable under " + AppContext.BaseDirectory);
            if (key == NoneHeldOut) return;
            Assert.True(CorpusHarness.Available, "element tables or .resw strings not reachable");

            var session = QuestionCorpus.Session(file, key);
            var state = new DialogueState();

            foreach (var corpusCase in session)
            {
                var problem = Describe(corpusCase, state);

                // A known failure (corpus format 2) is expected to fail today: failing is a pass
                // here, and passing is a failure, so the mark comes off the moment the fix lands.
                // It ends the thread either way, as a real failure would.
                if (corpusCase.KnownFailure is { } why)
                {
                    if (problem is null)
                        Assert.Fail($"{corpusCase.Where} now passes; remove its known_failure ({why})");
                    return;
                }

                if (problem is null) continue;

                // A thread stops at its first bad hop on purpose: every turn after it inherits a
                // dialogue state the corpus never intended, so those failures would describe a
                // conversation that never happened.
                Assert.Fail($"{corpusCase.Where} {problem}"
                    + (corpusCase.Notes.Length > 0 ? $"\n  note: {corpusCase.Notes}" : string.Empty));
            }
        }

        /// <summary>Evaluate one row, returning what went wrong or null when it passed.</summary>
        private string? Describe(CorpusCase corpusCase, DialogueState state)
        {
            try
            {
                return Evaluate(corpusCase, state);
            }
            catch (Exception ex)
            {
                // A throw is a failure of that row, not of the run. The corpus is a scoreboard, and
                // one query that crashes the planner must not hide the 3,000 behind it.
                return $"threw {ex.GetType().Name}: {ex.Message}";
            }
        }

        private string? Evaluate(CorpusCase corpusCase, DialogueState state)
        {
            if (corpusCase.ExpectIntent == "LANGUAGE")
            {
                var detected = CorpusHarness.DetectLanguage(corpusCase.Query, state.ActiveLanguage);
                state.ActiveLanguage = detected;
                return detected == corpusCase.Lang
                    ? null
                    : $"'{corpusCase.Query}' was detected as {detected}, expected {corpusCase.Lang}";
            }

            var stack = CorpusHarness.StackFor(corpusCase.Lang);

            switch (corpusCase.ExpectIntent)
            {
                case "NOSPLIT":
                    return ClauseSplitter.Split(corpusCase.Query) is not null
                        ? $"'{corpusCase.Query}' must not split, but did"
                        : null;

                case "COMPOUND":
                {
                    var clauses = ClauseSplitter.Split(corpusCase.Query);
                    if (clauses is null)
                        return $"'{corpusCase.Query}' must split into clauses, but did not";
                    if (clauses.Count < 2)
                        return $"'{corpusCase.Query}' split into only {clauses.Count} clause";

                    var answer = stack.Engine.Answer(corpusCase.Query, state);
                    if (answer is null)
                        return $"'{corpusCase.Query}' split correctly but the engine declined it";

                    return string.IsNullOrWhiteSpace(answer.Text)
                        ? $"'{corpusCase.Query}' produced a blank compound answer"
                        : null;
                }
            }

            var plan = stack.Planner.Plan(corpusCase.Query, state);
            var claimed = plan.Intent != Intent.Unknown && plan.Confidence >= stack.Planner.Threshold;

            if (corpusCase.MustDefer)
            {
                return claimed
                    ? $"'{corpusCase.Query}' should have been declined but was claimed as {plan.Intent} " +
                      $"(conf={Fixed(plan.Confidence)}, evidence={string.Join(",", plan.Evidence)})"
                    : null;
            }

            if (!claimed)
            {
                return $"'{corpusCase.Query}' was not claimed (intent={plan.Intent}, " +
                       $"conf={Fixed(plan.Confidence)})";
            }

            if (corpusCase.ExpectIntent is { } expected && !NameMatches(plan.Intent, expected))
            {
                return $"'{corpusCase.Query}' planned as {plan.Intent}, expected {expected}";
            }

            var missing = corpusCase.ExpectFields.Where(f => !plan.FieldIds.Contains(f)).ToList();
            if (missing.Count > 0)
            {
                return $"'{corpusCase.Query}' resolved fields [{string.Join(",", plan.FieldIds)}], " +
                       $"missing [{string.Join(",", missing)}]";
            }

            var result = stack.Executor.Execute(plan);
            if (result is null)
                return $"'{corpusCase.Query}' planned as {plan.Intent} but produced no result";
            if (result is ExecutionResult.Empty)
                return $"'{corpusCase.Query}' produced an empty result";

            var composed = stack.Composer.Compose(result, plan);
            if (string.IsNullOrWhiteSpace(composed.Text))
                return $"'{corpusCase.Query}' composed a blank answer from {result.GetType().Name}";

            // The renderer understands only `###`, `**bold**` and `• `; a leaked resource sentinel
            // or a leaked missing-value sentinel is a user-visible defect, not a cosmetic one.
            if (composed.Text.Contains("str:", StringComparison.Ordinal))
                return $"'{corpusCase.Query}' leaked an unresolved string resource: {Head(composed.Text)}";
            if (composed.Text.Contains("---", StringComparison.Ordinal))
                return $"'{corpusCase.Query}' leaked the missing-value sentinel: {Head(composed.Text)}";

            // Record the turn so the next row in this session can inherit from it.
            state.NoteAnswer(plan, ResultKeys(result));

            if (EnforceCards && corpusCase.ExpectCard is { } expectedCard)
            {
                var card = composed.Card?.Kind;
                if (string.Equals(expectedCard, "none", StringComparison.Ordinal))
                {
                    if (card is not null)
                        return $"'{corpusCase.Query}' should produce no card but produced {card}";
                }
                else if (card is null || !NameMatches(card.Value, expectedCard))
                {
                    return $"'{corpusCase.Query}' produced card {card?.ToString() ?? "none"}, " +
                           $"expected {expectedCard}";
                }
            }

            // Content, not only routing (roadmap 0.6): which row, which number.
            if (corpusCase.ExpectRow is { } expectedRow)
            {
                if (result is not ExecutionResult.Dataset dataset)
                    return $"'{corpusCase.Query}' should answer from {expectedRow}, but produced {result.GetType().Name}";
                if (!string.Equals(dataset.Row.Key, expectedRow, StringComparison.Ordinal))
                    return $"'{corpusCase.Query}' answered from {dataset.Row.Key}, expected {expectedRow}";
            }

            if (corpusCase.ExpectValue is { } expectedValue)
            {
                if (ValueOf(result) is not { } actual)
                    return $"'{corpusCase.Query}' should carry the value {expectedValue}, but {result.GetType().Name} has none";
                if (Math.Abs(actual - expectedValue) > Math.Max(Math.Abs(expectedValue) * 1e-3, 1e-12))
                    return $"'{corpusCase.Query}' carries {actual}, expected {expectedValue}";
            }

            return null;
        }

        /// <summary>The one number an answer stands on, for <c>expect_value</c>.</summary>
        /// <summary>
        /// The one number an answer stands on, for <c>expect_value</c>. For a ranking it is the first
        /// element's, which pins a superlative's direction; for a comparison, the largest value of the
        /// first field compared, which tells a comparison of melting points from the default table
        /// that merely includes them, whatever order the elements come in (roadmap 1.5).
        /// </summary>
        private static double? ValueOf(ExecutionResult result) => result switch
        {
            ExecutionResult.Property p => p.Quantity?.Value,
            ExecutionResult.ElementList l => l.Results.FirstOrDefault()?.Quantity?.Value,
            ExecutionResult.Comparison c => c.FieldIds.Count == 0 || !c.Values.TryGetValue(c.FieldIds[0], out var first)
                ? null
                : first.Select(v => v.Quantity?.Value).Where(v => v.HasValue).Max(),
            ExecutionResult.Dataset d => d.Row.NumericValue,
            ExecutionResult.Aggregate a => a.Value,
            ExecutionResult.Formula f => f.Result.MolarMass,
            ExecutionResult.MoleConversion m => m.Moles,
            _ => null,
        };

        /// <summary>
        /// Whether an enum member is the one the corpus names.
        ///
        /// <para>The corpus is shared with Android byte for byte, so it carries Kotlin's
        /// <c>SCREAMING_SNAKE_CASE</c> member names - <c>PROPERTY_LOOKUP</c>, <c>NFPA_DIAMOND</c> -
        /// while the C# enums are PascalCase. Translating in the comparison rather than rewriting
        /// 3,162 rows is what keeps the next Android corpus sync a straight file copy.</para>
        ///
        /// <para>Underscores are dropped and the rest compared case-insensitively.
        /// <see cref="EnumNamesSurviveTheSnakeCaseFold"/> is what makes that safe: it proves the
        /// fold is injective over both enums, so this can never silently accept the wrong
        /// member.</para>
        /// </summary>
        private static bool NameMatches<T>(T actual, string corpusName) where T : struct, Enum =>
            string.Equals(Fold(actual.ToString()!), Fold(corpusName), StringComparison.OrdinalIgnoreCase);

        private static string Fold(string name) => name.Replace("_", string.Empty, StringComparison.Ordinal);

        /// <summary>
        /// The two enums the corpus names must stay distinguishable once underscores are folded out.
        ///
        /// <para>Without this, adding an <c>Intent.NoData</c> beside an existing <c>Intent.NoDat_a</c>
        /// - or any pair differing only in underscore placement or casing - would make
        /// <see cref="NameMatches"/> accept the wrong member, and the corpus would go green on a
        /// misrouted query. Cheap to assert, invisible if it ever broke.</para>
        /// </summary>
        [Fact]
        public void EnumNamesSurviveTheSnakeCaseFold()
        {
            AssertFoldIsInjective<Intent>();
            AssertFoldIsInjective<Atomic.Core.Ai.Cards.ChatCardKind>();
        }

        private static void AssertFoldIsInjective<T>() where T : struct, Enum
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in Enum.GetNames<T>())
            {
                var folded = Fold(name);
                Assert.False(seen.TryGetValue(folded, out var other),
                    $"{typeof(T).Name}.{name} and {typeof(T).Name}.{other} both fold to '{folded}', " +
                    "so a corpus intent name could match either");
                seen[folded] = name;
            }
        }

        private static IReadOnlyList<string> ResultKeys(ExecutionResult result) => result switch
        {
            ExecutionResult.Property p => [p.Element.Key],
            ExecutionResult.Comparison c => c.Elements.Select(e => e.Key).ToList(),
            ExecutionResult.ElementList l => l.Results.Select(v => v.Element.Key).ToList(),
            ExecutionResult.Aggregate a => a.Contributors.Select(v => v.Element.Key).ToList(),
            ExecutionResult.Isotopes i => [i.Element.Key],
            ExecutionResult.Safety s => [s.Element.Key],
            ExecutionResult.Comparative c => [c.Winner.Key, c.Loser.Key],
            ExecutionResult.Neighbour n => [n.To.Key],
            ExecutionResult.Nuclide n => [n.Element.Key],
            ExecutionResult.IsotopeComparison c =>
                new[] { c.Left.Element.Key, c.Right.Element.Key }.Distinct(StringComparer.Ordinal).ToList(),
            ExecutionResult.NoData d => d.Element is null ? [] : [d.Element.Key],
            _ => [],
        };

        private static string Fixed(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

        private static string Head(string text) =>
            text.Length <= 120 ? text : text[..120];
    }
}
