/**
 * The password rules, stated where somebody choosing a password can see them.
 *
 * These mirror the server: {@code Auth:Password} in IdentityRegistration plus WeakPasswordValidator.
 * They are a courtesy, not the enforcement — the server checks again and has the final say, and it
 * knows things this file cannot, such as the full banned-password list. What this buys is that
 * somebody is told what is wanted *while typing* rather than after submitting, which is the
 * difference between one attempt and four.
 *
 * The numbers used to disagree. The form asked for eight characters and enabled its own button at
 * eight; the server wanted twelve and refused. Every password chosen at the stated minimum was
 * rejected after the fact, which reads as the software being broken rather than the password being
 * short. One definition now, and the form will not offer to submit something the server will not
 * take.
 */

/** Twelve, not eight. Length is the rule that actually costs an attacker something. */
export const MIN_PASSWORD = 12;

/**
 * Identity's RequiredUniqueChars. Stops "aaaaaaaaaaaa" and "abababababab" from clearing the length
 * rule, which is the obvious way to satisfy a long minimum without making anything harder to guess.
 */
export const MIN_UNIQUE_CHARS = 4;

export interface PasswordRule {
  readonly id: string;
  readonly label: string;
  /**
   * Whether this password satisfies the rule. `identity` is the email address and name the password
   * must not be built out of — the same things the server checks it against.
   */
  readonly met: (password: string, identity: readonly string[]) => boolean;
}

/**
 * Leetspeak is decoded before comparing, because the server does the same. Somebody whose email is
 * bea@shop.com learning that "b3a" is also refused should learn it here, not on submit.
 */
function decodeSubstitutions(value: string): string {
  return value
    .replace(/[@4]/g, 'a')
    .replace(/[3]/g, 'e')
    .replace(/[1!|]/g, 'i')
    .replace(/[0]/g, 'o')
    .replace(/[$5]/g, 's')
    .replace(/[7]/g, 't');
}

function containsIdentity(password: string, identity: readonly string[]): boolean {
  const readings = [password.toLowerCase(), decodeSubstitutions(password.toLowerCase())];

  return identity
    .filter((part) => part.length >= 3)
    .some((part) => readings.some((reading) => reading.includes(part.toLowerCase())));
}

export const PASSWORD_RULES: readonly PasswordRule[] = [
  {
    id: 'length',
    label: `At least ${MIN_PASSWORD} characters`,
    met: (password) => password.length >= MIN_PASSWORD,
  },
  {
    id: 'variety',
    label: `At least ${MIN_UNIQUE_CHARS} different characters`,
    met: (password) => new Set(password).size >= MIN_UNIQUE_CHARS,
  },
  {
    id: 'identity',
    label: 'Not built from your name or email address',
    // Vacuously true until there is something to type, so the list does not open with a red cross
    // against a rule nobody has had the chance to break yet.
    met: (password, identity) => password.length === 0 || !containsIdentity(password, identity),
  },
];

/**
 * The parts of somebody's identity a password must not contain, taken apart the way the server takes
 * them apart: the whole local part of the email, and the words of their name.
 */
export function identityParts(email?: string, name?: string): string[] {
  const parts: string[] = [];

  if (email) {
    const local = email.split('@')[0] ?? '';

    if (local) parts.push(local);
  }

  if (name) parts.push(...name.split(/\s+/).filter(Boolean));

  return parts;
}

/**
 * Whether this password can be submitted at all.
 *
 * Deliberately only the rules this file can actually check. The banned-password list lives on the
 * server and is far longer than anything worth shipping to a browser, so a password can pass here
 * and still be refused — the form reports that when it happens rather than pretending to know.
 */
export function meetsPasswordPolicy(password: string, identity: readonly string[]): boolean {
  return PASSWORD_RULES.every((rule) => rule.met(password, identity)) && password.length > 0;
}
