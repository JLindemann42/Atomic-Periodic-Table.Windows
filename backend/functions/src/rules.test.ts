/**
 * What the Firestore rules must guarantee.
 *
 * Runs against the emulator:  npm run test:rules
 *
 * These are the tests that matter most in the whole entitlement design. Every other failure mode
 * shows up as a user complaining; a rules failure shows up as nobody complaining at all, because
 * the people exploiting it are getting what they wanted.
 */

import assert from "node:assert/strict";
import { test, before, after } from "node:test";
import {
  assertFails,
  assertSucceeds,
  initializeTestEnvironment,
  RulesTestEnvironment,
} from "@firebase/rules-unit-testing";
import { readFileSync } from "node:fs";
import { doc, getDoc, setDoc, updateDoc } from "firebase/firestore";

let env: RulesTestEnvironment;

const ALICE = "alice-uid";
const MALLORY = "mallory-uid";

before(async () => {
  env = await initializeTestEnvironment({
    projectId: "atomic-rules-test",
    firestore: { rules: readFileSync("../firestore.rules", "utf8") },
  });
});

after(async () => {
  await env.cleanup();
});

test("a signed-in user reads and writes their own progress", async () => {
  const db = env.authenticatedContext(ALICE).firestore();

  await assertSucceeds(setDoc(doc(db, "users", ALICE), { xp: 1200, level: 12 }));
  await assertSucceeds(getDoc(doc(db, "users", ALICE)));
});

test("nobody touches another user's progress", async () => {
  const db = env.authenticatedContext(MALLORY).firestore();

  await assertFails(getDoc(doc(db, "users", ALICE)));
  await assertFails(setDoc(doc(db, "users", ALICE), { xp: 999999 }));
});

test("the per-platform tiers stay writable - they are descriptive, not load-bearing", async () => {
  const db = env.authenticatedContext(ALICE).firestore();

  await assertSucceeds(
    setDoc(doc(db, "users", ALICE), { entitlements: { windows: "pro+", android: "pro" } }),
  );
});

/**
 * The defect this whole plan exists to close. Before the split, this write was how a client
 * granted itself the product.
 */
test("a client cannot write entitlements.ultimate into its own progress document", async () => {
  const db = env.authenticatedContext(ALICE).firestore();

  await assertFails(
    setDoc(doc(db, "users", ALICE), { entitlements: { windows: "free", ultimate: true } }),
  );
});

test("nor can it sneak ultimate in through an update", async () => {
  const db = env.authenticatedContext(ALICE).firestore();

  await assertSucceeds(setDoc(doc(db, "users", ALICE), { xp: 10 }));
  await assertFails(updateDoc(doc(db, "users", ALICE), { "entitlements.ultimate": true }));
});

test("a user reads their own entitlement and cannot write it", async () => {
  await env.withSecurityRulesDisabled(async (admin) => {
    await setDoc(doc(admin.firestore(), "entitlements", ALICE), {
      ultimate: { active: true, source: "microsoft" },
    });
  });

  const db = env.authenticatedContext(ALICE).firestore();

  const snapshot = await assertSucceeds(getDoc(doc(db, "entitlements", ALICE)));
  assert.equal(snapshot.data()?.ultimate.active, true);

  await assertFails(setDoc(doc(db, "entitlements", ALICE), { ultimate: { active: true } }));
});

test("nobody reads another user's entitlement", async () => {
  const db = env.authenticatedContext(MALLORY).firestore();
  await assertFails(getDoc(doc(db, "entitlements", ALICE)));
});

/**
 * A purchase token is a bearer credential. A client that can list receipts can hand out its own,
 * and one purchase becomes unlimited accounts - so the collection is invisible, not merely
 * read-only.
 */
test("receipts are invisible to every client", async () => {
  await env.withSecurityRulesDisabled(async (admin) => {
    await setDoc(doc(admin.firestore(), "receipts", "hash-abc"), { uid: ALICE });
  });

  const db = env.authenticatedContext(ALICE).firestore();

  await assertFails(getDoc(doc(db, "receipts", "hash-abc")));
  await assertFails(setDoc(doc(db, "receipts", "hash-xyz"), { uid: ALICE }));
});

test("an unauthenticated client gets nothing at all", async () => {
  const db = env.unauthenticatedContext().firestore();

  await assertFails(getDoc(doc(db, "users", ALICE)));
  await assertFails(getDoc(doc(db, "entitlements", ALICE)));
});

test("collections nobody named stay closed", async () => {
  const db = env.authenticatedContext(ALICE).firestore();

  await assertFails(getDoc(doc(db, "somethingNew", ALICE)));
  await assertFails(setDoc(doc(db, "somethingNew", ALICE), { x: 1 }));
});
