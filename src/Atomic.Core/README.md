# Atomic.Core

Platform-free logic shared by the Windows app and (by construction) portable to any other
.NET host. Ported from the Android app's Kotlin sources.

## The rule

`Atomic.Core` targets plain `net8.0`. **Never** change it to `net8.0-windows`, and never add
a reference to `Microsoft.WindowsAppSDK`, `Windows.*` or `Microsoft.UI.*`. The target framework
is the enforcement mechanism — if UI or platform code creeps in here, the build breaks, which
is exactly what should happen.

Anything that needs the platform (file access, settings, resources, HTTP) is injected as an
interface or a delegate. See `IElementSource` (a `Func<string, Stream?>` opener) and
`IStringProvider` for the two established patterns.

## Namespace ↔ Kotlin package map

Namespaces mirror the Android package layout 1:1 so that future Android → Windows syncs are a
file-by-file diff rather than an archaeology exercise. One deliberate rename: Kotlin's
`ai.core` becomes `Ai.Engine`, because `Atomic.Core.Ai.Core` is unreadable.

| Kotlin package | C# namespace | Folder |
|---|---|---|
| `ai` | `Atomic.Core.Ai` | `Ai/` |
| `ai.core` | `Atomic.Core.Ai.Engine` | `Ai/Engine/` |
| `ai.nlu` | `Atomic.Core.Ai.Nlu` | `Ai/Nlu/` |
| `ai.retrieval` | `Atomic.Core.Ai.Retrieval` | `Ai/Retrieval/` |
| `ai.exec` | `Atomic.Core.Ai.Exec` | `Ai/Exec/` |
| `ai.compose` | `Atomic.Core.Ai.Compose` | `Ai/Compose/` |
| `ai.cards` | `Atomic.Core.Ai.Cards` | `Ai/Cards/` |
| `ai.data` | `Atomic.Core.Ai.Data` | `Ai/Data/` |
| `quiz` | `Atomic.Core.Quiz` | `Quiz/` |
| `model` (data-only) | `Atomic.Core.Models` | `Models/` |
| — | `Atomic.Core.Progression` | `Progression/` |
| — | `Atomic.Core.Sync` | `Sync/` |
| — | `Atomic.Core.Text` | `Text/` (regex / unicode / format shims) |
| — | `Atomic.Core.Localization` | `Localization/` |

## Culture discipline

CA1304/1305/1307/1310/1311 are errors, not warnings, in this project. That is deliberate: chat
language is detected per message and passed explicitly, so nothing here may read
`CultureInfo.CurrentCulture`. Use `ToLowerInvariant()`, never `ToLower()`; pass
`CultureInfo.InvariantCulture` to every parse and format.

See `../../docs/porting-notes.md` for the Kotlin → C# translation rules and the list of
intentional divergences from the Android sources.
