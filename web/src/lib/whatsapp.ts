/**
 * WhatsApp templates and campaigns: the placeholders a template may use (they become its parameters, in order), the
 * checks WhatsApp Business makes on a template's name, a preview with sample values, and the statuses in words.
 */
import type { ChipTone } from '@/components/ui/page';
import type { Errors } from './forms';
import type { CampaignStatus, MessageCounts, MessageStatus } from './types';

export const PLACEHOLDERS: ReadonlyArray<{ key: string; meaning: string; sample: string }> = [
  { key: 'FirstName', meaning: 'First name', sample: 'Akua' },
  { key: 'LastName', meaning: 'Last name', sample: 'Owusu' },
  { key: 'Name', meaning: 'Full name or company', sample: 'Akua Owusu' },
  { key: 'Points', meaning: 'Loyalty points', sample: '320' },
  { key: 'Balance', meaning: 'Balance owing', sample: '145.20' },
  { key: 'Store', meaning: 'The store sending it', sample: 'Downtown' },
  { key: 'ClientNumber', meaning: 'Customer number', sample: '10000001' },
];

const KNOWN = new Set(PLACEHOLDERS.map((p) => p.key));

/** Every `{Word}` of a body, in order, each once. */
export function placeholdersOf(body: string): string[] {
  const seen: string[] = [];
  for (const match of body.matchAll(/\{([^{}\s]+)\}/g)) if (!seen.includes(match[1])) seen.push(match[1]);
  return seen;
}

/** The placeholders of a body the server does not know. */
export function unknownPlaceholders(body: string): string[] {
  return placeholdersOf(body).filter((p) => !KNOWN.has(p));
}

export interface TemplateForm {
  name: string;
  language: string;
  body: string;
}

/** What WhatsApp and the server would refuse in a template. */
export function checkTemplate(form: TemplateForm): Errors<keyof TemplateForm> {
  const errors: Errors<keyof TemplateForm> = {};
  const name = form.name.trim();
  if (!name) errors.name = 'Give the template the name it has at WhatsApp.';
  else if (!/^[a-z0-9_]+$/.test(name)) errors.name = 'Lower-case letters, digits and _ only, like spring_sale.';
  else if (name.length > 512) errors.name = 'At most 512 characters.';
  const language = form.language.trim();
  if (!language) errors.language = 'Choose the language.';
  else if (!/^[a-z]{2,3}(_[A-Z]{2})?$/.test(language)) errors.language = 'A language code like en, en_US or ur.';
  const body = form.body.trim();
  const unknown = unknownPlaceholders(body);
  if (!body) errors.body = 'Type the message.';
  else if (body.length > 1024) errors.body = 'A template body has at most 1,024 characters.';
  else if (unknown.length) errors.body = `Not a placeholder: ${unknown.map((u) => `{${u}}`).join(', ')}.`;
  return errors;
}

/** The body with sample values in place of its placeholders (unknown ones stay as typed). */
export function previewBody(body: string, samples: Record<string, string> = {}): string {
  return body.replace(/\{([^{}\s]+)\}/g, (all, key: string) => samples[key] ?? PLACEHOLDERS.find((p) => p.key === key)?.sample ?? all);
}

export const LANGUAGES: ReadonlyArray<{ code: string; label: string }> = [
  { code: 'en', label: 'English' },
  { code: 'en_US', label: 'English (US)' },
  { code: 'en_GB', label: 'English (UK)' },
  { code: 'ur', label: 'Urdu' },
  { code: 'ar', label: 'Arabic' },
  { code: 'fr', label: 'French' },
];

export const CAMPAIGN_STATUS: Record<CampaignStatus, { label: string; tone: ChipTone }> = {
  Draft: { label: 'Draft', tone: 'plain' },
  Scheduled: { label: 'Scheduled', tone: 'warn' },
  Sending: { label: 'Sending', tone: 'warn' },
  Sent: { label: 'Sent', tone: 'ok' },
  Cancelled: { label: 'Cancelled', tone: 'plain' },
};

export const MESSAGE_STATUS: Record<MessageStatus, { label: string; tone: ChipTone; key: keyof MessageCounts }> = {
  Queued: { label: 'Queued', tone: 'plain', key: 'queued' },
  Sent: { label: 'Sent', tone: 'warn', key: 'sent' },
  Delivered: { label: 'Delivered', tone: 'ok', key: 'delivered' },
  Read: { label: 'Read', tone: 'ok', key: 'read' },
  Failed: { label: 'Failed', tone: 'danger', key: 'failed' },
  Error: { label: 'Error', tone: 'danger', key: 'error' },
};

export const MESSAGE_STATUSES = Object.keys(MESSAGE_STATUS) as MessageStatus[];

/** All the messages of the counts. */
export function countsTotal(counts: MessageCounts): number {
  return counts.queued + counts.sent + counts.delivered + counts.read + counts.failed + counts.error;
}

/** Messages that reached the phone (delivered or read) out of those sent, in percent; 0 before any was sent. */
export function deliveredPercent(counts: MessageCounts): number {
  const out = counts.sent + counts.delivered + counts.read + counts.failed;
  return out === 0 ? 0 : Math.round(((counts.delivered + counts.read) / out) * 1000) / 10;
}

/**
 * A draft can be changed and scheduled; a draft, or a cancelled one whose messages all stayed queued, can be deleted (the
 * server keeps a campaign once any message went out or was refused).
 */
export function campaignActions(status: CampaignStatus, counts: MessageCounts) {
  const sentSome = counts.sent + counts.delivered + counts.read + counts.failed + counts.error > 0;
  return {
    edit: status === 'Draft',
    schedule: status === 'Draft',
    cancel: status === 'Scheduled' || status === 'Sending',
    remove: status === 'Draft' || (status === 'Cancelled' && !sentSome),
  };
}
