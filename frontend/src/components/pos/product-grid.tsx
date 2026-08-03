'use client';

/*
 * next/image is deliberately not used for product pictures anywhere in this file.
 *
 * The optimiser fetches the source URL from the Next server, and these URLs go through the BFF proxy,
 * which authenticates on the browser's session cookie. A server-side fetch does not carry it, so every
 * tile would 401. The pictures are already capped at 2 MB, served with an ETag and cached privately —
 * which is the part of the optimiser that would have mattered here.
 */
/* eslint-disable @next/next/no-img-element */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { usePosStore } from '@/stores/pos-store';
import { posApi } from '@/lib/pos-api';
import type { PosGridGroup, PosGridItem, PosGridPage } from '@/types/pos';
import { cn } from '@/lib/utils';
import { money } from '@/components/pos/panels';

/**
 * The till's product picker.
 *
 * A cashier who knows the code scans or types it; this is for the ones who do not — the customer
 * pointing at a pastry, the item whose barcode has worn off. Tapping a tile rings it through the same
 * {@link usePosStore.scan} path a barcode takes, so pricing, variants, serials and tag-alongs all
 * behave identically no matter how the item was chosen.
 *
 * ## Why there are two layouts
 *
 * Pictures are optional, and a shop that has not photographed its catalogue would otherwise get a
 * screen of identical grey squares — worse than a list, because it is bigger and says less. So the
 * layout follows the data: the server reports whether anything in the current filter has a picture,
 * and the grid opens as tiles when it does and as rows when it does not. The toggle is still there,
 * because a shop that has photographed half its stock may want either, and that choice is remembered.
 */

/** Windows Explorer's two useful views, and its vocabulary — the one every cashier already knows. */
type ViewMode = 'tiles' | 'list';

const VIEW_STORAGE_KEY = 'retail25.pos.grid-view';
const PAGE_SIZE = 60;

/** Long enough that typing a word is one request, short enough to feel like filtering. */
const SEARCH_DEBOUNCE_MS = 200;

export function ProductGrid() {
  const { locationId, scan, busy } = usePosStore();

  const [page, setPage] = useState<PosGridPage | null>(null);
  const [items, setItems] = useState<PosGridItem[]>([]);
  const [departmentId, setDepartmentId] = useState<string | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [debounced, setDebounced] = useState('');
  const [skip, setSkip] = useState(0);
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);

  // null means "nobody has chosen" — the data decides. A real value is an explicit override and wins.
  const [chosenView, setChosenView] = useState<ViewMode | null>(null);

  useEffect(() => {
    const stored = window.localStorage.getItem(VIEW_STORAGE_KEY);
    if (stored === 'tiles' || stored === 'list') setChosenView(stored);
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(search.trim()), SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [search]);

  // Any change of filter starts the paging over. Without this, narrowing to a department while on
  // page three shows an empty grid and a "more" button that fills it.
  useEffect(() => {
    setSkip(0);
  }, [departmentId, categoryId, debounced]);

  const requestId = useRef(0);

  useEffect(() => {
    if (!locationId) return;

    // Responses can arrive out of order — a slow first keystroke landing after a fast third would
    // otherwise overwrite the newer results with older ones.
    const id = ++requestId.current;
    setLoading(true);

    void posApi
      .grid({ locationId, departmentId, categoryId, search: debounced, skip, take: PAGE_SIZE })
      .then((result) => {
        if (id !== requestId.current) return;
        setPage(result);
        setItems((previous) => (skip === 0 ? result.items : [...previous, ...result.items]));
        setFailed(false);
      })
      .catch(() => {
        if (id !== requestId.current) return;
        setFailed(true);
      })
      .finally(() => {
        if (id === requestId.current) setLoading(false);
      });
  }, [locationId, departmentId, categoryId, debounced, skip]);

  const view: ViewMode = chosenView ?? (page?.anyImages ? 'tiles' : 'list');

  const setView = useCallback((next: ViewMode) => {
    setChosenView(next);
    window.localStorage.setItem(VIEW_STORAGE_KEY, next);
  }, []);

  const add = useCallback(
    (item: PosGridItem) => {
      // By stock code rather than id: that is the identifier the resolver takes, and it is the one
      // that behaves the same whether the item came from a tile, a barcode or the F2 list.
      void scan(item.stockCode);
    },
    [scan],
  );

  const hasMore = page ? items.length < page.total : false;

  const departments = useMemo(
    () => (page?.departments ?? []).filter((group) => group.itemCount > 0),
    [page],
  );

  const categories = useMemo(
    () => (page?.categories ?? []).filter((group) => group.itemCount > 0),
    [page],
  );

  return (
    <section className="pos-panel flex min-h-0 flex-col" aria-label="Products">
      <GridToolbar
        search={search}
        onSearch={setSearch}
        view={view}
        onView={setView}
        total={page?.total ?? 0}
        shown={items.length}
      />

      {departments.length > 0 ? (
        <GroupStrip
          label="Departments"
          groups={departments}
          selected={departmentId}
          onSelect={(id) => {
            setDepartmentId(id);
            setCategoryId(null);
          }}
        />
      ) : null}

      {categories.length > 0 ? (
        <GroupStrip label="Categories" groups={categories} selected={categoryId} onSelect={setCategoryId} />
      ) : null}

      <div className="min-h-0 flex-1 overflow-y-auto p-2">
        {failed ? (
          <Empty>Products could not be loaded. Scanning still works.</Empty>
        ) : items.length === 0 && !loading ? (
          <Empty>
            {debounced ? `Nothing matches “${debounced}”.` : 'No items in this selection.'}
          </Empty>
        ) : view === 'tiles' ? (
          <TileView items={items} onPick={add} disabled={busy} />
        ) : (
          <ListView items={items} onPick={add} disabled={busy} />
        )}

        {hasMore ? (
          <div className="mt-2 flex justify-center">
            <button
              type="button"
              className="pos-button"
              onClick={() => setSkip(items.length)}
              disabled={loading}
            >
              {loading ? 'Loading…' : `Show more (${page!.total - items.length} left)`}
            </button>
          </div>
        ) : null}
      </div>
    </section>
  );
}

