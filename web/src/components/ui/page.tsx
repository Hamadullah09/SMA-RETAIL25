'use client';

import { ChevronLeft, ChevronRight, Search } from 'lucide-react';
import type { ReactNode } from 'react';
import { cx } from '@/lib/cx';
import { formatCount } from '@/lib/format';

/** The page title in Century Gothic, with what is shown on the right (e.g. "0 shown · 0 items"). */
export function PageHeader({ title, aside, children }: { title: ReactNode; aside?: ReactNode; children?: ReactNode }) {
  return (
    <header className="mb-4 flex flex-wrap items-end justify-between gap-x-6 gap-y-2">
      <h1 className="page-title max-sm:text-[28px] max-sm:leading-9">{title}</h1>
      {aside && <div className="pb-1 text-muted">{aside}</div>}
      {children}
    </header>
  );
}

/** A row of filters or buttons that wraps, like the desktop toolbars. */
export function Toolbar({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cx('no-print mb-3 flex flex-wrap items-center gap-2.5', className)}>{children}</div>;
}

/** The search box of a list: `/` focuses it from anywhere on the page. */
export function SearchBox({
  value,
  onChange,
  placeholder = 'Search',
  label = 'Search',
  className,
}: {
  value: string;
  onChange(value: string): void;
  placeholder?: string;
  label?: string;
  className?: string;
}) {
  return (
    <label className={cx('relative flex min-w-[220px] flex-1 items-center sm:max-w-[420px]', className)}>
      <span className="sr-only">{label}</span>
      <Search className="pointer-events-none absolute left-3.5 h-5 w-5 text-muted" aria-hidden="true" />
      <input
        type="search"
        data-list-search
        className="input pl-11 pr-10"
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
      <kbd className="keycap pointer-events-none absolute right-3 text-muted max-sm:hidden" aria-hidden="true">
        /
      </kbd>
    </label>
  );
}

/** "1–50 of 1,234" with the previous and next page buttons. */
export function Pager({
  page,
  pageSize,
  total,
  onPage,
  noun = 'rows',
}: {
  page: number;
  pageSize: number;
  total: number;
  onPage(page: number): void;
  noun?: string;
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const first = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(total, page * pageSize);
  return (
    <div className="no-print flex flex-wrap items-center justify-between gap-3 border-t border-line px-4 py-3">
      <p className="text-muted">
        {total === 0 ? `No ${noun}` : `${formatCount(first)}–${formatCount(last)} of ${formatCount(total)} ${noun}`}
      </p>
      {pages > 1 && (
        <div className="flex items-center gap-2">
          <button type="button" className="btn btn-icon h-11 min-h-0 w-11" onClick={() => onPage(page - 1)} disabled={page <= 1} aria-label="Previous page">
            <ChevronLeft className="h-5 w-5" />
          </button>
          <span className="min-w-[90px] text-center text-muted">
            Page {page} of {formatCount(pages)}
          </span>
          <button type="button" className="btn btn-icon h-11 min-h-0 w-11" onClick={() => onPage(page + 1)} disabled={page >= pages} aria-label="Next page">
            <ChevronRight className="h-5 w-5" />
          </button>
        </div>
      )}
    </div>
  );
}

/** A label and a value, for detail panels. */
export function Detail({ label, children, className }: { label: string; children: ReactNode; className?: string }) {
  return (
    <div className={cx('min-w-0', className)}>
      <dt className="text-note text-muted">{label}</dt>
      <dd className="break-words font-semibold text-ink">{children || '—'}</dd>
    </div>
  );
}

const CHIP = { ok: 'chip-ok', danger: 'chip-danger', warn: 'chip-warn', plain: 'chip-plain', dark: 'chip-dark' } as const;

export type ChipTone = keyof typeof CHIP;

/** A status chip: ok (green), danger (red), warn (sun), plain, dark. */
export function Chip({ tone = 'plain', children, className }: { tone?: ChipTone; children: ReactNode; className?: string }) {
  return <span className={cx('chip', CHIP[tone], className)}>{children}</span>;
}
