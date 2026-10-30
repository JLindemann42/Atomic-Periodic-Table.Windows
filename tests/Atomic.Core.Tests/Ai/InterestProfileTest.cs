using System.Collections.Generic;
using Atomic.Core.Ai;
using Xunit;

namespace Atomic.Core.Tests.Ai
{
    /// <summary>
    /// Which element the assistant remembers a user by.
    /// </summary>
    public class InterestProfileTest
    {
        [Fact]
        public void AFirstTimeUserHasNoFavourite() => Assert.Null(new InterestProfile().Favourite);

        [Fact]
        public void TheMostAskedAboutElementWins()
        {
            var profile = new InterestProfile();
            profile.Track("gold");
            profile.Track("iron");
            profile.Track("gold");

            Assert.Equal("gold", profile.Favourite);
        }

        /// <summary>Keys arrive in whatever case the caller has; the count must not split.</summary>
        [Fact]
        public void CaseAndSpaceDoNotSplitTheCount()
        {
            var profile = new InterestProfile();
            profile.Track("Gold");
            profile.Track("  gold ");
            profile.Track("GOLD");

            Assert.Equal(1, profile.Elements.Count);
            Assert.Equal(3, profile.Elements["gold"]);
        }

        /// <summary>
        /// A tie must not change the greeting between launches, which Android's maxByOrNull allows.
        /// </summary>
        [Fact]
        public void TiesAreBrokenTheSameWayEveryTime()
        {
            var first = new InterestProfile();
            first.Track("zinc");
            first.Track("argon");

            var second = new InterestProfile();
            second.Track("argon");
            second.Track("zinc");

            Assert.Equal(first.Favourite, second.Favourite);
            Assert.Equal("argon", first.Favourite);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NothingIsTrackedForAnEmptyKey(string? key)
        {
            var profile = new InterestProfile();
            profile.Track(key);

            Assert.Empty(profile.Elements);
            Assert.Null(profile.Favourite);
        }

        [Fact]
        public void ASavedProfileComesBack()
        {
            var profile = new InterestProfile();
            profile.Load(new Dictionary<string, int> { ["helium"] = 4, ["neon"] = 1 });

            Assert.Equal("helium", profile.Favourite);

            profile.Track("neon");
            profile.Track("neon");
            profile.Track("neon");
            profile.Track("neon");
            Assert.Equal("neon", profile.Favourite);
        }

        /// <summary>Loading replaces rather than merges, so a restore is not a doubling.</summary>
        [Fact]
        public void LoadingReplacesWhatWasThere()
        {
            var profile = new InterestProfile();
            profile.Track("gold");
            profile.Load(new Dictionary<string, int> { ["iron"] = 2 });

            Assert.Equal(1, profile.Elements.Count);
            Assert.Equal("iron", profile.Favourite);
        }

        /// <summary>A corrupt file must not produce a favourite with a zero count.</summary>
        [Fact]
        public void NonPositiveCountsAreDropped()
        {
            var profile = new InterestProfile();
            profile.Load(new Dictionary<string, int> { ["iron"] = 0, ["gold"] = -3 });

            Assert.Empty(profile.Elements);
        }

        [Fact]
        public void ClearForgetsEverything()
        {
            var profile = new InterestProfile();
            profile.Track("gold");
            profile.Clear();

            Assert.Null(profile.Favourite);
        }
    }
}