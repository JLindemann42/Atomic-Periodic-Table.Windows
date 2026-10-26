using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Text;

namespace Atomic.Core.Ai.Nlu
{
    /// <summary>
    /// Turns a natural-language question into an executable <see cref="QueryPlan"/>.
    ///
    /// <para>The planner deliberately declines most queries. It only claims one when there is
    /// positive evidence that it can do better than the existing handlers - an operator
    /// (comparator, superlative, aggregation, unit request), several elements to compare, or an
    /// element-and-field pair where the field is resolved and the data is genuinely absent.
    /// Everything else returns a plan with <see cref="Intent.Unknown"/> and low confidence so the
    /// caller falls through untouched.</para>
    ///
    /// <para>That is what makes this safe to add alongside the existing router rather than in place
    /// of it: a query the planner does not claim behaves exactly as it did before.</para>
    ///
    /// <para><b>This file holds the constants and the sub-planners.</b> The <c>Plan</c> cascade
    /// that calls them lives in QueryPlanner.cs. The split is by design: the cascade's guard
    /// <i>order</i> is its specification, and keeping it in a file of its own means a future
    /// Android sync diffs against 900 lines of ordering rather than 2,300 lines of everything.</para>
    /// </summary>
    public sealed partial class QueryPlanner
    {
        private readonly KnowledgeStore _store;
        private readonly DatasetIndex _datasets;
        private readonly EntityResolver _entities;
        private readonly FieldResolver _fields;
        private readonly HybridRetriever? _retriever;

        public QueryPlanner(
            KnowledgeStore store,
            DatasetIndex datasets,
            EntityResolver entities,
            FieldResolver fields,
            HybridRetriever? retriever)
        {
            _store = store;
            _datasets = datasets;
            _entities = entities;
            _fields = fields;
            _retriever = retriever;
        }

        /// <summary>Plans at or above this confidence are used; below it the caller falls back.</summary>
        public double Threshold => ConfidenceThreshold;

        // ---- Sub-planners ------------------------------------------------------------------

        /// <summary>
        /// Recognise the arithmetic questions: formula mass, composition, neutron counts and mole
        /// conversions. Returns null when the query is not one of those.
        /// </summary>
        /// <remarks>
        /// The internal order here is load-bearing and matches Android exactly:
        /// BALANCE, SOLUTION, DECAY, MOLE, ISOTOPE_COMPARISON, NUCLIDE_COUNT, ISOTOPES,
        /// FORMULA_MASS. Each of the first three sits above a branch that would otherwise swallow
        /// it - the reasons are on the individual steps.
        /// </remarks>
        internal QueryPlan? CalculationPlan(string rawQuery, string normalized)
        {
            // --- Balance an equation --------------------------------------------------------
            // First, and on the raw query. The arrow is the most specific syntax any of these
            // questions carry, and `normalized` is lowercased, which destroys every formula in the
            // sentence. A plan is produced only when the equation actually parses, so "x -> y"
            // written in prose falls through to everything below untouched.
            if (EquationArrow.IsMatch(rawQuery))
            {
                var outcome = EquationBalancer.Parse(rawQuery, symbol => _store.BySymbol(symbol) is not null);
                var evidence = outcome switch
                {
                    EquationBalancer.ParseOutcome.Sides sides =>
                        $"equation {sides.Reactants.Count} -> {sides.Products.Count}",
                    // Claimed too. The executor answers it with "too large", which is the truth;
                    // letting it fall through would answer a readable equation by recommending the
                    // balancer the user is already effectively using.
                    EquationBalancer.ParseOutcome.TooManySpeciesOutcome => "equation too large",
                    _ => null,
                };

                if (evidence is not null)
                {
                    return new QueryPlan(
                        Intent.BalanceEquation,
                        RawQuery: rawQuery,
                        Confidence: 0.9,
                        Evidence: [evidence]);
                }
            }

            // --- Solutions: molarity, mass-moles-concentration, dilution ---------------------
            // Above the mole branch, because MoleQuantity matches the "0.5 mol" inside "0.5 mol/L"
            // and would answer a molarity question with a particle count.
            if (SolutionPlan(rawQuery, normalized) is { } solution) return solution;

            // --- Decay over time -------------------------------------------------------------
            // Above the isotope branch further down: ISOTOPE_WORDS holds "decay", "half life" and
            // "how long does", so every one of these questions would otherwise be answered with
            // the isotope table instead of the arithmetic that was asked for.
            if (DecayPlan(rawQuery, normalized) is { } decay) return decay;

            // --- Moles to particles ----------------------------------------------------------
            if (MentionsAny(normalized, Lexicon.MOLE_WORDS))
            {
                var moles = ChemistryMath.MoleQuantity.Match(normalized);
                if (moles.Success)
                {
                    return new QueryPlan(
                        Intent.MoleConversion,
                        RawQuery: rawQuery,
                        Confidence: 0.85,
                        Evidence: [$"moles {moles.Groups[1].Value}"]);
                }

                // The other direction: a mass in grams, converted through the substance's molar
                // mass. "If I have 12 grams of carbon-12, how many moles is that" used to resolve
                // magnesium from the 12 and answer with a two-element list.
                var grams = Grams.Match(normalized);
                if (grams.Success)
                {
                    var substance = _entities.ResolveAll(rawQuery, limit: 1).FirstOrDefault();
                    if (substance is not null)
                    {
                        return new QueryPlan(
                            Intent.MoleConversion,
                            Entities: [new EntityRef.Element(substance.Key)],
                            RawQuery: rawQuery,
                            Confidence: 0.85,
                            Evidence: [$"grams {grams.Groups[1].Value}"]);
                    }
                }
            }

            // --- Two nuclides compared: "uranium-235 vs uranium-238" -------------------------
            var nuclides = NuclidesIn(normalized)
                .Select(n => (EntityRef)new EntityRef.Nuclide(n.Element.Key, n.MassNumber))
                .Distinct()
                .Take(2)
                .ToList();

            if (nuclides.Count == 2)
            {
                return new QueryPlan(
                    Intent.IsotopeComparison,
                    Entities: nuclides,
                    RawQuery: rawQuery,
                    Confidence: 0.9,
                    Evidence: ["nuclide comparison"]);
            }

            // --- Neutrons in a nuclide -------------------------------------------------------
            if (Lexicon.NEUTRON_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal)))
            {
                var match = Nuclide.Match(normalized);
                if (match.Success)
                {
                    var name = match.Groups[1].Value;
                    var element = NuclideElement(name);
                    // mass >= Z, as the two branches above also require. No nuclide has fewer
                    // nucleons than protons, and without it "how many neutrons in uranium-3" is
                    // claimed and reaches the executor, where 3 - 92 gives -89 neutrons. Android
                    // omits the guard here while checking it in ISOTOPE_COMPARISON and ISOTOPES;
                    // see D14. Added with corpus evidence, not on inspection: no row moves.
                    if (element is not null && TryMass(match, out var mass) && mass >= element.AtomicNumber)
                    {
                        return new QueryPlan(
                            Intent.NuclideCount,
                            Entities: [new EntityRef.Element(element.Key)],
                            // The mass number rides in Limit, as the decay branch also does.
                            Limit: mass,
                            RawQuery: rawQuery,
                            Confidence: 0.9,
                            Evidence: [$"nuclide {name}-{mass.ToString(CultureInfo.InvariantCulture)}"]);
                    }
                }
            }

