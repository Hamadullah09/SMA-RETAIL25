/**
 * UPC-A, for saying on screen what a barcode will print as.
 *
 * The server owns the rules — it decides what a label draws and what a scan rings — and this mirrors
 * only the check, so a form can tell somebody before they print a sheet that "12345" will come out as
 * Code 39 and not UPC-A. See Retail25.Domain.Identification.UpcA for the reasoning behind each rule.
 */

/** Twelve digits whose last is the right check digit for the first eleven. */
export function isUpcA(value: string | null | undefined): boolean {
  const code = value?.trim() ?? '';
  if (!/^\d{12}$/.test(code)) return false;

  return Number(code[11]) === upcCheckDigit(code.slice(0, 11));
}

/** Three times the digits in odd positions, plus the even ones, taken up to the next ten. */
export function upcCheckDigit(elevenDigits: string): number {
  if (!/^\d{11}$/.test(elevenDigits)) {
    throw new Error('A UPC-A check digit is computed over exactly eleven digits.');
  }

  let sum = 0;

  for (let index = 0; index < 11; index++) {
    const digit = Number(elevenDigits[index]);
    sum += index % 2 === 0 ? digit * 3 : digit;
  }

  return (10 - (sum % 10)) % 10;
}

export type BarcodeKind = 'upca' | 'code39' | 'none';

/** What a label will carry for this item when the run is left on its default. */
export function labelBarcodeKind(upc: string | null | undefined, stockCode: string): BarcodeKind {
  if (isUpcA(upc)) return 'upca';

  // Code 39 carries upper-case letters, digits and - . space $ / + % — the same set the server checks.
  const fallback = (upc?.trim() || stockCode).toUpperCase();
  return /^[0-9A-Z\-. $/+%]+$/.test(fallback) ? 'code39' : 'none';
}
