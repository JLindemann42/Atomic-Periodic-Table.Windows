using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Atomic.Core.Progression;
using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Progression
{
    /// <summary>
    /// Guards the achievement definitions against the two ways they can silently stop working:
    /// an id the phone cannot parse, and a definition nothing advances.
    /// </summary>
    public class AchievementsTest
    {
        /// <summary>
        /// Android reads the cloud id with <c>toIntOrNull()</c> and drops the entry when it fails.
        /// A readable id like "perfect_game" would therefore vanish on the phone with no error
        /// raised anywhere - the achievement would simply never sync.
        /// </summary>
        [Fact]
        public void EveryIdParsesAsAnInteger()
        {
            foreach (var achievement in Achievements.All)
            {
                Assert.True(
                    int.TryParse(achievement.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                    $"'{achievement.Id}' ({achievement.Title}) is not an integer, so Android would drop it");
            }
        }

        [Fact]
        public void IdsAreUnique() =>
            Assert.Equal(Achievements.All.Count, Achievements.All.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count());

        /// <summary>
        /// The exact ids Android's <c>AchievementModel.getList</c> declares. Hard-coded rather than
        /// derived, because this is the contract with the other platform: a divergence here means
        /// one side's progress lands on an achievement the other does not have.
        /// </summary>
        [Fact]
        public void MatchesTheAndroidIdSet()
        {
            string[] expected = ["1", "2", "3", "4", "5", "6", "7", "8", "101001", "101002", "101003", "101004"];

            Assert.Equal(expected.OrderBy(i => i, StringComparer.Ordinal),
                         Achievements.All.Select(a => a.Id).OrderBy(i => i, StringComparer.Ordinal));
        }

        [Fact]
        public void GoalsMatchAndroid()
        {
            var goals = Achievements.All.ToDictionary(a => a.Id, a => a.Goal, StringComparer.Ordinal);

            Assert.Equal(10, goals["1"]);
            Assert.Equal(100, goals["2"]);
            Assert.Equal(1000, goals["3"]);
            Assert.Equal(10000, goals["4"]);
            Assert.Equal(1000, goals["5"]);
            Assert.Equal(50, goals["6"]);
            Assert.Equal(1, goals["7"]);
            Assert.Equal(1000, goals["8"]);
            Assert.Equal(1, goals["101001"]);
            Assert.Equal(10, goals["101002"]);
            Assert.Equal(100, goals["101003"]);
            Assert.Equal(500, goals["101004"]);
        }

        /// <summary>
        /// Android declares achievements 5 and 8 and then increments them nowhere, so they sit at
        /// zero forever. Making the trigger part of the definition is what stops the same thing
        /// happening here - an achievement with no reachable trigger is a dead row.
        /// </summary>
        [Fact]
        public void EveryTriggerAdvancesSomething()
        {
            foreach (var trigger in Enum.GetValues<AchievementTrigger>())
            {
                var advances = Achievements.For(trigger).Count > 0 || UsageStatistics.For(trigger) is not null;
                Assert.True(advances, $"{trigger} advances neither an achievement nor a statistic");
            }
        }

        [Fact]
        public void EveryAchievementHasAGoalAboveZero() =>
            Assert.All(Achievements.All, a => Assert.True(a.Goal > 0, $"{a.Id} has goal {a.Goal}"));

        [Fact]
        public void ByIdFindsEveryDeclaredAchievement() =>
            Assert.All(Achievements.All, a => Assert.Equal(a, Achievements.ById(a.Id)));

        [Fact]
        public void ByIdReturnsNullForAnUnknownId() => Assert.Null(Achievements.ById("999999"));

        /// <summary>
        /// A round trip through the document must preserve every id, its progress and its ceiling -
        /// this is the path the phone actually reads.
        /// </summary>
        [Fact]
        public void SurvivesADocumentRoundTrip()
        {
            var snapshot = new ProgressSnapshot
            {
                Achievements = Achievements.All
                    .Select((a, i) => new AchievementProgress(a.Id, i, a.Goal) { Title = a.Title })
                    .ToList(),
                Statistics = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    [UsageStatistics.ElementsOpened] = 41,
                    [UsageStatistics.Calculations] = 7,
                    [UsageStatistics.Searches] = 3,
                },
            };

            var restored = ProgressDocument.FromDocument(
                ProgressDocument.ToDocument(snapshot, DateTimeOffset.UnixEpoch));

            Assert.Equal(
                snapshot.Achievements.OrderBy(a => a.Id, StringComparer.Ordinal),
                restored.Achievements.OrderBy(a => a.Id, StringComparer.Ordinal));

            Assert.Equal(41, restored.Statistics[UsageStatistics.ElementsOpened]);
            Assert.Equal(7, restored.Statistics[UsageStatistics.Calculations]);
            Assert.Equal(3, restored.Statistics[UsageStatistics.Searches]);
        }

        /// <summary>
        /// Android writes the achievement id as a <b>number</b>, not a string.
        /// </summary>
        /// <remarks>
        /// Reading only <c>stringValue</c> matched nothing the phone wrote, so every achievement it
        /// sent was discarded by the empty-id guard - and silently, because "no achievements this
        /// build recognises" looks exactly like "the user has earned none". Found by syncing a real
        /// account whose statistics arrived while its achievements stayed at zero.
        /// </remarks>
        [Fact]
        public void ReadsAnAchievementIdWrittenAsANumber()
        {
            var document = JsonNode.Parse("""
            {
              "fields": {
                "achievements": { "arrayValue": { "values": [
                  { "mapValue": { "fields": {
                      "id": { "integerValue": "3" },
                      "title": { "stringValue": "Chemical Professional Certified!" },
                      "progress": { "integerValue": "460" },
                      "maxProgress": { "integerValue": "1000" }
                  } } }
                ] } }
              }
            }
            """);

            var snapshot = ProgressDocument.FromDocument(document);

            var achievement = Assert.Single(snapshot.Achievements);
            Assert.Equal("3", achievement.Id);
            Assert.Equal(460, achievement.Progress);
            Assert.Equal(1000, achievement.MaxProgress);
            Assert.Equal("Chemical Professional Certified!", achievement.Title);
        }

        /// <summary>The id goes back out as a number, so the field's type does not flip-flop.</summary>
        [Fact]
        public void WritesAnAchievementIdAsANumber()
        {
            var snapshot = new ProgressSnapshot
            {
                Achievements = [new AchievementProgress("101002", 4, 10)],
            };

            var written = ProgressDocument.ToDocument(snapshot, DateTimeOffset.UnixEpoch);
            var entry = written["fields"]![ProgressDocument.Achievements]!["arrayValue"]!["values"]![0]!;

            Assert.NotNull(entry["mapValue"]!["fields"]!["id"]!["integerValue"]);
            Assert.Null(entry["mapValue"]!["fields"]!["id"]!["stringValue"]);
        }

        /// <summary>
        /// The document is co-owned, so both collections must be listed in the update mask or a
        /// Windows PATCH would delete whatever the phone had written there.
        /// </summary>
        [Fact]
        public void AchievementsAndStatisticsAreInTheUpdateMask()
        {
            Assert.Contains(ProgressDocument.Achievements, ProgressDocument.FieldPaths);
            Assert.Contains(ProgressDocument.Statistics, ProgressDocument.FieldPaths);
        }
    }
}
