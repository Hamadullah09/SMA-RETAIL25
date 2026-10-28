/**
 * Setting up an RFID hub that reads its readers from the server (docs/RFID.md, "A hub that reads its readers from the
 * server").
 */

/** The server address a hub is given: this site's origin with a trailing slash. */
export function serverAddress(origin: string): string {
  return origin.replace(/\/+$/, '') + '/';
}

/** The hub's command line with the server and its key. */
export function hubCommand(origin: string, key: string): string {
  return `SmaRetail.RfidHub.exe --server ${serverAddress(origin)} --serverkey ${key}`;
}

/** The `appsettings.json` next to the hub, which keeps the key off the service's command line. */
export function hubSettings(origin: string, key: string): string {
  return JSON.stringify({ server: serverAddress(origin), serverkey: key });
}

/** A key's hint as shown in the list: its first characters and an ellipsis. */
export function keyHint(hint: string): string {
  return hint.endsWith('…') ? hint : `${hint}…`;
}

/** Whether a hub name is acceptable: what the server takes (at most 40 characters; empty means "RFID hub"). */
export function checkHubName(name: string): string | undefined {
  return name.trim().length > 40 ? 'A hub name has at most 40 characters.' : undefined;
}
