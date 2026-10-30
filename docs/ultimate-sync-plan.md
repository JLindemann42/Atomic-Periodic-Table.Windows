# Syncing Ultimate: verifying a cross-store purchase

## The problem, in one sentence

Ultimate is bought from **one** store and must unlock PRO+ on a platform whose **own** store has no
licence to show for it — so on that platform, the only evidence is whatever the account document
says, and today the account document is written by the client.

`entitlements.ultimate` is a boolean in `users/{uid}`, and `users/{uid}` is PATCHed by the app with
the user's own ID token. Anyone who can sign in can set it. Nothing in the current design
distinguishes a real purchase from a one-line HTTP request, and nothing ever notices a refund.

That is fine today only because the product does not exist yet. **This work should land before
Ultimate ships**, because retrofitting trust onto entitlements already granted is a much worse job
than building it now, when the set of affected users is empty.

## Why this cannot be fixed on the client

The buying platform is fine without any of this: its own store holds the licence, and
`CheckUserLicensesAsync` already reads it. The problem is only the *other* platform, and there the
chain of custody is:

```
Microsoft Store  →  Windows app  →  Firestore  →  Android app
                    ^^^^^^^^^^^      ^^^^^^^^^
                    untrusted        stores whatever it was told
```

A client cannot vouch for itself. The link has to be broken by something the user does not control,
which means a server. There is no clever client-side arrangement that closes this — signing the
value with a shipped key just moves the key into the binary.

## The core idea: a lease, not a boolean

`ultimate: true` can only say *yes* or *no*. It cannot express "verified with Microsoft on Tuesday",
which is what "does the user still own it" actually needs.

Replace it with a **lease**:

```
ultimate: {
  active:     true
  source:     "microsoft"          // which store sold it
  productId:  "9N..."
  verifiedAt: 2026-08-23T09:14:00Z // server timestamp, last successful check
  expiresAt:  2026-09-22T09:14:00Z // server timestamp, verifiedAt + 30 days
}
```

This does three jobs at once:

- **Ownership** — `active` is only ever written after a store said yes.
- **Revocation** — a refund stops the renewal; the lease runs out on its own. Nothing has to
  successfully reach a revoked user to revoke them.
- **Offline tolerance** — the client honours a lease until `expiresAt`, so a paying user on a plane
  keeps PRO+. This is the same rule as D28 (*silence is not a negative*), expressed as a duration
  instead of a special case: an unreachable server is not evidence of anything until the lease is
  genuinely stale.

Lease length is a policy dial. **30 days verified daily** is the suggested start: a refunder keeps
access for at most a month, and a user with a flaky connection essentially never notices. Shorten it
later if abuse appears; lengthening it is always safe.

## Data model

Three pieces, and the split matters more than the shapes.

### 1. `users/{uid}` — client-owned, unchanged

```
entitlements.windows: "pro+"    // informational, this device's own store tier
entitlements.android: "pro"     // informational, the phone's own store tier
```

These stay client-written. They are **descriptive, not load-bearing**: each platform gates itself on
its own store licence, so a forged `entitlements.windows` unlocks nothing — the Windows app does not
read it, it writes it. They exist so the account page can show "Windows PRO+ / Android PRO".

**`entitlements.ultimate` is removed from this document**, and from `ProgressDocument.FieldPaths`.

### 2. `entitlements/{uid}` — server-owned

```
ultimate: { active, source, productId, verifiedAt, expiresAt }
updatedAt
```

Rules: readable by its owner, **writable by nobody**. Cloud Functions bypass rules via the Admin
SDK, so this needs no exception.

```javascript
match /entitlements/{uid} {
  allow read:  if request.auth != null && request.auth.uid == uid;
  allow write: if false;
}
```

A separate document, not a field on `users/{uid}`, because Firestore rules apply per document. The
progress document must stay client-writable for the sync that already works; the entitlement must
not be. They cannot be the same document.

### 3. `receipts/{receiptHash}` — server-owned, the replay guard

```
uid, platform, productId, boundAt
```

**Without this, one purchase unlocks unlimited accounts.** A purchase token is a bearer string; a
buyer can post theirs to a forum and every reader redeems it. Binding a receipt to the first uid that
redeems it, and refusing it for any other, is what makes the product sellable. Store a hash rather
than the token itself so the collection is not a list of live credentials.

Rebinding (a genuine "I bought this on my other account") is a support action, not an API.

## Verifying with each store

### Microsoft Store

Server-to-server ownership is the **collections API**:

1. Client: `StoreContext.GetCustomerCollectionsIdAsync(serviceTicket, publisherUserId)` → a *Store
   ID key*.
2. Client → Cloud Function: the Store ID key.
3. Function → `POST https://collections.mp.microsoft.com/v6.0/collections/query`, authenticated with
   an Azure AD token for audience `https://onestore.microsoft.com`, carrying the Store ID key.
4. Response enumerates the user's owned products; look for the Ultimate product id.

Prerequisite: an **Azure AD application registered and associated with the Partner Center account**.
This is the single largest setup task in the plan and is worth doing first, because it is the item
most likely to reveal a blocker.

### Google Play

1. Client: purchase token from Play Billing.
2. Function → `androidpublisher.purchases.products.get(packageName, productId, token)` →
   `purchaseState`, `acknowledgementState`.
