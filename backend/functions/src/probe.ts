/**
 * Prove the store APIs by hand, before a single Cloud Function is written.
 *
 * This is the whole point of P1. The Azure AD association behind the Microsoft collections API is
 * the item most likely to be a blocker, and finding that out after building three functions and a
 * data model would be an expensive way to learn it. Run this first; if both stores answer, the
 * rest of the plan is ordinary work.
 *
 *   npm run probe -- microsoft --key <storeIdKey> --product 9NXXXXXXXXXX
 *   npm run probe -- google    --token <purchaseToken> --product ultimate
 *
 * Credentials come from the environment, never from arguments - an argument ends up in shell
 * history:
 *
 *   MS_TENANT_ID, MS_CLIENT_ID, MS_CLIENT_SECRET
 *   PLAY_PACKAGE_NAME, PLAY_SERVICE_ACCOUNT_JSON   (path to the key file)
 */

import { readFileSync } from "node:fs";
import * as microsoft from "./stores/microsoft";
import * as google from "./stores/google";

function flag(name: string): string | undefined {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

function required(name: string, value: string | undefined): string {
  if (!value) {
    console.error(`missing --${name}`);
    process.exit(2);
  }
  return value;
}

function env(name: string): string {
  const value = process.env[name];
  if (!value) {
    console.error(`missing environment variable ${name}`);
    process.exit(2);
  }
  return value;
}

async function probeMicrosoft(): Promise<void> {
  const result = await microsoft.ownsProduct(
    required("key", flag("key")),
    required("product", flag("product")),
    {
      tenantId: env("MS_TENANT_ID"),
      clientId: env("MS_CLIENT_ID"),
      clientSecret: env("MS_CLIENT_SECRET"),
    },
  );
  report("Microsoft Store", result);
}

async function probeGoogle(): Promise<void> {
  const result = await google.ownsProduct(
    required("token", flag("token")),
    required("product", flag("product")),
    {
      packageName: env("PLAY_PACKAGE_NAME"),
      serviceAccount: JSON.parse(readFileSync(env("PLAY_SERVICE_ACCOUNT_JSON"), "utf8")),
    },
  );
  report("Google Play", result);
}

function report(store: string, result: { status: string; detail?: string }): void {
  console.log(`${store}: ${result.status}${result.detail ? ` - ${result.detail}` : ""}`);

  // "unknown" exits non-zero on purpose. It is the answer that means the setup is not finished,
  // and it is the one a reader is most likely to mistake for a plain "no".
  process.exit(result.status === "owned" ? 0 : result.status === "notOwned" ? 1 : 3);
}

const store = process.argv[2];
if (store === "microsoft") void probeMicrosoft();
else if (store === "google") void probeGoogle();
else {
  console.error("usage: npm run probe -- <microsoft|google> [flags]");
  process.exit(2);
}
