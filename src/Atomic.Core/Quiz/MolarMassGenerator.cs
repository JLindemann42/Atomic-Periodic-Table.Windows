using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Atomic.Core.Ai.Data;
using Atomic.Core.Localization;

namespace Atomic.Core.Quiz
{
    /// <summary>
    /// Formulas for the molar-mass game.
    ///
    /// <para>The app ships no compound dataset - every non-element table in <c>Models/</c> is
    /// elements, minerals, constants or prose - so this list is authored here. Chemical formulas are
    /// language-neutral, so unlike those tables it costs nothing to localize.</para>
    /// </summary>
    public static class Compounds
    {
        public static readonly IReadOnlyList<string> Formulas =
        [
            // Water, acids and bases
            "H2O", "H2O2", "HCl", "HBr", "HF", "HNO3", "HNO2", "H2SO4", "H2SO3", "H3PO4",
            "H2CO3", "H2S", "NaOH", "KOH", "LiOH", "Ca(OH)2", "Mg(OH)2", "Ba(OH)2", "NH3", "Al(OH)3",
            // Halides and simple salts
            "NaCl", "KCl", "LiCl", "CaCl2", "MgCl2", "AlCl3", "FeCl2", "FeCl3", "CuCl2", "ZnCl2",
            "NaF", "CaF2", "KI", "NaBr", "AgCl", "NH4Cl",
            // Sulfates, carbonates, nitrates, phosphates
            "CuSO4", "ZnSO4", "MgSO4", "FeSO4", "Na2SO4", "K2SO4", "CaSO4", "BaSO4",
            "Al2(SO4)3", "Fe2(SO4)3",
            // (NH4)2SO4 is omitted: ChemistryMath's strict parse rejects a formula that opens with
            // a group.
            "CaCO3", "Na2CO3", "K2CO3", "MgCO3", "NaHCO3", "Li2CO3",
            "NaNO3", "KNO3", "AgNO3", "Ca(NO3)2", "Cu(NO3)2", "NH4NO3",
            "Ca3(PO4)2", "Na3PO4", "K3PO4",
            // Oxidisers and other notable salts
            "KMnO4", "K2Cr2O7", "K2CrO4", "NaClO", "NaClO3", "KClO3", "Na2S2O3",
            // Oxides
            "CO", "CO2", "SO2", "SO3", "NO", "NO2", "N2O", "Fe2O3", "Fe3O4", "Al2O3",
            "CaO", "MgO", "ZnO", "CuO", "TiO2", "SiO2", "MnO2", "PbO2", "P4O10", "Na2O",
            // Organics
            "CH4", "C2H6", "C3H8", "C4H10", "C2H4", "C2H2", "C6H6", "C7H8",
            "CH3OH", "C2H5OH", "C3H7OH", "CH3COOH", "C6H12O6", "C12H22O11", "CO(NH2)2",
            // Miscellaneous
            "SiC", "PH3", "N2H4", "SF6", "CCl4", "CHCl3",
        ];
    }

    /// <summary>"What is the molar mass of Fe2(SO4)3?"</summary>
    public sealed class MolarMassGenerator : IQuestionGenerator
    {
        /// <summary>
        /// How far a distractor's mass may sit from the correct one, as a multiple.
        ///
        /// <para>Distractors are other real compounds' masses in the same neighbourhood, so a wrong
        /// answer is never obviously wrong by magnitude alone.</para>
        /// </summary>
        private const double NeighbourhoodLow = 0.4;

        private const double NeighbourhoodHigh = 2.5;

        public MolarMassGenerator(string categoryKey, int baseXp)
        {
            CategoryKey = categoryKey;
            BaseXp = baseXp;
        }

        public string CategoryKey { get; }

        public int BaseXp { get; }

        public IReadOnlyList<QuizQuestion> Generate(GeneratorContext context, int count)
        {
            var masses = Compounds.Formulas
                .Select(formula => (
                    Formula: formula,
                    Result: ChemistryMath.ParseFormula(
                        formula,
                        symbol => context.Store.BySymbol(symbol)?.Quantity("atomic_mass")?.Value)))
                .Where(p => p.Result is not null)
                .Select(p => (p.Formula, Mass: p.Result!.MolarMass))
                .ToList();

            if (masses.Count < 4) return [];

            var questions = new List<QuizQuestion>(count);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var attempts = count * 8;

            while (questions.Count < count && attempts-- > 0)
            {
                var (formula, mass) = masses.Pick(context.Random);
                if (!used.Add(formula)) continue;

                var correct = Format(context, mass);

                var wrongs = masses
                    .Where(p => !string.Equals(p.Formula, formula, StringComparison.Ordinal) &&
                                p.Mass >= mass * NeighbourhoodLow &&
                                p.Mass <= mass * NeighbourhoodHigh)
                    .Select(p => Format(context, p.Mass))
                    .Where(t => !string.Equals(t, correct, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .Shuffled(context.Random)
                    .Take(3)
                    .ToList();
                if (wrongs.Count < 3) continue;

                questions.Add(new QuizQuestion(
                    Question: context.String(S.question_molar_mass, formula),
                    CorrectAnswer: correct,
                    Alternatives: wrongs.Append(correct).Shuffled(context.Random),
                    BaseXp: BaseXp));
            }

            return questions;
        }

        /// <summary>
        /// Two decimals, invariant.
        /// </summary>
        /// <remarks>
        /// Android formats with the default locale, so a Swedish player sees "55,85". Invariant here
        /// because Atomic.Core must never read the ambient culture - that rule is compiler-enforced
        /// (CA1305) and exists so a chat answer's numbers do not change with the UI language. The
        /// consequence is cosmetic and self-consistent: the correct answer and every distractor go
        /// through this same function, so the answer stays selectable either way.
        /// </remarks>
        private static string Format(GeneratorContext context, double mass) =>
            context.String(S.molar_mass_value, mass.ToString("0.00", CultureInfo.InvariantCulture));
    }
}
