import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function formatCurrency(amount: number): string {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: 'AUD',
  }).format(amount);
}

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
