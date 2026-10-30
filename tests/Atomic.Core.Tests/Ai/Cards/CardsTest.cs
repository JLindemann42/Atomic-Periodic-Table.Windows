using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Atomic.Core.Ai.Cards;
using Atomic.Core.Ai.Compose;
using Atomic.Core.Ai.Data;
using Atomic.Core.Ai.Engine;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Cards
{
    public class CardSelectorTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static readonly ReswStringProvider Strings = new("en", fallBackToEnglish: true);

        private static ChatCard? Select(ExecutionResult result, ChatCardPolicy? policy = null) =>
            CardSelector.Select(result, new QueryPlan(Intent.Unknown), Service.Store, Strings,
                                Service.Localized("en"), policy);

        private static ElementRecord Element(string key) => Service.Store.Element(key)!;

        private static ExecutionResult.Property Property(string key, string field) =>
            new(Element(key), field, null, string.Empty, []);

        /// <summary>
        /// The rule the whole layer rests on. A card can only show something the answer has already
        /// asserted, so it is derived from the typed result and never from the query text - which is
        /// what stops the picture and the prose drifting apart.
        ///
        /// <para>The guarantee that matters: over all 118 elements, no kind is ever emitted whose
        /// reducer then returns null. A card the view cannot draw is worse than no card.</para>
        /// </summary>
        [Fact]
        public void NoCardIsEverEmittedWhoseReducerCannotProduceIt()
        {
            var offenders = new List<string>();

            foreach (var element in Service.Store.Elements)
            {
                foreach (var fieldId in FieldRegistry.All.Select(s => s.Id))
                {
                    var card = Select(Property(element.Key, fieldId));
                    if (card is null) continue;

                    var producible = card.Kind switch
                    {
                        ChatCardKind.PoissonBand => PoissonBandReducer.Of(element, Service.Store) is not null,
                        ChatCardKind.IonizationSeries => IonizationSeriesReducer.Of(element) is not null,
                        ChatCardKind.IsotopeDecay => IsotopeSeriesReducer.Of(element) is not null,
                        ChatCardKind.Abundance => AbundanceReducer.Of(element, Strings) is not null,
                        ChatCardKind.NfpaDiamond => NfpaLabeller.HasAnyRating(element.Nfpa),
                        ChatCardKind.CrystalStructure =>
                            CrystalSystemResolver.Resolve(RawCrystal(element)) is not null,
                        // The electron-shell and spectrum cards are drawn from data the guard has
                        // already checked, so there is no separate reducer to disagree with.
                        _ => true,
                    };

                    if (!producible) offenders.Add($"{element.Key}/{fieldId} -> {card.Kind}");
                }
            }

            Assert.Empty(offenders);

            static string? RawCrystal(ElementRecord e) => e.Value("crystal_structure") switch
            {
                FieldValue.Enum v => v.Localized,
                FieldValue.Text v => v.Raw,
                _ => null,
            };
        }

        [Fact]
        public void MostFieldsGetNoCardAtAll()
        {
            var withCard = FieldRegistry.All
                .Count(s => Select(Property("gold", s.Id)) is not null);

            // A wrong card is worse than no card, so the mapping is deliberately sparse.
            Assert.True(withCard < FieldRegistry.All.Count / 3,
                        $"{withCard} of {FieldRegistry.All.Count} fields produced a card");
        }

        /// <summary>
        /// The case the whole render-time locking scheme exists for. While Locked fell through to
        /// null, the Poisson band and the hazard diamond - both PRO - were the two cards a free user
        /// could never reach by any question.
        /// </summary>
        [Fact]
        public void AWithheldAnswerStillGetsTheCardItsUnlockedFormWouldHaveGot()
        {
            var locked = new ExecutionResult.Locked(Tier.Pro, ["poisson_ratio"], Element("gold"));
            var card = Select(locked);

            Assert.NotNull(card);
            Assert.Equal(ChatCardKind.PoissonBand, card!.Kind);
        }

        /// <summary>
        /// A single gated field that maps to no card must stay without one rather than borrowing its
        /// family's, which would illustrate a different property than the one asked about.
        /// </summary>
        [Fact]
        public void ASingleGatedFieldNeverBorrowsItsFamilysCard()
        {
            var noCardField = FieldRegistry.ByCategory(FieldCategory.Thermo)
                .First(s => Select(Property("gold", s.Id)) is null).Id;

            var locked = new ExecutionResult.Locked(Tier.Pro, [noCardField], Element("gold"));
            Assert.Null(Select(locked));
        }

        /// <summary>Several gated fields are a family, and map by their dominant category.</summary>
        [Fact]
        public void SeveralGatedFieldsMapByTheirDominantFamily()
        {
            var locked = new ExecutionResult.Locked(
                Tier.Pro, ["nfpa_health", "nfpa_flammability", "nfpa_instability"], Element("mercury"));

            Assert.Equal(ChatCardKind.NfpaDiamond, Select(locked)?.Kind);
        }

        /// <summary>
        /// A multi-element comparison gets nothing: every card kind is per-element, so any of them
        /// would misattribute half the answer.
        /// </summary>
        [Fact]
        public void AMultiElementComparisonGetsNoCard()
        {
            var two = new ExecutionResult.Comparison(
                [Element("gold"), Element("silver")], ["density"],
                new Dictionary<string, IReadOnlyList<ValuedElement>>(), []);

            Assert.Null(Select(two));
        }

        /// <summary>
        /// The spectrum is an image on a server rather than the app's own numbers, so it is the one
        /// card the policy can withhold when there is no network.
        /// </summary>
        [Fact]
        public void OnlyTheSpectrumCardIsWithheldOffline()
        {
            var spectrum = new ExecutionResult.EmissionSpectrum(Element("sodium"), []);

            Assert.NotNull(Select(spectrum));
            Assert.Null(Select(spectrum, new ChatCardPolicy(AllowNetworkCards: false)));

            // Everything else is drawn from local data and is unaffected.
            var safety = new ExecutionResult.Safety(Element("mercury"), Element("mercury").Nfpa!, true, []);
            Assert.NotNull(Select(safety, new ChatCardPolicy(AllowNetworkCards: false)));
        }

        [Fact]
        public void ADeclinedAnswerGetsNoCard() =>
            Assert.Null(Select(new ExecutionResult.Empty([])));

        [Fact]
        public void TheCardTitleFollowsTheActiveLanguage()
        {
            var swedish = CardSelector.Select(
                new ExecutionResult.EmissionSpectrum(Element("gold"), []),
                new QueryPlan(Intent.Unknown), Service.Store, Strings, Service.Localized("sv"));

            Assert.Equal("Guld", swedish?.Title);
        }
    }

    public class ReducerTest
    {
        private static readonly RetrievalService Service =
            RetrievalService.Get(TestAssets.ElementSource)
            ?? throw new InvalidOperationException("element assets not readable");

        private static readonly ReswStringProvider Strings = new("en", fallBackToEnglish: true);

        private static ElementRecord Element(string key) => Service.Store.Element(key)!;

        /// <summary>
        /// The teaching point of the series: sodium's second ionization costs nearly ten times its
        /// first, because the first emptied its outer shell.
        /// </summary>
        [Fact]
        public void IonizationJumpsAreRecordedAgainstTheShellThatEmptied()
        {
            var series = IonizationSeriesReducer.Of(Element("sodium"));

            Assert.NotNull(series);
            Assert.Contains(1, series!.ShellClosureAfter);
        }

        [Fact]
        public void AnElementWithTooFewStepsGetsNoSeries() =>
            // Hydrogen has one ionization energy; a chart of it says nothing a sentence does not.
            Assert.Null(IonizationSeriesReducer.Of(Element("hydrogen")));

        /// <summary>
        /// A stable isotope has no finite half-life, and plotting it at the axis maximum would
        /// assert a number the data does not contain.
        /// </summary>
        [Fact]
        public void AStableIsotopeCarriesNoLogSeconds()
        {
            var series = IsotopeSeriesReducer.Of(Element("carbon"));

            Assert.NotNull(series);
            Assert.All(series!.Points.Where(p => p.Stable), p => Assert.Null(p.LogSeconds));
            Assert.NotEmpty(series.StableMassNumbers);
        }

        [Fact]
        public void IsotopePointsAreOrderedByMassNumber()
        {
            var series = IsotopeSeriesReducer.Of(Element("tin"))!;
            Assert.Equal(series.Points.Select(p => p.MassNumber).OrderBy(m => m),
                         series.Points.Select(p => p.MassNumber));
        }

        /// <summary>
        /// The authored spellings are a mix of notations. Anything unrecognised lands in Other
        /// rather than being dropped, and this enumerates every distinct spelling in the real
        /// assets so a new one shows up here rather than as a silently grey marker.
        /// </summary>
        [Fact]
        public void EveryAuthoredDecayTypeMapsToAGroup()
        {
            var spellings = Service.Store.Elements
                .SelectMany(e => e.Isotopes)
                .Select(i => i.DecayType)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.NotEmpty(spellings);

            // Not one of them may throw, and the ones the data actually uses should be recognised
            // rather than all landing in Other.
            var recognised = spellings.Count(s => IsotopeSeriesReducer.Group(s) != DecayGroup.Other);
            Assert.True(recognised > spellings.Count / 2,
                        $"only {recognised} of {spellings.Count} authored decay types were recognised");
        }

        [Theory]
        [InlineData("α", DecayGroup.Alpha)]
        [InlineData("B-", DecayGroup.BetaMinus)]
        [InlineData("B+", DecayGroup.BetaPlusEc)]
        [InlineData("EC", DecayGroup.BetaPlusEc)]
        [InlineData("IT", DecayGroup.Isomeric)]
        [InlineData("SF", DecayGroup.Fission)]
        [InlineData("2p", DecayGroup.NucleonEmission)]
        [InlineData("n", DecayGroup.NucleonEmission)]
        [InlineData("---", DecayGroup.Other)]
        [InlineData("", DecayGroup.Other)]
        public void DecayNotationsMapAsAuthored(string spelling, DecayGroup expected) =>
            Assert.Equal(expected, IsotopeSeriesReducer.Group(spelling));

        /// <summary>
        /// "sp" is not proton emission. Kotlin's `matches` tests the whole string, and an unanchored
        /// search would read the p inside it.
        /// </summary>
        [Fact]
        public void NucleonEmissionIsMatchedOnTheWholeString() =>
            Assert.NotEqual(DecayGroup.NucleonEmission, IsotopeSeriesReducer.Group("sp"));

        /// <summary>
        /// Drawing all nine reservoirs as one run of bars claims a comparison the data does not
        /// support: sea water is µg/l and the sun is atom counts against silicon.
        /// </summary>
        [Fact]
        public void AbundanceBarsArePartitionedByUnit()
        {
            var profile = AbundanceReducer.Of(Element("gold"), Strings);

            Assert.NotNull(profile);
            Assert.True(profile!.Groups.Count > 1, "the reservoirs do not share one unit");
            Assert.Equal(profile.Bars.Count, profile.Groups.Sum(g => g.Bars.Count));
        }

        /// <summary>
        /// A log axis has no zero, so the bars are drawn from a real, labelled floor - otherwise
        /// their lengths mean nothing at all.
        /// </summary>
        [Fact]
        public void TheAbundanceFloorSitsBelowEveryBar()
        {
            var profile = AbundanceReducer.Of(Element("iron"), Strings)!;
            Assert.True(profile.FloorLog < profile.MinLog);
        }

        /// <summary>
        /// "Present but unquantified" is real information; a reducer that only handled numbers would
        /// silently drop those bars.
        /// </summary>
        [Fact]
        public void TraceBarsSurviveWithARenderableDisplay()
        {
            var withTrace = Service.Store.Elements
                .Select(e => AbundanceReducer.Of(e, Strings))
                .OfType<AbundanceProfile>()
                .SelectMany(p => p.Bars)
                .Where(b => b.Trace)
                .ToList();

            if (withTrace.Count == 0) return;   // no trace values in the shipped data

            Assert.All(withTrace, b =>
            {
                Assert.Null(b.LogMgPerKg);
                Assert.NotEmpty(b.Display);
                // Was the value's ToString on Android, which drew a class name and a hash.
                Assert.DoesNotContain("Atomic.Core", b.Display, StringComparison.Ordinal);
            });
        }

        /// <summary>
        /// The reservoirs stay in a fixed order so the same one sits in the same place for every
        /// element, which is what lets two cards be compared by eye.
        /// </summary>
        [Fact]
        public void AbundanceBarsKeepTheDeclaredReservoirOrder()
        {
            var profile = AbundanceReducer.Of(Element("iron"), Strings)!;
            var expected = AbundanceReducer.Order.Where(id => profile.Bars.Any(b => b.FieldId == id));

            Assert.Equal(expected, profile.Bars.Select(b => b.FieldId));
        }

        [Fact]
        public void ThePoissonAxisAlwaysHoldsTheIncompressibleLimit()
        {
            var band = PoissonBandReducer.Of(Element("gold"), Service.Store);

            Assert.NotNull(band);
            Assert.True(band!.AxisMax > PoissonBandReducer.IncompressibleLimit);
            Assert.NotEmpty(band.References);
        }

        /// <summary>
        /// The renderer falls back to "Cubic" for anything it does not recognise, which is a
        /// cosmetic guess on the element screen and a fabrication in chat, where the card is the
        /// answer.
        /// </summary>
        [Fact]
        public void AnUnrecognisedCrystalStringResolvesToNullRatherThanCubic()
        {
            Assert.Null(CrystalSystemResolver.Resolve("Quasicrystalline"));
            Assert.Null(CrystalSystemResolver.Resolve(""));
            Assert.Null(CrystalSystemResolver.Resolve(null));
            Assert.Equal("Cubic", CrystalSystemResolver.Resolve("cubic"));
        }

        /// <summary>The data spells hexagonal several ways, so it is matched loosely.</summary>
        [Theory]
        [InlineData("Hexagonal")]
        [InlineData("Hexagonal close-packed (hcp)")]
        [InlineData("Simple hexagonal")]
        public void HexagonalIsMatchedLoosely(string spelling) =>
            Assert.Equal("Hexagonal", CrystalSystemResolver.Resolve(spelling));

        /// <summary>
        /// Every authored crystal structure either resolves or is deliberately unrecognised - and
        /// anything that resolves must name a wireframe the app can actually draw.
        /// </summary>
        [Fact]
        public void EveryResolvedCrystalSystemIsOneTheAppCanDraw()
        {
            var resolved = Service.Store.Elements
                .Select(e => e.Value("crystal_structure"))
                .Select(v => v switch
                {
                    FieldValue.Enum en => en.Localized,
                    FieldValue.Text t => t.Raw,
                    _ => null,
                })
                .Select(CrystalSystemResolver.Resolve)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.NotEmpty(resolved);
            Assert.All(resolved, key => Assert.True(CrystalSystemResolver.KnownSystems.Contains(key), key));
        }

        [Fact]
        public void AnAbsentNfpaRatingShowsAsADashRatherThanAMisleadingZero()
        {
            var labels = NfpaLabeller.Labels(new Nfpa(null, null, null, null), Strings);

            Assert.Equal("–", labels.Health);
            Assert.Equal("–", labels.Fire);
            Assert.Equal("–", labels.Reactivity);
            Assert.Equal("–", labels.Special);
        }

        [Theory]
        [InlineData("Simple asphyxiant", "SA")]
        [InlineData("W", "W")]
        [InlineData("OX", "OX")]
        [InlineData("---", "–")]
        [InlineData("", "–")]
        public void TheSpecialQuadrantCodeIsDerivedFromTheAuthoredText(string special, string expected) =>
            Assert.Equal(expected, NfpaLabeller.SpecialCode(special));

        [Fact]
        public void HasAnyRatingIsFalseOnlyWhenEveryAxisIsEmpty()
        {
            Assert.False(NfpaLabeller.HasAnyRating(null));
            Assert.False(NfpaLabeller.HasAnyRating(new Nfpa(null, null, null, null)));
            Assert.True(NfpaLabeller.HasAnyRating(new Nfpa(2, null, null, null)));
            Assert.True(NfpaLabeller.HasAnyRating(new Nfpa(null, null, null, "W")));
        }

        [Fact]
        public void EmissionSpectrumUrlsAreBuiltFromTheSymbol() =>
            Assert.EndsWith("/Fe.gif", EmissionSpectrumUrl.Of(" Fe "), StringComparison.Ordinal);
    }

    public class DecadeLabelsTest
    {
        /// <summary>
        /// Half-lives span fifty decades, so a raw "1.0E+17 s" tick is useless. The largest unit
        /// that leaves a value of at least one is chosen.
        /// </summary>
        [Theory]
        [InlineData(0, "1 s")]
        [InlineData(2, "2 min")]
        [InlineData(5, "1 d")]
        [InlineData(-9, "1 ns")]
        public void TimeLabelsPickAReadableUnit(int log10Seconds, string expected) =>
            Assert.Equal(expected, DecadeLabels.TimeLabel(log10Seconds));

        [Theory]
        [InlineData(0, "1")]
        [InlineData(3, "10³")]
        [InlineData(-6, "10⁻⁶")]
        public void ScientificLabelsUseRealSuperscripts(int log10, string expected) =>
            Assert.Equal(expected, DecadeLabels.SciLabel(log10));

        /// <summary>
        /// A decade of spread is the threshold: below it a linear axis reads more directly, above it
        /// the small values collapse onto the baseline.
        /// </summary>
        [Fact]
        public void TheScaleIsChosenFromTheSpreadOfTheData()
        {
            Assert.Equal(ChartScale.Linear, DecadeLabels.SuggestedScale([1.0, 2.0, 3.0]));
            Assert.Equal(ChartScale.Log10, DecadeLabels.SuggestedScale([1.0, 1000.0]));
            // Fewer than two positive values is not a spread.
            Assert.Equal(ChartScale.Linear, DecadeLabels.SuggestedScale([5.0]));
            Assert.Equal(ChartScale.Linear, DecadeLabels.SuggestedScale([0.0, -1.0]));
        }

        /// <summary>Axis labels reach the user, so they must not follow the ambient culture.</summary>
        [Fact]
        public void LabelsAreInvariantUnderAHostileCulture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");
                Assert.Equal("1 d", DecadeLabels.TimeLabel(5));
                Assert.Equal("10⁻⁶", DecadeLabels.SciLabel(-6));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }

    public class CodecTest
    {
        [Fact]
        public void ACardRoundTrips()
        {
            var card = new ChatCard(
                ChatCardKind.PoissonBand, "gold", "Gold",
                new Dictionary<string, string> { ["scale"] = "log" });

            var decoded = ChatCardCodec.Decode(ChatCardCodec.Encode(card));

            Assert.Equal(card.Kind, decoded!.Kind);
            Assert.Equal(card.ElementKey, decoded.ElementKey);
            Assert.Equal(card.Title, decoded.Title);
            Assert.Equal("log", decoded.Args["scale"]);
        }

        /// <summary>
        /// The template selector is asked for every visible row on every layout pass, so it must not
        /// pay for a full decode just to choose a template.
        /// </summary>
        [Fact]
        public void TheKindCanBeReadWithoutDecodingTheRest()
        {
            var encoded = ChatCardCodec.Encode(new ChatCard(ChatCardKind.Abundance, "iron", "Iron"));
            Assert.Equal(ChatCardKind.Abundance, ChatCardCodec.KindOf(encoded));
        }

        [Fact]
        public void AValueCanNeverCorruptTheCardEncoding()
        {
            var hostile = new ChatCard(ChatCardKind.Abundance, "iron", "Ironnot a field");
            var decoded = ChatCardCodec.Decode(ChatCardCodec.Encode(hostile));

            Assert.Equal(ChatCardKind.Abundance, decoded!.Kind);
            Assert.Equal("iron", decoded.ElementKey);
        }

        [Fact]
        public void MalformedCardDataDecodesToNothingRatherThanThrowing()
        {
            Assert.Null(ChatCardCodec.Decode(null));
            Assert.Null(ChatCardCodec.Decode(""));
            Assert.Null(ChatCardCodec.Decode("NOT_A_KINDgoldGold"));
            Assert.Null(ChatCardCodec.Decode("Abundance"));
            Assert.Null(ChatCardCodec.Encode(null));
        }

        [Fact]
        public void ActionsRoundTrip()
        {
            IReadOnlyList<ChatAction> actions =
            [
                new("Open Gold", DeepLinkTarget.ElementInfo,
                    new Dictionary<string, string> { ["key"] = "gold" }),
                new("Open table", DeepLinkTarget.Solubility),
            ];

            var decoded = ChatActionCodec.Decode(ChatActionCodec.Encode(actions));

            Assert.Equal(2, decoded.Count);
            Assert.Equal("Open Gold", decoded[0].Label);
            Assert.Equal("gold", decoded[0].Args["key"]);
            Assert.Equal(DeepLinkTarget.Solubility, decoded[1].Target);
            Assert.Empty(decoded[1].Args);
        }

        /// <summary>An older stored message decodes to an empty list rather than needing a migration.</summary>
        [Fact]
        public void MalformedActionDataDecodesToAnEmptyListRatherThanThrowing()
        {
            Assert.Empty(ChatActionCodec.Decode(null));
            Assert.Empty(ChatActionCodec.Decode(""));
            Assert.Empty(ChatActionCodec.Decode("garbage"));
            Assert.Empty(ChatActionCodec.Decode("LabelNOT_A_TARGET"));
            Assert.Null(ChatActionCodec.Encode([]));
        }

        [Fact]
        public void ALabelCanNeverCorruptTheActionEncoding()
        {
            IReadOnlyList<ChatAction> hostile =
                [new("Open Gold", DeepLinkTarget.ElementInfo)];

            var decoded = ChatActionCodec.Decode(ChatActionCodec.Encode(hostile));

            Assert.Single(decoded);
            Assert.Equal(DeepLinkTarget.ElementInfo, decoded[0].Target);
        }

        [Fact]
        public void ACitationBecomesATappableChip()
        {
            var citation = new Citation(
                "Density", "Gold", DeepLinkTarget.ElementInfo,
                new Dictionary<string, string> { ["key"] = "gold" });

            var action = citation.ToAction(new ReswStringProvider("en", fallBackToEnglish: true));

            Assert.Contains("Gold", action.Label, StringComparison.Ordinal);
            Assert.Equal(DeepLinkTarget.ElementInfo, action.Target);
            Assert.Equal("gold", action.Args["key"]);
        }
    }
}
