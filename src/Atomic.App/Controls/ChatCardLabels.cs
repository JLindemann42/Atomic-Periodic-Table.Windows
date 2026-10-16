using Atomic.Core.Ai.Cards;
using Atomic.Core.Localization;

namespace Atomic.App.Controls
{
    /// <summary>
    /// What each card kind is called, in the user's language.
    ///
    /// <para>Reuses existing translated keys rather than adding vocabulary - every one of these
    /// already names the same thing somewhere else in the app.</para>
    /// </summary>
    public static class ChatCardLabels
    {
        public static string For(ChatCardKind kind, IStringProvider strings) =>
            WithoutTrailingColon(strings.Get(KeyFor(kind)));

        private static string KeyFor(ChatCardKind kind) => kind switch
        {
            ChatCardKind.ElectronShell => S.electron_shell_colon,
            ChatCardKind.CrystalStructure => S.crystal_structure,
            ChatCardKind.EmissionSpectrum => S.emission_title,
            ChatCardKind.PoissonBand => S.poissons_ratio,
            ChatCardKind.NfpaDiamond => S.hazard_diamond,
            ChatCardKind.IonizationSeries => S.table_ionization,
            ChatCardKind.IsotopeDecay => S.isotopes_title,
            ChatCardKind.Abundance => S.abundance_title,
            _ => S.ask_atomic,
        };

        /// <summary>
        /// Several of these keys are authored for a "Label: value" row, so they carry the colon.
        /// Used standalone as a caption it dangles. The fullwidth form is trimmed too, because that
        /// is the one Chinese uses.
        /// </summary>
        private static string WithoutTrailingColon(string label) => label.TrimEnd(':', '：', ' ');
    }
}
