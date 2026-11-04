import { describe, expect, it } from 'vitest';
import { campaignActions, checkTemplate, countsTotal, deliveredPercent, placeholdersOf, previewBody, unknownPlaceholders } from './whatsapp';

const none = { queued: 0, sent: 0, delivered: 0, read: 0, failed: 0, error: 0 };

describe('WhatsApp templates', () => {
  it('finds the placeholders in order, each once', () => {
    expect(placeholdersOf('Hi {FirstName}, you have {Points} points at {Store}. Bye {FirstName}!')).toEqual(['FirstName', 'Points', 'Store']);
    expect(unknownPlaceholders('Hi {FirstName} {Nickname}')).toEqual(['Nickname']);
  });

  it('checks the name, language and body as WhatsApp does', () => {
    expect(checkTemplate({ name: 'spring_sale', language: 'en', body: 'Hi {FirstName}' })).toEqual({});
    expect(checkTemplate({ name: 'Spring Sale', language: 'en', body: 'x' }).name).toMatch(/Lower-case/);
    expect(checkTemplate({ name: '', language: 'en', body: 'x' }).name).toMatch(/name/);
    expect(checkTemplate({ name: 'a', language: 'english', body: 'x' }).language).toMatch(/code/);
    expect(checkTemplate({ name: 'a', language: 'en_US', body: '' }).body).toMatch(/Type the message/);
    expect(checkTemplate({ name: 'a', language: 'ur', body: 'Hi {Nick}' }).body).toBe('Not a placeholder: {Nick}.');
  });

  it('previews with sample values', () => {
    expect(previewBody('Hi {FirstName}, {Points} points at {Store}.')).toBe('Hi Akua, 320 points at Downtown.');
    expect(previewBody('Hi {FirstName} {Nick}', { FirstName: 'Kofi' })).toBe('Hi Kofi {Nick}');
  });
});

describe('campaign figures', () => {
  it('adds up the messages and the delivered share', () => {
    const counts = { queued: 2, sent: 3, delivered: 4, read: 2, failed: 1, error: 1 };
    expect(countsTotal(counts)).toBe(13);
    expect(deliveredPercent(counts)).toBe(60);
    expect(deliveredPercent(none)).toBe(0);
  });

  it('allows what each status allows', () => {
    expect(campaignActions('Draft', none)).toEqual({ edit: true, schedule: true, cancel: false, remove: true });
    expect(campaignActions('Scheduled', { ...none, queued: 3 })).toEqual({ edit: false, schedule: false, cancel: true, remove: false });
    expect(campaignActions('Cancelled', { ...none, queued: 3 }).remove).toBe(true);
    expect(campaignActions('Cancelled', { ...none, sent: 1 }).remove).toBe(false);
    expect(campaignActions('Sent', { ...none, read: 5 })).toEqual({ edit: false, schedule: false, cancel: false, remove: false });
  });
});
