import { formatCount } from './format';
import type { UploadResponse } from './types';

/** What a package upload did, in one sentence for the user, and whether it failed. */
export function describeUpload(result: UploadResponse | null | undefined, locale?: string): { failed: boolean; message: string } {
  if (!result) return { failed: false, message: 'The package went in.' };
  if (result.status === 'Failed') return { failed: true, message: result.error || 'The server refused the package.' };
  if (result.status === 'Duplicate') return { failed: false, message: 'This package was sent before; nothing changed.' };
  const parts: string[] = [];
  if (result.inserted > 0) parts.push(`${formatCount(result.inserted, locale)} rows added`);
  if (result.updated > 0) parts.push(`${formatCount(result.updated, locale)} changed`);
  if (result.deleted > 0) parts.push(`${formatCount(result.deleted, locale)} deleted`);
  return { failed: false, message: parts.length ? `The package went in: ${parts.join(', ')}.` : 'The package went in; it changed nothing.' };
}
