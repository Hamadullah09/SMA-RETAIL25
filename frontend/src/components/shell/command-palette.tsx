'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
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

const RECENTS_KEY = 'r25.palette.recent';
const MAX_RECENTS = 6;

export function CommandPalette() {
  const router = useRouter();
  const { can, signOut } = useAuth();

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlighted, setHighlighted] = useState(0);
  const [recents, setRecents] = useState<string[]>([]);

  const inputRef = useRef<HTMLInputElement>(null);

  const commands = useMemo<Command[]>(
    () => [
      { id: 'pos', label: 'Point of sale', group: 'Go to', href: '/pos', keywords: 'till sell cash register', permission: 'pos.sell' },
      { id: 'inventory', label: 'Inventory', group: 'Go to', href: '/inventory', keywords: 'stock levels', permission: 'catalog.read' },
      { id: 'products', label: 'Products', group: 'Go to', href: '/catalog/products', keywords: 'items catalogue sku', permission: 'catalog.read' },
      { id: 'transfers', label: 'Stock transfers', group: 'Go to', href: '/inventory/transfers', keywords: 'move between stores locations van', permission: 'inventory.transfer' },
      { id: 'stock-counts', label: 'Stock counts', group: 'Go to', href: '/inventory/counts', keywords: 'stocktake count variance shrinkage', permission: 'inventory.count' },
      { id: 'bulk-adjust', label: 'Batch changes', group: 'Go to', href: '/catalog/bulk', keywords: 'bulk reprice price increase tax flags', permission: 'catalog.bulk_adjust' },
      { id: 'customers', label: 'Customers', group: 'Go to', href: '/customers', keywords: 'clients accounts', permission: 'customer.read' },
      { id: 'receivables', label: 'Receivables', group: 'Go to', href: '/receivables', keywords: 'ar invoices statements', permission: 'ar.read' },
      { id: 'purchasing', label: 'Purchasing', group: 'Go to', href: '/purchasing', keywords: 'orders po', permission: 'purchasing.read' },
      { id: 'suppliers', label: 'Suppliers', group: 'Go to', href: '/purchasing/suppliers', keywords: 'vendors reorder', permission: 'purchasing.read' },
      { id: 'reports', label: 'Reports', group: 'Go to', href: '/reports', keywords: 'analysis', permission: 'reports.sales' },
      { id: 'sales-log', label: 'Sales log', group: 'Go to', href: '/reports/sales', keywords: 'history transactions receipts reprint export', permission: 'reports.sales' },
      { id: 'admin', label: 'Administration', group: 'Go to', href: '/admin', keywords: 'setup configuration', permission: 'settings.read' },
      { id: 'setup', label: 'Setup', group: 'Go to', href: '/admin/settings', keywords: 'taxes pos printers hardware users stations tenders numbering departments categories groupings', permission: 'settings.read' },
      { id: 'undelete', label: 'Undelete items', group: 'Go to', href: '/admin/undelete', keywords: 'restore deleted recover', permission: 'catalog.delete' },
      { id: 'audit', label: 'Audit log', group: 'Go to', href: '/admin/audit', keywords: 'history who changed', permission: 'audit.read' },
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
        className="pos-panel w-full max-w-lg shadow-lg"
        onClick={(event) => event.stopPropagation()}
      >
        <input
          ref={inputRef}
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
          className="w-full border-b border-[rgb(var(--border))] bg-transparent px-3 py-3 text-sm outline-none"
        />

        <ul className="max-h-80 overflow-y-auto py-1">
          {results.length === 0 ? (
            <li className="px-3 py-6 text-center text-sm text-[rgb(var(--text-muted))]">Nothing matches.</li>
          ) : (
            results.map((command, index) => (
              <li key={command.id}>
                <button
                  type="button"
                  onMouseEnter={() => setHighlighted(index)}
                  onClick={() => run(command)}
                  className={cn(
                    'flex w-full items-center justify-between px-3 py-2 text-left text-sm',
                    index === highlighted && 'bg-[rgb(var(--surface))]',
                  )}
                >
                  <span>{command.label}</span>
                  <span className="text-xs text-[rgb(var(--text-muted))]">{command.group}</span>
                </button>
              </li>
            ))
          )}
        </ul>

        <p className="border-t border-[rgb(var(--border))] px-3 py-1.5 text-xs text-[rgb(var(--text-muted))]">
          <kbd className="rounded-sm border border-[rgb(var(--border))] px-1 font-mono">↑↓</kbd> move ·{' '}
          <kbd className="rounded-sm border border-[rgb(var(--border))] px-1 font-mono">↵</kbd> open ·{' '}
          <kbd className="rounded-sm border border-[rgb(var(--border))] px-1 font-mono">esc</kbd> close
        </p>
      </div>
    </div>
  );
}
