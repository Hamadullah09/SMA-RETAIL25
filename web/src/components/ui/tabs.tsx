'use client';

import { useRef } from 'react';
import { cx } from '@/lib/cx';

/** A row of pills on a white band, the chosen one charcoal (the desktop's TabControl). Arrow keys move between them. */
export function Tabs<K extends string>({
  tabs,
  value,
  onChange,
  label,
  className,
}: {
  tabs: ReadonlyArray<{ key: K; label: string }>;
  value: K;
  onChange(key: NoInfer<K>): void;
  label: string;
  className?: string;
}) {
  const refs = useRef<Array<HTMLButtonElement | null>>([]);
  return (
    <div role="tablist" aria-label={label} className={cx('tabs', className)}>
      {tabs.map((tab, i) => (
        <button
          key={tab.key}
          ref={(el) => {
            refs.current[i] = el;
          }}
          type="button"
          role="tab"
          aria-selected={tab.key === value}
          tabIndex={tab.key === value ? 0 : -1}
          className="tab"
          onClick={() => onChange(tab.key)}
          onKeyDown={(e) => {
            if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
            e.preventDefault();
            const next = (i + (e.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
            onChange(tabs[next].key);
            refs.current[next]?.focus();
          }}
        >
          {tab.label}
        </button>
      ))}
    </div>
  );
}
