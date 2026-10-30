// Project-wide type aliases.
//
// These stand in for Kotlin `typealias` declarations in the Android sources. A plain
// `using X = ...` inside a file is only visible in that file, which would mean repeating it in
// every file that mentions the type; `global using` matches Kotlin's project-wide scope, so the
// ported signatures read the same as the originals.

global using ElementRow = System.Collections.Generic.IReadOnlyDictionary<string, object?>;
