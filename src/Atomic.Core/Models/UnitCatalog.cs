using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomic.Core.Models
{
    /// <summary>A unit and its factor relative to its category's base unit.</summary>
    public sealed record UnitDefinition(string Name, double Factor);

    /// <summary>
    /// The unit conversion tables, shared by the Unit Conversion tool and the agent.
    ///
    /// <para>Every category except <see cref="Temperature"/> is purely multiplicative: each unit
    /// carries a factor relative to that category's base unit, so converting is
    /// <c>value * from.Factor / to.Factor</c>. Temperature is affine and handled separately.</para>
    ///
    /// <para>Not to be confused with <c>Ai.Data.UnitConverter</c>, which converts the physical
    /// quantities in the element table (g/cm³, kJ/mol) and knows nothing about this catalogue.
    /// This one backs a user-facing tool with a fixed, curated unit list.</para>
    ///
    /// <para>Verified identical to Android's UnitCatalog.kt: 14 categories, 65 units, every factor
    /// matching to within 1e-12.</para>
    /// </summary>
    public static class UnitCatalog
    {
        public const string Temperature = "Temperature";

        public static readonly IReadOnlyDictionary<string, IReadOnlyList<UnitDefinition>> Categories =
            new Dictionary<string, IReadOnlyList<UnitDefinition>>(StringComparer.Ordinal)
            {
                ["Length"] =
                [
                    new("Meter", 1.0), new("Kilometer", 1000.0), new("Centimeter", 0.01),
                    new("Millimeter", 0.001), new("Inch", 0.0254), new("Foot", 0.3048),
                    new("Yard", 0.9144), new("Mile", 1609.344),
                ],
                ["Mass"] =
                [
                    new("Kilogram", 1.0), new("Gram", 0.001), new("Milligram", 0.000001),
                    new("Pound", 0.45359237), new("Ounce", 0.0283495231),
                ],
                ["Volume"] =
                [
                    new("Liter", 1.0), new("Milliliter", 0.001), new("Cubic meter", 1000.0),
                    new("Gallon", 3.78541), new("Pint", 0.473176),
                ],
                ["Area"] =
                [
                    new("Square meter", 1.0), new("Square kilometer", 1_000_000.0),
                    new("Square centimeter", 0.0001), new("Square mile", 2_589_988.11),
                    new("Acre", 4046.85642),
                ],
                ["Velocity"] =
                [
                    new("Meter/second", 1.0), new("Kilometer/hour", 0.277778),
                    new("Mile/hour", 0.44704), new("Foot/second", 0.3048),
                ],
                ["Energy"] =
                [
                    new("Joule", 1.0), new("Kilojoule", 1000.0), new("Calorie", 4.184),
                    new("Kilocalorie", 4184.0), new("Watt hour", 3600.0),
                ],
                ["Frequency"] =
                [
                    new("Hertz", 1.0), new("Kilohertz", 1000.0),
                    new("Megahertz", 1_000_000.0), new("Gigahertz", 1_000_000_000.0),
                ],
                [Temperature] =
                [
                    // The factors are meaningless here - temperature is affine, so Convert routes
                    // this category to ConvertTemperature before any factor is read.
                    new("Celsius", 0.0), new("Fahrenheit", 0.0), new("Kelvin", 0.0),
                ],
                ["Time"] =
                [
                    new("Second", 1.0), new("Millisecond", 0.001), new("Minute", 60.0),
                    new("Hour", 3600.0), new("Day", 86400.0),
                ],
                ["Force"] =
                [
                    new("Newton", 1.0), new("Kilonewton", 1000.0), new("Dyne", 0.00001),
                    new("Pound-force", 4.4482216), new("Kilogram-force", 9.80665),
                ],
                ["Power"] =
                [
                    new("Watt", 1.0), new("Kilowatt", 1000.0),
                    new("Megawatt", 1_000_000.0), new("Horsepower", 745.699872),
                ],
                ["Voltage"] =
                [
                    new("Volt", 1.0), new("Millivolt", 0.001), new("Kilovolt", 1000.0),
                ],
                ["Resistance"] =
                [
                    new("Ohm", 1.0), new("Milliohm", 0.001),
                    new("Kiloohm", 1000.0), new("Megaohm", 1_000_000.0),
                ],
                ["Pressure"] =
                [
                    new("Pascal", 1.0), new("Kilopascal", 1000.0), new("Bar", 100_000.0),
                    new("Atmosphere", 101_325.0), new("PSI", 6894.757),
                ],
            };

        /// <summary>Units in a category, or empty if the category is unknown.</summary>
        public static IReadOnlyList<UnitDefinition> Units(string category) =>
            Categories.TryGetValue(category, out var units) ? units : [];

        /// <summary>The category a unit name belongs to, or null if no category defines it.</summary>
        public static string? CategoryOf(string unitName) =>
            Categories.FirstOrDefault(
                e => e.Value.Any(u => string.Equals(u.Name, unitName, StringComparison.Ordinal))).Key;

        /// <summary>
        /// Convert between two units of the same category.
        /// </summary>
        /// <returns>The converted value, or null if the category or either unit is unknown.</returns>
        public static double? Convert(string category, string from, string to, double value)
        {
            if (string.Equals(category, Temperature, StringComparison.Ordinal))
                return ConvertTemperature(from, to, value);

            if (!Categories.TryGetValue(category, out var units))
                return null;

            var fromDef = units.FirstOrDefault(u => string.Equals(u.Name, from, StringComparison.Ordinal));
            var toDef = units.FirstOrDefault(u => string.Equals(u.Name, to, StringComparison.Ordinal));
            if (fromDef is null || toDef is null)
                return null;

            return value * fromDef.Factor / toDef.Factor;
        }

        /// <summary>
        /// Affine temperature conversion. An unknown unit name passes the value through as Celsius,
        /// matching Android.
        /// </summary>
        public static double ConvertTemperature(string from, string to, double value)
        {
            var celsius = from switch
            {
                "Celsius" => value,
                "Fahrenheit" => (value - 32) * 5 / 9,
                "Kelvin" => value - 273.15,
                _ => value,
            };

            return to switch
            {
                "Celsius" => celsius,
                "Fahrenheit" => celsius * 9 / 5 + 32,
                "Kelvin" => celsius + 273.15,
                _ => celsius,
            };
        }
    }
}
