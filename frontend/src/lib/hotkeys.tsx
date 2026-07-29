'use client';

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';

/**
 * Scoped, declarative hotkeys (doc 08 §Keyboard model).
 *
 * Scopes are a stack, and only the top one receives keys. That is what lets `F4` mean *Pay* on the
 * sale screen and *Copies* inside the payment dialog, exactly as the legacy system behaved (guide
 * p.8) — without every handler having to ask "is a dialog open?".
 *
 * Because every binding is registered with its label, the cheat sheet and the command palette are
 * generated from the live registry rather than maintained alongside it. They cannot drift.
 */

export type HotkeyScope = 'pos' | 'dialog' | 'grid' | 'global';

export interface HotkeyBinding {
  /** `KeyboardEvent.key`, e.g. `F4`, or a modifier chord such as `Ctrl+K`. */
  combo: string;
  scope: HotkeyScope;
  label: string;
  group?: string;
  handler: (event: KeyboardEvent) => void;
  /** Excluded from the palette but still bound — used for raw entry keys. */
  hidden?: boolean;
  disabled?: boolean;
}

interface HotkeyRegistry {
  register: (binding: HotkeyBinding) => () => void;
  pushScope: (scope: HotkeyScope) => () => void;
  activeScope: HotkeyScope;
  bindings: HotkeyBinding[];
}

const HotkeyContext = createContext<HotkeyRegistry | null>(null);

/** Normalises a keyboard event into the `Ctrl+Shift+K` form bindings are written in. */
export function comboFrom(event: KeyboardEvent): string {
  const parts: string[] = [];
  if (event.ctrlKey || event.metaKey) parts.push('Ctrl');
  if (event.altKey) parts.push('Alt');
  if (event.shiftKey && event.key.length > 1) parts.push('Shift');
  parts.push(event.key.length === 1 ? event.key.toUpperCase() : event.key);
  return parts.join('+');
}

/**
 * Typing in a field should not fire a shortcut — except the function keys, which are the whole
 * point of a till that never needs a mouse and must work while the cursor sits in the scan box.
 */
function shouldIgnore(event: KeyboardEvent): boolean {
  const target = event.target as HTMLElement | null;
  if (!target) return false;

  const isFunctionKey = /^F\d{1,2}$/.test(event.key);
  if (isFunctionKey || event.ctrlKey || event.metaKey) return false;

  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
}

export function HotkeyProvider({ children }: { children: ReactNode }) {
  const bindingsRef = useRef<HotkeyBinding[]>([]);
  const [bindings, setBindings] = useState<HotkeyBinding[]>([]);
  const [scopeStack, setScopeStack] = useState<HotkeyScope[]>(['pos']);

  const activeScope = scopeStack[scopeStack.length - 1] ?? 'pos';

  const register = useCallback((binding: HotkeyBinding) => {
    bindingsRef.current = [...bindingsRef.current, binding];
    setBindings(bindingsRef.current);

    return () => {
      bindingsRef.current = bindingsRef.current.filter((b) => b !== binding);
      setBindings(bindingsRef.current);
    };
  }, []);

  const pushScope = useCallback((scope: HotkeyScope) => {
    setScopeStack((stack) => [...stack, scope]);

    // Pop the last occurrence rather than every match: two stacked dialogs of the same scope must
    // each release exactly the entry they pushed.
    return () =>
      setScopeStack((stack) => {
        const index = stack.lastIndexOf(scope);
        return index < 0 ? stack : [...stack.slice(0, index), ...stack.slice(index + 1)];
      });
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (shouldIgnore(event)) return;

      const combo = comboFrom(event);
      const current = scopeStack[scopeStack.length - 1] ?? 'pos';

      const match = bindingsRef.current.find(
        (b) => !b.disabled && b.combo === combo && (b.scope === current || b.scope === 'global'),
      );

      if (!match) return;

      // F-keys otherwise trigger browser behaviour (F3 opens find, F5 reloads). A till that
      // reloaded mid-sale would be unusable.
      event.preventDefault();
      event.stopPropagation();
      match.handler(event);
    };

    window.addEventListener('keydown', onKeyDown, true);
    return () => window.removeEventListener('keydown', onKeyDown, true);
  }, [scopeStack]);

  const value = useMemo<HotkeyRegistry>(
    () => ({ register, pushScope, activeScope, bindings }),
    [register, pushScope, activeScope, bindings],
  );

  return <HotkeyContext.Provider value={value}>{children}</HotkeyContext.Provider>;
}

function useRegistry(): HotkeyRegistry {
  const registry = useContext(HotkeyContext);
  if (!registry) throw new Error('Hotkeys must be used inside a HotkeyProvider.');
  return registry;
}

/** Binds one shortcut for as long as the component is mounted. */
export function useHotkey(
  combo: string,
  handler: (event: KeyboardEvent) => void,
  options: { scope?: HotkeyScope; label: string; group?: string; disabled?: boolean; hidden?: boolean },
) {
  const { register } = useRegistry();
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  const { scope = 'pos', label, group, disabled, hidden } = options;

  useEffect(
    () =>
      register({
        combo,
        scope,
        label,
        group,
        disabled,
        hidden,
        handler: (event) => handlerRef.current(event),
      }),
    [register, combo, scope, label, group, disabled, hidden],
  );
}

/** Pushes a scope while a dialog is open, so the dialog's keys win over the sale screen's. */
export function useHotkeyScope(scope: HotkeyScope, active = true) {
  const { pushScope } = useRegistry();

  useEffect(() => {
    if (!active) return undefined;
    return pushScope(scope);
  }, [pushScope, scope, active]);
}

/** Everything currently bound, for the cheat sheet and the command palette. */
export function useHotkeyBindings(): HotkeyBinding[] {
  const { bindings, activeScope } = useRegistry();

  return useMemo(
    () => bindings.filter((b) => !b.hidden && (b.scope === activeScope || b.scope === 'global')),
    [bindings, activeScope],
  );
}
