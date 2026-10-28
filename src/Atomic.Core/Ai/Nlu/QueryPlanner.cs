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
    /// The planning cascade. The sub-planners it calls, and the constants, are in
    /// QueryPlanner.Support.cs.
    ///
    /// <para><b>The order of the guards below is the specification.</b> Each one exists because a
    /// query it claims was previously claimed by a later branch and answered as a different
    /// question; the comment on each records which. Reordering them does not fail loudly - it makes
    /// an intent unreachable and produces a confident answer to a question nobody asked.
    /// <c>QueryPlannerCascadeTest</c> pins the order.</para>
    /// </summary>
    public sealed partial class QueryPlanner
    {
        /// <summary>
        /// The order the branches are preferred in, as a fact a test can assert: <see cref="Branch"/>'s,
        /// which <see cref="Arbiter.Pick"/> chooses by (roadmap 1.8). It was a hand-kept list beside
        /// the code it described; it is now read off the enum the planner actually uses.
        /// </summary>
        internal static readonly IReadOnlyList<string> CascadeOrder =
            Enum.GetValues<Branch>()
                .Select(b => System.Text.RegularExpressions.Regex.Replace(b.ToString(), "(?<!^)([A-Z])", "-$1").ToLowerInvariant())
                .ToList();

        /// <summary>
        /// The plan for <paramref name="query"/>, corrected for misspelling when it would otherwise
        /// be declined (roadmap 1.6).
        ///
        /// <para>Correction runs only on a <see cref="DeclineReason.NoMatch"/>: a question something
        /// already matched is left as written, so a typo can never move an answer that was right.
        /// The corrected question must then be answered from the element data; one only a dataset
        /// row would answer is a guess on a guess - "tell me about mars" is one edit from "mass" -
        /// and the original decline stands.</para>
        /// </summary>
        public QueryPlan Plan(string query, DialogueState state)
        {
            var plan = PlanWords(query, state);
            if (plan.Declined != DeclineReason.NoMatch) return plan;
            var corrected = _speller.Value.Correct(query);
            if (corrected == query) return plan;
            var second = PlanWords(corrected, state);
            var answered = second.Intent != Intent.Unknown && second.Intent != Intent.DatasetLookup &&
                           second.Confidence >= Threshold;
            return answered ? second with { Evidence = [.. second.Evidence, "spelling: " + corrected] } : plan;
        }

        private QueryPlan PlanWords(string query, DialogueState state) =>
            Arbiter.Pick(Proposals(query, state))!.Plan;

        /// <summary>
        /// Every plan the branches propose for <paramref name="query"/>, in the order they proposed
        /// (roadmap 1.8). The last is always <see cref="Branch.NoMatch"/>, so there is always one
        /// to pick.
        ///
        /// <para>The branches still run in the order the cascade had, because the steps between them
        /// refine what the later ones see - the subject a follow-up inherits, a threshold read off
        /// an element. What changed is that a branch proposes instead of ending the method.</para>
        /// </summary>
        internal IReadOnlyList<Proposal> Proposals(string query, DialogueState state)
        {
            var proposals = new List<Proposal>();
            // Each proposal keeps the evidence it had when it was made: the list below is shared,
            // and the branches further down keep adding to it.
            void Propose(Branch branch, QueryPlan plan) =>
                proposals.Add(new Proposal(branch, plan with { Evidence = plan.Evidence.ToList() }));

            // Everything below reads words, and "smältpunktsskillnaden" or "pinakamatigas" is one
            // word carrying two (roadmap 1.5).
            var rawQuery = _morphology.Expand(query);
            var normalized = TextMatching.NormalizeForLookup(rawQuery);

            EarlyDecline(rawQuery, normalized, state, Propose);

            // ---- Calculations ---------------------------------------------------------------
            // Checked first among the real intents: these questions name a formula or a nuclide,
            // which the element and field resolvers would otherwise pick apart into unrelated
            // matches. "the molar mass of H2SO4" used to retrieve the dictionary definition of
            // molar mass instead of a number.
            if (CalculationPlan(rawQuery, normalized) is { } calculation) Propose(Branch.Calculation, calculation);

            var operators = OperatorExtractor.Extract(rawQuery);
            var elementMatches = _entities.ResolveAll(rawQuery, limit: 4);
            var fieldMatches = _fields.ResolveAll(rawQuery, limit: 3);
            var evidence = new List<string>(6);

            // ---- A set read off an element: "what else is in that group" (roadmap 1.7) ------
            // The element is the reference, not the subject: the one named ("the same block as
            // iron") or the one the conversation is about. Its set joins the subsets, so every
            // branch below - list, count, superlative, statistic - ranges over it, and the words
            // that named the set stop naming a property. "Else" and "other" leave the reference out
            // of a list or a count, never out of a ranking: "más" and "mais" make superlatives too,
            // and the densest in iron's group may be iron.
            if (SetDimension(normalized) is { } dimension &&
                (elementMatches.FirstOrDefault()?.Key ?? state.FocusElement) is { } referenceKey &&
                _store.Element(referenceKey) is { } reference &&
                SetFilter(dimension, reference) is { } setFilter)
            {
                var asksForMembers = operators.SuperlativeDescending is null && operators.TopN is null &&
                                     operators.Aggregation == Aggregation.None && operators.Comparators.Count == 0 &&
                                     operators.Range is null;
                var excludes = (asksForMembers || operators.Aggregation == Aggregation.Count) &&
                               MentionsAny(normalized, Lexicon.OTHER_WORDS);
                var filters = new List<Filter> { setFilter };
                if (excludes) filters.Add(new Filter.Excludes(new HashSet<string>(StringComparer.Ordinal) { reference.Key }));
                evidence.Add($"{dimension} of {reference.Key}" + (excludes ? ", without it" : ""));
                operators = operators with { SubsetFilters = operators.SubsetFilters.Concat(filters).ToList() };
                elementMatches = elementMatches.Where(m => m.Key != reference.Key).ToList();
                fieldMatches = fieldMatches.Where(m => m.Spec.Id != DimensionFields[dimension]).ToList();
                if (asksForMembers)
                {
                    Propose(Branch.SetOfAnElement, new QueryPlan(
                        Intent.FilterList,
                        FieldIds: fieldMatches.Select(m => m.Spec.Id).ToList(),
                        Filters: operators.SubsetFilters,
                        Limit: DefaultListLimit,
                        Confidence: 0.85,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- Standalone unit conversion --------------------------------------------------
            // Placed here rather than in CalculationPlan, because the guard that separates this
            // from a property lookup needs the resolved field and element, and neither is available
            // where the calculations run.
            if (UnitConvertPlan(rawQuery, operators) is { } conversion) Propose(Branch.UnitConvert, conversion);

            // ---- A compound named in words ---------------------------------------------------
            // Before inheritance, so "and the boiling point of water?" cannot inherit the thread's
            // element. Only when no element is named outside the compound: "water's oxygen" is
            // oxygen, and "how much gold is in sea water" is about gold. And not when the
            // compound's words are part of a field's own name: "and in sea water" asks for the
            // abundance in sea water of the element the thread is about.
            if (elementMatches.Count == 0)
            {
                var compound = _entities.ScanCompounds(rawQuery).Compounds.FirstOrDefault(c =>
                    !fieldMatches.Any(f => TextMatching.ContainsWord(
                        TextMatching.NormalizeForLookup(f.Matched), c.Surface)));
                if (compound is not null &&
                    CompoundPlan(rawQuery, normalized, compound, fieldMatches.Count > 0) is { } compoundPlan)
                    Propose(Branch.Compound, compoundPlan);
            }

            // ---- Narrative questions ---------------------------------------------------------
            // Usage, biological role and etymology are answered from prose, not from a field. They
            // are declined here rather than left to fall through, because falling through is what
            // caused the worst defect in the agent: the query names an element, resolves no field,
            // and the inheritance below hands it the previous turn's field. "Vad används titan
            // till" was answered with titanium's atomic number that way.
            //
            // A resolved field wins over this: "what is titanium used for in aerospace, and its
            // density" still has a real question in it.
            if (fieldMatches.Count == 0 &&
                Lexicon.NARRATIVE_INTENTS.Any(w => normalized.Contains(w, StringComparison.Ordinal)))
                Propose(Branch.NarrativeDecline, Decline(rawQuery, DeclineReason.Narrative));

            // Etymology declines even with a field in hand, because the field is the *subject* of
            // the question rather than the thing being asked for: "where does that symbol come
            // from" resolves `symbol`, and answering it with a value - tin's nine abundance
            // reservoirs, as reported - mistakes what the sentence is about.
            if (Lexicon.ETYMOLOGY_INTENTS.Any(w => normalized.Contains(w, StringComparison.Ordinal)))
                Propose(Branch.EtymologyDecline, Decline(rawQuery, DeclineReason.Etymology));

            // ---- Explanations ----------------------------------------------------------------
            // "Why does atomic radius increase down a group" and "what is a halogen" want prose,
            // not a value or a list of matching elements. Both would otherwise be captured: the
            // first by the plain field definition, the second by a subset list.
            //
            // An operator disqualifies the definitional reading. "What is the average
            // electronegativity of the halogens" opens with a definition frame and names a topic
            // the dictionary has, so it was answered with the definition of electronegativity - the
            // word "average" and the subset were never reached, because this check runs before
            // aggregation. A subset on its own is deliberately not disqualifying: "what is a
            // halogen" names a family and wants the definition of it, not a list of its members.
            var carriesOperator = operators.Aggregation != Aggregation.None ||
                                  operators.SuperlativeDescending is not null ||
                                  operators.Comparators.Count > 0 ||
                                  operators.TopN is not null ||
                                  operators.Range is not null;

            // A follow-up with a subject in play is asking about that subject. "What are its
            // isotopes" opens with a definition frame and names a topic the dictionary holds, so it
            // was answered with the definition of "isotope" instead of the element's isotopes.
            var continuesAThread = elementMatches.Count == 0 &&
                                   state.FocusElement is not null &&
                                   ReadsAsFollowUp(normalized);

            // A mechanism question is checked even mid-thread. Every reported case was a follow-up
            // - "why does that trend hold", "how does that change above Curie temperature" - so
            // excluding follow-ups here exempted exactly the turns that were failing.
            if (AsksHowItWorks(normalized) || (!carriesOperator && !continuesAThread))
            {
                if (ExplanationPlan(rawQuery, normalized, elementMatches.Count > 0) is { } explanation)
                    Propose(Branch.Explanation, explanation);
            }

            // ---- Slot-emptiness inheritance --------------------------------------------------
            // A follow-up supplies one slot and inherits the other. This is what makes "and its
            // density?" work in every language without pronoun lists.
            var elementKeys = elementMatches.Select(m => m.Key).ToList();
            var fieldIds = fieldMatches.Select(m => m.Spec.Id).ToList();

            // A question that ranges over the whole table has no subject to inherit. Without this
            // guard, "which element has the highest melting point" asked three turns into a thread
            // about tungsten inherits tungsten and collapses into a plain property lookup - the
            // opposite failure to a stale field, and just as wrong.
            //
            // Deliberately *not* keyed on IsListQuestion: Lexicon.WHICH contains "what", so that
            // flag is true for "what is its density" and using it here silently disabled the whole
            // follow-up mechanism. Only the operators that genuinely scan the table count.
            var rangesOverTable =
                operators.SuperlativeDescending is not null ||
                operators.TopN is not null ||
                // "How many are there" ranges; "how many neutrons does it have" does not, and the
                // difference is whether a field was named.
                (operators.Aggregation != Aggregation.None && fieldMatches.Count == 0) ||
                // "Which elements are radioactive" ranges; "is it radioactive" carries the same
                // subset word but is a question about the subject already in play.
                (operators.SubsetFilters.Count > 0 && operators.IsListQuestion) ||
                // "Noble gases", "the halogens", "ädelgaserna" - a family name and nothing else is
                // a request for that family, not a continuation of whatever came before.
                NamesOnlyAFamily(normalized, operators, elementMatches.Count > 0);

            // Set when a continuation supplied no slot of its own, so the field is not inherited
            // on top of the element. See below.
            var contributedNothing = false;

            if (elementKeys.Count == 0 && !rangesOverTable)
            {
                // Inherit the subject whenever the turn reads as a continuation, not only when a
                // field resolved. "and in fahrenheit" resolves no field, "what are its isotopes"
                // resolves no field the isotope branch uses, and both were falling through to a
                // dataset match.
                //
                // A plural anaphor inherits the whole set, not the focus. "And their melting
                // points" after comparing gold and silver is a two-element question; taking only
                // the focus answered half of it and gave no sign that half was dropped.
                var plural = Lexicon.PLURAL_ANAPHORS.Any(a => TextMatching.ContainsWord(normalized, a));
                var previousSet = PreviousPair(state);

                // Spanish "sus" and Portuguese "seus" agree with the thing possessed, not the
                // possessor, so "cuáles son sus isótopos" is *its* isotopes - one element with
                // several isotopes - and inheriting the pair answered it as a comparison.
                if (plural && previousSet is not null && !AsksForIsotopes(normalized))
                {
                    elementKeys = previousSet.ToList();
                    evidence.Add($"inherited {previousSet.Count} elements");
                }
                else if ((fieldIds.Count > 0 || ContinuesTheThread(normalized)) &&
                         state.FocusElement is { } focus &&
                         !AsksForADefinition(normalized))
                {
                    elementKeys = [focus];
                    evidence.Add($"inherited element {focus}");

                    // Nothing in this query was its own: no element, no field, nothing to range
                    // over. Remember that, because inheriting the field as well would rebuild the
                    // previous question exactly and answer it again.
                    // A unit counts: "and in fahrenheit" names neither element nor field, but it
                    // is a real question about the same value on a different scale.
                    contributedNothing = fieldIds.Count == 0 &&
                                         !operators.HasOperatorEvidence &&
                                         operators.Ordinal is null &&
                                         operators.RankOrdinal is null &&
                                         !operators.IsListQuestion;
                }
            }

            // Inheriting a *field* is riskier than inheriting an element, because a full question
            // that resolved no field is usually a new question rather than a continuation - and
            // answering it with the previous field is confidently wrong rather than merely
            // unhelpful. So it is limited to queries that actually read as follow-ups.
            //
            // A follow-up that ranges over the set rather than over one element inherits the field
            // too: "is that the highest" after listing metals by density has no element and no
            // field of its own, and every part of it comes from the turn before.
            // A query that names an element *and* states a predicate about it is not elliptical:
            // it omits nothing. "Is gold a metal" has a subject and a complement, and inheriting
            // the previous field answered it about Discovered By - the field left over from a
            // question about radium. A follow-up that carries a filter but no element ("what
            // about the noble gases") is still elliptical and still inherits.
            var statesItsOwnPredicate = elementKeys.Count > 0 && operators.SubsetFilters.Count > 0;

            var elliptical = ContinuesTheThread(normalized) &&
                             !statesItsOwnPredicate &&
                             (elementKeys.Count > 0 ||
                              operators.SuperlativeDescending is not null ||
                              operators.Aggregation != Aggregation.None);

            // A continuation that supplied neither slot is not a follow-up question - it is the
            // previous question over again. Inheriting both gave "more" after "the melting point
            // of iron" iron's melting point a second time, word for word. One slot has to be the
            // user's own for there to be a new question at all.
            if (fieldIds.Count == 0 && state.LastFieldIds.Count > 0 && elliptical && !contributedNothing)
            {
                fieldIds = state.LastFieldIds.ToList();
                evidence.Add($"inherited field {fieldIds[0]}");
            }

            // A bare "how many are there" after "list the noble gases" is asking about that set.
            // The filters are inherited rather than the question being answered over all 118
            // elements, which is the only reading that makes the follow-up mean anything.
            var subsetFilters = operators.SubsetFilters;
            if (subsetFilters.Count == 0)
            {
                // Only a follow-up that itself ranges over a set - "how many are there", "which is
                // the densest" - inherits the previous turn's filters. A follow-up asking for one
                // field of one element must not, or "what is its atomic number" after listing the
                // halogens turns back into a list of halogens.
                var previous = state.LastPlan?.Filters ?? [];
                var wantsASet = operators.Aggregation != Aggregation.None ||
                                operators.SuperlativeDescending is not null;

                if (previous.Count > 0 && elementKeys.Count == 0 && wantsASet && ReadsAsFollowUp(normalized))
                {
                    evidence.Add($"inherited {previous.Count} filter(s)");
                    subsetFilters = previous;
                }
                else
                {
                    subsetFilters = [];
                }
            }

            var comparatorFilters = operators.Comparators
                // A comparator needs a field to compare against.
                .Where(_ => fieldIds.Count > 0)
                .Select(c => new Filter.FieldCompare(fieldIds[0], c.Op, c.Value))
                .ToList();

            // ---- Comparative against the focus element ---------------------------------------
            // "is gold denser" after a turn about lead, "which is denser" after comparing two. The
            // adjective is the signal - it is what makes the turn a comparison rather than a
            // lookup, and without this the question fell through to a filtered list of everything
            // denser.
            //
            // Hindi and Urdu have no comparative adjective to key on: the comparison is carried by
            // the particle alone, and the adjective stays in its plain form - "क्या यह चांदी से भारी है"
            // is "is it heavier than silver" with भारी meaning simply "heavy". The particle plus a
            // named field plus a pronoun standing for the other subject is the whole of the
            // evidence there is, and without accepting it the turn was answered as a lookup.
            var comparativeCue =
                Lexicon.COMPARATIVE_ADJECTIVES.Keys.Any(a => normalized.Contains(a, StringComparison.Ordinal)) ||
                (fieldIds.Count > 0 &&
                 MentionsAny(normalized, Lexicon.THAN_WORDS) &&
                 MentionsAny(normalized, Lexicon.ANAPHORS));

            if (elementMatches.Count == 1 && state.FocusElement is { } focusKey &&
                !string.Equals(elementMatches[0].Key, focusKey, StringComparison.Ordinal) && comparativeCue)
            {
                var pair = new[] { focusKey, elementMatches[0].Key };
                if (ComparativePlan(rawQuery, normalized, pair, fieldIds) is { } against)
                {
                    evidence.Add("comparative against focus element");
                    Propose(Branch.ComparativeAgainstFocus, against with { Evidence = [.. against.Evidence, .. evidence] });
                }

                // ComparativePlan reads an explicit "X is denser than Y" and needs a comparator, an
                // adjective or a degree word to do it - none of which a Hindi or Urdu comparative
                // has. When the cue said this is a comparison and the sentence cannot be parsed as
                // one, the pair and the field are still enough to answer it.
                if (fieldIds.Count > 0)
                {
                    evidence.Add("comparative against focus element");
                    Propose(Branch.ComparativeAgainstFocus, new QueryPlan(
                        Intent.Comparative,
                        Entities: pair.Select(k => (EntityRef)new EntityRef.Element(k)).ToList(),
                        FieldIds: [fieldIds[0]],
                        TargetUnit: operators.TargetUnit ?? state.LastTargetUnit,
                        Confidence: 0.8,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- Comparative over the previous answer ----------------------------------------
            // Neither element named - "which is denser", "which melts first", "which conducts heat
            // better". Both subjects come from the previous turn, which is the only place they
            // exist.
            //
            // elementMatches, not elementKeys: by this point inheritance has already put the focus
            // element into elementKeys, which masked every elliptical comparative the thread
            // produced.
            var recentPair = PreviousPair(state);
            if (elementMatches.Count == 0 && recentPair is not null &&
                operators.Comparators.Count == 0 && operators.SubsetFilters.Count == 0)
            {
                var adjective = LongestComparativeAdjective(normalized);
                var comparativeField = fieldIds.FirstOrDefault()
                                       ?? adjective?.Value.Field
                                       ?? state.LastFieldIds.FirstOrDefault();

                // A bare request for the margin - "by how much", "what is the difference" - names
                // neither subject nor field and is only meaningful as a continuation.
                var wantsTheMargin = Lexicon.MARGIN_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal));

                // Naming the word "element" is what says the question ranges over the table rather
                // than over the pair - "which **element** has the highest melting point" is a
                // superlative however deep into a thread it is asked, while the elliptical "which
                // is densest" is not. Without that half, three explicit superlatives collapsed into
                // comparisons.
                var ellipticalSuperlative = operators.SuperlativeDescending is not null &&
                                            !MentionsAny(normalized, Lexicon.ELEMENT_WORDS);

                // Exactly a pair, and the turn before must not have been a list. After "which
                // elements are radioactive" the follow-up "which is the heaviest" ranges over that
                // list and is a superlative; only a thread with two elements in play makes it a
                // comparison.
                //
                // A morphological superlative counts too. Swedish and German mark it on the word -
                // "tätast", "dichteste" - so CarriesADegreeWord cannot see it, and "vilket är
                // tätast" after asking about gold and then silver ranked all 118 instead of
                // choosing between the two under discussion.
                var comparesThePair = recentPair.Count == 2 &&
                                      state.LastResultKeys.Count <= 2 &&
                                      operators.SubsetFilters.Count == 0 &&
                                      (CarriesADegreeWord(normalized) || ellipticalSuperlative);

                // An isotope question is not a comparison, however elliptical: "what are its
                // isotopes" was being answered as one.
                var asksSomethingElse = Lexicon.ISOTOPE_WORDS.Any(w => TextMatching.ContainsWord(normalized, w));

                var readsAsComparison =
                    !asksSomethingElse &&
                    (operators.SuperlativeDescending is null || comparesThePair) &&
                    (adjective is not null || wantsTheMargin ||
                     (comparesThePair && operators.SuperlativeDescending is not null) ||
                     (fieldIds.Count > 0 && !NamesOneSubject(normalized) &&
                      Lexicon.SELECTIVE.Any(w => TextMatching.ContainsWord(normalized, w))));

                if (comparativeField is not null && readsAsComparison)
                {
                    evidence.Add("comparative over the previous answer");
                    Propose(Branch.ComparativeOverPrevious, new QueryPlan(
                        Intent.Comparative,
                        Entities: recentPair.Take(2).Select(k => (EntityRef)new EntityRef.Element(k)).ToList(),
                        FieldIds: [comparativeField],
                        // The shared target unit is derived further down; take it from the operators
                        // and the thread directly, so an elliptical turn keeps the unit asked for.
                        TargetUnit: operators.TargetUnit ?? state.LastTargetUnit,
                        Confidence: 0.8,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- Periphrastic comparative ----------------------------------------------------
            // Two elements and a degree word is a comparison *between those two*. Decided ahead of
            // the superlative branch, because the languages that need it mark both with the same
            // word: "l'oro è più denso del piombo" would otherwise rank the whole table.
            //
            // Chinese writes "compare" as 比较, which *contains* its comparison particle 比 - so
            // "比较这两个" ("compare the two") looked like "this is more … than that" and was
            // answered with a winner instead of a side-by-side table. The verb is masked before the
            // particle is looked for, the same way the degree phrases are in OperatorExtractor.
            var withoutCompareVerbs = Lexicon.COMPARE.Aggregate(normalized, (acc, verb) =>
                verb.Length > 1
                    ? acc.Replace(verb, new string(' ', verb.Length), StringComparison.Ordinal)
                    : acc);

            var marksAComparison = CarriesADegreeWord(normalized) ||
                                   MentionsAny(withoutCompareVerbs, Lexicon.THAN_WORDS);

            if (elementKeys.Count == 2 && fieldIds.Count > 0 && marksAComparison &&
                operators.Comparators.Count == 0 && operators.SubsetFilters.Count == 0)
            {
                // ComparativePlan first: it reads the explicit "is X denser than Y" shape and is
                // what produces the ratio for "how much denser is gold than aluminium". Only when
                // it cannot parse the sentence is the plan built here.
                if (ComparativePlan(rawQuery, normalized, elementKeys, fieldIds) is { } explicitPlan)
                {
                    evidence.Add($"comparative {string.Join(", ", fieldIds)}");
                    Propose(Branch.PeriphrasticComparative, explicitPlan with { Evidence = [.. explicitPlan.Evidence, .. evidence] });
                }

                evidence.Add("periphrastic comparative");
                Propose(Branch.PeriphrasticComparative, new QueryPlan(
                    Intent.Comparative,
                    Entities: elementKeys.Select(k => (EntityRef)new EntityRef.Element(k)).ToList(),
                    FieldIds: [fieldIds[0]],
                    TargetUnit: operators.TargetUnit,
                    Confidence: 0.8,
                    RawQuery: rawQuery,
                    Evidence: evidence));
            }

            // A threshold can name an element instead of a number: "denser than iron" means denser
            // than iron's density. Only when a single element is named - with two, the question is
            // a direct comparison between them and the elements are the subject rather than a bound.
            if (comparatorFilters.Count == 0 && elementKeys.Count == 1 &&
                ElementThreshold(normalized, elementKeys, fieldIds) is { } threshold)
            {
                comparatorFilters.Add(threshold);
                // The element supplied the threshold; it is not also a subject of the query.
                elementKeys = [];
                // The adjective also names the property, so the results can be sorted and shown by
                // it: "lighter than aluminium" is about density even though no field was written.
                if (fieldIds.Count == 0) fieldIds = [threshold.FieldId];
                evidence.Add("threshold from element");
            }

            // A range: "which elements melt between 1000 and 2000 kelvin".
            if (operators.Range is { } range && fieldIds.Count > 0)
            {
                comparatorFilters.Add(
                    new Filter.FieldCompare(fieldIds[0], Op.Between, range.Low, range.High));
                evidence.Add($"range on {fieldIds[0]}");
            }

            if (comparatorFilters.Count > 0)
                evidence.Add($"comparator on {fieldIds.FirstOrDefault()}");
            if (subsetFilters.Count > 0)
                evidence.Add($"subset {subsetFilters.Count}");

            var allFilters = subsetFilters.Concat(comparatorFilters).ToList();
            var targetUnit = operators.TargetUnit ?? (fieldIds.Count > 0 ? state.LastTargetUnit : null);

            // ---- Direct comparative: "is gold denser than lead" ------------------------------
            // A question with a one-word answer should get one, not a property table. Checked
            // before the general comparison so the answer leads with yes/no or the winner.
            if (elementKeys.Count >= 2 &&
                ComparativePlan(rawQuery, normalized, elementKeys, fieldIds) is { } direct)
                Propose(Branch.DirectComparative, direct with { Evidence = [.. direct.Evidence, .. evidence] });

            // ---- Aggregation: "average electronegativity of the halogens" --------------------
            // A filter is not required: "the average density of all elements" ranges over the whole
            // table, which is a complete question. It previously fell through to a dataset match.
            var aggregatesOverEverything = operators.Aggregation != Aggregation.None &&
                                           elementKeys.Count == 0 && fieldIds.Count > 0;

            if (operators.Aggregation != Aggregation.None &&
                (allFilters.Count > 0 || elementKeys.Count > 1 || aggregatesOverEverything))
            {
                var fieldId = fieldIds.FirstOrDefault();
                if (operators.Aggregation == Aggregation.Count || fieldId is not null)
                {
                    evidence.Add($"aggregation {operators.Aggregation}");
                    Propose(Branch.Aggregate, new QueryPlan(
                        Intent.Aggregate,
                        Entities: elementKeys.Select(k => (EntityRef)new EntityRef.Element(k)).ToList(),
                        FieldIds: fieldId is null ? [] : [fieldId],
                        Filters: allFilters,
                        Aggregation: operators.Aggregation,
                        SortField: fieldId,
                        TargetUnit: targetUnit,
                        Confidence: 0.85,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- Superlative and filtered list -----------------------------------------------
            var wantsRanking = operators.SuperlativeDescending is not null || operators.TopN is not null;
            if (wantsRanking || comparatorFilters.Count > 0)
            {
                var sortField = fieldIds.FirstOrDefault() ?? InferSortField(rawQuery);
                if (sortField is not null)
                {
                    // "which element has the lowest X" is a superlative, not a list, even though it
                    // opens with "which". Only an explicit count, a threshold, or a list question
                    // with no superlative asks for more than one row.
                    var isList = operators.TopN is not null || comparatorFilters.Count > 0 ||
                                 (operators.IsListQuestion && operators.SuperlativeDescending is null);
                    evidence.Add(isList ? $"filtered list on {sortField}" : $"superlative on {sortField}");

                    // "the third densest element" wants one element, the third down the ranking,
                    // not the top one. Carried as FieldOrdinal so the executor can skip past.
                    var rankOrdinal = operators.RankOrdinal;
                    if (rankOrdinal is not null) evidence.Add($"rank {rankOrdinal}");

                    Propose(Branch.SuperlativeOrFilteredList, new QueryPlan(
                        isList && rankOrdinal is null ? Intent.FilterList : Intent.Superlative,
                        FieldIds: [sortField],
                        FieldOrdinal: rankOrdinal,
                        Filters: allFilters,
                        SortField: sortField,
                        SortDescending: operators.SuperlativeDescending ?? true,
                        Limit: rankOrdinal is not null ? 1 : operators.TopN ?? (isList ? DefaultListLimit : 1),
                        TargetUnit: targetUnit,
                        Confidence: comparatorFilters.Count > 0 ? 0.85 : 0.8,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- Subset list: "which elements are radioactive", "list the noble gases" -------
            // "compare the halogens on electronegativity" names no elements to compare - the subset
            // is the operand. The honest answer is the family ranked by that field, so a compare
            // cue counts as a list request when a subset is present.
            var comparesASubset = Lexicon.COMPARE.Any(w => normalized.Contains(w, StringComparison.Ordinal)) &&
                                  elementKeys.Count == 0;

            if (subsetFilters.Count > 0 &&
                (operators.IsListQuestion || comparesASubset ||
                 NamesOnlyAFamily(normalized, operators, elementKeys.Count > 0)))
            {
                // A named field is only used as the displayed column when it is something
                // measurable - "radioactive" resolves to a field but is already expressed by the
                // filter itself, so requiring no field here would make that query unplannable.
                var displayField = NumericFieldOrNull(fieldIds.FirstOrDefault());
                evidence.Add("subset list");
                Propose(Branch.SubsetList, new QueryPlan(
                    Intent.FilterList,
                    FieldIds: displayField is null ? [] : [displayField],
                    // Every filter, not just the subset: "which metals are lighter than aluminium"
                    // is a subset question that also carries a threshold, and dropping it here
                    // would list all metals.
                    Filters: allFilters,
                    SortField: displayField,
                    Limit: DefaultListLimit,
                    Confidence: 0.75,
                    RawQuery: rawQuery,
                    Evidence: evidence));
            }

            // ---- Field coverage: "which elements have a recorded curie point" ----------------
            // A list question with a field and no constraint at all. The set is everything with a
            // value, which for the sparse fields is the interesting part of the answer: the
            // composer already reports how many of the pool were missing, so this says "17 of 118"
            // rather than implying the other 101 are zero.
            if (allFilters.Count == 0 && elementKeys.Count == 0 && operators.IsListQuestion &&
                Lexicon.HAS_VALUE_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal)))
            {
                var coverageField = NumericFieldOrNull(fieldIds.FirstOrDefault());
                if (coverageField is not null)
                {
                    evidence.Add("field coverage");
                    Propose(Branch.FieldCoverageList, new QueryPlan(
                        Intent.FilterList,
                        FieldIds: [coverageField],
                        SortField: coverageField,
                        SortDescending: true,
                        Limit: DefaultListLimit,
                        Confidence: 0.75,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- "compare with iron" while the conversation is about gold --------------------
            // The second element is the one already in focus.
            if (elementKeys.Count == 1 && state.FocusElement is { } compareFocus &&
                !string.Equals(elementKeys[0], compareFocus, StringComparison.Ordinal) &&
                Lexicon.COMPARE.Any(w => normalized.Contains(w, StringComparison.Ordinal)))
            {
                elementKeys = [compareFocus, elementKeys[0]];
                evidence.Add("comparison against focus element");

                // The direct-comparative check ran further up, when only one element was known, so
                // it could not fire. Re-run it now that the pair is complete: "how does that
                // compare to iron" is a request for a winner, and without this it fell through to
                // the side-by-side table the branch below builds.
                if (ComparativePlan(rawQuery, normalized, elementKeys, fieldIds) is { } rerun)
                    Propose(Branch.CompareWithFocus, rerun with { Evidence = [.. rerun.Evidence, .. evidence] });
            }

            // ---- Neighbour: "what comes after carbon" ----------------------------------------
            if (elementKeys.FirstOrDefault() is { } neighbourKey &&
                _store.Element(neighbourKey) is { } subject &&
                NeighbourDirection(normalized) is { } direction &&
                _store.ByNumber(subject.AtomicNumber + direction) is { } target)
            {
                evidence.Add($"neighbour {(direction > 0 ? "after" : "before")}");
                Propose(Branch.Neighbour, new QueryPlan(
                    Intent.Neighbour,
                    Entities: [new EntityRef.Element(subject.Key), new EntityRef.Element(target.Key)],
                    Limit: direction,
                    Confidence: 0.85,
                    RawQuery: rawQuery,
                    Evidence: evidence));
            }

            // ---- Element versus alloy: "the difference between iron and steel" ---------------
            // An alloy is not an element, so a side-by-side property table is not available and not
            // what is being asked. The alloy's own entry explains how it differs from its base
            // metal, which is the answer.
            //
            // Unless the query also names an element *and* a field, in which case it is an ordinary
            // property lookup that happens to mention an alloy: "Young's modulus of steel's iron"
            // asks for iron's stiffness, and returning the encyclopaedia entry for steel answered a
            // question nobody asked.
            var asksForAFieldOfAnElement = elementKeys.Count > 0 && fieldIds.Count > 0;
            if (!asksForAFieldOfAnElement && AlloyIn(normalized) is { } alloy)
            {
                evidence.Add($"alloy {alloy.Id}");
                Propose(Branch.Alloy, new QueryPlan(
                    Intent.DatasetLookup,
                    Entities: [new EntityRef.DatasetRow(DatasetIndex.Datasets.Alloy, alloy.Id)],
                    Confidence: 0.8,
                    RawQuery: rawQuery,
                    Evidence: evidence));
            }

            // ---- Selective comparison: "which melts first, iron or copper" -------------------
            // Two elements and a question that asks to *choose* between them. A side-by-side table
            // technically contains the answer, but the question was a question.
            if (elementKeys.Count == 2 && fieldIds.Count == 1 &&
                Lexicon.SELECTIVE.Any(w => TextMatching.ContainsWord(normalized, w)) &&
                ComparativePlan(rawQuery, normalized, elementKeys, fieldIds) is { } selective)
            {
                evidence.Add("selective comparison");
                Propose(Branch.SelectiveComparison, selective with { Evidence = [.. selective.Evidence, .. evidence] });
            }

            // ---- Comparison: two or more elements named together ----------------------------
            if (elementKeys.Count >= 2)
            {
                // "in terms of reactivity, density and applications" names several aspects at once.
                // Every aspect that maps to a field is compared; the rest are reported as not
                // comparable from stored data rather than silently dropped.
                var aspects = _fields.ResolveAll(rawQuery, limit: 6).Select(m => m.Spec.Id).ToList();
                var unsupported = UnsupportedAspects(normalized);

                evidence.Add($"comparison of {elementKeys.Count} elements");
                if (aspects.Count > 1) evidence.Add($"aspects {aspects.Count}");

                IReadOnlyList<string> comparisonFields =
                    aspects.Count > 0 ? aspects :
                    fieldIds.Count > 0 ? fieldIds :
                    DefaultComparisonFields;

                Propose(Branch.Comparison, new QueryPlan(
                    Intent.Comparison,
                    Entities: elementKeys.Select(k => (EntityRef)new EntityRef.Element(k)).ToList(),
                    FieldIds: comparisonFields,
                    TargetUnit: targetUnit,
                    Confidence: 0.8,
                    RawQuery: rawQuery,
                    Evidence: [.. evidence, .. unsupported.Select(a => $"no field for {a}")]));
            }

            var element = elementKeys.FirstOrDefault() is { } singleKey ? _store.Element(singleKey) : null;

            // "In the earth's crust" is one question, and the data answers it in two columns:
            // earth_crust covers 47 elements and crustal_rocks covers 72, with different elements
            // in each. Gold and oxygen are in the second and not the first, so asking for their
            // crustal abundance resolved a real field, found it empty and replied "no data
            // recorded" - with the number sitting in the sibling column. The two are the same
            // measurement from different sources, so falling through is not a guess.
            if (element is not null)
            {
                fieldIds = fieldIds.Select(fieldId =>
                    CrustSiblings.TryGetValue(fieldId, out var sibling) &&
                    element.Value(fieldId).IsMissing &&
                    !element.Value(sibling).IsMissing
                        ? sibling
                        : fieldId).ToList();
            }

            // ---- Isotopes, safety and spectra: their own shapes, not single fields -----------
            // Checked before field resolution because "isotopes" and "half life" both resolve to a
            // field label, which would answer with the common-neutron count instead of the list.
            if (element is not null)
            {
                if (MentionsAny(normalized, Lexicon.ISOTOPE_WORDS))
                {
                    evidence.Add("isotopes");
                    Propose(Branch.Isotopes, new QueryPlan(
                        Intent.Isotopes,
                        Entities: [new EntityRef.Element(element.Key)],
                        Limit: operators.TopN ?? DefaultIsotopeLimit,
                        Confidence: 0.85,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }

                if (MentionsAny(normalized, Lexicon.SAFETY_WORDS))
                {
                    evidence.Add("safety");
                    Propose(Branch.Safety, new QueryPlan(
                        Intent.Safety,
                        Entities: [new EntityRef.Element(element.Key)],
                        Confidence: 0.85,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }

                // Also checked before field resolution: "what does the emission spectrum of iron
                // look like" matched `appearance` and answered with the metal's colour, and
                // "emission spectrum of sodium" resolved nothing at all and was declined.
                if (MentionsAny(normalized, Lexicon.SPECTRUM_WORDS))
                {
                    evidence.Add("emission spectrum");
                    Propose(Branch.EmissionSpectrum, new QueryPlan(
                        Intent.EmissionSpectrum,
                        Entities: [new EntityRef.Element(element.Key)],
                        Confidence: 0.85,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- Category lookup: a whole family of properties for one element ---------------
            // A specific field wins over a family word. Several field names contain one - "thermal
            // conductivity" contains "thermal" - and because this branch sits above property
            // lookup, asking for one figure returned the whole family instead.
            //
            // Abundance is the exception, and it has to be, because the nine abundance fields all
            // carry "abundance" in their labels - so "what is the abundance of gold" resolves a
            // field and would lose the whole-family answer it has always given.
            if (element is not null &&
                (fieldMatches.Count == 0 || AbundanceFamilyQuestion(normalized) || AsksForAFamily(normalized)) &&
                CategoryIn(normalized) is { } category)
            {
                var populated = FieldRegistry.ByCategory(category)
                    .Where(spec => !element.Value(spec.Id).IsMissing)
                    .Select(spec => spec.Id)
                    .ToList();

                if (populated.Count >= 2)
                {
                    evidence.Add($"category {category}");
                    Propose(Branch.CategoryLookup, new QueryPlan(
                        Intent.CategoryLookup,
                        Entities: [new EntityRef.Element(element.Key)],
                        FieldIds: populated,
                        TargetUnit: targetUnit,
                        Confidence: 0.8,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- Property lookup --------------------------------------------------------------
            if (element is not null && fieldIds.FirstOrDefault() is { } propertyField)
            {
                // A bare "tell me about gold" is narrative, not a field lookup, and belongs to the
                // personality layer. Only decline when no field was actually named, though - "what
                // is the density of gold" also opens with "what is".
                //
                // "Was ist mit Kupfer" opens with the same two words as "was ist Kupfer" and means
                // the opposite thing - it asks for the field the thread is already about. Without
                // the exemption the overview guard declined every German and Filipino "what about X".
                var overviewOnly = fieldMatches.Count == 0 &&
                                   Lexicon.OVERVIEW_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal)) &&
                                   !AsksWhatAbout(normalized);

                if (!overviewOnly)
                {
                    evidence.Add(
                        element.Value(propertyField).IsMissing &&
                        _store.QuantityIn(element, propertyField, targetUnit) is null
                            ? $"no data for {propertyField}"
                            : $"property {propertyField}");

                    Propose(Branch.PropertyLookup, new QueryPlan(
                        Intent.PropertyLookup,
                        Entities: [new EntityRef.Element(element.Key)],
                        FieldIds: [propertyField],
                        FieldOrdinal: operators.Ordinal,
                        TargetUnit: targetUnit,
                        Confidence: 0.8,
                        RawQuery: rawQuery,
                        Evidence: evidence));
                }
            }

            // ---- Dataset row, when retrieval is confident ------------------------------------
            if (DatasetPlan(rawQuery, normalized, elementKeys, fieldIds, evidence) is { } dataset)
                Propose(Branch.DatasetRow, dataset);

            Propose(Branch.NoMatch, Decline(rawQuery, DeclineReason.NoMatch));
            return proposals;
        }

        /// <summary>
        /// The four guards that run before any real intent is considered.
        ///
        /// <para>Separated because none of them needs the operators, the resolvers or the dialogue
        /// state - they are decisions about whether the sentence is a chemistry question at all.</para>
        /// </summary>
        private void EarlyDecline(string rawQuery, string normalized, DialogueState state, Action<Branch, QueryPlan> propose)
        {
            // ---- Conversation, not chemistry -------------------------------------------------
            // Checked before anything else. A greeting or a quiz request has no grounded answer,
            // but the dataset fallback further down will happily supply one: "quiz me" scored 0.70
            // against the constants table and was answered with the mass of an electron. Retrieval
            // cannot tell that a query is not a question, so this has to be an explicit early exit.
            if (Lexicon.DEFER_TO_PERSONALITY.Any(
                    w => TextMatching.ContainsWord(normalized, w) ||
                         (w.Contains(' ', StringComparison.Ordinal) &&
                          normalized.Contains(w, StringComparison.Ordinal))))
                propose(Branch.DeferToPersonality, Decline(rawQuery, DeclineReason.Conversational));

            // An element number past 118 has a definite answer - there is no such element - and the
            // worst thing to do is guess. Declining lets the personality layer say so; answering
            // from retrieval produced "Electron mass" for "tell me about element 119".
            if (_entities.OutOfRangeElementNumber(rawQuery) is not null)
                propose(Branch.OutOfRangeElement, Decline(rawQuery, DeclineReason.NoSuchElement));

            // ---- Concepts with no backing field ----------------------------------------------
            // "समान" is Hindi for "similar" and also the first word of "समान समूह" - "the same
            // group". The unbacked-concept guard therefore declined every Hindi "which elements are
            // in the same group as carbon", which is a perfectly answerable structural question.
            var namesTheSameGroup = Lexicon.SAME_AS_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal));

            // An equation is written, not described. "Balance this reaction: Fe + O2 -> Fe2O3"
            // carries a reaction word, and the unbacked-concept guard declines every sentence that
            // does - but the arrow is far stronger evidence of what the sentence is than the noun
            // beside it.
            var writesAnEquation = EquationArrow.IsMatch(rawQuery);

            if (!namesTheSameGroup && !writesAnEquation &&
                Lexicon.UNBACKED_CONCEPTS.Any(w => normalized.Contains(w, StringComparison.Ordinal)))
            {
                // A question *about* one of these concepts wants it explained, and the dictionary
                // can do that: "why are alkali metals so reactive". A question asking which element
                // is the most reactive wants an element, and only the bespoke scoring rule can
                // answer it - so a superlative disqualifies the explanation.
                var superlative =
                    Lexicon.MOST.Any(w => normalized.Contains(w, StringComparison.Ordinal)) ||
                    Lexicon.LEAST.Any(w => normalized.Contains(w, StringComparison.Ordinal));

                if (!superlative &&
                    ExplanationPlan(rawQuery, normalized, hasElement: false) is { } explanation)
                    propose(Branch.UnbackedExplanation, explanation);

                // Mentioning one of these in passing does not block a comparison that also asks
                // about real properties: "compare lithium, sodium and potassium on reactivity,
                // density and applications" answers the parts it has fields for and says which
                // parts it could not.
                //
                // A comparable field is required, not just several elements. Without that condition
                // "compare the reactivity of sodium and gold" and "what happens when sodium and
                // chlorine react" would both be answered as property tables, which is not what
                // either is asking.
                var comparableAspect = _fields.ResolveAll(rawQuery, limit: 3).Count > 0;
                var severalElements = _entities.ResolveAll(rawQuery, limit: 3).Count >= 2;
                if (!comparableAspect || !severalElements)
                    propose(Branch.UnbackedConcept, Decline(rawQuery, DeclineReason.UnbackedConcept));
            }

            // ---- "How many elements are there" -----------------------------------------------
            // A plain fact about the table itself. Without this it retrieves whichever dictionary
            // entry happens to score highest, which is not an answer.
            //
            // The aggregation has to actually *be* a count. Chinese asks for every statistic with
            // 多少 ("how much"), so "镧系元素的中位原子量是多少" - the median atomic mass of the
            // lanthanides - matched the count word, the word "element" and no subset, and came back
            // as the size of the periodic table.
            //
            // Not when the elements are a set read off the one in focus: "how many elements are in
            // its period" names no element, and was answered with 118.
            if (Lexicon.COUNT.Any(w => normalized.Contains(w, StringComparison.Ordinal)) &&
                Lexicon.ELEMENT_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal)) &&
                _entities.ResolveAll(rawQuery, limit: 1).Count == 0 &&
                (SetDimension(normalized) is null || state.FocusElement is null))
            {
                var operators = OperatorExtractor.Extract(rawQuery);
                if (operators.SubsetFilters.Count == 0 && operators.Aggregation == Aggregation.Count)
                {
                    propose(Branch.CountOfAllElements, new QueryPlan(
                        Intent.Aggregate,
                        Aggregation: Aggregation.Count,
                        Confidence: 0.85,
                        RawQuery: rawQuery,
                        Evidence: ["count of all elements"]));
                }
            }

            return;
        }

        /// <summary>
        /// A row from one of the app's tables, when retrieval is confident and the element and
        /// field path has nothing to offer.
        ///
        /// <para>A one- or two-word query carries too little signal for a lexical match to mean
        /// anything: "sure" retrieved sulfur, "hey" and "42" retrieved dictionary rows. Anything
        /// that short and unrecognised is conversational, and the personality layer owns it.</para>
        ///
        /// <para>An element in the query does not disqualify it. "Standard electrode potential of
        /// zinc" and "solubility of sodium chloride" both name an element and both are dataset
        /// questions - the element alone cannot answer them, because no <i>field</i> resolved
        /// either. A named element keeps the question unless the query also names a table
        /// topic, so a bare "tell me about gold" still falls through to the overview.</para>
        /// </summary>
        private QueryPlan? DatasetPlan(
            string rawQuery,
            string normalized,
            IReadOnlyList<string> elementKeys,
            IReadOnlyList<string> fieldIds,
            List<string> evidence)
        {
            if (elementKeys.Count > 0 && fieldIds.Count > 0) return null;
            if (!LongEnoughForDataset(normalized)) return null;
            if (_retriever is null) return null;

            // A named element keeps the question unless the query also names a *table topic*.
            // "Tell me about gold" is about gold, and the overview answers it; "standard electrode
            // potential of zinc" names a subject no element field covers and only the electrode
            // table holds, which is evidence of the same kind as the element, pointing the other
            // way. Chatter names no table. Before roadmap 1.1 the element case had a higher score
            // bar instead, but the scores it compared were normalised to at most 0.55 and the bar
            // was 0.6, so it was this rule in effect; it is now written as what it was.
            var namesATableTopic = Lexicon.DATASET_TOPIC_CUES.Any(
                cue => normalized.Contains(cue, StringComparison.Ordinal));
            if (elementKeys.Count > 0 && !namesATableTopic) return null;

            if (BestDatasetRow(normalized) is not { } match) return null;

            var separator = match.Id.IndexOf(':', StringComparison.Ordinal);
            var dataset = match.Id[..separator];
            evidence.Add($"dataset {dataset}");
            evidence.Add(string.Create(CultureInfo.InvariantCulture, $"coverage {match.Coverage:0.00}"));
            return new QueryPlan(
                Intent.DatasetLookup,
                Entities: [new EntityRef.DatasetRow(dataset, match.Id[(separator + 1)..])],
                Confidence: 0.7,
                RawQuery: rawQuery,
                Evidence: evidence);
        }

        /// <summary>
        /// The reference-table row that covers the query's content, or null when none covers
        /// enough of it to answer from (roadmap 1.1).
        ///
        /// <para>Retrieval used to rank with BM25 normalised against the top hit, so the best row
        /// always scored the same and nothing could abstain: "what is the mass of an electron" was
        /// answered from the Pauli exclusion principle, "what is the nernst equation" from the
        /// Arrhenius equation, and "what is the mass of the moon" from atomic mass. Coverage is a
        /// share of the query's own content - see <see cref="Bm25Index.Coverage"/> - so one
        /// threshold means the same thing for every query, and a word the corpus has never seen
        /// counts against a match rather than being ignored.</para>
        ///
        /// <para>Ranked by coverage, then by how much of the row's title the query names (the
        /// mineral "Quartz" over the elastic "α-Quartz (SiO2)"), then by how much of the query is in
        /// the title ("Proton mass" over the glossary's "Proton"); a data table before the glossary
        /// when all three tie, because the tables hold values and the glossary only defines; BM25
        /// last.</para>
        ///
        /// <para>A query with one content word must name the row's whole <i>title</i>, in one of its
        /// wordings. Every real one-word question does - "what is brass", "tell me about pyrite" -
        /// while a word that is only in a body is a coincidence ("tell me more" covered the Magnalium
        /// alloy, whose description says "more than cost"), and so is a word that is only part of a
        /// title: "what is the meaning of life" was answered with the half-life equation, "what is
        /// light" with the speed of light. A formula in brackets is notation, so "tell me about
        /// periclase" still finds "Periclase (MgO)".</para>
        /// </summary>
        private CoverageHit? BestDatasetRow(string normalized)
        {
            if (_retriever is null) return null;
            var stopwords = Lexicon.STOPWORDS["en"].Concat(
                Lexicon.STOPWORDS.TryGetValue(_entities.ActiveLanguage, out var own) ? own : []);
            return _retriever.DatasetCoverage(DatasetQuery(normalized), stopwords)
                .Where(m => m.Coverage >= MinDatasetCoverage && (m.Words > 1 || m.NamesTitle))
                .OrderByDescending(m => m.Coverage)
                .ThenByDescending(m => m.TitlePrecision)
                .ThenByDescending(m => m.TitleRecall)
                .ThenBy(m => m.Id.StartsWith(DatasetIndex.Datasets.Dictionary + ":", StringComparison.Ordinal))
                .ThenByDescending(m => m.Bm25)
                .FirstOrDefault();
        }

        /// <summary>
        /// The query with every phrase <see cref="Lexicon.DATASET_GLOSS"/> knows <i>replaced</i> by
        /// its English name.
        ///
        /// <para>The tables are English, so "vad är stål gjort av" can only reach the row titled
        /// "Steel" through the gloss. <see cref="WithDatasetGloss"/> appends it, which suited BM25;
        /// coverage would count the Swedish words as unknown content the row does not cover, so here
        /// the gloss stands in for them. Only the search string is glossed; the answer is still
        /// composed in the user's language.</para>
        /// </summary>
        private static string DatasetQuery(string normalized)
        {
            var text = normalized;
            foreach (var (phrase, english) in Lexicon.DATASET_GLOSS)
            {
                if (!TextMatching.ContainsWord(text, phrase)) continue;
                text = phrase.Any(c => c > '\u2000')
                    ? text.Replace(phrase, $" {english} ", StringComparison.Ordinal)
                    : System.Text.RegularExpressions.Regex.Replace(
                        text, $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(phrase)}(?![\p{{L}}\p{{N}}])", english);
            }
            return text;
        }

        /// <summary>
        /// Route "why …" and "what is a …" to the concept that explains it.
        ///
        /// <para>A definitional frame is ignored when an element is named, so "what is the atomic
        /// mass of gold" gives gold's value rather than the definition of atomic mass. A "why"
        /// question still takes the explanation even with an element present, because "why is
        /// chromium's electron configuration unusual" is asking about the rule.</para>
        /// </summary>
        private QueryPlan? ExplanationPlan(string rawQuery, string normalized, bool hasElement)
        {
            var isWhy = Lexicon.WHY_WORDS.Any(
                w => normalized.StartsWith(w + " ", StringComparison.Ordinal) ||
                     normalized.Contains(" " + w + " ", StringComparison.Ordinal));

            // A definitional frame over a family named in the *plural* is a request for its members,
            // not for its definition. Deliberately narrowed to isDefinition: "why are noble gases
            // unreactive" is still a question the dictionary answers, and isWhy is untouched.
            var isDefinition = !hasElement &&
                Lexicon.DEFINITION_FRAMES.Any(f => normalized.StartsWith(f, StringComparison.Ordinal)) &&
                !Lexicon.PLURAL_SERIES_WORDS.Any(w => normalized.Contains(w, StringComparison.Ordinal));

            var isMechanism = AsksHowItWorks(normalized);
            if (!isWhy && !isDefinition && !isMechanism) return null;

            // Longest topic first, so "electron configuration unusual" beats "electron".
            var topic = ExplanationTopicsByLength
                .Cast<KeyValuePair<string, string>?>()
                .FirstOrDefault(e => normalized.Contains(e!.Value.Key, StringComparison.Ordinal));

            // A "why" question is answered from the dictionary when the topic is genuinely what it
            // is about - "why are alkali metals so reactive" wants the reactivity entry. A
            // *mechanism* frame is not: it asks how one thing follows from another, and any topic
            // it happens to mention is incidental. Returning a definition there looks like an
            // answer and is not one.
            //
            // A definition wants the definition. The topics map to the periodic-trend entries, which
            // answer "why does electronegativity increase", but "what is electronegativity" was
            // answered with the trend too in every language whose word reaches this path (roadmap
            // 1.3); English only escaped because its word resolves a field first.
            var row = isMechanism || topic is null
                ? null
                : (isDefinition && !isWhy ? PlainDefinition(topic.Value.Value) : null)
                  ?? _datasets.Row(DatasetIndex.Datasets.Dictionary, topic.Value.Value);

            // No explanation to give - so decline, rather than falling through to the property path.
            //
            // This is the single most damaging behaviour reported. A mechanism question mentions a
            // real field in passing, the planner resolves it, and the user gets a confident, cited
            // number answering a question they did not ask: "why does radius decrease left to
            // right, despite adding electrons" came back "Potassium's Electrons is 19". Saying
            // nothing is a far better answer than saying that.
            if (row is null) return isWhy || isMechanism ? Decline(rawQuery, DeclineReason.UnexplainedWhy) : null;

            return new QueryPlan(
                Intent.DatasetLookup,
                Entities: [new EntityRef.DatasetRow(DatasetIndex.Datasets.Dictionary, row.Id)],
                Confidence: 0.85,
                RawQuery: rawQuery,
                Evidence: [isWhy ? $"explains {topic!.Value.Value}" : $"defines {topic!.Value.Value}"]);
        }

        /// <summary>"Electronegativity" for "Periodic trend: electronegativity", when the glossary has one.</summary>
        private DatasetRow? PlainDefinition(string topic)
        {
            if (!topic.StartsWith(TrendPrefix, StringComparison.Ordinal)) return null;
            var concept = topic[TrendPrefix.Length..];
            return _datasets.Rows.FirstOrDefault(r =>
                r.Dataset == DatasetIndex.Datasets.Dictionary &&
                string.Equals(r.Title, concept, StringComparison.OrdinalIgnoreCase));
        }

        // ---- Cascade predicates --------------------------------------------------------------

        /// <summary>Whether the query carries a degree word that could mark either degree.</summary>
        private static bool CarriesADegreeWord(string normalized) =>
            MentionsAny(normalized, Lexicon.AMBIGUOUS_DEGREE);

        /// <summary>
        /// Whether the query asks how something works rather than what a value is.
        ///
        /// <para>A comparison cue disqualifies it. "How does it compare to lead" opens with the same
        /// "how does" as "how does that change above the Curie temperature", but it has a grounded
        /// answer and declining it would break the compound question it usually arrives in.</para>
        /// </summary>
        private static bool AsksHowItWorks(string normalized)
        {
            if (!Lexicon.MECHANISM_FRAMES.Any(f => normalized.Contains(f, StringComparison.Ordinal)))
                return false;
            if (Lexicon.COMPARE.Any(w => normalized.Contains(w, StringComparison.Ordinal)))
                return false;
            if (Lexicon.COMPARATIVE_ADJECTIVES.Keys.Any(a => TextMatching.ContainsWord(normalized, a)))
                return false;
            return true;
        }

        /// <summary>
        /// Whether the query explicitly asks for a <i>family</i> of properties.
        ///
        /// <para>"Magnetic properties of iron" names a family even though <c>magnetic_type</c> also
        /// resolves, so the specific-field-wins rule has to yield to the word "properties".</para>
        /// </summary>
        private static bool AsksForAFamily(string normalized) =>
            FamilyWords.Any(w => normalized.Contains(w, StringComparison.Ordinal));

        /// <summary>
        /// Whether an abundance question is about the whole picture rather than one reservoir.
        ///
        /// <para>"What is the abundance of gold" wants all nine figures; "how much gold is in sea
        /// water" wants one. Both resolve an abundance field, because every one of those labels
        /// contains the word "abundance", so the distinction has to come from whether a reservoir
        /// is named.</para>
        /// </summary>
        private static bool AbundanceFamilyQuestion(string normalized)
        {
            if (CategoryIn(normalized) != FieldCategory.Abundance) return false;
            return !ReservoirWords.Any(w => normalized.Contains(w, StringComparison.Ordinal));
        }

        /// <summary>
        /// Whether a query reads as a continuation of the previous turn rather than a new question.
        ///
        /// <para>Deliberately shape-based rather than a list of pronouns and connectives per
        /// language. A follow-up that means "the same thing, for this element" is short - "and
        /// iron?", "what about silver", "och silver", "und kupfer" - because everything else is
        /// carried by context.</para>
        ///
        /// <para>Counted over whitespace-separated words rather than over tokens, because a token
        /// split also breaks on the hyphen and the apostrophe: French "combien y en a-t-il" is four
        /// words and six tokens, so a four-word follow-up was over the five-token limit and every
        /// French "how many are there" was declined.</para>
        /// </summary>
        private static bool ReadsAsFollowUp(string normalized, int limit = MaxFollowUpTokens) =>
            Whitespace.Split(normalized.Trim()).Count(w => w.Length > 0) <= limit;

        /// <summary>
        /// Whether the query names something the corpus has never heard of.
        ///
        /// <para>BM25 always has a best answer however unlike the corpus the query is, and no
        /// threshold on its score can say otherwise - the scores are normalised against the top hit,
        /// so the best one is always the same number. What <i>can</i> be said with certainty is that
        /// "poem", "weather" and "dinner" appear in no document.</para>
        /// </summary>
        private bool MentionsSomethingUnknown(string rawQuery) =>
            _retriever?.MentionsUnknownTopic(rawQuery) ?? false;

        /// <summary>
        /// Whether a short turn is continuing the thread rather than changing the subject.
        ///
        /// <para>Length alone said yes to "what's the weather today", which then inherited iron and
        /// its electrical type and answered "Iron's Electrical Type is Conductor" - the thread's own
        /// subject, attached to a question that had nothing to do with it.</para>
        ///
        /// <para>The asymmetry is what makes this safe to act on: refusing to inherit produces a
        /// decline, which the personality layer handles gracefully, while inheriting wrongly
        /// produces a cited answer to a question nobody asked. When no retriever is wired the check
        /// cannot run and the old length-only behaviour stands.</para>
        /// </summary>
        /// <summary>
        /// Whether this is a definitional question about a term the dictionary knows - "what is
        /// electronegativity" rather than "what is its electronegativity".
        /// </summary>
        /// <remarks>
        /// <para>Such a question names no element, so inheriting the one in focus turns it into a
        /// property lookup and answers something nobody asked: after discussing gold, "what is
        /// electronegativity" came back "Gold's Electronegativity is 2.54". The corpus pins it as
        /// a DATASET_LOOKUP (ds-dict-1), and the dictionary path is guarded by <c>!hasElement</c>,
        /// so the inherited element is exactly what shuts it out.</para>
        ///
        /// <para>An anaphor excludes it: "what is <i>its</i> density" is about the element in
        /// focus and must keep inheriting. Likewise a frame with no dictionary topic behind it -
        /// there is nothing better to fall through to.</para>
        /// </remarks>
        private bool AsksForADefinition(string normalized)
        {
            // "What is its density" is about the element in focus, not about the word.
            if (Lexicon.ANAPHORS.Any(a => TextMatching.ContainsWord(normalized, a))) return false;

            // A definition question opens with an interrogative; a follow-up opens with a
            // connective ("and the melting point?") or is a bare noun phrase. The interrogatives
            // are taken from DEFINITION_FRAMES rather than written out again, so the twelve
            // languages stay in one place - the frames themselves are too specific to match on,
            // since they carry an article ("what is a") and the bare "what is" is not among them.
            var opener = TextMatching.SplitQueryTokens(normalized).FirstOrDefault();
            if (string.IsNullOrEmpty(opener)) return false;

            var interrogative = Lexicon.DEFINITION_FRAMES
                .Select(f => TextMatching.SplitQueryTokens(f).FirstOrDefault())
                .Any(w => !string.IsNullOrEmpty(w) && string.Equals(w, opener, StringComparison.Ordinal));
            if (!interrogative) return false;

            // And the thing asked about has an entry of its own. Without a better answer to fall
            // through to, inheriting the element in focus is still the best available.
            return _datasets.InDataset(DatasetIndex.Datasets.Dictionary).Any(r => NamesEntry(normalized, r.Title));
        }

        /// <summary>Whether the query names this dictionary entry.</summary>
        /// <remarks>
        /// The topic entries are titled "Periodic trend: ionization energy", so a question about
        /// periodic trends matches no title whole. The part before the colon is the topic, and it
        /// is matched as a substring rather than a word because the question asks about it in the
        /// plural - "periodic trends" against "periodic trend".
        /// </remarks>
        private static bool NamesEntry(string normalized, string title)
        {
            var full = TextMatching.NormalizeForLookup(title);
            if (TextMatching.ContainsWord(normalized, full)) return true;

            var colon = full.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) return false;

            var topic = full[..colon].Trim();
            return topic.Length > 0 && normalized.Contains(topic, StringComparison.Ordinal);
        }

        private bool ContinuesTheThread(string normalized)
        {
            // An explicit anaphor settles it. The unknown-word test exists to catch a turn that
            // changes the subject, and "is it dangerous" plainly does not - while its content word
            // is safety vocabulary, which appears nowhere in a corpus built from element data.
            var anaphoric = Lexicon.ANAPHORS.Any(a => TextMatching.ContainsWord(normalized, a));

            // An anaphor also buys two more words of room. Hindi and Urdu spell "its" as two words
            // and put the copula in a third, so the same follow-up that is five words in English is
            // seven in Urdu, and the length gate declined it.
            if (!ReadsAsFollowUp(normalized, anaphoric ? MaxAnaphoricFollowUp : MaxFollowUpTokens))
                return false;
            if (anaphoric) return true;

            // So does an explicit "what about X" frame, with the same failure if it is missing:
            // "paano naman ang tanso" is built from two Filipino function words that appear in no
            // document, so the unknown-term test read a follow-up as a new subject.
            if (AsksWhatAbout(normalized)) return true;

            // So does a bare unit. "In celsius" is only ever a request to restate the previous
            // answer, and the transliterated unit names - सेल्सियस, سیلسیس, 摄氏度 - appear in no
            // document.
            if (Lexicon.UNIT_WORDS.Keys.Any(
                    u => u.Length >= MinUnitCue && TextMatching.ContainsWord(normalized, u)))
                return true;

            // Safety vocabulary appears in no document - the corpus is element data and dataset
            // rows, not hazard sheets - so "è pericoloso" reads as a change of subject on every
            // measure the index has. It is not one: it is the shortest possible follow-up.
            if (Lexicon.SAFETY_WORDS.Any(w => TextMatching.ContainsWord(normalized, w))) return true;

            return !MentionsSomethingUnknown(normalized);
        }

        /// <summary>Whether the query is the "same question, different subject" frame.</summary>
        private static bool AsksWhatAbout(string normalized) =>
            Lexicon.WHAT_ABOUT_FRAMES.Any(f => normalized.Contains(f, StringComparison.Ordinal));

        /// <summary>
        /// Whether the query points at <i>one</i> subject already in play.
        ///
        /// <para>The Romance interrogatives are all in <c>SELECTIVE</c> - "cuál", "quelle", "qual" -
        /// and they open an ordinary question as readily as a choosing one: "cuál es su densidad" is
        /// "what is its density", not "which of the two is denser". The singular possessive settles
        /// it, and without this every fourth turn of a Spanish, French, Italian or Portuguese thread
        /// turned into a comparison of the two elements before it.</para>
        /// </summary>
        private static bool NamesOneSubject(string normalized)
        {
            if (Lexicon.PLURAL_ANAPHORS.Any(a => TextMatching.ContainsWord(normalized, a))) return false;
            return Lexicon.ANAPHORS.Any(a => TextMatching.ContainsWord(normalized, a));
        }

        /// <summary>Whether the query is asking about isotopes, however elliptically.</summary>
        private static bool AsksForIsotopes(string normalized) =>
            Lexicon.ISOTOPE_WORDS.Any(w => TextMatching.ContainsWord(normalized, w));

        /// <summary>
        /// Whether the query is a family name and little else.
        ///
        /// <para>The token budget is what makes this safe: "noble gases" and "the halogens" qualify,
        /// while "which noble gas has the highest boiling point" does not and is handled by the
        /// superlative branch.</para>
        /// </summary>
        private static bool NamesOnlyAFamily(string normalized, Operators operators, bool hasElement)
        {
            // An element in the query means it is the subject, however short the query is: "is gold
            // radioactive" is three tokens and carries a subset filter, but it asks about gold.
            if (hasElement) return false;

            // An anaphor means the subject is already in play. "Is it radioactive" is the same
            // length as "noble gases" and carries a family word too, but it is a follow-up.
            if (Lexicon.ANAPHORS.Any(a => TextMatching.ContainsWord(normalized, a))) return false;

            if (operators.SubsetFilters.Count == 0) return false;
            if (operators.Aggregation != Aggregation.None ||
                operators.SuperlativeDescending is not null) return false;

            return TextMatching.SplitQueryTokens(normalized).Count(t => t.Trim().Length > 0)
                   <= MaxBareFamilyTokens;
        }

        /// <summary>
        /// The two elements the thread has in play, or null.
        ///
        /// <para>The previous answer when it named two, otherwise the last two discussed - "which is
        /// bigger" after looking up sodium and then potassium in separate turns is the same
        /// question, and neither turn produced a pair.</para>
        /// </summary>
        private static IReadOnlyList<string>? PreviousPair(DialogueState state)
        {
            if (state.LastResultKeys.Count >= 2) return state.LastResultKeys;
            var recent = state.RecentElements.Take(2).ToList();
            return recent.Count == 2 ? recent : null;
        }

        /// <summary>The field id when it names a numeric field, otherwise null.</summary>
        private static string? NumericFieldOrNull(string? fieldId) =>
            fieldId is not null &&
            FieldRegistry.ById.TryGetValue(fieldId, out var spec) &&
            spec.Kind == FieldKind.Numeric
                ? fieldId
                : null;

        /// <summary>A plan the caller should fall through on, and why.</summary>
        private static QueryPlan Decline(string rawQuery, DeclineReason reason) =>
            new(Intent.Unknown, Confidence: 0.0, RawQuery: rawQuery, Declined: reason);

        private static readonly Regex Whitespace = JavaRegex.Compile(@"\s+");

        private static readonly KeyValuePair<string, string>[] ExplanationTopicsByLength =
            Lexicon.EXPLANATION_TOPICS.OrderByDescending(e => e.Key.Length).ToArray();
    }
}
