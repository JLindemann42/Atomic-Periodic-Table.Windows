using Atomic_PeriodicTable.Tables;

namespace Atomic_WinUI.Helpers
{
    public static class LatticeParameterFormatter
    {
        public static string GetLatticeSystemParameters(string crystalSystem, LatticeConstants constants)
        {
            string a = constants?.A ?? "-";
            string b = constants?.B ?? "-";
            string c = constants?.C ?? "-";
            string alpha = constants?.A ?? "-";
            string beta = constants?.B ?? "-";
            string gamma = constants?.C ?? "-";

            return crystalSystem switch
            {
                "Cubic" => $"a = b = c = {a}; ? = ? = ? = 90°",
                "Tetragonal" => $"a = b = {a}; c = {c}; ? = ? = ? = 90°",
                "Orthorhombic" => $"a = {a}; b = {b}; c = {c}; ? = ? = ? = 90°",
                "Hexagonal" => $"a = b = {a}; c = {c}; ? = ? = 90°; ? = 120°",
                "Trigonal" or "Rhombohedral" => $"a = b = c = {a}; ? = ? = ? = {alpha}° (? 90°)",
                "Monoclinic" => $"a = {a}; b = {b}; c = {c}; ? = ? = 90°; ? = {beta}° (? 90°)",
                "Triclinic" => $"a = {a}; b = {b}; c = {c}; ? = {alpha}°; ? = {beta}°; ? = {gamma}° (all ? 90°)",
                _ => $"a = {a}; b = {b}; c = {c}; ? = {alpha}°; ? = {beta}°; ? = {gamma}°"
            };
        }
    }
}
