using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Atomic.Core.Ai.Data
{
    /// <summary>
    /// Converts between the units the element data actually uses, so a question like "melting point
    /// of gold in Fahrenheit" can be answered from whichever scale is stored.
    ///
    /// <para>Most dimensions are multiplicative and convert through a base unit. Temperature is
    /// affine and is handled separately - that distinction is why a single factor table is not
    /// enough.</para>
    /// </summary>
    public static class UnitConverter
    {
        /// <summary>Aliases mapped onto a canonical spelling, so (g/cm^3), g/cm3 and g/cm³ all agree.</summary>
        private static readonly FrozenDictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["k"] = "K", ["kelvin"] = "K", ["kelvins"] = "K",
            ["c"] = "°C", ["°c"] = "°C", ["celsius"] = "°C", ["centigrade"] = "°C", ["degc"] = "°C",
            ["f"] = "°F", ["°f"] = "°F", ["fahrenheit"] = "°F", ["degf"] = "°F",
            ["g/cm3"] = "g/cm³", ["g/cm^3"] = "g/cm³", ["g/cm³"] = "g/cm³",
            ["kg/m3"] = "kg/m³", ["kg/m^3"] = "kg/m³", ["kg/m³"] = "kg/m³",
            ["u"] = "u", ["amu"] = "u", ["da"] = "u", ["dalton"] = "u",
            ["pm"] = "pm", ["picometer"] = "pm", ["picometre"] = "pm",
            ["nm"] = "nm", ["angstrom"] = "Å", ["å"] = "Å", ["a"] = "Å",
            ["ev"] = "eV", ["kj/mol"] = "kJ/mol", ["j/mol"] = "J/mol",
            ["gpa"] = "GPa", ["mpa"] = "MPa", ["pa"] = "Pa", ["kpa"] = "kPa",
            ["m/s"] = "m/s", ["km/h"] = "km/h",
            ["nωm"] = "nΩm", ["nohmm"] = "nΩm", ["μωm"] = "µΩm", ["µωm"] = "µΩm", ["ωm"] = "Ωm",
            ["cm3/mol"] = "cm³/mol", ["cm^3/mol"] = "cm³/mol", ["cm³/mol"] = "cm³/mol",
            ["mg/kg"] = "mg/kg", ["ppm"] = "mg/kg", ["%"] = "%",

            // Sea-water abundance. NFKC folds U+00B5 MICRO SIGN onto U+03BC GREEK SMALL MU before
            // this lookup runs, so the Greek key is the one that fires; the ASCII spelling is here
            // for typed queries. No Factors entry: mass per volume does not convert to mass per
            // mass without a density.
            ["μg/l"] = "µg/l", ["ug/l"] = "µg/l",

            ["b"] = "b", ["barn"] = "b", ["barns"] = "b",
            ["w/(m·k)"] = "W/(m·K)", ["w/(m*k)"] = "W/(m·K)", ["w/mk"] = "W/(m·K)",
            ["j/(g·k)"] = "J/(g·K)", ["j/(g*k)"] = "J/(g·K)",
            ["j/(mol·k)"] = "J/(mol·K)", ["j/(mol*k)"] = "J/(mol·K)",

            // Time. The canonical year token is "yr", not "a": "a" is already angstrom above, and a
            // query reading "1.5 a" means 1.5 Å in this app's data far more often than 1.5 years.
            // For the same reason ka/Ma/Ga are absent - they would have to share the collision.
            ["s"] = "s", ["sec"] = "s", ["second"] = "s", ["seconds"] = "s", ["sekund"] = "s",
            ["sekunder"] = "s", ["sekunde"] = "s", ["sekunden"] = "s", ["segundo"] = "s",
            ["segundos"] = "s", ["seconde"] = "s", ["secondes"] = "s", ["secondi"] = "s",
            ["ms"] = "ms", ["millisecond"] = "ms", ["milliseconds"] = "ms",
            ["us"] = "µs", ["microsecond"] = "µs", ["microseconds"] = "µs",
            ["ns"] = "ns", ["nanosecond"] = "ns", ["nanoseconds"] = "ns",
            ["min"] = "min", ["minute"] = "min", ["minutes"] = "min", ["minuter"] = "min",
            ["minuten"] = "min", ["minuto"] = "min", ["minutos"] = "min", ["minuti"] = "min",
            ["h"] = "h", ["hr"] = "h", ["hrs"] = "h", ["hour"] = "h", ["hours"] = "h",
            ["timme"] = "h", ["timmar"] = "h", ["stunde"] = "h", ["stunden"] = "h",
            ["hora"] = "h", ["horas"] = "h", ["heure"] = "h", ["heures"] = "h",
            ["ora"] = "h", ["ore"] = "h",
            ["d"] = "d", ["day"] = "d", ["days"] = "d", ["dag"] = "d", ["dagar"] = "d",
            ["tag"] = "d", ["tage"] = "d", ["dia"] = "d", ["dias"] = "d", ["jour"] = "d",
            ["jours"] = "d", ["giorno"] = "d", ["giorni"] = "d",

            // No bare "y" and no bare "an": both are ordinary words. Spanish "y" is "and", so
            // "compara uranio-235 y uranio-238" read as "235 years" and became a decay calculation.
            ["yr"] = "yr", ["yrs"] = "yr", ["year"] = "yr", ["years"] = "yr",

            // Accented and inflected forms as authored. Canonical applies NFKC, which leaves å and
            // ñ as single code points, so a folded spelling never matches what the user types.
            ["ar"] = "yr", ["år"] = "yr", ["jahr"] = "yr", ["jahre"] = "yr", ["jahren"] = "yr",
            ["ano"] = "yr", ["anos"] = "yr",
            ["año"] = "yr", ["años"] = "yr", ["ans"] = "yr", ["annee"] = "yr",
            ["annees"] = "yr", ["anno"] = "yr", ["anni"] = "yr", ["taon"] = "yr",
            ["kyr"] = "kyr", ["myr"] = "Myr", ["gyr"] = "Gyr",

            // Volume, base litre.
            ["l"] = "L", ["litre"] = "L", ["litres"] = "L", ["liter"] = "L", ["liters"] = "L",
            ["ml"] = "mL", ["millilitre"] = "mL", ["millilitres"] = "mL", ["milliliter"] = "mL",
            ["milliliters"] = "mL", ["cc"] = "cm³",
            ["ul"] = "µL", ["microlitre"] = "µL", ["microliter"] = "µL",
            ["dm3"] = "dm³", ["dm^3"] = "dm³", ["dm³"] = "dm³",
            ["cm3"] = "cm³", ["cm^3"] = "cm³", ["cm³"] = "cm³",
            ["m3"] = "m³", ["m^3"] = "m³", ["m³"] = "m³",

            // Amount of substance.
            ["mole"] = "mol", ["moles"] = "mol", ["moli"] = "mol", ["mols"] = "mol",
            ["mmol"] = "mmol", ["millimole"] = "mmol", ["millimoles"] = "mmol",
            ["umol"] = "µmol", ["micromole"] = "µmol", ["micromoles"] = "µmol",

            // Molar concentration. "mm" is deliberately absent - it is millimetre to every reader,
            // and mapping it to millimolar would make "0.5 mm" a concentration without warning.
            ["molar"] = "M", ["mol/l"] = "M", ["mol/liter"] = "M", ["mol/litre"] = "M",
            ["moles per litre"] = "M", ["moles per liter"] = "M", ["mol/dm3"] = "mol/dm³",
            ["mol/m3"] = "mol/m³", ["millimolar"] = "mM", ["micromolar"] = "µM",
        }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>
        /// Units whose meaning depends on capitalisation.
        ///
        /// <para><see cref="Canonical"/> lowercases before consulting the aliases, which is right
        /// for "Fahrenheit" and "GPa" and fatal for these: M is molar and m is metre, and case is
        /// the only thing that tells them apart. Checked before the fold, so the pair
        /// survives.</para>
        /// </summary>
        private static readonly FrozenDictionary<string, string> CaseSensitive = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["M"] = "M", ["mM"] = "mM", ["µM"] = "µM", ["μM"] = "µM", ["m"] = "m", ["L"] = "L",
        }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>Multiplicative units: canonical name to dimension and factor against the base unit.</summary>
        private static readonly FrozenDictionary<string, (Dimension Dimension, double Factor)> Factors =
            new Dictionary<string, (Dimension, double)>(StringComparer.Ordinal)
            {
                // Length, base metre
                ["pm"] = (Dimension.Length, 1e-12),
                ["Å"] = (Dimension.Length, 1e-10),
                ["nm"] = (Dimension.Length, 1e-9),
                ["m"] = (Dimension.Length, 1.0),
                // Density, base kg/m³
                ["g/cm³"] = (Dimension.Density, 1000.0),
                ["kg/m³"] = (Dimension.Density, 1.0),
                // Mass, base u
                ["u"] = (Dimension.Mass, 1.0),
                // Energy, base eV
                ["eV"] = (Dimension.Energy, 1.0),
                // Energy per mole, base kJ/mol
                ["kJ/mol"] = (Dimension.EnergyPerMol, 1.0),
                ["J/mol"] = (Dimension.EnergyPerMol, 0.001),
                // Pressure, base Pa
                ["Pa"] = (Dimension.Pressure, 1.0),
                ["kPa"] = (Dimension.Pressure, 1e3),
                ["MPa"] = (Dimension.Pressure, 1e6),
                ["GPa"] = (Dimension.Pressure, 1e9),
                // Velocity, base m/s
                ["m/s"] = (Dimension.Velocity, 1.0),
                ["km/h"] = (Dimension.Velocity, 1.0 / 3.6),
                // Resistivity, base Ωm
                ["nΩm"] = (Dimension.Resistivity, 1e-9),
                ["µΩm"] = (Dimension.Resistivity, 1e-6),
                ["Ωm"] = (Dimension.Resistivity, 1.0),
                // Molar volume, base cm³/mol
                ["cm³/mol"] = (Dimension.VolumePerMol, 1.0),
                // Concentration, base mg/kg
                ["mg/kg"] = (Dimension.Concentration, 1.0),
                ["%"] = (Dimension.Concentration, 10_000.0),
                // Area, base barn
                ["b"] = (Dimension.Area, 1.0),
                // Conductivity and heat capacity have one unit each in this data
                ["W/(m·K)"] = (Dimension.Conductivity, 1.0),
                ["J/(g·K)"] = (Dimension.HeatCapacity, 1.0),
                ["J/(mol·K)"] = (Dimension.HeatCapacity, 1.0),
                // Time, base second. The year is the Julian year, 365.25 days, matching what
                // IsotopeParser uses to normalise half-lives - the two have to agree or a decay
                // answer contradicts the isotope table it cites.
                ["s"] = (Dimension.Time, 1.0),
                ["ms"] = (Dimension.Time, 1e-3),
                ["µs"] = (Dimension.Time, 1e-6),
                ["ns"] = (Dimension.Time, 1e-9),
                ["min"] = (Dimension.Time, 60.0),
                ["h"] = (Dimension.Time, 3600.0),
                ["d"] = (Dimension.Time, 86400.0),
                ["yr"] = (Dimension.Time, 3.15576e7),
                ["kyr"] = (Dimension.Time, 3.15576e10),
                ["Myr"] = (Dimension.Time, 3.15576e13),
                ["Gyr"] = (Dimension.Time, 3.15576e16),
                // Volume, base litre
                ["L"] = (Dimension.Volume, 1.0),
                ["mL"] = (Dimension.Volume, 1e-3),
                ["µL"] = (Dimension.Volume, 1e-6),
                ["dm³"] = (Dimension.Volume, 1.0),
                ["cm³"] = (Dimension.Volume, 1e-3),
                ["m³"] = (Dimension.Volume, 1000.0),
                // Amount of substance, base mol
                ["mol"] = (Dimension.Amount, 1.0),
                ["mmol"] = (Dimension.Amount, 1e-3),
                ["µmol"] = (Dimension.Amount, 1e-6),
                // Molar concentration, base mol/L
                ["M"] = (Dimension.Molarity, 1.0),
                ["mol/dm³"] = (Dimension.Molarity, 1.0),
                ["mM"] = (Dimension.Molarity, 1e-3),
                ["µM"] = (Dimension.Molarity, 1e-6),
                ["mol/m³"] = (Dimension.Molarity, 1e-3),
            }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>
        /// Conversions that cross a dimension because a physical constant relates them.
        ///
        /// <para>One eV per particle is 96.485 kJ per mole - the same energy counted per particle
        /// rather than per mole. Kept out of the factor table deliberately: putting the two in one
        /// dimension would make an ionisation energy in eV and a fusion enthalpy in kJ/mol look
        /// interchangeable to a field comparison, and a ranking would silently mix them. Applied
        /// only when a user asks for the conversion outright, never by comparison or sorting.</para>
        /// </summary>
        private static readonly FrozenDictionary<(Dimension From, Dimension To), double> Bridges =
            new Dictionary<(Dimension, Dimension), double>
            {
                [(Dimension.Energy, Dimension.EnergyPerMol)] = 96.48533212,
                [(Dimension.EnergyPerMol, Dimension.Energy)] = 1.0 / 96.48533212,
            }.ToFrozenDictionary();

        private static readonly FrozenSet<string> TemperatureUnits =
            new[] { "K", "°C", "°F" }.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>
        /// Canonical spelling for a raw unit string, or null when it is blank.
        ///
        /// <para>NFKC is applied so the two mu characters agree: the element data writes U+00B5
        /// MICRO SIGN, which NFKC folds to U+03BC GREEK SMALL LETTER MU. Both sides of every
        /// comparison run through here, so µm/(m·K) and μm/(m·K) are the same unit.</para>
        /// </summary>
        public static string? Canonical(string? raw)
        {
            if (raw is null) return null;

            var trimmed = raw.Normalize(NormalizationForm.FormKC).Trim().Trim('(', ')').Trim();
            if (trimmed.Length == 0) return null;

            if (CaseSensitive.TryGetValue(trimmed, out var exact)) return exact;
            if (Aliases.TryGetValue(trimmed.ToLowerInvariant(), out var alias)) return alias;
            return trimmed;
        }

        /// <summary>
        /// The canonical form of a token, but only when it is a unit this converter actually knows.
        ///
        /// <para><see cref="Canonical"/> echoes anything it does not recognise straight back, which
        /// is right for a value read off the element data - the authored unit is the truth even
        /// when the table has never heard of it. A query is the opposite case: something has to
        /// decide whether the word after a number is a unit at all, and an echo answers "yes" to
        /// everything.</para>
        /// </summary>
        public static string? KnownUnit(string? token)
        {
            var c = Canonical(token);
            if (c is null) return null;
            return TemperatureUnits.Contains(c) || Factors.ContainsKey(c) ? c : null;
        }

        /// <summary>The dimension a unit belongs to, or null when it is unknown.</summary>
        public static Dimension? DimensionOf(string? unit)
        {
            var c = Canonical(unit);
            if (c is null) return null;
            if (TemperatureUnits.Contains(c)) return Dimension.Temperature;
            return Factors.TryGetValue(c, out var f) ? f.Dimension : null;
        }

        /// <summary>
        /// Convert a value between two units.
        /// </summary>
        /// <returns>Null when the units are unknown or of different dimensions.</returns>
        public static double? Convert(double value, string? from, string? to)
        {
            var f = Canonical(from);
            var t = Canonical(to);
            if (f is null || t is null) return null;
            if (string.Equals(f, t, StringComparison.Ordinal)) return value;

            if (TemperatureUnits.Contains(f) && TemperatureUnits.Contains(t))
                return ConvertTemperature(value, f, t);

            if (!Factors.TryGetValue(f, out var fromEntry)) return null;
            if (!Factors.TryGetValue(t, out var toEntry)) return null;
            if (fromEntry.Dimension != toEntry.Dimension) return null;

            return value * fromEntry.Factor / toEntry.Factor;
        }

        /// <summary>
        /// Convert within a dimension, or across one of the bridges when the dimensions differ.
        ///
        /// <para>Used only by the standalone unit-conversion intent, where the user has named both
        /// units and asked for the crossing explicitly. Ranking, filtering and property lookups go
        /// through <see cref="Convert(double, string?, string?)"/>, which refuses it.</para>
        /// </summary>
        public static double? ConvertAcrossBridges(double value, string? from, string? to)
        {
            if (Convert(value, from, to) is { } direct) return direct;

            var f = Canonical(from);
            var t = Canonical(to);
            if (f is null || t is null) return null;

            var fromDim = DimensionOf(f);
            var toDim = DimensionOf(t);
            if (fromDim is null || toDim is null) return null;
            if (!Bridges.TryGetValue((fromDim.Value, toDim.Value), out var bridge)) return null;

            // Through each dimension's base unit, so "96485 J/mol in eV" works as well as kJ/mol.
            if (!Factors.TryGetValue(f, out var fromEntry)) return null;
            if (!Factors.TryGetValue(t, out var toEntry)) return null;
            return value * fromEntry.Factor * bridge / toEntry.Factor;
        }

        /// <summary>Whether two units can be converted, within a dimension or across a bridge.</summary>
        public static bool Convertible(string? from, string? to)
        {
            var fromDim = DimensionOf(from);
            var toDim = DimensionOf(to);
            if (fromDim is null || toDim is null) return false;
            return fromDim == toDim || Bridges.ContainsKey((fromDim.Value, toDim.Value));
        }

        /// <summary>Whether converting crosses a dimension through a physical constant.</summary>
        public static bool IsBridged(string? from, string? to)
        {
            var fromDim = DimensionOf(from);
            var toDim = DimensionOf(to);
            if (fromDim is null || toDim is null) return false;
            return fromDim != toDim && Bridges.ContainsKey((fromDim.Value, toDim.Value));
        }

        /// <summary>Affine temperature conversion between K, °C and °F.</summary>
        public static double? ConvertTemperature(double value, string from, string to)
        {
            var kelvin = from switch
            {
                "K" => value,
                "°C" => value + 273.15,
                "°F" => (value - 32.0) * 5.0 / 9.0 + 273.15,
                _ => (double?)null,
            };
            if (kelvin is not { } k) return null;

            return to switch
            {
                "K" => k,
                "°C" => k - 273.15,
                "°F" => (k - 273.15) * 9.0 / 5.0 + 32.0,
                _ => null,
            };
        }

        /// <summary>Convert a <see cref="Quantity"/> into a target unit, carrying range and flags across.</summary>
        public static Quantity? Convert(Quantity quantity, string? to)
        {
            if (Convert(quantity.Value, quantity.Unit, to) is not { } value) return null;

            var high = quantity.High is { } h ? Convert(h, quantity.Unit, to) : null;
            var target = Canonical(to);

            return quantity with
            {
                Value = value,
                High = high,
                Unit = target,
                Display = FormatValue(value) + (target is null ? string.Empty : " " + target),
                // The unit is written into Display right here, so a renderer must not add it again.
                UnitAuthored = target is not null,
            };
        }

        /// <summary>Render a converted number without trailing noise: 2 decimals, trimmed.</summary>
        public static string FormatValue(double value)
        {
            var rounded = Math.Round(value * 100.0, MidpointRounding.AwayFromZero) / 100.0;
            return rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e15
                ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
                : rounded.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
