using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Atomic.Core.Ai.Retrieval;
using Atomic.Core.Text;
using Xunit;

namespace Atomic.Core.Tests.Ai.Retrieval
{
    /// <summary>
    /// Asserts the ported tokenizer reproduces the JVM's output exactly.
    ///
    /// <para>This is the tripwire for the engine port. <see cref="UnicodeScripts"/> and
    /// <see cref="JavaRegex"/> are the two places where a mistake compiles, runs, throws nothing,
    /// and quietly builds a different index from the one Android ships - so recall drops and
    /// nothing fails. The fixtures are dumped from the JVM by
    /// <c>tools/port/DumpTokenizerGolden.java</c>, run on the same JDK Gradle uses for the Android
    /// unit tests.</para>
    ///
    /// <para>If one of these fails, the fix is almost never to regenerate the golden file.</para>
    /// </summary>
    public class TokenizerGoldenTest
    {
        private static string GoldenDir => Path.Combine(AppContext.BaseDirectory, "golden");

        public static TheoryData<string, string, string[]> Cases()
        {
            var data = new TheoryData<string, string, string[]>();
            foreach (var line in File.ReadLines(Path.Combine(GoldenDir, "tokens.tsv")))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var fields = line.Split('\t');
                data.Add(fields[0], Unescape(fields[1]), fields.Skip(2).ToArray());
            }
            return data;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ProducesTheSameTokensAsTheJvm(string language, string input, string[] expected)
        {
            var actual = Tokenizer.Tokenize(input, language);
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// Per-character script agreement. A failure here names the exact code point, which turns
        /// a puzzling token mismatch into a one-line fix in the generated range table.
        /// </summary>
        [Fact]
        public void ScriptAndCategoryAgreeWithTheJvmForEveryCharacterInTheFixture()
        {
            var mismatches = new List<string>();

            foreach (var line in File.ReadLines(Path.Combine(GoldenDir, "script_probe.tsv")))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split('\t');
                var c = (char)int.Parse(f[0][2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                var script = UnicodeScripts.Of(c).ToString().ToUpperInvariant();
                var expectedScript = f[1].Replace("_", "", StringComparison.Ordinal);
                if (script != expectedScript)
                    mismatches.Add($"{f[0]}: script {script}, JVM says {expectedScript}");

                var isLetterOrDigit = char.IsLetterOrDigit(c);
                if (isLetterOrDigit != bool.Parse(f[2]))
                    mismatches.Add($"{f[0]}: IsLetterOrDigit {isLetterOrDigit}, JVM says {f[2]}");
            }

            Assert.True(mismatches.Count == 0,
                $"{mismatches.Count} character(s) classified differently:\n  " +
                string.Join("\n  ", mismatches.Take(20)));
        }

        [Fact]
        public void FixtureIsActuallyLoaded()
        {
            // A silently empty golden file would make every Theory above vacuously pass.
            Assert.True(Cases().Count > 30, "golden fixture missing or truncated");
        }

        private static string Unescape(string s) => s
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
