using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Atomic.Core.Ai.Data;

namespace Atomic.Core.Ai.Engine
{
    /// <summary>
    /// What the current user is allowed to be told.
    ///
    /// <para>The agent has to honour the same paywall the rest of the app does. Without this it is a
    /// way around it: the Mechanical section of the element screen shows Young's modulus as a lock,
    /// while asking "what is the Young's modulus of gold" answered in full - and "which element has
    /// the highest Young's modulus" leaked a ranking over the whole gated column.</para>
    ///
    /// <para>Defaults to full access so every existing construction site - and every unit test -
    /// keeps working unchanged; the app supplies the real entitlements.</para>
    /// </summary>
    public sealed record Entitlements(bool IsPro = true, bool IsProPlus = true)
    {
        /// <summary>Whether the user owns what the tier requires. PRO+ implies PRO.</summary>
        public bool Allows(Tier tier) => tier switch
        {
            Tier.Free => true,
            Tier.Pro => IsPro || IsProPlus,
            Tier.ProPlus => IsProPlus,
            _ => throw new UnreachableException($"unhandled tier {tier}"),
        };

        /// <summary>Whether a field's value may be revealed.</summary>
        public bool AllowsField(string fieldId) => Allows(TierOf(fieldId));

        /// <summary>
        /// The tier a field sits behind.
        ///
        /// <para>Banked and multi-unit fields are addressed as <c>ionization_energy#3</c> and
        /// <c>melting_point@K</c>, so the suffix is stripped before the lookup - otherwise a gated
        /// field would come back FREE simply because it was asked for in a particular unit.</para>
        /// </summary>
        public static Tier TierOf(string fieldId)
        {
            var cut = fieldId.IndexOfAny(['#', '@']);
            var baseId = cut < 0 ? fieldId : fieldId[..cut];
            return FieldRegistry.ById.TryGetValue(baseId, out var spec) ? spec.Tier : Tier.Free;
        }

        /// <summary>The strictest tier among these fields, or null when they are all free.</summary>
        public Tier? BlockingTier(IEnumerable<string> fieldIds)
        {
            var blocked = fieldIds
                .Select(TierOf)
                .Where(t => !Allows(t))
                .ToList();

            return blocked.Count == 0 ? null : blocked.Max();
        }

        /// <summary>Full access. The default, so tests and existing callers are unaffected.</summary>
        public static readonly Entitlements Full = new();
    }
}
