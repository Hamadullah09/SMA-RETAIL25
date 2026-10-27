/** Joins class names, skipping the falsy ones. */
export function cx(...names: Array<string | false | null | undefined | 0>): string {
  return names.filter(Boolean).join(' ');
}
