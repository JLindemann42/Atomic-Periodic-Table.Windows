using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Ai
{
    /// <summary>
    /// The screens a chat can be opened from (roadmap 2.9), as the ids <c>datasets/screen_chips.tsv</c>
    /// uses. The element page and the periodic table are not here: an element page focuses its element,
    /// and the table has the general chips. Mirrors Android's <c>Screens</c>.
    /// </summary>
    public static class Screens
    {
        public const string Nuclides = "nuclides";
        public const string Emission = "emission";
        public const string Constants = "constants";
        public const string Dictionary = "dictionary";
        public const string Equations = "equations";
        public const string Poisson = "poisson";
        public const string Geology = "geology";
        public const string Solubility = "solubility";
        public const string Ions = "ions";
        public const string Electrode = "electrode";
        public const string Ph = "ph";
        public const string Alloys = "alloys";
        public const string MolarMass = "molar_mass";
        public const string UnitConverter = "unit_converter";
        public const string IdealGas = "ideal_gas";
        public const string Balancer = "balancer";
        public const string Games = "games";
    }

    /// <summary>
    /// The questions a chat opened from a screen offers first (roadmap 2.9): from the nuclide chart,
    /// isotope questions; from the ideal gas calculator, gas-law ones. Mirrors Android's
    /// <c>ScreenChips</c>.
    ///
    /// <para>Shared data, so both apps offer the same chips. <c>ScreenChipsTest</c> asks every one in
    /// every language and fails on any the assistant would decline. A language without a row for a
    /// screen gets the general chips instead; there is no English fallback, since a chip in another
    /// language than the chat is worse than none.</para>
    /// </summary>
    public sealed class ScreenChips
    {
        private readonly IReadOnlyDictionary<(string Screen, string Language), List<string>> _chips;

        private ScreenChips(IReadOnlyDictionary<(string, string), List<string>> chips) => _chips = chips;

        public static readonly ScreenChips Empty = new(new Dictionary<(string, string), List<string>>());

        /// <summary>The chips for <paramref name="screen"/> in <paramref name="language"/>, in file order; empty when there are none.</summary>
        public IReadOnlyList<string> ForScreen(string screen, string language) =>
            _chips.TryGetValue((screen, language), out var chips) ? chips : [];

        /// <summary>Every screen and language that has chips, for the test.</summary>
        public IEnumerable<(string Screen, string Language)> Keys => _chips.Keys;

        /// <summary>Rows of <c>screen</c>, <c>lang</c>, <c>chip</c>; <c>#</c> starts a comment and the first other row is the header.</summary>
        public static ScreenChips Parse(string? text)
        {
            if (text is null) return Empty;
            var chips = new Dictionary<(string, string), List<string>>();
            var rows = text.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
                .Skip(1);
            foreach (var row in rows)
            {
                var cells = row.Split('\t');
                if (cells.Length < 3 || string.IsNullOrWhiteSpace(cells[2])) continue;
                var key = (cells[0], cells[1]);
                if (!chips.TryGetValue(key, out var list)) chips[key] = list = [];
                list.Add(cells[2].Trim());
            }
            return new ScreenChips(chips);
        }
    }
}
