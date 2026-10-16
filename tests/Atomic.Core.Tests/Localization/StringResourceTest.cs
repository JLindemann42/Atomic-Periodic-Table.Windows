using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Localization;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Localization
{
    public class StringResourceTest
    {
        private static readonly string[] Expected =
            ["af", "de", "en-US", "es", "es-AR", "es-MX", "fil", "fr", "hi", "it",
             "pt-BR", "sv", "ur", "ur-IN", "zh-Hans"];

        [Fact]
        public void AllLanguageResourcesArePresent() =>
            Assert.Equal(Expected, ReswStringProvider.AvailableLanguages());

        [Fact]
        public void EnglishHasEveryKeyInS()
        {
            // S.g.cs is generated from the English resources, so a key there with no value would
            // mean the generator and the writer disagreed.
            var english = new ReswStringProvider("en");
            var missing = KeysInS().Where(k => !english.HasOwn(k)).ToList();
            Assert.Empty(missing);
        }

        [Theory]
        [InlineData("sv")]
        [InlineData("de")]
        [InlineData("fr")]
        [InlineData("hi")]
        [InlineData("ur")]
        [InlineData("zh")]
        [InlineData("es")]
        public void TranslationsCoverAtLeastNinetyPercent(string language)
        {
            var english = new ReswStringProvider("en");
            var other = new ReswStringProvider(language);
            var keys = KeysInS().ToList();

            var covered = keys.Count(other.HasOwn);
            var ratio = (double)covered / keys.Count;

            Assert.True(ratio >= 0.90,
                $"{language} covers {covered}/{keys.Count} ({ratio:P1}) of English keys");
        }

        [Fact]
        public void MissingKeyIsVisibleRatherThanEmpty()
        {
            var strict = new ReswStringProvider("sv");
            Assert.Equal("str:no_such_key_anywhere", strict.Get("no_such_key_anywhere"));
        }

        [Fact]
        public void FallbackServesEnglishWhenAskedTo()
        {
            var english = new ReswStringProvider("en");
            var fallback = new ReswStringProvider("sv", fallBackToEnglish: true);

            // A key English has but Swedish does not; if Swedish is complete, this is vacuous.
            var key = KeysInS().FirstOrDefault(k => english.HasOwn(k) && !new ReswStringProvider("sv").HasOwn(k));
            if (key is null)
                return;

            Assert.Equal(english.Get(key), fallback.Get(key));
            Assert.StartsWith(ReswStringProvider.MissingPrefix, new ReswStringProvider("sv").Get(key));
        }

        [Fact]
        public void FormatSpecifiersSurvivedConversionUnchanged()
        {
            // The whole reason JavaFormat exists. If a conversion step ever "helpfully" rewrote
            // these to {0}, every formatted answer would come out with a literal %1$s or worse.
            var english = new ReswStringProvider("en");
            Assert.Equal("Uses of iron", english.Get("ai_suggest_element.0", "iron"));
            Assert.Equal("**Density**: high", english.Get("ai_aggregate_result", "Density", "high"));
        }

        [Fact]
        public void ArraysReadUntilAGap()
        {
            var facts = new ReswStringProvider("en").Array("ai_facts");
            Assert.NotEmpty(facts);
            Assert.All(facts, f => Assert.False(string.IsNullOrWhiteSpace(f)));
        }

        [Fact]
        public void PluralsAreAddressableBySuffix()
        {
            var english = new ReswStringProvider("en");
            Assert.Equal("Lost 1 life", english.Get("lives_lost_result.One", 1));
            Assert.Equal("Lost 3 lives", english.Get("lives_lost_result.Other", 3));
        }

        [Fact]
        public void EveryLanguageAgreesWithEnglishOnArgumentCount()
        {
            // A translation that drops or invents a placeholder produces either a missing value or
            // a stray "%2$s" in the user's sentence. Neither throws, so nothing else would catch it.
            //
            // The flag set deliberately excludes the space flag, and the conversion set is only the
            // letters actually used. Java would read "+0% bonus active" as carrying a %b
            // conversion - percent, space flag, 'b' - but that string is never formatted with
            // arguments, and the twelve translations escape the literal percent inconsistently
            // (Chinese writes %%, Hindi follows it with a Devanagari letter). Counting those as
            // placeholders reports six mismatches that are all an artifact of the regex.
            var spec = new Regex(@"%(?:(\d+)\$)?[-#+0,(]*\d*(?:\.\d+)?([sdfeExX%])", RegexOptions.Compiled);
            var english = new ReswStringProvider("en");
            var problems = new List<string>();

            foreach (var language in new[] { "sv", "de", "fr", "hi", "ur", "zh", "es", "it", "pt", "af", "fil" })
            {
                var other = new ReswStringProvider(language);
                foreach (var key in KeysInS())
                {
                    if (!other.HasOwn(key)) continue;
                    var en = ArgCount(spec, english.Get(key));
                    var tr = ArgCount(spec, other.Get(key));
                    if (en != tr)
                        problems.Add($"{language}/{key}: en expects {en}, translation uses {tr}");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} placeholder mismatches:\n  " + string.Join("\n  ", problems.Take(15)));
        }

        private static int ArgCount(Regex spec, string template)
        {
            var max = 0;
            var implicitCount = 0;
            foreach (Match m in spec.Matches(template))
            {
                if (m.Groups[2].Value == "%") continue;
                if (m.Groups[1].Success)
                    max = Math.Max(max, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                else
                    implicitCount++;
            }
            return Math.Max(max, implicitCount);
        }

        private static IEnumerable<string> KeysInS() =>
            typeof(S).GetFields()
                .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!);
    }
}
