'use client';

import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { api } from '@/lib/api';
import { cx } from '@/lib/cx';
import { HotkeyScope, useHotkey } from '@/lib/hotkeys';
import { commandsFor, P, searchCommands, type Command } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { ICONS } from './icons';

/**
 * Ctrl+K: "Go to". Type a few letters of a page, an action or a report; ↑ ↓ choose, Enter goes, Esc closes. Like the
 * desktop's palette (dlg-palette), it lists only what the user may open.
 */
export function CommandPalette({ open, onClose }: { open: boolean; onClose(): void }) {
  if (!open || typeof document === 'undefined') return null;
  return createPortal(<PaletteBody onClose={onClose} />, document.body);
}

function PaletteBody({ onClose }: { onClose(): void }) {
  const router = useRouter();
  const { permissions, can, signOut } = useSession();
  const [text, setText] = useState('');
  const [index, setIndex] = useState(0);
  const input = useRef<HTMLInputElement>(null);
  const list = useRef<HTMLUListElement>(null);

  const reports = useQuery({ queryKey: ['reports'], queryFn: () => api.reports(), enabled: can(P.reports), staleTime: 10 * 60_000 });
  const commands = useMemo(() => commandsFor(permissions, reports.data ?? []), [permissions, reports.data]);
  const matches = useMemo(() => searchCommands(commands, text), [commands, text]);

  useEffect(() => input.current?.focus(), []);
  useEffect(() => setIndex(0), [text]);
  useEffect(() => {
    list.current?.querySelector<HTMLElement>(`[data-index="${index}"]`)?.scrollIntoView({ block: 'nearest' });
  }, [index]);

  function run(command: Command | undefined) {
    if (!command) return;
    onClose();
    if (command.action === 'sign-out') void signOut();
    else if (command.path) router.push(command.path);
  }

  return (
    <div
      className="fixed inset-0 z-overlay flex animate-fade-in items-start justify-center bg-ink/40 p-3 pt-[8vh] sm:p-6 sm:pt-[10vh]"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <HotkeyScope>
        <PaletteKeys onClose={onClose} />
        <div role="dialog" aria-modal="true" aria-label="Go to" className="w-full max-w-[740px] animate-rise-in rounded-card bg-page p-5 shadow-dialog sm:p-6">
          <h2 className="mb-3 font-heading text-[26px] leading-9">Go to</h2>
          <input
            ref={input}
            type="text"
            role="combobox"
            aria-expanded="true"
            aria-controls="palette-list"
            aria-activedescendant={matches[index] ? `palette-${matches[index].id}` : undefined}
            aria-label="Type a page, an action or a report"
            className="input h-14 border-sun-dark bg-card text-lead"
            placeholder="Type a page, an action or a report"
            value={text}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') {
                e.preventDefault();
                setIndex((i) => Math.min(matches.length - 1, i + 1));
              } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                setIndex((i) => Math.max(0, i - 1));
              } else if (e.key === 'Enter') {
                e.preventDefault();
                run(matches[index]);
              }
            }}
          />
          <ul ref={list} id="palette-list" role="listbox" aria-label="Results" className="mt-3 max-h-[min(420px,55vh)] overflow-y-auto pr-1">
            {matches.length === 0 && <li className="px-4 py-6 text-center text-muted">Nothing matches “{text}”.</li>}
            {matches.map((command, i) => {
              const Icon = ICONS[command.icon];
              return (
                <li
                  key={command.id}
                  id={`palette-${command.id}`}
                  role="option"
                  aria-selected={i === index}
                  data-index={i}
                  className={cx(
                    'flex min-h-[50px] cursor-pointer items-center gap-3 rounded-[14px] px-4',
                    i === index ? 'bg-charcoal text-white' : 'text-ink hover:bg-stone-light',
                  )}
                  onMouseMove={() => setIndex(i)}
                  onClick={() => run(command)}
                >
                  <Icon className="h-5 w-5 shrink-0 opacity-80" strokeWidth={1.8} aria-hidden="true" />
                  <span className="min-w-0 flex-1 truncate">{command.title}</span>
                  {command.group && <span className={cx('text-note max-md:hidden', i === index ? 'text-cream' : 'text-muted')}>{command.group}</span>}
                  {command.shortcut && <span className={cx('text-note max-sm:hidden', i === index ? 'text-cream' : 'text-muted')}>{command.shortcut}</span>}
                  <span className={cx('min-w-[60px] text-right text-note', i === index ? 'text-cream' : 'text-muted')}>
                    {command.group ? `${command.group} · ${command.kind}` : command.kind}
                  </span>
                </li>
              );
            })}
          </ul>
          <p className="mt-3 text-muted">↑ ↓ to choose · Enter to go · Esc to close</p>
        </div>
      </HotkeyScope>
    </div>
  );
}

function PaletteKeys({ onClose }: { onClose(): void }) {
  useHotkey('Esc', onClose);
  useHotkey('Ctrl+K', onClose);
  return null;
}
