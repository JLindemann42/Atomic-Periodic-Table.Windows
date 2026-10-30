using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Ai.Data
{
    /// <summary>
    /// Every shipped description is in its own language (roadmap 1.10). Mirrors Android's
    /// <c>ElementDescriptionLanguageTest</c>.
    ///
    /// <para>Narrative answers and the overview are cut from the description sentence by sentence,
    /// and an English sentence in another language's description is dropped or, with nothing else
    /// left, answered in English. Afrikaans, Filipino and Swedish were left part-English, and French,
    /// Hindi, Italian and Portuguese kept a few English rows; 277 descriptions in all. The rule is the
    /// shared data's <c>tools/element_descriptions.py</c>: four English function words make an English
    /// clause, not counting the ones that are words of the language itself.</para>
    /// </summary>
    public class ElementDescriptionLanguageTest
    {
        private static readonly Regex EnglishFunctionWords = new(
            @"\b(the|and|which|with|has|have|is|are|was|were|been|of|from|that|its|it|only|also|" +
            @"most|used|found|other|than|but|such|these|those|there|their|when|while|into|about)\b");

        /// <summary>Afrikaans writes "Goud is 'n chemiese element", "dit was" and "of" (or).</summary>
        private static readonly Dictionary<string, HashSet<string>> OwnWords = new()
        {
            ["af"] = ["is", "was", "of"],
        };

        private static Dictionary<string, string> Descriptions(string language)
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(TestAssets.AssetDirectory, $"elements_{language}.json")));
            return document.RootElement.EnumerateObject().ToDictionary(
                element => element.Name,
                element => element.Value.TryGetProperty("description", out var text) ? text.GetString() ?? "" : "");
        }

        [Fact]
        public void NoDescriptionIsPartEnglish()
        {
            var partEnglish = TestAssets.Languages.Where(language => language != "en").SelectMany(language =>
            {
                var own = OwnWords.GetValueOrDefault(language) ?? [];
                return Descriptions(language)
                    .Where(entry => EnglishFunctionWords.Matches(entry.Value.ToLowerInvariant())
                        .Count(match => !own.Contains(match.Value)) >= 4)
                    .Select(entry => $"{language}/{entry.Key}");
            }).ToList();
            Assert.Empty(partEnglish);
        }

        [Fact]
        public void TheSymbolIsNotTranslated()
        {
            // Nobelium's symbol is the English word "no", and three locales translated it.
            foreach (var language in TestAssets.Languages)
                Assert.Matches("(?<![A-Za-z])No(?![a-z])", Descriptions(language)["nobelium"]);
        }
    }
}
