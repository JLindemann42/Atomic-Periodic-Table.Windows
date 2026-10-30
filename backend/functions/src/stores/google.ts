/**
 * Ownership checks against the Google Play Developer API.
 *
 * Play works the other way round from Microsoft: the client holds a purchase token that names the
 * purchase directly, and the server asks Play what that token is worth. The token is a bearer
 * string - anyone holding it can ask the same question and get the same yes - which is why
 * redemption binds a token to the first account that uses it. See receipts/ in firestore.rules.
 *
 * Prerequisite: a Google service account with Play Developer API access, granted "View financial
 * data" on the app in Play Console. See docs/ultimate-sync-setup.md.
 */

import { GoogleAuth } from "google-auth-library";
import { OwnershipResult } from "./result";

const SCOPE = "https://www.googleapis.com/auth/androidpublisher";

/** purchaseState in the products.get response. 0 = purchased, 1 = cancelled, 2 = pending. */
const PURCHASED = 0;

export interface GoogleCredentials {
  packageName: string;
  /** The service-account JSON, as an object. Never a path inside a deployed function. */
  serviceAccount: Record<string, unknown>;
}

async function authorizedFetch(
  url: string,
  credentials: GoogleCredentials,
): Promise<{ ok: boolean; status: number; body: string }> {
  const auth = new GoogleAuth({ credentials: credentials.serviceAccount as never, scopes: [SCOPE] });
  const client = await auth.getClient();
  const token = await client.getAccessToken();

  const response = await fetch(url, {
    headers: { Authorization: `Bearer ${token.token ?? ""}` },
  });

  return { ok: response.ok, status: response.status, body: await response.text() };
}

/**
 * Is this purchase token a live purchase of `productId`?
 */
export async function ownsProduct(
  purchaseToken: string,
  productId: string,
  credentials: GoogleCredentials,
): Promise<OwnershipResult> {
  const url =
    `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/` +
    `${encodeURIComponent(credentials.packageName)}/purchases/products/` +
    `${encodeURIComponent(productId)}/tokens/${encodeURIComponent(purchaseToken)}`;

  let response: { ok: boolean; status: number; body: string };
  try {
    response = await authorizedFetch(url, credentials);
  } catch (error) {
    return { status: "unknown", detail: `network: ${String(error)}` };
  }

  // A token Play has never seen, or one for a different product, is a definite no - it is the
  // answer to "is this a purchase", and Play knows every purchase it made.
  if (response.status === 404 || response.status === 400) {
    return { status: "notOwned" };
  }

  if (!response.ok) {
    return { status: "unknown", detail: `play: ${response.status} ${response.body}` };
  }

  try {
    const parsed = JSON.parse(response.body) as { purchaseState?: number };
    return parsed.purchaseState === PURCHASED ? { status: "owned" } : { status: "notOwned" };
  } catch (error) {
    return { status: "unknown", detail: `parse: ${String(error)}` };
  }
}

/**
 * Purchase tokens voided since `sinceMillis` - refunds, chargebacks and revocations.
 *
 * Cheaper than re-asking about every live lease, and the only signal that arrives without being
 * asked for. The sweep uses this first and falls back to per-token checks for leases it did not
 * see here.
 */
export async function voidedTokens(
  sinceMillis: number,
  credentials: GoogleCredentials,
): Promise<{ tokens: string[] } | { error: string }> {
  const url =
    `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/` +
    `${encodeURIComponent(credentials.packageName)}/purchases/voidedpurchases` +
    `?startTime=${sinceMillis}`;

  try {
    const response = await authorizedFetch(url, credentials);
    if (!response.ok) return { error: `play: ${response.status} ${response.body}` };

    const parsed = JSON.parse(response.body) as {
      voidedPurchases?: Array<{ purchaseToken?: string }>;
    };

    return {
      tokens: (parsed.voidedPurchases ?? [])
        .map((v) => v.purchaseToken)
        .filter((t): t is string => typeof t === "string"),
    };
  } catch (error) {
    return { error: String(error) };
  }
}