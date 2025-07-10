using System;

namespace Atomic_WinUI.Helpers
{
    public static class TemperatureFormatter
    {
        public static string FormatTemperatureAllUnits(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "-";

            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return input;

            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                return input;

            string unit = parts[1].ToUpperInvariant();

            double kelvin, celsius, fahrenheit;

            switch (unit)
            {
                case "K":
                    kelvin = value;
                    celsius = kelvin - 273.15;
                    fahrenheit = celsius * 9 / 5 + 32;
                    break;
                case "°C":
                case "C":
                    celsius = value;
                    kelvin = celsius + 273.15;
                    fahrenheit = celsius * 9 / 5 + 32;
                    break;
                case "°F":
                case "F":
                    fahrenheit = value;
                    celsius = (fahrenheit - 32) * 5 / 9;
                    kelvin = celsius + 273.15;
                    break;
                default:
                    return input;
            }

            return $"{kelvin:0} K = {celsius:0.##} °C = {fahrenheit:0.##} °F";
        }
    }
}