/* ------------------------------------------------------------------ toolbar */

function GridToolbar({
  search,
  onSearch,
  view,
  onView,
  total,
  shown,
}: {
  search: string;
  onSearch: (value: string) => void;
  view: ViewMode;
  onView: (view: ViewMode) => void;
  total: number;
  shown: number;
}) {
  return (
    <div className="pos-panel-header flex items-center gap-2">
      <input
        type="search"
        className="pos-input min-w-0 flex-1"
        placeholder="Search products"
        value={search}
        onChange={(event) => onSearch(event.target.value)}
        aria-label="Search products"
      />

      <span className="whitespace-nowrap text-caption tabular-nums text-ink-muted" aria-live="polite">
        {shown} of {total}
      </span>

      {/*
        A radio group, not two buttons: these are one setting with two states, and a screen reader
        should say which is current rather than offering two unrelated actions.
      */}
      <div className="flex rounded-sm border border-subtle" role="radiogroup" aria-label="View">
        <ViewButton current={view} value="tiles" onSelect={onView} label="Large thumbnails">
          <TilesIcon />
        </ViewButton>
        <ViewButton current={view} value="list" onSelect={onView} label="List">
          <ListIcon />
        </ViewButton>
      </div>
    </div>
  );
}

function ViewButton({
  current,
  value,
  onSelect,
  label,
  children,
}: {
  current: ViewMode;
  value: ViewMode;
  onSelect: (view: ViewMode) => void;
  label: string;
  children: React.ReactNode;
}) {
  const active = current === value;

  return (
    <button
      type="button"
      role="radio"
      aria-checked={active}
      aria-label={label}
      title={label}
      onClick={() => onSelect(value)}
      className={cn(
        'flex min-h-control w-9 items-center justify-center first:rounded-l-sm last:rounded-r-sm',
        active ? 'bg-accent text-accent-foreground' : 'text-ink-muted hover:bg-panel-sunken',
      )}
    >
      {children}
    </button>
  );
}

/* ------------------------------------------------------------------ heading strips */

function GroupStrip({
  label,
  groups,
  selected,
  onSelect,
}: {
  label: string;
  groups: PosGridGroup[];
  selected: string | null;
  onSelect: (id: string | null) => void;
}) {
  return (
    <div className="flex items-center gap-1 overflow-x-auto border-b border-subtle px-2 py-1.5" aria-label={label}>
      <Chip active={selected === null} onClick={() => onSelect(null)}>
        All
      </Chip>
      {groups.map((group) => (
        <Chip key={group.id} active={selected === group.id} onClick={() => onSelect(group.id)}>
          {group.name}
          <span className="ml-1 tabular-nums opacity-60">{group.itemCount}</span>
        </Chip>
      ))}
    </div>
  );
}

function Chip({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cn(
        'whitespace-nowrap rounded-full border px-3 py-1 text-label transition-colors',
        active
          ? 'border-accent bg-accent text-accent-foreground'
          : 'border-subtle text-ink-muted hover:bg-panel-sunken',
      )}
    >
      {children}
    </button>
  );
}

/* ------------------------------------------------------------------ the two views */

