/**
 * Ownership checks against the Microsoft Store collections API.
 *
 * The client cannot be asked "do you own this" - that is the whole reason this file exists. What
 * the client *can* do is hand over a Store ID key, which is a signed, short-lived token naming the
 * Store account without granting anything. This module trades that key for the account's product
 * collection, server-side, using an Azure AD identity the user does not control.
 *
 * Prerequisite: an Azure AD application registered and associated with the Partner Center account
 * that publishes the app. That association is what makes the token below acceptable to the
 * collections service, and it is the single most likely thing in this plan to be a blocker - see
 * docs/ultimate-sync-setup.md.
 */

import { OwnershipResult } from "./result";

/** Audience for the Azure AD token. Not a URL that is ever fetched - an identifier. */
const COLLECTIONS_AUDIENCE = "https://onestore.microsoft.com";

const COLLECTIONS_QUERY = "https://collections.mp.microsoft.com/v6.0/collections/query";

export interface MicrosoftCredentials {
  tenantId: string;
  clientId: string;
  clientSecret: string;
}

/**
 * An Azure AD client-credentials token for the collections service.
 *
 * Deliberately not cached across invocations. Tokens last an hour and a redemption is rare, so a
 * cache would trade a negligible saving for a class of bug - a stale token surviving a credential
 * rotation - that only shows up as purchases mysteriously failing to verify.
 */
async function accessToken(credentials: MicrosoftCredentials): Promise<string> {
  const response = await fetch(
    `https://login.microsoftonline.com/${credentials.tenantId}/oauth2/v2.0/token`,
    {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        grant_type: "client_credentials",
        client_id: credentials.clientId,
        client_secret: credentials.clientSecret,
        scope: `${COLLECTIONS_AUDIENCE}/.default`,
      }),
    },
  );

  const body = await response.text();
  if (!response.ok) {
    throw new Error(`Azure AD refused the token request: ${response.status} ${body}`);
  }

  const parsed = JSON.parse(body) as { access_token?: string };
  if (!parsed.access_token) {
    throw new Error("Azure AD returned no access_token.");
  }
  return parsed.access_token;
}

/**
 * Does this Store account own `productId`?
 *
 * Returns a three-state answer rather than a boolean. "The service could not be reached" and "the
 * user does not own it" must never collapse into the same value: one is a reason to retry, the
 * other is a reason to let a lease lapse, and treating the first as the second is precisely the
 * bug that made PRO disappear on this app once already (see D28 in porting-notes).
 */
export async function ownsProduct(
  storeIdKey: string,
  productId: string,
  credentials: MicrosoftCredentials,
): Promise<OwnershipResult> {
  let token: string;
  try {
    token = await accessToken(credentials);
  } catch (error) {
    return { status: "unknown", detail: `token: ${String(error)}` };
  }

  let response: Response;
  try {
    response = await fetch(COLLECTIONS_QUERY, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
        // Required by the service; any stable GUID per request is acceptable.
        "Request-Id": crypto.randomUUID(),
      },
      body: JSON.stringify({
        beneficiaries: [{ identitytype: "b2b", identityValue: storeIdKey, localTicketReference: "" }],
        // Durables only. A consumable would never be an entitlement.
        productSkuIds: [{ productId }],
        maxPageSize: 100,
      }),
    });
  } catch (error) {
    return { status: "unknown", detail: `network: ${String(error)}` };
  }

  const body = await response.text();

  // A rejected Store ID key is a definite "this key proves nothing", not an outage - but it is
  // also not proof the user lacks the product, so it stays "unknown" and the caller refuses the
  // redemption rather than revoking anything.
  if (!response.ok) {
    return { status: "unknown", detail: `collections: ${response.status} ${body}` };
  }

  let items: Array<{ productId?: string; status?: string }>;
  try {
    items = (JSON.parse(body) as { items?: Array<{ productId?: string; status?: string }> }).items ?? [];
  } catch (error) {
    return { status: "unknown", detail: `parse: ${String(error)}` };
  }

  const owned = items.some(
    (item) => item.productId === productId && (item.status ?? "Active") === "Active",
  );

  return owned ? { status: "owned" } : { status: "notOwned" };
}
