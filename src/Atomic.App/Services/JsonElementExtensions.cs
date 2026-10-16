using System.Text.Json;

namespace Atomic.App.Services
{
    public static class JsonElementExtensions
    {
        /// <summary>
        /// Reads a field as text, or null when it is absent or JSON null.
        ///
        /// <para>Element JSON is almost entirely strings, but the three NFPA hazard fields are
        /// bare numbers. <c>GetString()</c> throws on those, so callers that just want to display
        /// a value need this rather than the raw accessor.</para>
        /// </summary>
        public static string? Field(this JsonElement row, string name)
        {
            if (!row.TryGetProperty(name, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Object or JsonValueKind.Array => null,
                _ => value.GetRawText(),
            };
        }

        /// <summary>A field from a nested object, e.g. lattice_constants.a.</summary>
        public static string? Field(this JsonElement row, string name, string nested)
        {
            if (!row.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
                return null;
            return value.Field(nested);
        }
    }
}
