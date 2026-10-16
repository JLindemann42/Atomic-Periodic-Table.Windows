/**
 * What a store said about ownership.
 *
 * Three states, never a boolean. "The store could not be reached" and "the user does not own it"
 * are different facts with opposite consequences: the first is a reason to retry and leave the
 * lease alone, the second is a reason to let it lapse. Collapsing them is the bug that once made
 * this app's PRO disappear the moment a user opened the PRO page (D28), and a boolean return type
 * makes that collapse the path of least resistance.
 */
export type OwnershipResult =
  | { status: "owned" }
  | { status: "notOwned" }
  | { status: "unknown"; detail: string };

/** True only for a definite yes. */
export const isOwned = (result: OwnershipResult): boolean => result.status === "owned";

/**
 * True only for a definite no.
 *
 * The only answer that may end an entitlement. Written as its own function so that a caller
 * revoking on `!isOwned(...)` reads as obviously wrong.
 */
export const isDefinitelyNotOwned = (result: OwnershipResult): boolean => result.status === "notOwned";