using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Atomic.Core.Progression;
using Atomic.Core.Sync;

namespace Atomic.App.Services
{
    /// <summary>What a sync did.</summary>
    public sealed record SyncOutcome(bool Success, string? Error = null, DateTimeOffset? At = null);

    /// <summary>
    /// Reads and writes <c>users/{uid}</c> over the Firestore REST API, and reconciles it with
    /// local progress.
    ///
    /// <para>The merge itself is <see cref="ProgressMerge"/> and <see cref="NotesCodec"/> - pure,
    /// and tested well before this class existed. What lives here is only transport and the
    /// local read/write, which keeps the part that can destroy data separable from the part that
    /// can fail on a network.</para>
    /// </summary>
    public static class CloudSync
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static FirebaseSession? _session;

        public static bool IsSignedIn => TokenStore.SignedInUid is not null;

        public static string? SignedInUid => TokenStore.SignedInUid;

        public static DateTimeOffset? LastSyncedAt { get; private set; }

        /// <summary>Raised after any sync that actually ran, successful or not.</summary>
        public static event EventHandler<SyncOutcome>? Synced;

        /// <summary>Sign in interactively, then sync once so the two devices agree immediately.</summary>
        public static async Task<SyncOutcome> SignInAndSyncAsync()
        {
            try
            {
                _session = await GoogleSignIn.SignInAsync().ConfigureAwait(true);
                return await SyncAsync().ConfigureAwait(true);
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
            {
                return new SyncOutcome(false, e.Message);
            }
        }

        public static void SignOut()
        {
            GoogleSignIn.SignOut();
            _session = null;
            LastSyncedAt = null;
        }

        // ---- Automatic sync ----------------------------------------------------------------------

        private static readonly TimeSpan Period = TimeSpan.FromMinutes(5);

        /// <summary>How stale a sync must be before an unforced request will run another.</summary>
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(2);

        private static readonly SemaphoreSlim OneAtATime = new(1, 1);
        private static Timer? _timer;
        private static DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

        /// <summary>
        /// Sync now, and keep syncing while the app runs.
        /// </summary>
        /// <remarks>
        /// <para>Called once at launch. Signing in is the only thing the user should have to do -
        /// there is no sync button, because a button that must be pressed for progress to be safe
        /// is a button that will eventually not be pressed.</para>
        ///
        /// <para>Safe to call when signed out or unconfigured; the tick checks each time, so a
        /// sign-in later in the session is picked up without restarting anything.</para>
        /// </remarks>
        public static void StartAutoSync()
        {
            _timer ??= new Timer(_ => _ = AutoSyncAsync(), null, TimeSpan.Zero, Period);
        }

        /// <summary>
        /// Sync unless one just ran, returning null when there was nothing to do.
        /// </summary>
        /// <remarks>
        /// <para>Null means skipped - not signed in, not configured, or synced recently enough -
        /// and is deliberately distinct from a <see cref="SyncOutcome"/> reporting failure. A
        /// caller showing an error must not show one for a sync that never ran.</para>
        ///
        /// <para>Single-flight: a second caller awaits the first rather than starting a competing
        /// read-merge-write against the same document.</para>
        /// </remarks>
        /// <summary>Whether a sync could do anything, without attempting one.</summary>
        public static bool CanSync => FirebaseSecrets.IsConfigured && IsSignedIn;

        public static async Task<SyncOutcome?> AutoSyncAsync(bool force = false)
        {
            if (!CanSync) return null;

            await OneAtATime.WaitAsync().ConfigureAwait(true);
            try
            {
                if (!force && DateTimeOffset.UtcNow - _lastAttempt < MinimumInterval) return null;
                _lastAttempt = DateTimeOffset.UtcNow;

                var outcome = await SyncAsync().ConfigureAwait(true);
                Synced?.Invoke(null, outcome);

                // The tools' saved items ride the same timer; they own their own part of the document.
                _ = FavoritesSync.SyncAsync();
                return outcome;
            }
            finally
            {
                OneAtATime.Release();
            }
        }

        /// <summary>
        /// Merge local and cloud progress, then write the result back.
        /// </summary>
        /// <remarks>
        /// The merge is monotonic, so running this twice is harmless and running it on every launch
        /// is safe. Local state is only ever moved forward - see <see cref="ApplyLocally"/>.
        /// </remarks>
        public static async Task<SyncOutcome> SyncAsync()
        {
            try
            {
                var session = await CurrentSessionAsync().ConfigureAwait(true);
                if (session is null) return new SyncOutcome(false, "Not signed in.");

                var cloudDocument = await GetAsync(session).ConfigureAwait(true);
                var cloud = ProgressDocument.FromDocument(cloudDocument);
                var local = ReadLocal();

                var merged = ProgressMerge.Merge(local, cloud);
                merged = merged with { Notes = MergeNotes(local.Notes, cloud.Notes) };

                await PatchAsync(session, merged).ConfigureAwait(true);
                ApplyLocally(merged);

                LastSyncedAt = DateTimeOffset.Now;
                return new SyncOutcome(true, null, LastSyncedAt);
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
            {
                return new SyncOutcome(false, e.Message);
            }
        }

        // ---- Local state -----------------------------------------------------------------------

        private static ProgressSnapshot ReadLocal() => new()
        {
            Xp = ProgressStore.Xp,
            Level = ProgressStore.Level,
            Streak = ProgressStore.Streak,
            StreakLastPlay = string.IsNullOrEmpty(SettingsStore.LastPlayDate)
                ? null
                : SettingsStore.LastPlayDate,
            Achievements = ReadLocalAchievements(),
            Statistics = AchievementStore.StatisticsSnapshot(),

            // Reported per platform so the phone can tell "PRO+ on Windows" from "PRO+ on
            // Android". Without the platform the shared document cannot distinguish two separate
            // purchases from two separate stores.
            WindowsTier = Atomic.Core.Licensing.EntitlementRules.ToWire(SettingsStore.Entitlement.Tier),
            Ultimate = SettingsStore.HasUltimate,
            Notes = NotesCodec.Format(ReadLocalNotes(), CodeByAtomicNumber()),
        };

        /// <summary>
        /// Local achievement progress, carrying the goal and title this build knows.
        /// </summary>
        /// <remarks>
        /// <para>The goal travels because the merge takes the larger of the two ceilings: a
        /// platform that has raised a target must not have it clamped back by one that has
        /// not.</para>
        ///
        /// <para>The title travels because Android writes one, and a Windows PATCH replaces the
        /// whole array - omitting it would strip a field the phone put there. Android never reads
        /// it back, so it is courtesy rather than function.</para>
        /// </remarks>
        private static IReadOnlyList<AchievementProgress> ReadLocalAchievements() =>
            Atomic.Core.Progression.Achievements.All
                .Select(a => new AchievementProgress(
                    a.Id, AchievementStore.Progress(a.Id), a.Goal) { Title = a.Title })
                .ToList();

        /// <summary>
        /// Move local state forward to the merged values.
        /// </summary>
        /// <remarks>
        /// Only ever forward. The merge already guarantees it, but writing it as an explicit
        /// comparison means a future change to either side cannot quietly start moving a counter
        /// backwards.
        /// </remarks>
        private static void ApplyLocally(ProgressSnapshot merged)
        {
            if (merged.Xp > ProgressStore.Xp) ProgressStore.Xp = merged.Xp;
            if (merged.Streak > SettingsStore.Streak) SettingsStore.Streak = merged.Streak;

            if (merged.StreakLastPlay is { Length: > 0 } day &&
                StreakMath.ParseDay(day) is { } parsed &&
                (StreakMath.ParseDay(SettingsStore.LastPlayDate) is not { } current || parsed > current))
            {
                SettingsStore.LastPlayDate = day;
            }

            if (merged.AndroidTier is { Length: > 0 } androidTier)
                SettingsStore.AndroidTier = androidTier;

            // Ultimate crosses stores: bought on the phone, it unlocks PRO+ here, and the local
            // Store has no licence to show for it. Grant-only, like every other rule in the merge.
            if (merged.Ultimate)
            {
                SettingsStore.HasUltimate = true;
                SettingsStore.Entitlement = Atomic.Core.Licensing.EntitlementRules.WithUltimate(
                    SettingsStore.Entitlement, ultimate: true);
            }

            AchievementStore.ApplyMerged(
                merged.Achievements
                    .Select(a => new AchievementProgressValue(a.Id, a.Progress))
                    .ToList(),
                merged.Statistics);

            WriteLocalNotes(NotesCodec.Parse(merged.Notes, AtomicNumberByCode()));
        }

        // ---- Notes -----------------------------------------------------------------------------

        private static string? MergeNotes(string? local, string? cloud) =>
            NotesCodec.Format(
                NotesCodec.Merge(
                    NotesCodec.Parse(local, AtomicNumberByCode()),
                    NotesCodec.Parse(cloud, AtomicNumberByCode())),
                CodeByAtomicNumber());

        private static NotesDocument ReadLocalNotes()
        {
            var byNumber = new Dictionary<int, string>();
            foreach (var atomicNumber in Enumerable.Range(1, 118))
            {
                var note = SettingsStore.GetElementNotes(atomicNumber);
                if (!string.IsNullOrWhiteSpace(note)) byNumber[atomicNumber] = note;
            }

            // Unmapped notes are not read from local settings because local settings cannot hold
            // one - they are keyed by atomic number. They survive by riding through the cloud blob.
            return new NotesDocument(byNumber, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        private static void WriteLocalNotes(NotesDocument document)
        {
            foreach (var (atomicNumber, note) in document.ByAtomicNumber)
            {
                var existing = SettingsStore.GetElementNotes(atomicNumber) ?? string.Empty;
                // Longer wins, matching the merge rule. Writing unconditionally would let a shorter
                // merged value overwrite a local edit made since the merge was computed.
                if (note.Length > existing.Length) SettingsStore.SetElementNotes(atomicNumber, note);
            }
        }

        private static Dictionary<string, int>? _codeToNumber;
        private static Dictionary<int, string>? _numberToCode;

        private static Dictionary<string, int> AtomicNumberByCode()
        {
            BuildMaps();
            return _codeToNumber!;
        }

        private static Dictionary<int, string> CodeByAtomicNumber()
        {
            BuildMaps();
            return _numberToCode!;
        }

        /// <summary>
        /// The element_code map, read once from the shipped table.
        /// </summary>
        /// <remarks>
        /// <para>This is the mapping the whole notes feature rests on: Android keys notes by an
        /// opaque code, Windows by atomic number. See <see cref="NotesCodec"/>.</para>
        ///
        /// <para>Read from the raw JSON rather than from <c>Element</c>, which carries no
        /// <c>element_code</c> property. Adding one would mean touching a class P1 deliberately
        /// kept byte-identical and rooted in the serializer context, for a field only sync uses.</para>
        /// </remarks>
        private static void BuildMaps()
        {
            if (_codeToNumber is not null) return;

            var toNumber = new Dictionary<string, int>(StringComparer.Ordinal);
            var toCode = new Dictionary<int, string>();

            foreach (var key in ElementRepository.Keys())
            {
                if (ElementRepository.Raw(key) is not { } row) continue;

                if (!row.TryGetProperty("element_code", out var codeValue)) continue;
                if (!row.TryGetProperty("element_atomic_number", out var numberValue)) continue;

                var code = codeValue.ValueKind == System.Text.Json.JsonValueKind.String
                    ? codeValue.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(code)) continue;

                var rawNumber = numberValue.ValueKind == System.Text.Json.JsonValueKind.String
                    ? numberValue.GetString()
                    : null;
                if (!int.TryParse(rawNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var atomicNumber))
                    continue;

                toNumber[code] = atomicNumber;
                toCode[atomicNumber] = code;
            }

            _codeToNumber = toNumber;
            _numberToCode = toCode;
        }

        // ---- For other owners of the document -------------------------------------------------

        /// <summary>
        /// The user's whole document, for a caller that owns a different part of it than progress.
        /// </summary>
        /// <returns>The document, or null when there is none yet - a first sign-in.</returns>
        /// <exception cref="InvalidOperationException">Not signed in, or the request was refused.</exception>
        public static async Task<JsonNode?> ReadDocumentAsync()
        {
            var session = await CurrentSessionAsync().ConfigureAwait(true)
                          ?? throw new InvalidOperationException("Not signed in.");

            return await GetAsync(session).ConfigureAwait(true);
        }

        /// <summary>
        /// Write the fields <paramref name="updateMaskQuery"/> names, and only those.
        /// </summary>
        /// <remarks>
        /// The mask is required. A PATCH without one replaces the whole document, which this document
        /// co-owned with Android must never have done to it - so an empty mask is refused here rather
        /// than sent.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Not signed in, no mask, or the request was refused.</exception>
        public static async Task WriteFieldsAsync(JsonObject body, string updateMaskQuery)
        {
            if (!updateMaskQuery.Contains("updateMask.fieldPaths=", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing a PATCH without an update mask.");

            var session = await CurrentSessionAsync().ConfigureAwait(true)
                          ?? throw new InvalidOperationException("Not signed in.");

            await PatchBodyAsync(session, body, updateMaskQuery).ConfigureAwait(true);
        }

        // ---- Transport -------------------------------------------------------------------------

        private static string DocumentUrl(string uid) =>
            "https://firestore.googleapis.com/v1/projects/" +
            FirebaseSecrets.Config.ProjectId +
            "/databases/(default)/documents/users/" + Uri.EscapeDataString(uid);

        private static async Task<FirebaseSession?> CurrentSessionAsync()
        {
            if (_session is { } live && !live.NeedsRefresh) return live;

            _session = await GoogleSignIn.RestoreAsync().ConfigureAwait(true);
            return _session;
        }

        private static async Task<JsonNode?> GetAsync(FirebaseSession session)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, DocumentUrl(session.Uid));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.IdToken);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);

            // A first sign-in has no document yet, which is not an error - it is the empty side of
            // the merge.
            if (response.StatusCode == HttpStatusCode.NotFound) return null;

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Describe(body, response));

            return JsonNode.Parse(body);
        }

        /// <summary>
        /// Write the merged snapshot.
        /// </summary>
        /// <remarks>
        /// <para><b>Always with an update mask.</b> A bare PATCH replaces the whole document, and
        /// this document is co-owned with Android: a field this build has never heard of would be
        /// deleted from the user's phone. <see cref="ProgressDocument.FieldPaths"/> is the
        /// exhaustive list of what this build owns, and a test asserts it matches exactly what is
        /// written.</para>
        /// </remarks>
        private static Task PatchAsync(FirebaseSession session, ProgressSnapshot snapshot) =>
            PatchBodyAsync(
                session,
                ProgressDocument.ToDocument(snapshot, DateTimeOffset.UtcNow),
                ProgressDocument.UpdateMaskQuery());

        private static async Task PatchBodyAsync(FirebaseSession session, JsonObject body, string updateMaskQuery)
        {
            var url = DocumentUrl(session.Uid) + updateMaskQuery;

            using var request = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.IdToken);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return;

            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(Describe(text, response));
        }

        private static string Describe(string body, HttpResponseMessage response) =>
            JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>()
            ?? response.StatusCode.ToString(CultureInfo.InvariantCulture);
    }
}