            // --- Half-life of a specific nuclide ---------------------------------------------
            if (MentionsAny(normalized, Lexicon.ISOTOPE_WORDS))
            {
                var match = Nuclide.Match(normalized);
                if (match.Success)
                {
                    var element = NuclideElement(match.Groups[1].Value);
                    if (element is not null && TryMass(match, out var mass) && mass >= element.AtomicNumber)
                    {
                        return new QueryPlan(
                            Intent.Isotopes,
                            Entities: [new EntityRef.Element(element.Key)],
                            Limit: DefaultIsotopeLimit,
                            RawQuery: rawQuery,
                            Confidence: 0.9,
                            Evidence: [$"isotope {element.Key}-{mass.ToString(CultureInfo.InvariantCulture)}"]);
                    }
                }
            }

            // --- Formula mass and composition ------------------------------------------------
            var wantsMass = Lexicon.MOLAR_MASS_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal));
            var wantsComposition = Lexicon.COMPOSITION_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal));
            if (!wantsMass && !wantsComposition) return null;

            var candidate = CompoundIn(rawQuery, normalized);
            if (candidate is null) return null;

            return new QueryPlan(
                Intent.FormulaMass,
                FieldIds: wantsComposition ? ["composition"] : [],
                RawQuery: candidate,
                Confidence: 0.9,
                Evidence: [wantsComposition ? $"composition of {candidate}" : $"molar mass of {candidate}"]);
        }

        /// <summary>
        /// A standalone unit conversion, on a number the user supplied.
        ///
        /// <para>Every one of the conditions below exists to keep this off a query that already has
        /// a better answer. Taken together they say: there is a number with a unit, a target was
        /// named, this is not an element's property being asked for, nothing is being ranked or
        /// filtered, and the two units actually relate.</para>
        ///
        /// <para>The property-lookup guard is deliberately run against the query with its
        /// <b>units removed</b>. A unit token resolves a field all by itself - "GPa" reaches the
        /// pressure fields, "eV" the ionisation energies, "Pa" even resolves protactinium - so a
        /// guard that simply asked whether a field resolved declined "convert 5 GPa to MPa" on the
        /// strength of the very units being converted. What actually distinguishes "density of gold
        /// in kg/m³" is that a field and an element are named <i>outside</i> the unit expression.</para>
        /// </summary>
        internal QueryPlan? UnitConvertPlan(string rawQuery, Operators operators)
        {
            // Ranking and filtering carry numbers with units too: "elements denser than 5 g/cm³".
            if (operators.Comparators.Count > 0 || operators.SubsetFilters.Count > 0) return null;
            if (operators.SuperlativeDescending is not null || operators.TopN is not null) return null;
            if (operators.Aggregation != Aggregation.None || operators.Range is not null) return null;

            var source = QuantityScanner.Scan(rawQuery).FirstOrDefault();
            if (source is null) return null;

            // The scanner's own reading comes first. OperatorExtractor.TargetUnit accepts any unit
            // with a preposition on either side, which is right for a property lookup - there is no
            // source quantity there to confuse it - but here it picks the unit being converted
            // *from*: in "convert 5 GPa to MPa" the GPa has "to" on its right, so it was read as
            // the target and the conversion collapsed to a no-op.
            var target = QuantityScanner.TargetUnit(rawQuery) ?? operators.TargetUnit;
            if (target is null) return null;
            if (string.Equals(target, source.Unit, StringComparison.Ordinal)) return null;

            // A dimensional mismatch means the query was misread. Declining is the honest move; the
            // alternative is inventing a relationship between a temperature and a volume.
            if (!UnitConverter.Convertible(source.Unit, target)) return null;

            var residual = WithoutUnits(rawQuery, source);
            if (_fields.ResolveAll(residual, limit: 1).Count > 0 &&
                _entities.ResolveAll(residual, limit: 1).Count > 0) return null;

            return new QueryPlan(
                Intent.UnitConvert,
                RawQuery: rawQuery,
                TargetUnit: target,
                // Below the 0.9 of the syntax-driven branches, since a unit token is weaker
                // evidence than an arrow or a nuclide, and above PropertyLookup's 0.8 so a phrasing
                // that reaches both resolves toward the conversion.
                Confidence: 0.85,
                Evidence: [$"convert {source.Unit} to {target}"]);
        }

        /// <summary>
        /// The query with its unit expression taken out, for the property-lookup test above.
        ///
        /// <para>Removes the scanned quantity's own span and every remaining token that is itself a
        /// unit, so what is left is the words the sentence used to name something - which is where
        /// a genuine property lookup keeps its field and its element.</para>
        /// </summary>
        private static string WithoutUnits(string rawQuery, ScannedQuantity source)
        {
            var length = Math.Min(source.Length, rawQuery.Length - source.Start);
            var withoutSource = length > 0 ? rawQuery.Remove(source.Start, length) : rawQuery;

            return string.Join(" ", withoutSource.Split(' ').Where(token =>
            {
                var cleaned = token.Trim(',', ';', '.', '(', ')');
                if (cleaned.Length == 0) return true;
                return UnitConverter.KnownUnit(cleaned) is null &&
                       !Lexicon.UNIT_WORDS.ContainsKey(cleaned.ToLowerInvariant()) &&
                       !Lexicon.SCRIPT_UNIT_WORDS.ContainsKey(cleaned);
            }));
        }

        /// <summary>
        /// A solution-chemistry question: molarity, a mass from a concentration, or a dilution.
        ///
        /// <para>Sits above the mole branch on purpose. <c>MoleQuantity</c> matches the "0.5 mol"
        /// inside "0.5 mol/L", so left below it "how many grams of NaCl for 250 mL of 0.5 mol/L"
        /// would be answered as a particle count.</para>
        /// </summary>
        internal QueryPlan? SolutionPlan(string rawQuery, string normalized)
        {
            var molarities = QuantityScanner.Of(rawQuery, Dimension.Molarity);
            var volumes = QuantityScanner.Of(rawQuery, Dimension.Volume);
            var amounts = QuantityScanner.Of(rawQuery, Dimension.Amount);
            var masses = Grams.Matches(normalized).Count;

            var framed = MentionsAny(normalized, Lexicon.SOLUTION_WORDS);
            var dilutes = MentionsAny(normalized, Lexicon.DILUTION_WORDS);

            // Two independent quantities, or one plus an explicit frame. A single "250 mL" on its
            // own is not a solution question, and claiming it would take volume conversions away
            // from the converter that answers them properly.
            var quantities = molarities.Count + volumes.Count + amounts.Count + masses;
            if (quantities < 2) return null;
            if (molarities.Count == 0 && !framed && !dilutes) return null;

            return new QueryPlan(
                Intent.SolutionCalc,
                RawQuery: rawQuery,
                Literal: CompoundIn(rawQuery, normalized),
                // An explicit molarity unit is unambiguous syntax, on a par with an arrow or a
                // nuclide. A frame word carrying units spelled out in words is weaker, and matches
                // what the mole branch claims for the same kind of evidence.
                Confidence: molarities.Count > 0 ? 0.9 : 0.85,
                Evidence:
                [
                    dilutes ? "dilution" : "solution",
                    $"quantities {quantities.ToString(CultureInfo.InvariantCulture)}",
                ]);
        }

        /// <summary>
        /// A decay-arithmetic question: how much is left after a time, or how long a decay takes.
        ///
        /// <para>Sits above the isotope branch, which fires at 0.9 on ISOTOPE_WORDS - a list holding
        /// "decay", "half life", "stable" and "how long does". Placed after it, this intent would
        /// never run at all.</para>
        ///
        /// <para>Requires a number to work with. Without that gate a bare "half-life of carbon-14"
        /// becomes an arithmetic question with no arithmetic in it, and the isotope table answers
        /// that better.</para>
        /// </summary>
        internal QueryPlan? DecayPlan(string rawQuery, string normalized)
        {
            var nuclides = NuclidesIn(normalized).Take(2).ToList();

            // Two nuclides written out is a comparison - "uranium-235 vs uranium-238" - and the
            // branch above answers it properly. Claiming it here would answer a side-by-side
            // question with arithmetic about one of the two.
            if (nuclides.Count != 1) return null;
            var (element, mass) = nuclides[0];

            var times = QuantityScanner.Of(rawQuery, Dimension.Time);
            var amounts = Grams.Matches(normalized).Count +
                          QuantityScanner.Of(rawQuery, Dimension.Amount).Count;
            var asksHowMuch = MentionsAny(normalized, Lexicon.REMAINS_WORDS);

            if (times.Count == 0 && amounts < 2) return null;

            return new QueryPlan(
                Intent.DecayCalc,
                Entities: [new EntityRef.Element(element.Key)],
                // The mass number rides in Limit, as the neutron-count branch also does.
                Limit: mass,
                RawQuery: rawQuery,
                Confidence: times.Count > 0 && (asksHowMuch || amounts > 0) ? 0.9 : 0.85,
                Evidence: [$"decay {element.Key}-{mass.ToString(CultureInfo.InvariantCulture)}"]);
        }

        /// <summary>
        /// Recognise a question about how two elements stand relative to one another.
        ///
        /// <para>Covers three phrasings that all reduce to the same operation:</para>
        /// <list type="bullet">
        /// <item>"is gold denser than lead" - a claim to confirm or deny</item>
        /// <item>"which is heavier, gold or silver" - pick the winner</item>
        /// <item>"how much denser is gold than lead" - the ratio</item>
        /// </list>
        ///
        /// <para>The comparative adjective usually <i>is</i> the property, so it is consulted when
        /// no field was resolved explicitly - "denser" means density, "heavier" means atomic mass.</para>
        /// </summary>
        internal static QueryPlan? ComparativePlan(
            string rawQuery,
            string normalized,
            IReadOnlyList<string> elementKeys,
            IReadOnlyList<string> fieldIds)
        {
            var adjective = LongestComparativeAdjective(normalized);

            var fieldId = adjective?.Value.Field ?? fieldIds.FirstOrDefault();
            if (fieldId is null) return null;
            var greaterWins = adjective?.Value.Ascending ?? true;

            // Against the raw query too: the Romance copulas differ from their conjunctions only by
            // an accent, which normalisation folds away.
            var opener = rawQuery.Trim().ToLowerInvariant();
            var isYesNo = Lexicon.YESNO_OPENERS.Any(
                o => normalized.StartsWith(o, StringComparison.Ordinal) ||
                     opener.StartsWith(o, StringComparison.Ordinal));
            var isWhich = Lexicon.WHICH_OF_TWO.Any(w => normalized.Contains(w, StringComparison.Ordinal));
            var byHowMuch = Lexicon.BY_HOW_MUCH.Any(w => normalized.Contains(w, StringComparison.Ordinal));

            // Without one of these framings it is an ordinary side-by-side comparison.
            if (!isYesNo && !isWhich && !byHowMuch && adjective is null) return null;

            return new QueryPlan(
                Intent.Comparative,
                Entities: elementKeys.Take(2).Select(k => (EntityRef)new EntityRef.Element(k)).ToList(),
                FieldIds: [fieldId],
                SortDescending: greaterWins,
                // Limit doubles as the question shape: 1 yes/no, 2 which-of, 3 by-how-much.
                Limit: byHowMuch ? 3 : isYesNo ? 1 : 2,
                Confidence: 0.85,
                RawQuery: rawQuery,
                Evidence: [$"comparative {fieldId}"]);
        }

        /// <summary>
        /// A threshold expressed as another element: "denser than iron", "hotter than tungsten".
        ///
        /// <para>Takes the named element's own value for the field as the bound, so the comparison
        /// is against a real figure rather than requiring the user to know it.</para>
        /// </summary>
        internal Filter.FieldCompare? ElementThreshold(
            string normalized,
            IReadOnlyList<string> elementKeys,
            IReadOnlyList<string> fieldIds)
        {
            var greater = Lexicon.GREATER.Any(w => TextMatching.ContainsWord(normalized, w));
            var less = Lexicon.LESS.Any(w => TextMatching.ContainsWord(normalized, w));
            var adjective = LongestComparativeAdjective(normalized);

            // Periphrastic comparatives: the Romance languages build "denser than gold" out of
            // three separate words - "plus denses que l'or", "más densos que el oro" - so neither a
            // single comparative adjective nor a GREATER phrase is present to find. The pattern is
            // a degree word plus a comparison particle, with the field carried by the bare adjective
            // ("denses"), which FieldResolver already resolves. English "more X than" lands here too.
            var particle = Lexicon.THAN_WORDS.Any(w => TextMatching.ContainsWord(normalized, w));
            var periphrasticGreater = particle &&
                Lexicon.MOST.Any(w => TextMatching.ContainsWord(normalized, w));
            var periphrasticLess = particle &&
                Lexicon.LEAST.Any(w => TextMatching.ContainsWord(normalized, w));

            if (!greater && !less && adjective is null && !periphrasticGreater && !periphrasticLess)
                return null;

            var fieldId = adjective?.Value.Field ?? fieldIds.FirstOrDefault();
            if (fieldId is null) return null;

            var reference = elementKeys.Select(k => _store.Element(k)).FirstOrDefault(e => e is not null);
            if (reference is null) return null;

            var bound = reference.Quantity(fieldId);
            if (bound is null) return null;

            var wantsGreater =
                adjective is not null ? adjective.Value.Value.Ascending :
                greater ? true :
                less ? false :
                periphrasticGreater && !periphrasticLess;

            return new Filter.FieldCompare(fieldId, wantsGreater ? Op.Gt : Op.Lt, bound);
        }

        // ---- Shared predicates and lookups --------------------------------------------------

        /// <summary>
        /// The compound a query is about, written as a formula or named in words.
        ///
        /// <para>Shared by the formula-mass and solution branches so the two cannot disagree about
        /// what "table salt" means.</para>
        /// </summary>
        internal string? CompoundIn(string rawQuery, string normalized)
        {
            // Prefer an explicit formula in the raw text, where capitalisation survives.
            foreach (Match match in Formula.Matches(rawQuery))
            {
                var candidate = match.Value;
                if (!candidate.Any(char.IsDigit) && candidate.Length <= 2) continue;
                if (ChemistryMath.ParseFormula(candidate, AtomicMassOf) is not null) return candidate;
            }

            // "percentage composition of water" names a compound rather than writing it. Matched on
            // word boundaries: a plain Contains found "salt" inside "basalt", so "what is the
            // composition of basalt" was answered with the formula mass of NaCl. Longest name
            // first, so "table salt" is preferred over "salt".
            foreach (var (name, formula) in CommonCompoundsByLength)
            {
                if (TextMatching.ContainsWord(normalized, name)) return formula;
            }
            return null;
        }

        private double? AtomicMassOf(string symbol) =>
            _store.BySymbol(symbol)?.Quantity("atomic_mass")?.Value;

        /// <summary>
        /// The element a nuclide names, in any supported language.
        ///
        /// <para><c>Store.Element</c> is keyed on the lowercase English name, so Swedish "uran 238"
        /// and German "uran 238" resolved nothing and fell through to a property lookup. The entity
        /// resolver already carries every language's names and symbols.</para>
        /// </summary>
        private ElementRecord? NuclideElement(string name) =>
            _store.Element(name)
            ?? _store.BySymbol(name)
            ?? (_entities.ResolveAll(name, limit: 1).FirstOrDefault() is { } match
                ? _store.Element(match.Key)
                : null);

        /// <summary>
        /// Every nuclide the query names, in order, deduplicated.
        ///
        /// <para>Every match, not the first. The nuclide pattern is <c>word gap digits</c>, and a
        /// sentence like "how much of 100 g of carbon-14 remains" matches "of 100" before it
        /// reaches the nuclide - so taking the first match declines the question on the strength of
        /// the word "of".</para>
        ///
        /// <para>The mass number must be at least the atomic number, which is what rejects those
        /// accidental matches: no nuclide has fewer nucleons than protons.</para>
        /// </summary>
        private IEnumerable<(ElementRecord Element, int MassNumber)> NuclidesIn(string normalized)
        {
            var seen = new HashSet<(string, int)>();
            foreach (Match match in Nuclide.Matches(normalized))
            {
                var element = NuclideElement(match.Groups[1].Value);
                if (element is null) continue;
                if (!TryMass(match, out var mass)) continue;
                if (mass < element.AtomicNumber) continue;
                if (!seen.Add((element.Key, mass))) continue;
                yield return (element, mass);
            }
        }

        private static bool TryMass(Match match, out int mass) =>
            int.TryParse(match.Groups[2].Value, NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out mass);

        /// <summary>
        /// Whether a query mentions one of a lexicon's phrases.
        ///
        /// <para>Single Latin words match on word boundaries, multi-word phrases and non-spaced
        /// scripts by containment. Plain Contains over the whole list is what let "sure" match the
        /// French safety word "sur" and answer a bare acknowledgement with an NFPA hazard rating.</para>
        /// </summary>
        private static bool MentionsAny(string normalized, IReadOnlyList<string> phrases)
        {
            foreach (var phrase in phrases)
            {
                if (phrase.Contains(' ', StringComparison.Ordinal) || phrase.Any(c => c > 0x2000))
                {
                    if (normalized.Contains(phrase, StringComparison.Ordinal)) return true;
                    continue;
                }
                if (TextMatching.ContainsWord(normalized, phrase)) return true;

                // A short grammatical ending is still the same word - Swedish "farligt" for
                // "farlig", German "gefährliche" for "gefährlich". Only for phrases long enough
                // that a suffix match cannot be an accident; that length floor is what keeps "sure"
                // from matching "sur".
                if (phrase.Length < MinSuffixMatch) continue;
                if (TextMatching.SplitQueryTokens(normalized).Any(
                        word => word.Length > phrase.Length &&
                                word.Length - phrase.Length <= MaxWordSuffix &&
                                word.StartsWith(phrase, StringComparison.Ordinal)))
                    return true;
            }
            return false;
        }

        /// <summary>The longest comparative adjective the query carries, or null.</summary>
        private static KeyValuePair<string, (string Field, bool Ascending)>? LongestComparativeAdjective(
            string normalized)
        {
            foreach (var entry in ComparativeAdjectivesByLength)
                if (TextMatching.ContainsWord(normalized, entry.Key)) return entry;
            return null;
        }

        /// <summary>+1 for the element after, -1 for the one before, null when not a neighbour question.</summary>
        internal static int? NeighbourDirection(string normalizedQuery)
        {
            foreach (var (word, direction) in NeighbourWordsByLength)
                if (normalizedQuery.Contains(word, StringComparison.Ordinal)) return direction;
            return null;
        }

        /// <summary>
        /// The alloy a query names, longest name first so "stainless steel" beats "steel".
        ///
        /// <para>The alloy table is English, so the query is glossed first: "vad är stål gjort av"
        /// names Steel as plainly as "what is steel made of" does, and matching only the English
        /// title left it to lexical retrieval, which ranked <i>Stainless steel</i> above it.</para>
        /// </summary>
        internal DatasetRow? AlloyIn(string normalizedQuery)
        {
            var glossed = WithDatasetGloss(normalizedQuery, normalizedQuery);
            return _datasets.InDataset(DatasetIndex.Datasets.Alloy)
                .OrderByDescending(row => row.Title.Length)
                .FirstOrDefault(row => TextMatching.ContainsWord(glossed, row.Title.ToLowerInvariant()));
        }

        /// <summary>The property family a query names, longest phrase first so "heat" cannot shadow one.</summary>
        internal static FieldCategory? CategoryIn(string normalizedQuery)
        {
            foreach (var (word, category) in CategoryWordsByLength)
                if (normalizedQuery.Contains(word, StringComparison.Ordinal)) return category;
            return null;
        }

        /// <summary>
        /// The raw query with the English name of any glossed topic appended.
        ///
        /// <para>Appended rather than substituted, so the original words still contribute to the
        /// score and a query that happens to contain a glossed word is not rewritten out from under
        /// itself.</para>
        /// </summary>
        private static string WithDatasetGloss(string rawQuery, string normalized)
        {
            // On word boundaries. A plain Contains found the Swedish word for steel, "stål", inside
            // the English "cry**stal**lographic", so "crystallographic data for iron" was glossed
            // with "steel" and answered from the alloy table.
            var glosses = Lexicon.DATASET_GLOSS
                .Where(e => TextMatching.ContainsWord(normalized, e.Key))
                .Select(e => e.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return glosses.Count == 0 ? rawQuery : rawQuery + " " + string.Join(" ", glosses);
        }

        /// <summary>
        /// Some superlatives name the property implicitly: "densest" already means density. Only
        /// used when no field was resolved explicitly.
        /// </summary>
        internal static string? InferSortField(string rawQuery)
        {
            var q = TextMatching.NormalizeForLookup(rawQuery);
            foreach (var (word, fieldId) in ImplicitSuperlatives)
                if (q.Contains(word, StringComparison.Ordinal)) return fieldId;
            return null;
        }

        /// <summary>
        /// Aspects a multi-part comparison asked for that no stored field can supply.
        ///
        /// <para>Naming them lets the answer say what it could not cover, instead of quietly
        /// answering a narrower question than the one asked.</para>
        /// </summary>
        internal static IReadOnlyList<string> UnsupportedAspects(string normalizedQuery) =>
            UncomparableAspects.Where(a => normalizedQuery.Contains(a, StringComparison.Ordinal)).ToList();

        // ---- Patterns -----------------------------------------------------------------------

        /// <summary>
        /// An element name followed by a mass number.
        ///
        /// <para><c>\p{L}</c> rather than <c>a-z</c>: a nuclide is written in whatever script the
        /// user writes in - "کاربن-14", "कार्बन-14", "碳-14" - and a Latin-only pattern found none
        /// of them, so every non-Latin nuclide question was declined.</para>
        ///
        /// <para><c>\p{M}</c> alongside <c>\p{L}</c> for the same reason the tokenizer needs it: an
        /// Indic vowel sign is a mark, not a letter, so a letters-only run stopped at the first
        /// matra and "ऑक्सीजन-18" was read as the nuclide "जन-18" - an element the table has never
        /// heard of, so the question was declined outright.</para>
        /// </summary>
        private static readonly Regex Nuclide =
            JavaRegex.Compile(@"([\p{L}\p{M}]{1,20})\s*[-\s]\s*(\d{1,3})");

        /// <summary>A mass in grams, in the spellings users actually write.</summary>
        private static readonly Regex Grams =
            JavaRegex.Compile(@"([\d.]+)\s*(?:g|gram|grams|gm|gramm|gramme|grammes)\b");

        /// <summary>
        /// A candidate chemical formula. Deliberately case-insensitive - users write "h2so4" far
        /// more often than "H2SO4" - with ChemistryMath deciding whether it actually parses.
        /// </summary>
        private static readonly Regex Formula =
            JavaRegex.Compile(@"\b([A-Za-z][A-Za-z0-9()]{1,15})\b");

        /// <summary>
        /// A chemical equation's arrow.
        ///
        /// <para>The balancer's, not a copy of it. Android's planner used to carry its own pattern
        /// and the two drifted: only the planner's excluded <c>==</c>, <c>&lt;=</c>, <c>&gt;=</c>
        /// and <c>!=</c>, so a query the planner correctly refused to read as an equation was read
        /// as one the moment it reached the solver.</para>
        /// </summary>
        private static readonly Regex EquationArrow = EquationBalancer.Arrow;

        // ---- Tables, sorted once ------------------------------------------------------------
        //
        // Android sorts each of these inside the method that reads it, so a single query re-sorts
        // the comparative adjectives up to three times and the compound names once per branch.

        private static readonly KeyValuePair<string, (string Field, bool Ascending)>[]
            ComparativeAdjectivesByLength =
                Lexicon.COMPARATIVE_ADJECTIVES.OrderByDescending(e => e.Key.Length).ToArray();

        private static readonly KeyValuePair<string, string>[] CommonCompoundsByLength =
            Lexicon.COMMON_COMPOUNDS.OrderByDescending(e => e.Key.Length).ToArray();

        private static readonly KeyValuePair<string, int>[] NeighbourWordsByLength =
            Lexicon.NEIGHBOUR_WORDS.OrderByDescending(e => e.Key.Length).ToArray();

        private static readonly KeyValuePair<string, FieldCategory>[] CategoryWordsByLength =
            Lexicon.CATEGORY_WORDS.OrderByDescending(e => e.Key.Length).ToArray();

        // ---- Constants ----------------------------------------------------------------------

        private const double ConfidenceThreshold = 0.7;

        /// <summary>
        /// The share of a query's content a reference-table row must cover to be answered from
        /// (roadmap 1.1).
        ///
        /// <para>Read off the dev corpus. The rows that must be declined cover at most 0.66 - "what
        /// is the charge of the sulfate ion" by the glossary's "Ion", which holds two of its three
        /// words - and every row that must be answered covers all of its question once the tables
        /// carry the words people use for them (anion names, the base unit, "colour", "standard").
        /// Set nearer the declines than the answers on purpose: a wrong answer costs more than a
        /// missing one.</para>
        /// </summary>
        private const double MinDatasetCoverage = 0.75;

        private const int DefaultListLimit = 10;
        private const int DefaultIsotopeLimit = 8;

        /// <summary>
        /// Shortest query a lexical dataset match is trusted on.
        ///
        /// <para>One or two words carry too little signal: "sure" retrieved sulfur and "hey"
        /// retrieved a dictionary row, both above the fallback's confidence floor.</para>
        /// </summary>
        private const int MinDatasetTokens = 3;

        /// <summary>
        /// Shortest token treated as naming a topic rather than joining a sentence.
        ///
        /// <para>Four, so that pronouns and prepositions - which are absent from the corpus for
        /// reasons that have nothing to do with the subject - cannot block a legitimate follow-up.</para>
        /// </summary>
        private const int MinTopicWord = 4;

        /// <summary>
        /// Longest query still read as "just a family name".
        ///
        /// <para>Three, to cover a definite article and a plural in the languages that write them
        /// separately ("the noble gases"), while staying well short of a real question.</para>
        /// </summary>
        private const int MaxBareFamilyTokens = 3;

        /// <summary>Shortest lexicon word that may be matched by stem rather than by whole word.</summary>
        private const int MinSuffixMatch = 5;

        /// <summary>Longest grammatical ending accepted on a lexicon word.</summary>
        private const int MaxWordSuffix = 3;

        /// <summary>
        /// Longest query that still reads as a follow-up for the purpose of inheriting a field.
        ///
        /// <para>Five words covers the shapes the mechanism exists for - "and iron?", "what about
        /// silver", "and its melting point" - without swallowing a self-contained question that
        /// happens to resolve no field.</para>
        /// </summary>
        private const int MaxFollowUpTokens = 5;

        /// <summary>The same, for a turn that carries an explicit pronoun.</summary>
        private const int MaxAnaphoricFollowUp = 7;

        /// <summary>Shortest unit name that counts as a follow-up cue; below this it is a stray letter.</summary>
        private const int MinUnitCue = 3;

        /// <summary>
        /// Abundance columns that measure the same thing from a different source.
        ///
        /// <para>Only the crustal pair. The other seven reservoirs - sea water, the Sun, meteorites,
        /// the human body - are genuinely different places, and substituting between them would
        /// answer a question about one with a figure from another.</para>
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> CrustSiblings =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["abundance_earth_crust"] = "abundance_crustal_rocks",
                ["abundance_crustal_rocks"] = "abundance_earth_crust",
            };

        /// <summary>Words that explicitly ask for a family of properties rather than one of them.</summary>
        private static readonly string[] FamilyWords =
        [
            "properties", "egenskaper", "eigenschaften", "propriedades", "propiedades",
            "proprietes", "proprieta", "eienskappe", "katangian", "गुण", "خواص", "性质",
        ];

        /// <summary>
        /// Reservoirs an abundance question can name, which narrow it to a single field.
        ///
        /// <para>Pre-folded for diacritics, matching how the query is normalised before it gets here.</para>
        /// </summary>
        private static readonly string[] ReservoirWords =
        [
            "crust", "soil", "urban", "sea water", "seawater", "ocean", "rock", "sun", "solar",
            "meteorite", "human body", "in the body",
            "jordskorpan", "jord", "havsvatten", "berg", "solen", "solsystem", "meteoriter",
            "manniskokroppen", "kroppen",
            "erdkruste", "boden", "meerwasser", "gestein", "sonne", "sonnensystem", "meteoriten",
            "korper", "corteza", "agua de mar", "cuerpo", "croute", "eau de mer", "corps",
            "地壳", "海水", "人体",
        ];

        /// <summary>
        /// Aspects users ask to compare that the element data does not hold.
        ///
        /// <para>Reactivity is derived rather than stored, and applications and uses live in prose
        /// descriptions rather than in a comparable field.</para>
        /// </summary>
        private static readonly string[] UncomparableAspects =
            ["reactivity", "reactive", "application", "applications", "uses", "used for"];

        /// <summary>Fields shown when elements are compared without naming a property.</summary>
        private static readonly string[] DefaultComparisonFields =
            ["atomic_number", "atomic_mass", "density", "melting_point", "electronegativity"];

        /// <summary>
        /// Superlative words that imply their own field.
        ///
        /// <para>A superlative that names no field takes it from the adjective. The non-English
        /// forms matter as much as the English ones: "quale elemento è il più denso" resolved no
        /// field at all and was declined outright.</para>
        /// </summary>
        private static readonly IReadOnlyList<KeyValuePair<string, string>> ImplicitSuperlatives =
        [
            new("tat", "density"), new("tatt", "density"),
            new("piu denso", "density"), new("mais denso", "density"),
            new("densidad", "density"), new("pinakadensidad", "density"),
            new("ghana", "density"), new("kasif", "density"),
            new("elektronegativt", "electronegativity"), new("elektronegativ", "electronegativity"),
            new("densest", "density"),
            new("heaviest", "atomic_mass"),
            new("lightest", "atomic_mass"),
            new("hardest", "mohs_hardness"),
            new("softest", "mohs_hardness"),
            new("hottest", "melting_point"),
            new("tatast", "density"),
            new("tyngst", "atomic_mass"),
            new("lattast", "atomic_mass"),
            new("dichteste", "density"),
            new("schwerste", "atomic_mass"),
            new("mas denso", "density"),
            new("mas pesado", "atomic_mass"),
            new("plus dense", "density"),
            new("plus lourd", "atomic_mass"),
            new("最密", "density"),
            new("最重", "atomic_mass"),
        ];
    }
}
