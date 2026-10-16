using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Atomic.Core.Tests.Fixtures;
using Xunit;

namespace Atomic.Core.Tests.Localization
{
    /// <summary>
    /// Every <c>{loc:Str Key=...}</c> in the page markup must name a real resource.
    ///
    /// <para>The markup extension resolves at load time and a missing key produces the
    /// <c>str:</c> sentinel - a label reading "str:densty_colon" that nothing else in the build
    /// would flag. The keys are raw strings in XAML rather than <c>S</c> constants, so the compiler
    /// cannot help; this is what stands in for it.</para>
    /// </summary>
    public sealed class LocStrKeyTest
    {
        private static readonly Regex Usage =
            new(@"\{loc:Str\s+Key=([A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);

        private static string Root => Path.Combine(AppContext.BaseDirectory, "pagexaml");

        [Fact]
        public void EveryKeyUsedInMarkupExists()
        {
            Assert.True(Directory.Exists(Root), $"page markup not copied to {Root}");

            var english = new ReswStringProvider("en");
            var missing = new List<string>();
            var used = 0;

            foreach (var page in Directory.EnumerateFiles(Root, "*.xaml", SearchOption.AllDirectories))
            {
                foreach (Match match in Usage.Matches(File.ReadAllText(page)))
                {
                    used++;
                    var key = match.Groups[1].Value;
                    if (!english.HasOwn(key)) missing.Add($"{Path.GetFileName(page)}: {key}");
                }
            }

            // A guard against the glob silently matching nothing, which would make this vacuous.
            Assert.True(used > 50, $"only {used} loc:Str usages found - is the csproj glob right?");
            Assert.Empty(missing);
        }
    }
}
