/**
 * A store's locations on the web: the checks before one is added or renamed (as the server makes them), and which one is
 * the store's main location (where new items and lists go) and which its hold location (where failed goods go).
 */
import type { LocationInfo } from './types';

export interface LocationForm {
  code: string;
  name: string;
  isHold: boolean;
}

/** What stops a location from being saved, by field; empty when it may go. The code is only checked for a new one. */
export function locationErrors(form: LocationForm, list: readonly LocationInfo[], editing: LocationInfo | null): Partial<Record<keyof LocationForm, string>> {
  const errors: Partial<Record<keyof LocationForm, string>> = {};
  if (!editing) {
    const code = form.code.trim().toUpperCase();
    if (code.length < 1 || code.length > 3) errors.code = 'A location code has 1 to 3 characters.';
    else if (list.some((l) => l.code.toUpperCase() === code)) errors.code = `The store has a location ${code} already.`;
  }
  if (form.name.trim().length > 50) errors.name = 'A location name has at most 50 characters.';
  // A new location makes two; a store's only location cannot hold its failed goods.
  if (form.isHold && editing && list.length < 2) errors.isHold = 'Add another location first: the only location cannot be the hold location.';
  return errors;
}

/** The location new items and lists use when none is chosen: the first by code that is not the hold location. */
export function mainLocation(list: readonly LocationInfo[]): LocationInfo | undefined {
  return list.find((l) => !l.isHold) ?? list[0];
}

/**
 * The hold location an inspection of output made at `outputLocationId` offers first: the store's marked one, unless it is
 * the output's own location. Empty when there is none to offer.
 */
export function defaultHold(list: readonly LocationInfo[], outputLocationId: string): string {
  return list.find((l) => l.isHold && l.id !== outputLocationId)?.id ?? '';
}
