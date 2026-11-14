import { describe, expect, it } from 'vitest';
import { defaultHold, locationErrors, mainLocation } from './locations';
import type { LocationInfo } from './types';

function location(id: string, code: string, isHold = false): LocationInfo {
  return { id, version: 'v-1', code, name: code, isHold, items: 0 };
}

describe('store locations', () => {
  const pos = location('l-pos', 'POS');
  const hold = location('l-hld', 'HLD', true);

  it('checks a new code: 1 to 3 characters, once per store', () => {
    expect(locationErrors({ code: 'bak', name: 'Back room', isHold: false }, [pos], null)).toEqual({});
    expect(locationErrors({ code: '', name: '', isHold: false }, [pos], null).code).toBe('A location code has 1 to 3 characters.');
    expect(locationErrors({ code: 'HOLD', name: '', isHold: false }, [pos], null).code).toBe('A location code has 1 to 3 characters.');
    expect(locationErrors({ code: 'pos', name: '', isHold: false }, [pos], null).code).toBe('The store has a location POS already.');
    expect(locationErrors({ code: 'X', name: 'n'.repeat(51), isHold: false }, [pos], null).name).toBe('A location name has at most 50 characters.');
  });

  it('keeps the code of a location being renamed, and never makes the only location the hold one', () => {
    expect(locationErrors({ code: 'POS', name: 'Front', isHold: false }, [pos], pos)).toEqual({});
    expect(locationErrors({ code: 'POS', name: 'Front', isHold: true }, [pos], pos).isHold).toMatch(/Add another location first/);
    expect(locationErrors({ code: 'HLD', name: 'Hold', isHold: true }, [hold, pos], hold)).toEqual({});
    // A new location marked as the hold one makes the second.
    expect(locationErrors({ code: 'HLD', name: 'Hold', isHold: true }, [pos], null)).toEqual({});
  });

  it('puts new items and lists at the first location that is not the hold one, and offers the hold location for failed goods', () => {
    expect(mainLocation([hold, pos])).toBe(pos);
    expect(mainLocation([location('l-a', 'A'), pos])?.code).toBe('A');
    expect(mainLocation([])).toBeUndefined();
    expect(defaultHold([hold, pos], 'l-pos')).toBe('l-hld');
    expect(defaultHold([hold, pos], 'l-hld')).toBe('');
    expect(defaultHold([pos], 'l-pos')).toBe('');
  });
});
