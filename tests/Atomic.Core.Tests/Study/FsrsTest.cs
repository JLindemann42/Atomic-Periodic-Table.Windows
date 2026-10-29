using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Atomic.Core.Study;
using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Study
{
    /// <summary>
    /// The study coach's scheduler against <c>shared/golden/fsrs.tsv</c>, which
    /// <c>tools/fsrs_golden.py</c> writes and Android's <c>FsrsTest</c> reads too (roadmap 2.6);
    /// the deck, its merge, and the wire format.
    /// </summary>
    public class FsrsTest
    {
        private const long Start = 1_700_000_000_000L;

        private static string GoldenPath => Path.Combine(AppContext.BaseDirectory, "golden", "fsrs.tsv");

        [Fact]
        public void EveryGoldenReviewMatches()
        {
            Assert.True(File.Exists(GoldenPath), $"{GoldenPath} missing - is the shared submodule checked out?");
            var rows = File.ReadAllLines(GoldenPath)
                .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("case\t", StringComparison.Ordinal))
                .ToList();
            Assert.True(rows.Count > 30);

            var name = "";
            CardState? card = null;
            var now = Start;
            foreach (var row in rows)
            {
                var c = row.Split('\t');
                if (c[0] != name) { name = c[0]; card = null; now = Start; }
                var step = int.Parse(c[1], CultureInfo.InvariantCulture);
                if (step > 0) now += (long)Math.Round(double.Parse(c[2], CultureInfo.InvariantCulture) * Fsrs.DayMs, MidpointRounding.AwayFromZero);
                card = Fsrs.Review(card, int.Parse(c[3], CultureInfo.InvariantCulture), now);
                var label = $"{c[0]} step {c[1]}";
                AssertClose(label, double.Parse(c[4], CultureInfo.InvariantCulture), card.Stability);
                AssertClose(label, double.Parse(c[5], CultureInfo.InvariantCulture), card.Difficulty);
                Assert.True(long.Parse(c[6], CultureInfo.InvariantCulture) == (card.Due - card.Last) / Fsrs.DayMs, label);
                Assert.True(int.Parse(c[7], CultureInfo.InvariantCulture) == card.Reps, label);
                Assert.True(int.Parse(c[8], CultureInfo.InvariantCulture) == card.Lapses, label);
            }
        }

        [Fact]
        public void TheDeckSchedulesOnlyItsSkills()
        {
            var deck = new StudyDeck();
            Assert.Null(deck.Record("superlative_highest", "gold", true, Start));
            Assert.Null(deck.Record("periodic_group", null, true, Start));
            Assert.Equal("periodic_group:gold", deck.Record("periodic_group", "Gold", false, Start));
            Assert.Single(deck.Snapshot());
        }

        [Fact]
        public void DueCardsComeMostOverdueFirst()
        {
            var deck = new StudyDeck();
            deck.Record("periodic_group", "gold", true, Start);
            deck.Record("periodic_period", "iron", false, Start);
            Assert.Empty(deck.Due(Start));
            Assert.Equal(["periodic_period:iron"], deck.Due(Start + 2 * Fsrs.DayMs));
            Assert.Equal(["periodic_period:iron", "periodic_group:gold"], deck.Due(Start + 10 * Fsrs.DayMs));
        }

        [Fact]
        public void TheLaterReviewWinsAMerge()
        {
            var older = Fsrs.Review(null, Fsrs.Good, Start);
            var newer = Fsrs.Review(older, Fsrs.Again, Start + 3 * Fsrs.DayMs);
            var local = new ProgressSnapshot { Study = new Dictionary<string, CardState> { ["periodic_group:gold"] = newer, ["periodic_period:iron"] = older } };
            var cloud = new ProgressSnapshot { Study = new Dictionary<string, CardState> { ["periodic_group:gold"] = older, ["valence_electrons:neon"] = older } };

            var merged = ProgressMerge.Merge(local, cloud).Study;
            Assert.Equal(3, merged.Count);
            Assert.Equal(newer, merged["periodic_group:gold"]);
            Assert.Equal(merged.OrderBy(e => e.Key, StringComparer.Ordinal), ProgressMerge.Merge(cloud, local).Study.OrderBy(e => e.Key, StringComparer.Ordinal));
        }

        [Fact]
        public void CardsRoundTripTheWireFormatAndTheLocalFile()
        {
            var card = Fsrs.Review(null, Fsrs.Good, Start);
            var cards = new Dictionary<string, CardState> { ["periodic_group:gold"] = card };
            var wire = JsonNode.Parse(ProgressDocument.StudyValue(cards).ToJsonString());
            Assert.Equal(card, ProgressDocument.ReadStudy(wire)["periodic_group:gold"]);
            Assert.Equal(card, StudyDeck.Decode(StudyDeck.Encode(cards))["periodic_group:gold"]);
            Assert.Empty(StudyDeck.Decode("garbage\nline"));
        }

        [Fact]
        public void TheStudyFieldIsWrittenAndMasked()
        {
            Assert.Contains(ProgressDocument.Study, ProgressDocument.FieldPaths);
            var document = ProgressDocument.ToDocument(ProgressSnapshot.Empty, DateTimeOffset.UnixEpoch);
            Assert.NotNull(document["fields"]![ProgressDocument.Study]);
        }

        private static void AssertClose(string label, double expected, double actual) =>
            Assert.True(Math.Abs(expected - actual) <= 1e-9 * Math.Max(1.0, Math.Abs(expected)), $"{label}: expected {expected}, was {actual}");
    }
}
