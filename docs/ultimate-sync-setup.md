# P1 setup: standing up the entitlement backend

Everything in this document needs an account I cannot sign into, so it is a checklist for you
rather than work I can do. The code it refers to is already in `backend/` — the point of P1 is to
find out whether these three services will actually talk to each other **before** anything is built
on top of them.

Work in order. Step 3 is the one that decides whether the plan survives.

---

## 0. Local tooling

None of this exists on the machine yet:

```bash
winget install OpenJS.NodeJS.LTS
```

Then, in a new shell:

```bash
npm install -g firebase-tools
```

The Firestore emulator also needs a JDK. There is a Java on this machine already, but an old one —
if `firebase emulators:start` complains, install a current JDK.

```bash
cd backend/functions
npm install
```

Nothing above touches a live project.

---

## 1. Firebase: Functions on the existing project

The project is `atomic---periodic-table-41532`, already referenced in `backend/.firebaserc`.

```bash
firebase login
firebase use default
```

Cloud Functions requires the project to be on the **Blaze** plan. At this volume — one call per
purchase, one scheduled sweep a day — it stays inside the free allowance; Blaze is a billing account
on file, not a bill. Set a budget alert anyway.

Verify the rules compile and the tests pass, entirely locally:

```bash
cd backend/functions
npm run test:rules
```

That runs `src/rules.test.ts` against the emulator. It proves the split the whole design rests on:
a client can write its own progress, cannot write `entitlements.ultimate`, can read but not write
its own entitlement, and cannot see receipts at all.

**Do not deploy the rules yet.** The shipped Windows client still writes `entitlements.ultimate`,
and Firestore rejects a whole write when one field is refused — deploying now would stop progress
sync for anyone running the current build. Rules go out after P3, once the client has stopped
writing that field.

---

## 2. Google Play: service account

1. **Google Cloud console** → the project backing Play → *IAM & Admin* → *Service Accounts* →
   create one, e.g. `atomic-play-verifier`. No project roles needed.
2. Create a **JSON key** for it. Save it outside the repo, or at
   `backend/functions/play-service-account.json` — already gitignored.
3. **Play Console** → *Users and permissions* → invite that service account's email → grant, for
   the Atomic app only, *View financial data, orders, and cancellation survey responses*.
4. Enable the **Google Play Android Developer API** in the Cloud project.

Permissions can take a few hours to propagate. A `401` immediately after granting is usually
patience, not a mistake.

---

## 3. Microsoft Store: Azure AD association

**This is the step most likely to be a blocker, which is why it is worth doing before anything is
built on it.**

1. **Azure portal** → *Microsoft Entra ID* → *App registrations* → new registration, e.g.
   `atomic-store-verifier`. Single tenant.
2. Note the **Directory (tenant) ID** and **Application (client) ID**.
3. *Certificates & secrets* → new **client secret**. Copy it now; it is shown once.
4. **Partner Center** → *Account settings* → *User management* → *Azure AD applications* → add the
   application you just registered, and give it a role that can read collections
   (*Manager* is sufficient).

The association in step 4 is what makes the token acceptable to the collections service. An app
registration on its own gets a valid Azure AD token that the Store then rejects, which is a
confusing failure — if step 5 returns `unknown` with a 401 from `collections.mp.microsoft.com`, this
step is where to look.

---

## 4. Prove both stores answer

This is the actual deliverable of P1. Nothing else in the plan is worth writing until both of these
print `owned`.

You need a real purchase to test against. For Microsoft that means a product the signed-in account
owns — any existing add-on will do, PRO or PRO+; it does not have to be Ultimate, which does not
exist yet. Use its Store id.

### 4a. Mint a service ticket

The Store will only hand a key to the app that owns the products, and it wants an Azure AD token
first. That token is the *service ticket*. It is minted from the client secret, which is why it is
minted **here** and pasted into the app rather than the app holding the secret:

```powershell
$env:MS_TENANT_ID = "..."
$env:MS_CLIENT_ID = "..."
$env:MS_CLIENT_SECRET = "..."

$response = Invoke-RestMethod -Method Post `
  -Uri "https://login.microsoftonline.com/$env:MS_TENANT_ID/oauth2/v2.0/token" `
  -Body @{
      grant_type    = "client_credentials"
      client_id     = $env:MS_CLIENT_ID
      client_secret = $env:MS_CLIENT_SECRET
      scope         = "https://onestore.microsoft.com/.default"
  }

$response.access_token | Set-Clipboard
```

A failure here is step 3's app registration, not step 4's association — the two fail at different
points, which is useful.

### 4b. Turn it into a Store ID key

A **debug-only panel at the top of the PRO page** does this. It is deliberately conspicuous, orange,
and headed *TEMPORARY*; it is `Visibility="Collapsed"` in the markup and revealed only under
`#if DEBUG`, so a Release build cannot show it even if the markup outlives its usefulness.

1. Run a Debug build and open the PRO page.
2. Paste the service ticket and press **Get Store ID key**.
3. The key is copied to the clipboard and written to `%TEMP%\atomic_store_id_key.txt`.

Keys are short-lived, so run the probe promptly. Both destinations exist because a value that has to
be retyped off a screen will have expired before it is typed.

If the Store returns nothing, the usual causes are a ticket for the wrong audience or nobody signed
in to the Store on that machine. `Felaktig parameter.` / `Invalid parameter.` means the ticket was
rejected outright.

**Delete the panel once the key is captured** — the markup block in `ProPage.xaml` and the
`GetStoreKey_Click` handler, both marked with the same banner.

### 4c. Probe

```bash
cd backend/functions

export MS_TENANT_ID=...
export MS_CLIENT_ID=...
export MS_CLIENT_SECRET=...
npm run probe -- microsoft --key "<storeIdKey>" --product 9NMMDNZHJKP4
```

```bash
export PLAY_PACKAGE_NAME=com.jlindemann.science
export PLAY_SERVICE_ACCOUNT_JSON=./play-service-account.json
npm run probe -- google --token "<purchaseToken>" --product <playProductId>
```

Exit codes are deliberate:

| Output | Exit | Means |
|---|---|---|
| `owned` | 0 | This store is ready. |
| `notOwned` | 1 | Everything worked; that account does not own that product. Still a pass for setup. |
| `unknown` | 3 | Setup is not finished. The detail names which call failed. |

`unknown` is the one to read carefully — it is the answer most easily mistaken for a plain "no", and
distinguishing the two is the reason the whole verification path returns three states rather than a
boolean.

---

## 5. Store the credentials where the functions can reach them

Once both probe, put them somewhere deployed code can read — `firebase functions:secrets:set` for
the Microsoft secret and the Play key, not `functions.config()`, which is deprecated.

```bash
firebase functions:secrets:set MS_CLIENT_SECRET
firebase functions:secrets:set PLAY_SERVICE_ACCOUNT_JSON
```

Tenant id, client id and package name are not secrets and can be plain environment config.

---

## What P1 does not include

No function is deployed and no client behaviour changes. `redeemUltimate`, the lease document and
the sweep are P2 and P4; the client reading a lease is P3. If step 3 or 4 cannot be made to work,
the fallback named at the end of `ultimate-sync-plan.md` — selling Ultimate separately per store —
costs nothing that has been built here, which is the reason for proving it first.