3. For revocation, `purchases.voidedpurchases.list` reports refunds and chargebacks directly, which
   is cheaper than re-querying every purchase.

Prerequisite: a **Google service account with Play Developer API access**, key held in function
config, never in an app.

> I have not re-checked these API versions against current documentation. Treat the endpoint names
> as the shape of the solution and confirm them when implementing — both vendors have moved these
> before.

## Cloud Functions

Three, all small.

| Function | Trigger | Job |
|---|---|---|
| `redeemUltimate` | callable | Verify a receipt, bind it in `receipts/`, write the first lease. |
| `refreshUltimate` | callable | Client asks near expiry; re-verify, extend `expiresAt`. |
| `sweepUltimate` | scheduled, daily | Re-verify leases expiring within N days; consume Play's voided-purchases feed; let refunded leases lapse. |

`refreshUltimate` exists so a live user renews promptly rather than waiting for the sweep; the sweep
exists so a user who never opens the app still stops being entitled. Neither alone is enough.

The sweep should **never delete** a lease. It stops renewing, and optionally records
`lastCheck: { status: "refunded", at: … }`. Deleting loses the audit trail exactly when a support
question is most likely.

## Client changes

### Windows

- `ProPage` purchase path: on a successful Ultimate purchase, call `redeemUltimate` instead of
  setting `SettingsStore.HasUltimate = true` directly.
- `CloudSync`: read `entitlements/{uid}` as a second GET; stop writing `entitlements.ultimate`.
- `SettingsStore`: cache `UltimateVerifiedAt` / `UltimateExpiresAt` alongside the flag, so the lease
  survives a restart offline.
- `EntitlementRules.WithUltimate(platformTier, ultimate)` becomes lease-aware:

```csharp
public static Entitlement WithUltimate(Entitlement platformTier, UltimateLease? lease, DateTimeOffset now) =>
    lease is { Active: true } && now < lease.ExpiresAt ? Entitlement.ProPlus : platformTier;
```

Still pure, still in `Atomic.Core`, still testable without a network — which is the whole reason the
rule lives there. The lease boundary cases (expired, absent, active-but-stale, clock skew) are unit
tests, not things to discover in production.

- Refresh when the lease is inside its last third, on the existing auto-sync tick. No new scheduler.

### Android

Symmetric, and **Android has to move first on one thing**: it must start writing
`entitlements.android` before the Windows account page can show that badge at all (already noted in
D29). Redemption and lease reading are the same shape.

## What happens when things go wrong

| Situation | Behaviour | Why |
|---|---|---|
| Offline, lease valid | PRO+ | The lease is the point. |
| Offline, lease expired | Falls back to the platform's own store tier | Not a revocation — the user simply has no cross-platform evidence right now. |
| Signed out | Platform tier only | Ultimate is an account entitlement; there is no account. |
| Refunded | Lease lapses within ≤ lease length | No message needs to reach the device. |
| Verification service down | Lease keeps renewing? **No** — it keeps *running*, and the sweep retries | Outages must not grant, and must not revoke faster than the lease. |
| Receipt already bound to another uid | Redemption refused, support path | The replay guard. |
| Clock set backwards | Can extend a lease locally | Accepted: the buying platform still gates on its own store licence, so the exposure is one device on the non-buying platform. |

## Sequencing

**P1 — Foundations, no behaviour change.** Firebase project gains Functions + rules + emulator.
Azure AD app registered and associated; Play service account created. Verify by calling both store
APIs by hand for a known purchase. *This phase is where the plan either works or doesn't.*

**P2 — Redemption.** `redeemUltimate`, `receipts/` binding, `entitlements/{uid}` written. Client
still ignores it. Shippable and invisible.

**P3 — Client honours the lease.** `UltimateLease` in `Atomic.Core` with tests; Windows reads the
server document; `entitlements.ultimate` removed from the client's update mask. Now the grant is
real and unforgeable.

**P4 — Renewal and revocation.** `refreshUltimate` + `sweepUltimate` + voided-purchases feed.

**P5 — Android parity.** Same reading, plus writing `entitlements.android`.

P1–P3 is the minimum that makes Ultimate safe to sell. P4 can follow within a release, because until
someone has bought and refunded, there is nothing to revoke.

## Cost and risk

Trivial at this scale: a daily sweep over a handful of leases, plus one callable per purchase, sits
inside the Cloud Functions free tier. The real costs are **setup complexity** (Azure AD association
is fiddly and under-documented) and **a new deployable** — the project currently has no backend at
all, and this adds one that must be versioned, tested and kept alive.

The largest risk is that the Microsoft collections path is harder to stand up than expected. It is
worth spending the first day proving it end-to-end by hand, before writing any function, exactly as
the OAuth spike was pulled forward to P0 in the main port plan.

## The alternative worth naming

**Sell Ultimate on each store separately and drop the cross-platform grant.** No server, no leases,
no functions — each platform reads its own licence, which already works today.

It fails the product's purpose: the point of Ultimate is buying once. Charging twice for "buy once
everywhere" is worse than not shipping it. But it is the honest fallback if the backend turns out to
be more than this project wants to own, and it costs nothing to keep in reserve.

A middle option — client-written grant, receipts stored for later audit — is not worth building.
It carries the full cost of the data model and none of the trust.