function TileView({
  items,
  onPick,
  disabled,
}: {
  items: PosGridItem[];
  onPick: (item: PosGridItem) => void;
  disabled: boolean;
}) {
  return (
    <ul className="grid grid-cols-[repeat(auto-fill,minmax(9rem,1fr))] gap-2">
      {items.map((item) => (
        <li key={item.id}>
          <button
            type="button"
            onClick={() => onPick(item)}
            disabled={disabled}
            className="group flex h-full w-full flex-col overflow-hidden rounded border border-subtle bg-panel text-left transition-colors hover:border-accent disabled:opacity-60"
          >
            <Thumbnail item={item} />
            <span className="flex flex-1 flex-col gap-0.5 p-2">
              <span className="line-clamp-2 text-label font-medium leading-tight text-ink">{item.name}</span>
              <span className="mt-auto flex items-baseline justify-between gap-1">
                <span className="text-body font-medium tabular-nums text-ink">{money(item.regularPrice)}</span>
                <StockPip onHand={item.onHand} />
              </span>
            </span>
          </button>
        </li>
      ))}
    </ul>
  );
}

function ListView({
  items,
  onPick,
  disabled,
}: {
  items: PosGridItem[];
  onPick: (item: PosGridItem) => void;
  disabled: boolean;
}) {
  return (
    <ul className="divide-y divide-subtle rounded border border-subtle">
      {items.map((item) => (
        <li key={item.id}>
          <button
            type="button"
            onClick={() => onPick(item)}
            disabled={disabled}
            className="flex w-full min-h-control items-center gap-3 px-3 py-2 text-left hover:bg-panel-sunken disabled:opacity-60"
          >
            {/* A 32px thumbnail where one exists — Explorer's list view does the same. */}
            {item.hasImage ? (
              <img
                src={`/api/proxy/products/${item.id}/image`}
                alt=""
                loading="lazy"
                className="h-8 w-8 shrink-0 rounded-sm object-cover"
              />
            ) : (
              <span className="h-8 w-8 shrink-0" aria-hidden="true" />
            )}

            <span className="w-20 shrink-0 truncate font-mono text-caption text-ink-muted">{item.stockCode}</span>
            <span className="min-w-0 flex-1 truncate text-body text-ink">{item.name}</span>
            <StockPip onHand={item.onHand} />
            <span className="w-20 shrink-0 text-right text-body font-medium tabular-nums text-ink">
              {money(item.regularPrice)}
            </span>
          </button>
        </li>
      ))}
    </ul>
  );
}

/**
 * The tile's picture, or a stand-in built from the item's own name.
 *
 * The stand-in is a monogram on a hue derived from the stock code, not a generic placeholder icon —
 * a wall of identical boxes is unscannable, whereas colour and two letters give the eye something to
 * aim at even before it reads anything. The hue is a pure function of the code, so an item is the
 * same colour on every till and after every reload.
 */
function Thumbnail({ item }: { item: PosGridItem }) {
  const [broken, setBroken] = useState(false);

  if (item.hasImage && !broken) {
    return (
      <img
        src={`/api/proxy/products/${item.id}/image`}
        alt=""
        loading="lazy"
        onError={() => setBroken(true)}
        className="aspect-square w-full bg-panel-sunken object-cover"
      />
    );
  }

  return (
    <span
      aria-hidden="true"
      className="flex aspect-square w-full items-center justify-center text-h3 font-semibold uppercase tracking-tight text-white"
      style={{ backgroundColor: `hsl(${hueOf(item.stockCode)} 42% 42%)` }}
    >
      {monogram(item.name)}
    </span>
  );
}

function StockPip({ onHand }: { onHand: number }) {
  if (onHand > 0) return null;

  return (
    <span className="pos-badge bg-warning/15 text-warning" title="None in stock">
      0
    </span>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return <p className="p-6 text-center text-body text-ink-muted">{children}</p>;
}

function monogram(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '?';
  if (words.length === 1) return words[0].slice(0, 2);
  return `${words[0][0]}${words[1][0]}`;
}

/** FNV-1a, for no reason beyond being short, stable and well spread across the wheel. */
function hueOf(seed: string): number {
  let hash = 0x811c9dc5;
  for (let index = 0; index < seed.length; index += 1) {
    hash ^= seed.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }
  return Math.abs(hash) % 360;
}

function TilesIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
      <rect x="1" y="1" width="6" height="6" rx="1" />
      <rect x="9" y="1" width="6" height="6" rx="1" />
      <rect x="1" y="9" width="6" height="6" rx="1" />
      <rect x="9" y="9" width="6" height="6" rx="1" />
    </svg>
  );
}

function ListIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
      <rect x="1" y="2" width="14" height="2" rx="1" />
      <rect x="1" y="7" width="14" height="2" rx="1" />
      <rect x="1" y="12" width="14" height="2" rx="1" />
    </svg>
  );
}
