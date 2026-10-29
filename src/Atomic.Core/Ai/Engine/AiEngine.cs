using System;
using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Exec;
using Atomic.Core.Ai.Nlu;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai.Engine
{
    /// <summary>
    /// Plans, executes and composes an answer from the app's own data.
    ///
    /// <para>The engine answers only when its plan clears the planner's confidence threshold.
    /// Anything it does not claim returns null and the caller falls through - so this adds
    /// capability without putting existing answers at risk.</para>
    ///
    /// <para>What it adds that a keyword router could not express: filtering, ranking and
    /// aggregating across all 118 elements in one question; unit conversion and addressing a
    /// specific slot of a banked field; follow-ups that inherit the element or property from the
    /// previous turn; and saying plainly that the app has no value, instead of printing a
    /// sentinel.</para>
    /// </summary>
    public sealed class AiEngine
    {
        private readonly KnowledgeStore _store;
        private readonly QueryPlanner _planner;
        private readonly QueryExecutor _executor;
        private readonly AnswerComposer _composer;

        /// <param name="entitlements">
        /// What the user may be told. Defaulted to full access so tests are unaffected; the app
        /// supplies the real entitlements.
        /// </param>
        /// <param name="cardPolicy">
        /// Which visuals may be attached. The app passes the user's offline setting through here, so
        /// the chat withholds the emission spectrum image exactly where the element screen does.
        /// </param>
        public AiEngine(
            KnowledgeStore store,
            DatasetIndex datasets,
            LocalizedView? localized,
            IStringProvider strings,
            EntityResolver entities,
            HybridRetriever? retriever,
            Entitlements? entitlements = null,
            ChatCardPolicy? cardPolicy = null,
            Explanations? explanations = null)
        {
            _store = store;
            _planner = new QueryPlanner(store, datasets, entities, new FieldResolver(strings), retriever, explanations);
            _executor = new QueryExecutor(store, datasets, localized, strings, entitlements, explanations);
            _composer = new AnswerComposer(store, localized, strings, cardPolicy);
        }

        /// <summary>
        /// The plan behind the most recent answer, or null when the engine deferred.
        ///
        /// <para>The plan is otherwise discarded inside <see cref="Answer"/>, which leaves the caller
        /// unable to say <i>what</i> the engine understood - the difference between "the engine
        /// claimed 80% of questions" and "the engine claimed 80% of questions and they were nearly
        /// all property lookups".</para>
        /// </summary>
        public QueryPlan? LastPlan { get; private set; }

        /// <summary>
        /// Why the most recent <see cref="Answer"/> returned null, or null when it answered.
        /// Telemetry only: a reason, never the question (roadmap 0.7).
        /// </summary>
        public DeclineReason? LastDecline { get; private set; }

        /// <summary>
        /// Answer a question, or return null to defer.
        /// </summary>
        /// <param name="state">
        /// Carries the conversation's focus so follow-ups resolve; updated on success.
        /// </param>
        public ComposedAnswer? Answer(string query, DialogueState state)
        {
            LastPlan = null;
            LastDecline = null;
            if (string.IsNullOrWhiteSpace(query)) return Declined(DeclineReason.NoMatch);

            // A question that asks two things is tried first. The whole query often plans to
            // something - "the density of gold and how does it compare to lead" plans as a
            // comparison - so checking single-clause first would answer half the question and never
            // notice the other half.
            if (CompoundAnswer(query, state) is { } compound) return compound;

            var plan = _planner.Plan(query, state);
            if (plan.Intent == Intent.Unknown) return Declined(plan.Declined ?? DeclineReason.NoMatch);
            if (plan.Confidence < _planner.Threshold) return Declined(DeclineReason.LowConfidence);

            var result = _executor.Execute(plan);
            if (result is null) return Declined(DeclineReason.NoResult);

            var composed = _composer.Compose(result, plan);
            if (string.IsNullOrWhiteSpace(composed.Text)) return Declined(DeclineReason.NoResult);

            state.NoteAnswer(plan, ResultKeys(result));
            LastPlan = plan;
            return composed;
        }

        private ComposedAnswer? Declined(DeclineReason reason)
        {
            LastDecline = reason;
            return null;
        }

        /// <summary>
        /// Answer a two-part question, or return null so it is planned as one.
        ///
        /// <para>Each clause is planned in turn against a scratch state, so the second inherits the
        /// subject of the first - "…and how does <b>it</b> compare to lead" resolves through exactly
        /// the same slot-inheritance that makes a follow-up turn work.</para>
        ///
        /// <para>All or nothing: if either clause fails to plan, the whole attempt is abandoned and
        /// the query falls through to be handled whole. A partial compound answer would be worse
        /// than the single-clause answer it replaced.</para>
        /// </summary>
        private ComposedAnswer? CompoundAnswer(string query, DialogueState state)
        {
            var clauses = ClauseSplitter.Split(query);
            if (clauses is null) return null;

            var scratch = state.CopyOf();
            var answers = new List<ComposedAnswer>(clauses.Count);
            var citations = new List<Citation>(clauses.Count * 2);
            QueryPlan? lastClausePlan = null;

            foreach (var clause in clauses)
            {
                var plan = _planner.Plan(clause, scratch);
                if (plan.Intent == Intent.Unknown || plan.Confidence < _planner.Threshold) return null;
                lastClausePlan = plan;

                var result = _executor.Execute(plan);
                if (result is null) return null;

                // Bodies only, so the merged answer carries one sources section rather than one
                // after every clause.
                var composed = _composer.ComposeBody(result, plan);
                if (string.IsNullOrWhiteSpace(composed.Text)) return null;

                scratch.NoteAnswer(plan, ResultKeys(result));
                answers.Add(composed);
                citations.AddRange(result.Citations);
            }

            if (answers.Count < 2) return null;

            state.Adopt(scratch);
            // The final clause's plan. A compound answer has no single plan, and the last clause is
            // the one the conversation is left focused on.
            LastPlan = lastClausePlan;

            return new ComposedAnswer(
                Text: string.Join("\n\n", answers.Select(a => a.Text)) +
                      _composer.CitationsFor(citations.DistinctBy(c => (c.Label, c.Source)).ToList()),
                Actions: answers.SelectMany(a => a.Actions).DistinctBy(a => a.Label).ToList(),
                // One card per message. Two visuals in a single bubble is clutter, and it would make
                // the panel's template scheme combinatorial for no gain.
                Card: answers.Select(a => a.Card).FirstOrDefault(c => c is not null));
        }

        /// <summary>Plan without executing. Exposed so tests can assert planning separately from rendering.</summary>
        public QueryPlan Plan(string query, DialogueState state) => _planner.Plan(query, state);

        /// <summary>
        /// The plan an answer to <paramref name="query"/> would use, or null where it would decline -
        /// without answering, and against a copy of <paramref name="state"/>, so asking changes
        /// nothing. Used to offer only the follow-up chips the engine can answer (roadmap 2.1).
        /// </summary>
        public QueryPlan? WouldAnswer(string query, DialogueState state) =>
            _planner.Plan(query, state.CopyOf()) is { } plan &&
            plan.Intent != Intent.Unknown && plan.Confidence >= _planner.Threshold
                ? plan
                : null;

        /// <summary>
        /// Run an intent directly against a known element, bypassing query parsing.
        ///
        /// <para>Used when the conversation already establishes what is being asked - a "yes" to a
        /// suggested follow-up, or one answer falling back to another - so there is no
        /// natural-language question to plan from.</para>
        /// </summary>
        public ComposedAnswer? AnswerFor(Intent intent, string elementKey)
        {
            var plan = new QueryPlan(
                intent,
                Entities: [new EntityRef.Element(elementKey)],
                Limit: intent == Intent.Isotopes ? DefaultIsotopes : 1,
                Confidence: 1.0);

            var result = _executor.Execute(plan);
            if (result is null) return null;

            LastPlan = plan;
            return _composer.Compose(result, plan);
        }

        /// <summary>
        /// One named field of one element, with no question to parse.
        ///
        /// <para>Used by "tell me more", which chooses the field itself and needs the answer
        /// rendered exactly as a real lookup would render it - same units, same citation, same
        /// entitlement gate. Composing that sentence by hand instead would be a second renderer to
        /// keep in step, and it would quietly hand out the PRO-gated fields, two of which
        /// (Young's modulus and Poisson's ratio) are on the list it picks from.</para>
        /// </summary>
        public ComposedAnswer? FieldAnswerFor(string elementKey, string fieldId)
        {
            if (_store.Element(elementKey) is not { } element) return null;
            if (element.Value(fieldId).IsMissing) return null;

            var plan = new QueryPlan(
                Intent.PropertyLookup,
                Entities: [new EntityRef.Element(elementKey)],
                FieldIds: [fieldId],
                Confidence: 1.0);

            var result = _executor.Execute(plan);
            if (result is null) return null;

            LastPlan = plan;
            return _composer.Compose(result, plan);
        }

        /// <summary>Every populated field of one family, for one element.</summary>
        public ComposedAnswer? CategoryAnswerFor(string elementKey, FieldCategory category)
        {
            if (_store.Element(elementKey) is not { } element) return null;

            var populated = FieldRegistry.ByCategory(category)
                .Where(spec => !element.Value(spec.Id).IsMissing)
                .Select(spec => spec.Id)
                .ToList();

            if (populated.Count == 0) return null;

            var plan = new QueryPlan(
                Intent.CategoryLookup,
                Entities: [new EntityRef.Element(elementKey)],
                FieldIds: populated,
                Confidence: 1.0);

            var result = _executor.Execute(plan);
            if (result is null) return null;

            LastPlan = plan;
            return _composer.Compose(result, plan);
        }

        private const int DefaultIsotopes = 8;

        /// <summary>
        /// The elements an answer was about, for the dialogue state to remember.
        ///
        /// <para>A conversion, a balanced equation and a solution calculation are about a number or
        /// a compound, not about an element, so none of them establishes a subject for "and its
        /// density?" to inherit. A <i>withheld</i> answer does - the user asked about that element,
        /// and the follow-up should still resolve.</para>
        /// </summary>
        private static IReadOnlyList<string> ResultKeys(ExecutionResult result) => result switch
        {
            ExecutionResult.Property r => [r.Element.Key],
            ExecutionResult.Comparison r => r.Elements.Select(e => e.Key).ToList(),
            ExecutionResult.ElementList r => r.Results.Select(v => v.Element.Key).ToList(),
            ExecutionResult.Trend r => r.Points.Select(v => v.Element.Key).ToList(),
            ExecutionResult.Explanation r => r.ExplainedTrend?.Points.Select(v => v.Element.Key).ToList()
                ?? (r.Ionization is null ? [] : [r.Ionization.Key]),
            ExecutionResult.Aggregate r => r.Contributors.Select(v => v.Element.Key).ToList(),
            ExecutionResult.Isotopes r => [r.Element.Key],
            ExecutionResult.Safety r => [r.Element.Key],
            ExecutionResult.EmissionSpectrum r => [r.Element.Key],
            ExecutionResult.Comparative r => [r.Winner.Key, r.Loser.Key],
            ExecutionResult.Neighbour r => [r.To.Key],
            ExecutionResult.Nuclide r => [r.Element.Key],
            ExecutionResult.IsotopeComparison r =>
                new[] { r.Left.Element.Key, r.Right.Element.Key }.Distinct(StringComparer.Ordinal).ToList(),
            ExecutionResult.NoData r => r.Element is null ? [] : [r.Element.Key],
            ExecutionResult.Locked r => r.Element is null ? [] : [r.Element.Key],
            ExecutionResult.Decay r => [r.Element.Key],
            ExecutionResult.IonConfiguration r => [r.Element.Key],
            _ => [],
        };
    }
}
