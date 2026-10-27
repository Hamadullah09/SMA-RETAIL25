/**
 * Keyboard shortcuts written the way the desktop shows them on its key caps: `Ctrl+K`, `F5`, `Ctrl+Shift+G`, `Del`,
 * `/`. On a Mac, Ctrl also matches the Command key.
 */

export interface Combo {
  ctrl: boolean;
  shift: boolean;
  alt: boolean;
  key: string;
}

const KEY_NAMES: Record<string, string> = {
  del: 'delete',
  esc: 'escape',
  enter: 'enter',
  space: ' ',
  up: 'arrowup',
  down: 'arrowdown',
  left: 'arrowleft',
  right: 'arrowright',
};

/** `Ctrl+Shift+G` → { ctrl, shift, key: 'g' }. */
export function parseCombo(text: string): Combo {
  const parts = text.split('+').map((p) => p.trim());
  // "Ctrl++" would be the plus key; nobody uses it here.
  const last = parts.pop() ?? '';
  const mods = parts.map((p) => p.toLowerCase());
  const lower = last.toLowerCase();
  return {
    ctrl: mods.includes('ctrl') || mods.includes('cmd'),
    shift: mods.includes('shift'),
    alt: mods.includes('alt'),
    key: KEY_NAMES[lower] ?? lower,
  };
}

interface KeyLike {
  key: string;
  code?: string;
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
}

/** Whether a key event is the combo. Digits match by code too, so Ctrl+Shift+1 is not taken for Ctrl+!. */
export function matchCombo(event: KeyLike, combo: Combo | string): boolean {
  const c = typeof combo === 'string' ? parseCombo(combo) : combo;
  const ctrl = event.ctrlKey || event.metaKey;
  if (ctrl !== c.ctrl || event.altKey !== c.alt) return false;
  const key = event.key.toLowerCase();
  // `/` and `?` come with Shift on many layouts; only require Shift when the combo says so for letters and F-keys.
  const shiftMatters = c.key.length > 1 || /[a-z0-9]/.test(c.key);
  if (shiftMatters && event.shiftKey !== c.shift) return false;
  if (/^[0-9]$/.test(c.key) && event.code) return event.code === 'Digit' + c.key || event.code === 'Numpad' + c.key;
  return key === c.key;
}

/** Plain keys (letters, `/`, Delete) must not fire while the user types in a box. */
export function isTypingTarget(target: EventTarget | null): boolean {
  if (!target || typeof (target as HTMLElement).tagName !== 'string') return false;
  const element = target as HTMLElement;
  const tag = element.tagName;
  if (tag === 'TEXTAREA' || tag === 'SELECT') return true;
  if (tag === 'INPUT') {
    const type = (element as HTMLInputElement).type;
    return !['checkbox', 'radio', 'button', 'submit', 'reset', 'file', 'range', 'color'].includes(type);
  }
  return element.isContentEditable === true;
}

/** Whether a combo may fire while typing: those with Ctrl or Alt, the F-keys and Esc. */
export function firesWhileTyping(combo: Combo | string): boolean {
  const c = typeof combo === 'string' ? parseCombo(combo) : combo;
  return c.ctrl || c.alt || c.key === 'escape' || /^f\d{1,2}$/.test(c.key);
}
