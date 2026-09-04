'use client';

import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth-config';
import { cn } from '@/lib/utils';

/**
 * Ctrl+K (doc 08 §Keyboard model).
 *
 * Every navigation target in one searchable list, ranked by what this person actually uses. It is
 * the answer to a back office with forty screens: a supervisor should not have to remember whether
 * stock transfers live under Inventory or under Purchasing, and a menu deep enough to hold
 * everything is a menu nobody reads.
 *
 * Entries are filtered by permission, so the palette never offers a screen the server would refuse.
 */

interface Command {
  id: string;
  label: string;
  group: string;
  href?: string;
  action?: () => void;
  keywords?: string;
  permission?: string;
}

import { PALETTE_ROUTES } from '@/lib/routes';

const RECENTS_KEY = 'r25.palette.recent';
const MAX_RECENTS = 6;

/** An option's DOM id. A route id is a path, and a path is not a valid `id` on its own. */
function optionId(id: string): string {
  return `palette-${id.replace(/[^a-z0-9]+/gi, '-')}`;
}

export function CommandPalette() {
  const router = useRouter();
  const { can, signOut } = useAuth();

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlighted, setHighlighted] = useState(0);
  const [recents, setRecents] = useState<string[]>([]);

  const inputRef = useRef<HTMLInputElement>(null);
  const listId = useId();

  /**
   * Everything in the registry, plus the actions that are not places.
   *
   * This was a hand-written list of twenty destinations, and the application has thirty-three. It
   * could not reach the dashboard, previous sales, orders, backup, accounting or seven of the nine
   * reports — screens somebody looking for them by name would conclude did not exist. Reading the
   * registry means a new route is in the palette the moment it is declared.
   */
  const commands = useMemo<Command[]>(
    () => [
      ...PALETTE_ROUTES.map((route) => ({
        id: route.href,
        label: route.label,
        group: 'Go to',
        href: route.href,
        keywords: route.keywords,
        permission: route.permission,
      })),
      { id: 'signout', label: 'Sign out', group: 'Session', action: () => void signOut(), keywords: 'logout leave' },
    ],
    [signOut],
  );

  const available = useMemo(
    () => commands.filter((command) => !command.permission || can(command.permission)),
    [commands, can],
  );

  useEffect(() => {
    try {
      const stored = localStorage.getItem(RECENTS_KEY);
      setRecents(stored ? (JSON.parse(stored) as string[]) : []);
    } catch {
      setRecents([]);
    }
  }, []);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setOpen((current) => !current);
        setQuery('');
        setHighlighted(0);
      }

      if (event.key === 'Escape') {
        setOpen(false);
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  useEffect(() => {
    if (open) {
      // A frame's delay: the input does not exist until this render commits.
      requestAnimationFrame(() => inputRef.current?.focus());
    }
  }, [open]);

  const results = useMemo(() => {
    const needle = query.trim().toLowerCase();

    if (!needle) {
      // With no query, recency is the best ranking available — a supervisor opens the same three
      // screens all day.
      const ranked = [...available].sort((a, b) => {
        const ai = recents.indexOf(a.id);
        const bi = recents.indexOf(b.id);
        if (ai === bi) return 0;
        if (ai === -1) return 1;
        if (bi === -1) return -1;
        return ai - bi;
      });

      return ranked;
    }

    return available.filter(
      (command) =>
        command.label.toLowerCase().includes(needle) ||
        command.keywords?.toLowerCase().includes(needle) ||
        command.group.toLowerCase().includes(needle),
    );
  }, [available, query, recents]);

  const run = (command: Command) => {
    const next = [command.id, ...recents.filter((id) => id !== command.id)].slice(0, MAX_RECENTS);
    setRecents(next);

    try {
      localStorage.setItem(RECENTS_KEY, JSON.stringify(next));
    } catch {
      // A browser with storage disabled loses ranking, not function.
    }

    setOpen(false);

    if (command.href) {
      router.push(command.href);
    } else {
      command.action?.();
    }
  };

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-black/40 p-4 pt-24"
      role="presentation"
      onClick={() => setOpen(false)}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Command palette"
        className="pos-panel w-full max-w-lg shadow-overlay"
        onClick={(event) => event.stopPropagation()}
      >
        {/*
          A combobox, declared as one.

          It was a bare text box next to an unlabelled list of buttons: a screen-reader user typing
          into it heard nothing appear, nothing about how many things matched, and nothing about
          which one Enter would open. The list is a listbox, its rows are options, and the input
          points at the highlighted one -- which is also what makes the highlight mean something to
          somebody who cannot see it.
        */}
        <input
          ref={inputRef}
          role="combobox"
          aria-expanded="true"
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={results[highlighted] ? optionId(results[highlighted].id) : undefined}
          aria-label="Search screens and actions"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setHighlighted(0);
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown') {
              event.preventDefault();
              setHighlighted((i) => Math.min(i + 1, results.length - 1));
            }

            if (event.key === 'ArrowUp') {
              event.preventDefault();
              setHighlighted((i) => Math.max(i - 1, 0));
            }

            if (event.key === 'Enter' && results[highlighted]) {
              event.preventDefault();
              run(results[highlighted]);
            }
          }}
          placeholder="Search screens and actions…"
          className="w-full border-b border-subtle bg-transparent px-3 py-3 text-body outline-none"
        />

        {/* Said out loud, once per change of query, for anyone who cannot see the list move. */}
        <p aria-live="polite" className="sr-only">
          {results.length === 0
            ? 'Nothing matches.'
            : `${results.length} result${results.length === 1 ? '' : 's'}. ${results[highlighted]?.label ?? ''} selected.`}
        </p>

        <ul id={listId} role="listbox" aria-label="Results" className="max-h-80 overflow-y-auto py-1">
          {results.length === 0 ? (
            <li className="px-3 py-6 text-center text-body text-ink-muted">Nothing matches.</li>
          ) : (
            results.map((command, index) => (
              <li
                key={command.id}
                id={optionId(command.id)}
                role="option"
                aria-selected={index === highlighted}
                onMouseEnter={() => setHighlighted(index)}
                onClick={() => run(command)}
                className={cn(
                  'flex cursor-pointer items-center justify-between gap-3 px-3 text-body',
                  index === highlighted ? 'bg-accent-soft text-accent-text' : 'hover:bg-panel-hover',
                )}
                style={{ minHeight: 'var(--control-height)' }}
              >
                <span>{command.label}</span>
                <span className="text-label text-ink-muted">{command.group}</span>
              </li>
            ))
          )}
        </ul>

        <p className="border-t border-subtle px-3 py-1.5 text-label text-ink-muted">
          <kbd className="rounded-sm border border-subtle px-1 font-mono">↑↓</kbd> move ·{' '}
          <kbd className="rounded-sm border border-subtle px-1 font-mono">↵</kbd> open ·{' '}
          <kbd className="rounded-sm border border-subtle px-1 font-mono">esc</kbd> close
        </p>
      </div>
    </div>
  );
}
