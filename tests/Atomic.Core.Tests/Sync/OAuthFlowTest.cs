using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Atomic.Core.Sync;
using Xunit;

namespace Atomic.Core.Tests.Sync
{
    public sealed class OAuthFlowTest
    {
        [Fact]
        public void EachAttemptGetsFreshCodes()
        {
            var a = OAuthFlow.NewCodes();
            var b = OAuthFlow.NewCodes();

            Assert.NotEqual(a.Verifier, b.Verifier);
            Assert.NotEqual(a.State, b.State);
        }

        /// <summary>
        /// The challenge must be the S256 hash of the verifier, or Google rejects the exchange with
        /// a generic invalid_grant long after the mistake was made.
        /// </summary>
        [Fact]
        public void TheChallengeIsTheS256HashOfTheVerifier()
        {
            var codes = OAuthFlow.NewCodes();

            var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codes.Verifier)))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            Assert.Equal(expected, codes.Challenge);
        }

        /// <summary>
        /// RFC 7636 requires base64url with no padding. Plain base64 is wrong twice - "+" and "/"
        /// are not URL-safe, and "=" padding is disallowed.
        /// </summary>
        [Fact]
        public void CodesAreBase64UrlWithoutPadding()
        {
            var codes = OAuthFlow.NewCodes();

            foreach (var value in new[] { codes.Verifier, codes.Challenge, codes.State })
            {
                Assert.DoesNotContain('+', value);
                Assert.DoesNotContain('/', value);
                Assert.DoesNotContain('=', value);
            }

            // RFC 7636: 43 to 128 characters.
            Assert.InRange(codes.Verifier.Length, 43, 128);
        }

        [Fact]
        public void TheRedirectIsLoopbackOnTheAssignedPort() =>
            Assert.Equal("http://127.0.0.1:51234/", OAuthFlow.RedirectUri(51234));

        [Fact]
        public void TheAuthorizationUrlCarriesEverythingGoogleNeeds()
        {
            var codes = OAuthFlow.NewCodes();
            var url = OAuthFlow.AuthorizationUrl("client-123", OAuthFlow.RedirectUri(9000), codes);

            Assert.StartsWith(OAuthFlow.AuthorizeEndpoint + "?", url, StringComparison.Ordinal);
            Assert.Contains("client_id=client-123", url, StringComparison.Ordinal);
            Assert.Contains("response_type=code", url, StringComparison.Ordinal);
            Assert.Contains("code_challenge_method=S256", url, StringComparison.Ordinal);
            Assert.Contains("code_challenge=" + codes.Challenge, url, StringComparison.Ordinal);
            Assert.Contains("state=" + codes.State, url, StringComparison.Ordinal);
            Assert.Contains(Uri.EscapeDataString("http://127.0.0.1:9000/"), url, StringComparison.Ordinal);

            // The verifier is the secret standing in for a client secret. It must never appear in a
            // URL the browser - and anything reading the browser's history - can see.
            Assert.DoesNotContain(codes.Verifier, url, StringComparison.Ordinal);
        }

        [Fact]
        public void ACallbackParsesItsQuery()
        {
            var callback = OAuthFlow.ParseCallback("?code=abc123&state=xyz&scope=openid+email");

            Assert.Equal("abc123", callback.Code);
            Assert.Equal("xyz", callback.State);
            Assert.Null(callback.Error);
        }

        [Fact]
        public void AnEscapedValueIsUnescaped() =>
            Assert.Equal("a b/c", OAuthFlow.ParseCallback("?code=a%20b%2Fc&state=s").Code);

        [Fact]
        public void ADenialParsesAsAnError()
        {
            var callback = OAuthFlow.ParseCallback("?error=access_denied&state=xyz");

            Assert.Equal("access_denied", callback.Error);
            Assert.Null(callback.Code);
        }

        /// <summary>
        /// The state check is the only thing tying a callback to the request that started it. The
        /// loopback port is reachable by every process on the machine, so without it any page the
        /// user happens to have open could drive a code into the listener.
        /// </summary>
        [Fact]
        public void OnlyACallbackWithTheMatchingStateIsValid()
        {
            var codes = OAuthFlow.NewCodes();

            Assert.True(OAuthFlow.IsValid(new OAuthFlow.Callback("code", codes.State, null), codes));

            Assert.False(OAuthFlow.IsValid(new OAuthFlow.Callback("code", "someone-elses", null), codes));
            Assert.False(OAuthFlow.IsValid(new OAuthFlow.Callback("code", null, null), codes));
            Assert.False(OAuthFlow.IsValid(new OAuthFlow.Callback(null, codes.State, null), codes));
            Assert.False(OAuthFlow.IsValid(new OAuthFlow.Callback("code", codes.State, "access_denied"), codes));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("?")]
        public void AnUnusableCallbackIsNeverValid(string? query) =>
            Assert.False(OAuthFlow.IsValid(OAuthFlow.ParseCallback(query), OAuthFlow.NewCodes()));
    }

    public sealed class ProgressDocumentTest
    {
        private static readonly DateTimeOffset Now =
            new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void ASnapshotRoundTripsThroughTheDocument()
        {
            var original = new ProgressSnapshot
            {
                Xp = 12_345,
                Level = 31,
                Streak = 9,
                StreakLastPlay = "2026-08-14",
                Notes = "<AAA>gold</AAA>",
                // Numeric ids, because that is the contract: Android parses the id with
                // toIntOrNull() and drops anything else, so "first_win" would never have
                // survived a real round trip however cleanly it round-tripped here.
                Achievements = [new("7", 1, 1), new("6", 12, 50)],
                Statistics = new Dictionary<string, int> { ["1"] = 40, ["2"] = 22 },
            };

            var document = JsonNode.Parse(ProgressDocument.ToDocument(original, Now).ToJsonString());
            var read = ProgressDocument.FromDocument(document);

            Assert.Equal(original.Xp, read.Xp);
            Assert.Equal(original.Level, read.Level);
            Assert.Equal(original.Streak, read.Streak);
            Assert.Equal(original.StreakLastPlay, read.StreakLastPlay);
            Assert.Equal(original.Notes, read.Notes);
            Assert.Equal(original.Achievements, read.Achievements);
            Assert.Equal(
                original.Statistics.OrderBy(e => e.Key),
                read.Statistics.OrderBy(e => e.Key));
        }

        /// <summary>
        /// The rule that keeps a Windows sync from destroying data on the user's phone: every field
        /// written must be covered by the mask, and every path in the mask must be written.
        /// </summary>
        /// <remarks>
        /// Compared on the root segment, because a mask path may reach into a map -
        /// <c>entitlements.windows</c> covers part of the <c>entitlements</c> field, and masking
        /// the whole map instead would delete the half the phone owns.
        /// </remarks>
        [Fact]
        public void TheUpdateMaskMatchesExactlyWhatIsWritten()
        {
            var document = ProgressDocument.ToDocument(ProgressSnapshot.Empty, Now);
            var written = ((JsonObject)document["fields"]!).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

            var masked = ProgressDocument.FieldPaths
                .Select(p => p.Split('.')[0])
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal(masked, written);
        }

        /// <summary>
        /// A mask path that reaches into a map must name a key the document actually writes there,
        /// or the sync would claim to own a sub-field it never sets.
        /// </summary>
        [Fact]
        public void EveryNestedMaskPathNamesAWrittenKey()
        {
            var document = ProgressDocument.ToDocument(ProgressSnapshot.Empty, Now);

            foreach (var path in ProgressDocument.FieldPaths.Where(p => p.Contains('.', StringComparison.Ordinal)))
            {
                var parts = path.Split('.');
                var map = document["fields"]![parts[0]]!["mapValue"]!["fields"]!;

                Assert.True(map[parts[1]] is not null, $"{path} is masked but never written");
            }
        }

        [Fact]
        public void TheUpdateMaskQueryNamesEveryOwnedField()
        {
            var query = ProgressDocument.UpdateMaskQuery();

            Assert.StartsWith("?", query, StringComparison.Ordinal);
            foreach (var path in ProgressDocument.FieldPaths)
                Assert.Contains("updateMask.fieldPaths=" + path, query, StringComparison.Ordinal);
        }

        /// <summary>
        /// A field this build has never heard of belongs to Android and must not appear in the
        /// snapshot, the document, or the mask - the mask is what leaves it alone on the server.
        /// </summary>
        [Fact]
        public void AFieldOnlyAndroidKnowsIsNeitherReadNorMasked()
        {
            var document = JsonNode.Parse(
                """
                {"fields":{
                  "xp":{"integerValue":"500"},
                  "somethingAndroidAddedLater":{"stringValue":"keep me"}
                }}
                """);

            Assert.Equal(500, ProgressDocument.FromDocument(document).Xp);
            Assert.DoesNotContain("somethingAndroidAddedLater", ProgressDocument.FieldPaths);
            Assert.DoesNotContain("somethingAndroidAddedLater", ProgressDocument.UpdateMaskQuery(), StringComparison.Ordinal);
        }

        [Fact]
        public void ProgressSyncNeverTouchesChatHistory()
        {
            // The regression this pins: the progress PATCH masked "chats" while writing a list of
            // strings in a shape Android's conversation maps never match, so every Windows sync
            // replaced the user's Android chat history with an empty array. Chat history is not
            // progress's to write - neither in the mask, nor in the body.
            Assert.DoesNotContain("chats", ProgressDocument.FieldPaths);
            Assert.DoesNotContain("chats", ProgressDocument.UpdateMaskQuery(), StringComparison.Ordinal);

            var body = ProgressDocument.ToDocument(ProgressSnapshot.Empty, Now);
            Assert.Null(body["fields"]?["chats"]);
        }

        /// <summary>
        /// A document holding Android's real chat history - a list of conversation maps - reads
        /// without error, and nothing about it leaks into progress.
        /// </summary>
        [Fact]
        public void AndroidShapedChatsDoNotDisturbProgress()
        {
            var document = JsonNode.Parse("""
                {"fields":{
                  "xp":{"integerValue":"120"},
                  "chats":{"arrayValue":{"values":[{"mapValue":{"fields":{
                    "id":{"stringValue":"abc"},
                    "title":{"stringValue":"What is gold?"},
                    "timestamp":{"integerValue":"1760000000000"},
                    "language":{"stringValue":"en"},
                    "messages":{"arrayValue":{"values":[]}}}}}]}}}}
                """);

            Assert.Equal(120, ProgressDocument.FromDocument(document).Xp);
        }

        [Fact]
        public void AnEmptyOrMalformedDocumentReadsAsDefaults()
        {
            foreach (var document in new[]
                     {
                         null,
                         JsonNode.Parse("{}"),
                         JsonNode.Parse("""{"fields":{}}"""),
                         JsonNode.Parse("""{"fields":{"xp":{"stringValue":"not a number"}}}"""),
                     })
            {
                var read = ProgressDocument.FromDocument(document);

                Assert.Equal(0, read.Xp);
                Assert.Null(read.StreakLastPlay);
                Assert.Empty(read.Achievements);
                Assert.Empty(read.Statistics);
            }
        }

        /// <summary>An achievement with no id cannot be merged, so it is dropped rather than kept.</summary>
        [Fact]
        public void AnAchievementWithNoIdIsDropped()
        {
            var document = JsonNode.Parse(
                """
                {"fields":{"achievements":{"arrayValue":{"values":[
                  {"mapValue":{"fields":{"progress":{"integerValue":"3"}}}},
                  {"mapValue":{"fields":{"id":{"stringValue":"real"},"progress":{"integerValue":"4"}}}}
                ]}}}}
                """);

            var read = ProgressDocument.FromDocument(document);

            Assert.Equal("real", Assert.Single(read.Achievements).Id);
        }

        /// <summary>
        /// Windows must never write the phone's half of the entitlement map. Masking the whole
        /// map, or writing an android key, would delete a tier this build cannot see.
        /// </summary>
        [Fact]
        public void OnlyTheWindowsHalfOfTheEntitlementMapIsWritten()
        {
            Assert.Contains(ProgressDocument.WindowsTierPath, ProgressDocument.FieldPaths);
            Assert.Contains(ProgressDocument.UltimatePath, ProgressDocument.FieldPaths);
            Assert.DoesNotContain(ProgressDocument.Entitlements, ProgressDocument.FieldPaths);
            Assert.DoesNotContain(ProgressDocument.AndroidTierPath, ProgressDocument.FieldPaths);

            var written = ProgressDocument.ToDocument(
                new ProgressSnapshot { WindowsTier = "pro+", AndroidTier = "pro", Ultimate = true },
                Now);

            var entitlements = written["fields"]![ProgressDocument.Entitlements]!["mapValue"]!["fields"]!;
            Assert.NotNull(entitlements["windows"]);
            Assert.NotNull(entitlements["ultimate"]);
            Assert.Null(entitlements["android"]);
        }

        /// <summary>
        /// Each platform owns its own tier. Taking a maximum across them would let a PRO+ phone
        /// unlock a Windows machine that has paid for nothing - which is what Ultimate is for, and
        /// it says so explicitly rather than leaking through a merge.
        /// </summary>
        [Fact]
        public void EachPlatformKeepsItsOwnTier()
        {
            var local = new ProgressSnapshot { WindowsTier = "free" };
            var cloud = new ProgressSnapshot { WindowsTier = "pro+", AndroidTier = "pro+" };

            var merged = ProgressMerge.Merge(local, cloud);

            Assert.Equal("free", merged.WindowsTier);
            Assert.Equal("pro+", merged.AndroidTier);
            Assert.False(merged.Ultimate);
        }

        [Fact]
        public void UltimateOnlyEverGrants()
        {
            Assert.True(ProgressMerge.Merge(
                new ProgressSnapshot { Ultimate = true },
                new ProgressSnapshot { Ultimate = false }).Ultimate);

            Assert.True(ProgressMerge.Merge(
                new ProgressSnapshot { Ultimate = false },
                new ProgressSnapshot { Ultimate = true }).Ultimate);
        }

        [Fact]
        public void TheEntitlementMapRoundTripsThroughTheDocument()
        {
            var snapshot = new ProgressSnapshot { WindowsTier = "pro+", Ultimate = true };

            var read = ProgressDocument.FromDocument(
                JsonNode.Parse(ProgressDocument.ToDocument(snapshot, Now).ToJsonString()));

            Assert.Equal("pro+", read.WindowsTier);
            Assert.True(read.Ultimate);
        }

        /// <summary>
        /// The document a sync writes must be readable by the merge that runs next, or the first
        /// round trip would quietly reset every counter.
        /// </summary>
        [Fact]
        public void AWrittenDocumentMergesCleanlyWithItself()
        {
            var snapshot = new ProgressSnapshot
            {
                Xp = 4200,
                Streak = 5,
                StreakLastPlay = "2026-08-14",
                Achievements = [new("102", 3, 10)],
            };

            var read = ProgressDocument.FromDocument(
                JsonNode.Parse(ProgressDocument.ToDocument(snapshot, Now).ToJsonString()));
            var merged = ProgressMerge.Merge(snapshot, read);

            Assert.Equal(4200, merged.Xp);
            Assert.Equal(5, merged.Streak);
            Assert.Equal(3, merged.Achievements.Single().Progress);
        }
    }
}
