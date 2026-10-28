import { describe, expect, it } from 'vitest';
import { checkHubName, hubCommand, hubSettings, keyHint, serverAddress } from './rfid';

const key = 'rfh_0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJ';

describe('RFID hub set-up', () => {
  it('gives the hub this site as its server, with one trailing slash', () => {
    expect(serverAddress('https://pos.example.com')).toBe('https://pos.example.com/');
    expect(serverAddress('https://pos.example.com/')).toBe('https://pos.example.com/');
    expect(serverAddress('http://localhost:5080//')).toBe('http://localhost:5080/');
  });

  it('writes the command line of docs/RFID.md', () => {
    expect(hubCommand('https://pos.example.com', key)).toBe(`SmaRetail.RfidHub.exe --server https://pos.example.com/ --serverkey ${key}`);
  });

  it('writes the appsettings.json that keeps the key off the command line', () => {
    expect(JSON.parse(hubSettings('https://pos.example.com', key))).toEqual({ server: 'https://pos.example.com/', serverkey: key });
  });

  it('shows a key by its first characters only', () => {
    expect(keyHint('rfh_0123')).toBe('rfh_0123…');
    expect(keyHint('rfh_0123…')).toBe('rfh_0123…');
  });

  it('takes hub names of at most 40 characters', () => {
    expect(checkHubName('Back office PC')).toBeUndefined();
    expect(checkHubName('')).toBeUndefined();
    expect(checkHubName('x'.repeat(41))).toBe('A hub name has at most 40 characters.');
  });
});
