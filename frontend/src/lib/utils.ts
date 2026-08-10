import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/**
 * Re-exported so the eighteen screens that already import it from here keep working, while the money
 * itself is decided in one place. This was `Intl.NumberFormat('en-AU', { currency: 'AUD' })` — every
 * back-office figure printed in Australian dollars no matter what the shop had configured.
 */
export { formatCurrency } from './currency';

export function formatDate(date: string | Date): string {
  return new Intl.DateTimeFormat('en-AU', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(date));
}

/**
 * The record id a `<select>` or `<input>` just reported.
 *
 * A DOM control always hands back a string, whatever was put into the option. Empty stays empty
 * rather than becoming 0: '' is "nothing chosen", and 0 is the id a form holds while it is creating
 * a record — collapsing the two would turn an unset filter into "the new one".
 */
export function recordIdFrom(value: string): number | '' {
  return value === '' ? '' : Number(value);
}
