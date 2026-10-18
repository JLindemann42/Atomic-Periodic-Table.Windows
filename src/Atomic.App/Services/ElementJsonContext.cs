using System.Collections.Generic;
using System.Text.Json.Serialization;
using Atomic.App.Tools;

namespace Atomic.App.Services
{
    /// <summary>
    /// Source-generated serialization metadata for <see cref="Element"/>.
    ///
    /// <para>This is not an optimisation, it is a correctness fix. Every non-Debug configuration
    /// sets <c>PublishTrimmed</c>, and <see cref="Element"/> was being deserialized by reflection.
    /// The trimmer has no way to see that its ~60 properties are used, strips their setters, and
    /// <c>JsonSerializer.Deserialize</c> then returns an object with every property null - with no
    /// exception and no warning. Debug builds never reproduce it, so it only appears in a shipped
    /// package.</para>
    ///
    /// <para>Deserialize through <c>ElementJsonContext.Default.Element</c> rather than the
    /// reflection-based overloads, and the metadata is generated at compile time and rooted.</para>
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
    [JsonSerializable(typeof(Element))]
    [JsonSerializable(typeof(Dictionary<string, Element>))]
    // The user's saved favourites. Same failure mode as Element, and worse in effect: these are
    // the only things in the app the user actually created, and a trimmed build would quietly
    // return an empty list and then overwrite the file with it on the next save.
    [JsonSerializable(typeof(List<FavoriteCompound>))]
    [JsonSerializable(typeof(List<UnitConversionFavorite>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(List<SavedEquation>))]
    [JsonSerializable(typeof(List<SavedIdealGas>))]
    // The chat thread. Its Actions and Cards are codec strings rather than nested objects, so this
    // stays a flat record and a message written by an older build still deserializes.
    [JsonSerializable(typeof(Dictionary<string, int>))]
    [JsonSerializable(typeof(List<ChatMessage>))]
    // The saved conversations. The single thread above is still read once, to migrate it.
    [JsonSerializable(typeof(List<StoredChatSession>))]
    internal sealed partial class ElementJsonContext : JsonSerializerContext
    {
    }
}
