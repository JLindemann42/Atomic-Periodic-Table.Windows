using System.Collections.Generic;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Engine
{
    /// <summary>
    /// The plan contracts, against the real element table.
    ///
    /// <para>No Kotlin equivalent - Android covers these only through the corpus. They are worth
    /// pinning directly because every one encodes a decision that is invisible until it is wrong in
    /// an answer.</para>
    /// </summary>
    public class QueryPlanTest
    {
        private static readonly KnowledgeStore Store = KnowledgeStore.Build(TestAssets.English);

        private static ElementRecord Element(string key) => Store.Element(key)!;

        /// <summary>
        /// The rule that stops "elements denser than 5" from silently including everything
        /// unmeasured. A missing value is not zero and must never compare as one.
        /// </summary>
        [Fact]
        public void AnElementWithNoValueNeverMatchesAComparison()
        {
            var denserThanFive = new Filter.FieldCompare(
                "density", Op.Gt, new Quantity(5.0, Unit: "g/cm3"));

            Assert.True(denserThanFive.Matches(Element("gold"), Store));

            // Astatine has no measured density. It must fail a "greater than" *and* a "less than",
            // which is the tell that it is being excluded rather than defaulted.
            var astatine = Element("astatine");
            var hasDensity = astatine.Quantity("density") is not null;
            if (!hasDensity)
            {
                Assert.False(denserThanFive.Matches(astatine, Store));
                Assert.False(new Filter.FieldCompare("density", Op.Lt, new Quantity(5.0, Unit: "g/cm3"))
                    .Matches(astatine, Store));
            }
        }

        [Fact]
        public void BetweenNeedsBothBounds()
        {
            var noHigh = new Filter.FieldCompare("density", Op.Between, new Quantity(1.0, Unit: "g/cm3"));
            Assert.False(noHigh.Matches(Element("gold"), Store));

            var withHigh = noHigh with { High = 25.0 };
            Assert.True(withHigh.Matches(Element("gold"), Store));
        }

        [Fact]
        public void StructuralFiltersReadTheDerivedFields()
        {
            var gold = Element("gold");

            Assert.True(new Filter.InPeriod(6).Matches(gold, Store));
            Assert.True(new Filter.InGroup(11).Matches(gold, Store));
            Assert.True(new Filter.IsMetal(true).Matches(gold, Store));
            Assert.False(new Filter.IsSynthetic(true).Matches(gold, Store));
            Assert.True(new Filter.IsSynthetic(true).Matches(Element("americium"), Store));
        }

        [Fact]
        public void CandidatesAppliesEveryFilterAndStaysInAtomicNumberOrder()
        {
            var plan = new QueryPlan(
                Intent.FilterList,
                Filters: new List<Filter>
                {
                    new Filter.InSeries(new HashSet<SeriesId> { SeriesId.NobleGas }),
                    new Filter.IsRadioactive(false),
                });

            var candidates = plan.Candidates(Store);

            Assert.NotEmpty(candidates);
            Assert.All(candidates, e => Assert.Equal(SeriesId.NobleGas, e.Series));
            Assert.All(candidates, e => Assert.False(e.Radioactive));
            Assert.Equal(
                candidates.Select(e => e.AtomicNumber).OrderBy(z => z),
                candidates.Select(e => e.AtomicNumber));
        }

        [Fact]
        public void CollectionDefaultsAreEmptyNotNull()
        {
            var plan = new QueryPlan(Intent.Unknown);

            Assert.Empty(plan.Entities);
            Assert.Empty(plan.FieldIds);
            Assert.Empty(plan.Filters);
            Assert.Empty(plan.Evidence);
            Assert.Null(plan.PrimaryField);
            Assert.Empty(plan.ElementKeys);
        }

        [Fact]
        public void ElementKeysIgnoresNonElementEntities()
        {
            var plan = new QueryPlan(
                Intent.PropertyLookup,
                Entities: new List<EntityRef>
                {
                    new EntityRef.Element("gold"),
                    new EntityRef.DatasetRow("dictionary", "Density"),
                    new EntityRef.Nuclide("uranium", 238),
                });

            Assert.Equal(["gold"], plan.ElementKeys);
        }
    }

    public class EntitlementsTest
    {
        [Fact]
        public void ProPlusImpliesPro()
        {
            var proPlus = new Entitlements(IsPro: false, IsProPlus: true);

            Assert.True(proPlus.Allows(Tier.Free));
            Assert.True(proPlus.Allows(Tier.Pro));
            Assert.True(proPlus.Allows(Tier.ProPlus));
        }

        [Fact]
        public void FreeUserIsBlockedFromBothPaidTiers()
        {
            var free = new Entitlements(IsPro: false, IsProPlus: false);

            Assert.True(free.Allows(Tier.Free));
            Assert.False(free.Allows(Tier.Pro));
            Assert.False(free.Allows(Tier.ProPlus));
        }

        /// <summary>
        /// The suffix strip. Without it a gated field asked for in a particular unit comes back
        /// FREE, because "melting_point@K" is not a key in the registry.
        /// </summary>
        [Fact]
        public void BankedAndUnitSuffixesDoNotChangeAFieldsTier()
        {
            foreach (var fieldId in FieldRegistry.All.Select(s => s.Id))
            {
                var plain = Entitlements.TierOf(fieldId);
                Assert.Equal(plain, Entitlements.TierOf(fieldId + "@K"));
                Assert.Equal(plain, Entitlements.TierOf(fieldId + "#3"));
            }
        }

        [Fact]
        public void UnknownFieldsAreFreeRatherThanBlocking() =>
            Assert.Equal(Tier.Free, Entitlements.TierOf("not_a_field"));

        [Fact]
        public void BlockingTierReportsTheStrictestUnmetTier()
        {
            var free = new Entitlements(IsPro: false, IsProPlus: false);

            var pro = FieldRegistry.All.First(s => s.Tier == Tier.Pro).Id;
            var proPlus = FieldRegistry.All.FirstOrDefault(s => s.Tier == Tier.ProPlus)?.Id;
            var open = FieldRegistry.All.First(s => s.Tier == Tier.Free).Id;

            Assert.Null(free.BlockingTier([open]));
            Assert.Equal(Tier.Pro, free.BlockingTier([open, pro]));

            if (proPlus is not null)
                Assert.Equal(Tier.ProPlus, free.BlockingTier([open, pro, proPlus]));
        }

        [Fact]
        public void DefaultIsFullAccessSoTestsAndCallersAreUnaffected()
        {
            Assert.True(Entitlements.Full.Allows(Tier.ProPlus));
            Assert.True(new Entitlements().Allows(Tier.ProPlus));
        }
    }

    public class DialogueStateTest
    {
        private static QueryPlan Plan(params string[] fieldIds) =>
            new(Intent.PropertyLookup, FieldIds: fieldIds);

        [Fact]
        public void NoteAnswerRemembersFieldsAndFocus()
        {
            var state = new DialogueState();
            state.NoteAnswer(Plan("density"), ["gold"]);

            Assert.Equal("gold", state.FocusElement);
            Assert.Equal(["density"], state.LastFieldIds);
            Assert.Equal(["gold"], state.LastResultKeys);
        }

        /// <summary>
        /// An answer that named no element must not blank the fields the conversation is about,
        /// or "and its density?" loses the thread.
        /// </summary>
        [Fact]
        public void AnEmptyResultDoesNotErasePreviousContext()
        {
            var state = new DialogueState();
            state.NoteAnswer(Plan("density"), ["gold"]);
            state.NoteAnswer(new QueryPlan(Intent.Unknown), []);

            Assert.Equal("gold", state.FocusElement);
            Assert.Equal(["density"], state.LastFieldIds);
        }

        [Fact]
        public void RecentElementsAreNewestFirstDedupedAndCapped()
        {
            var state = new DialogueState();
            foreach (var key in new[] { "gold", "silver", "iron", "copper", "lead", "zinc" })
                state.NoteAnswer(Plan("density"), [key]);

            Assert.Equal(4, state.RecentElements.Count);
            Assert.Equal(["zinc", "lead", "copper", "iron"], state.RecentElements.ToArray());

            // Re-mentioning an element moves it to the front rather than duplicating it.
            state.NoteAnswer(Plan("density"), ["copper"]);
            Assert.Equal("copper", state.RecentElements.First!.Value);
            Assert.Single(state.RecentElements.Where(k => k == "copper"));
        }

        [Fact]
        public void MultipleResultsPutTheFirstAtTheFront()
        {
            var state = new DialogueState();
            state.NoteAnswer(Plan("density"), ["gold", "silver", "iron"]);

            Assert.Equal("gold", state.FocusElement);
            Assert.Equal(["gold", "silver", "iron"], state.RecentElements.ToArray());
        }

        /// <summary>
        /// The copy exists so a failed compound-question attempt cannot corrupt the real
        /// conversation. A shared deque would defeat the whole point.
        /// </summary>
        [Fact]
        public void CopyIsDetachedFromTheOriginal()
        {
            var state = new DialogueState();
            state.NoteAnswer(Plan("density"), ["gold"]);

            var copy = state.CopyOf();
            copy.NoteAnswer(Plan("melting_point"), ["iron"]);

            Assert.Equal("gold", state.FocusElement);
            Assert.Equal(["density"], state.LastFieldIds);
            Assert.Single(state.RecentElements);

            Assert.Equal("iron", copy.FocusElement);
            Assert.Equal(2, copy.RecentElements.Count);
        }

        [Fact]
        public void AdoptTakesOverASuccessfulSpeculativeState()
        {
            var state = new DialogueState { ActiveLanguage = "sv" };
            var speculative = state.CopyOf();
            speculative.NoteAnswer(Plan("melting_point"), ["iron"]);

            state.Adopt(speculative);

            Assert.Equal("iron", state.FocusElement);
            Assert.Equal(["melting_point"], state.LastFieldIds);
            // Adopt deliberately leaves the language alone - it is set by the message, not the plan.
            Assert.Equal("sv", state.ActiveLanguage);
        }

        [Fact]
        public void TargetUnitIsStickyAcrossFollowUps()
        {
            var state = new DialogueState();
            state.NoteAnswer(Plan("melting_point") with { TargetUnit = "K" }, ["gold"]);
            Assert.Equal("K", state.LastTargetUnit);

            // A follow-up with no unit keeps the one the user asked for.
            state.NoteAnswer(Plan("melting_point"), ["iron"]);
            Assert.Equal("K", state.LastTargetUnit);
        }

        [Fact]
        public void ClearResetsEverythingExceptTheLanguage()
        {
            var state = new DialogueState { ActiveLanguage = "de" };
            state.NoteAnswer(Plan("density") with { TargetUnit = "K" }, ["gold"]);

            state.Clear();

            Assert.Null(state.FocusElement);
            Assert.Empty(state.RecentElements);
            Assert.Empty(state.LastFieldIds);
            Assert.Null(state.LastPlan);
            Assert.Empty(state.LastResultKeys);
            Assert.Null(state.LastTargetUnit);
            Assert.Equal("de", state.ActiveLanguage);
        }
    }
}
