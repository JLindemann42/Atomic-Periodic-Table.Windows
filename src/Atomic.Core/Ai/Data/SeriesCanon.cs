using System;
using System.Diagnostics;
using System.Text;
using Atomic.Core.Localization;

namespace Atomic.Core.Ai.Data
{
    /// <summary>The element series (chemical family), as a language-invariant key.</summary>
    public enum SeriesId
    {
        AlkaliMetal,
        AlkalineEarthMetal,
        TransitionMetal,
        PostTransitionMetal,
        Metalloid,
        ReactiveNonmetal,
        OtherNonmetal,
        NobleGas,
        Halogen,
        Lanthanoid,
        Actinide,
        Unknown,
    }

    /// <summary>The s/p/d/f block an element sits in.</summary>
    public enum Block { S, P, D, F, Unknown }

    public static class SeriesIdExtensions
    {
        /// <summary>True for the series that are metals, used by "which metals…" style filters.</summary>
        public static bool IsMetal(this SeriesId id) => id
            is SeriesId.AlkaliMetal or SeriesId.AlkalineEarthMetal or SeriesId.TransitionMetal
            or SeriesId.PostTransitionMetal or SeriesId.Lanthanoid or SeriesId.Actinide;

        /// <summary>True for the series that are nonmetals.</summary>
        public static bool IsNonmetal(this SeriesId id) => id
            is SeriesId.ReactiveNonmetal or SeriesId.OtherNonmetal
            or SeriesId.NobleGas or SeriesId.Halogen;

        /// <summary>
        /// Resource key for the display label. Post-transition metals deliberately share the
        /// transition-metal label, and both nonmetal series share one - that is how the Android
        /// strings are authored, and diverging here would produce labels no translation covers.
        /// </summary>
        public static string LabelKey(this SeriesId id) => id switch
        {
            SeriesId.AlkaliMetal => S.ai_series_alkali_metal_label,
            SeriesId.AlkalineEarthMetal => S.ai_series_alkaline_earth_label,
            SeriesId.TransitionMetal => S.ai_series_transition_metal_label,
            SeriesId.PostTransitionMetal => S.ai_series_transition_metal_label,
            SeriesId.Metalloid => S.ai_series_metalloid_label,
            SeriesId.ReactiveNonmetal or SeriesId.OtherNonmetal => S.ai_series_nonmetal_label,
            SeriesId.NobleGas => S.ai_series_noble_gas_label,
            SeriesId.Halogen => S.ai_series_halogen_label,
            SeriesId.Lanthanoid => S.ai_series_lanthanide_label,
            SeriesId.Actinide => S.ai_series_actinide_label,
            SeriesId.Unknown => S.ai_series_nonmetal_label,
            _ => throw new UnreachableException($"unhandled series {id}"),
        };
    }

    /// <summary>
    /// Canonicalises the element data's free-text classification strings.
    ///
    /// <para><c>element_group</c> in the element table uses 15 distinct spellings for 11 concepts -
    /// both "Post-Transition Metals" and "Post-transition Metals", both "Other Nonmetal" and
    /// "Other Nonmetals", plus "Alkaline earth metal" and "Lanthanoids". The legacy agent matched
    /// these with <c>group.contains("Lanthanide")</c>, which never matches "Lanthanoids", so series
    /// filtering was partly broken. Canonicalising once here fixes that for every caller.</para>
    ///
    /// <para>Classification is language-invariant, so this always runs against the English table.</para>
    /// </summary>
    public static class SeriesCanon
    {
        /// <summary>
        /// Canonical series for an <c>element_group</c> string. Never returns null; unmatched maps
        /// to <see cref="SeriesId.Unknown"/>.
        /// </summary>
        public static SeriesId Series(string? rawGroup)
        {
            if (rawGroup is null) return SeriesId.Unknown;
            var key = Normalise(rawGroup);

            // Order matters throughout: "alkalineearth" must be tested before "alkali", and
            // "posttransition" before "transition", or the shorter match swallows the longer one.
            if (key.Length == 0) return SeriesId.Unknown;
            if (Has(key, "alkalineearth") || Has(key, "alkaliearth")) return SeriesId.AlkalineEarthMetal;
            if (Has(key, "alkali")) return SeriesId.AlkaliMetal;
            if (Has(key, "posttransition")) return SeriesId.PostTransitionMetal;
            if (Has(key, "transition")) return SeriesId.TransitionMetal;
            if (Has(key, "metalloid") || Has(key, "semimetal")) return SeriesId.Metalloid;
            if (Has(key, "noblegas") || Has(key, "inertgas")) return SeriesId.NobleGas;
            if (Has(key, "halogen")) return SeriesId.Halogen;
            // Lanthanoid / Lanthanide / Lanthanoids all collapse here.
            if (Starts(key, "lanthan")) return SeriesId.Lanthanoid;
            if (Starts(key, "actin")) return SeriesId.Actinide;
            if (Has(key, "reactivenonmetal")) return SeriesId.ReactiveNonmetal;
            if (Has(key, "othernonmetal")) return SeriesId.OtherNonmetal;
            if (Has(key, "nonmetal")) return SeriesId.OtherNonmetal;
            return SeriesId.Unknown;
        }

        /// <summary>Canonical block for an <c>element_block</c> string such as "d - block".</summary>
        public static Block Block(string? rawBlock)
        {
            if (rawBlock is null) return Data.Block.Unknown;
            var key = Normalise(rawBlock);
            if (Starts(key, "s")) return Data.Block.S;
            if (Starts(key, "p")) return Data.Block.P;
            if (Starts(key, "d")) return Data.Block.D;
            if (Starts(key, "f")) return Data.Block.F;
            return Data.Block.Unknown;
        }

        /// <summary>Canonical phase key for <c>element_phase</c>, matched against the English table.</summary>
        public static string? Phase(string? rawPhase)
        {
            if (rawPhase is null) return null;
            return Normalise(rawPhase) switch
            {
                "solid" => "solid",
                "liquid" => "liquid",
                "gas" => "gas",
                _ => null,
            };
        }

        /// <summary>
        /// <c>radioactive</c> is authored as "Yes"/"No" in every language, so it is safe to read
        /// directly.
        /// </summary>
        public static bool Radioactive(string? raw) =>
            raw is not null && Starts(Normalise(raw), "y");

        /// <summary>
        /// Lowercase, drop diacritics, strip everything that is not a letter or digit.
        /// NFD splits accents into combining marks, which are not letters or digits and so fall
        /// away.
        /// </summary>
        private static string Normalise(string raw)
        {
            var decomposed = raw.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            return sb.ToString();
        }

        private static bool Has(string haystack, string needle) =>
            haystack.Contains(needle, StringComparison.Ordinal);

        private static bool Starts(string haystack, string prefix) =>
            haystack.StartsWith(prefix, StringComparison.Ordinal);
    }
}
