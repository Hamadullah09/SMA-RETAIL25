import { describe, expect, it } from 'vitest';
import { firesWhileTyping, isTypingTarget, matchCombo, parseCombo } from './keys';

function key(k: string, mods: Partial<{ ctrl: boolean; shift: boolean; alt: boolean; meta: boolean; code: string }> = {}) {
  return {
    key: k,
    code: mods.code,
    ctrlKey: !!mods.ctrl,
    metaKey: !!mods.meta,
    shiftKey: !!mods.shift,
    altKey: !!mods.alt,
  };
}

describe('keyboard shortcuts', () => {
  it('reads the key caps', () => {
    expect(parseCombo('Ctrl+Shift+G')).toEqual({ ctrl: true, shift: true, alt: false, key: 'g' });
    expect(parseCombo('Del')).toEqual({ ctrl: false, shift: false, alt: false, key: 'delete' });
    expect(parseCombo('F5').key).toBe('f5');
  });

  it('matches what was pressed', () => {
    expect(matchCombo(key('k', { ctrl: true }), 'Ctrl+K')).toBe(true);
    expect(matchCombo(key('K', { meta: true }), 'Ctrl+K')).toBe(true);
    expect(matchCombo(key('k'), 'Ctrl+K')).toBe(false);
    expect(matchCombo(key('k', { ctrl: true, shift: true }), 'Ctrl+K')).toBe(false);
    expect(matchCombo(key('F5'), 'F5')).toBe(true);
    expect(matchCombo(key('Delete'), 'Del')).toBe(true);
    expect(matchCombo(key('/'), '/')).toBe(true);
  });

  it('matches digits by their key position', () => {
    expect(matchCombo(key('1', { ctrl: true, code: 'Digit1' }), 'Ctrl+1')).toBe(true);
    expect(matchCombo(key('&', { ctrl: true, code: 'Digit1' }), 'Ctrl+1')).toBe(true);
    expect(matchCombo(key('0', { ctrl: true, code: 'Digit0' }), 'Ctrl+1')).toBe(false);
  });

  it('lets only Ctrl, Alt, F-keys and Esc fire while typing', () => {
    expect(firesWhileTyping('Ctrl+S')).toBe(true);
    expect(firesWhileTyping('F5')).toBe(true);
    expect(firesWhileTyping('Esc')).toBe(true);
    expect(firesWhileTyping('/')).toBe(false);
    expect(firesWhileTyping('Del')).toBe(false);
  });

  it('knows a text box from a button', () => {
    expect(isTypingTarget({ tagName: 'INPUT', type: 'text' } as unknown as EventTarget)).toBe(true);
    expect(isTypingTarget({ tagName: 'INPUT', type: 'checkbox' } as unknown as EventTarget)).toBe(false);
    expect(isTypingTarget({ tagName: 'TEXTAREA' } as unknown as EventTarget)).toBe(true);
    expect(isTypingTarget({ tagName: 'BUTTON' } as unknown as EventTarget)).toBe(false);
    expect(isTypingTarget(null)).toBe(false);
  });
});
