'use client';

/**
 * Keyboard shortcuts for the whole app. A page registers its keys with {@link useHotkey} (buttons with a key cap do it
 * themselves); an open dialog takes the keyboard over, so only the keys of the topmost dialog fire while it is open.
 */
import { createContext, useContext, useEffect, useId, useMemo, useRef, type ReactNode } from 'react';
import { firesWhileTyping, isTypingTarget, matchCombo, parseCombo, type Combo } from './keys';

interface Entry {
  combo: Combo;
  scope: string;
  handler: (event: KeyboardEvent) => void;
  enabled: () => boolean;
}

interface Registry {
  add(entry: Entry): () => void;
  pushScope(scope: string): () => void;
}

const PAGE_SCOPE = 'page';

const RegistryContext = createContext<Registry | null>(null);
const ScopeContext = createContext<string>(PAGE_SCOPE);

export function HotkeysProvider({ children }: { children: ReactNode }) {
  const entries = useRef<Entry[]>([]);
  const scopes = useRef<string[]>([PAGE_SCOPE]);

  const registry = useMemo<Registry>(
    () => ({
      add(entry) {
        entries.current.push(entry);
        return () => {
          entries.current = entries.current.filter((e) => e !== entry);
        };
      },
      pushScope(scope) {
        scopes.current.push(scope);
        return () => {
          scopes.current = scopes.current.filter((s) => s !== scope);
        };
      },
    }),
    [],
  );

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.defaultPrevented || event.isComposing) return;
      const active = scopes.current[scopes.current.length - 1];
      const typing = isTypingTarget(event.target);
      // The latest registration wins, so a page's F5 beats a stale one being unmounted.
      for (let i = entries.current.length - 1; i >= 0; i--) {
        const entry = entries.current[i];
        if (entry.scope !== active || !matchCombo(event, entry.combo)) continue;
        if (typing && !firesWhileTyping(entry.combo)) continue;
        if (!entry.enabled()) continue;
        event.preventDefault();
        entry.handler(event);
        return;
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  return <RegistryContext.Provider value={registry}>{children}</RegistryContext.Provider>;
}

/** Runs `handler` when `combo` (as written on a key cap, e.g. `Ctrl+S`) is pressed, while `enabled`. */
export function useHotkey(combo: string | null | undefined, handler: (event: KeyboardEvent) => void, enabled = true): void {
  const registry = useContext(RegistryContext);
  const scope = useContext(ScopeContext);
  const latest = useRef({ handler, enabled });
  latest.current = { handler, enabled };

  useEffect(() => {
    if (!registry || !combo) return;
    return registry.add({
      combo: parseCombo(combo),
      scope,
      handler: (event) => latest.current.handler(event),
      enabled: () => latest.current.enabled,
    });
  }, [registry, combo, scope]);
}

/** Several keys at once: `{ 'Ctrl+1': goHome, 'Ctrl+2': undefined }` (undefined keys are not bound). */
export function useHotkeyMap(map: Record<string, (() => void) | undefined>): void {
  const registry = useContext(RegistryContext);
  const scope = useContext(ScopeContext);
  const latest = useRef(map);
  latest.current = map;
  const signature = Object.keys(map)
    .filter((k) => map[k])
    .join('|');

  useEffect(() => {
    if (!registry || !signature) return;
    const removers = signature.split('|').map((combo) =>
      registry.add({
        combo: parseCombo(combo),
        scope,
        handler: () => latest.current[combo]?.(),
        enabled: () => !!latest.current[combo],
      }),
    );
    return () => removers.forEach((remove) => remove());
  }, [registry, scope, signature]);
}

/** Everything inside gets its own keys, and the page's stop firing while it is mounted (dialogs, the palette). */
export function HotkeyScope({ children }: { children: ReactNode }) {
  const registry = useContext(RegistryContext);
  const id = useId();
  useEffect(() => registry?.pushScope(id), [registry, id]);
  return <ScopeContext.Provider value={id}>{children}</ScopeContext.Provider>;
}
