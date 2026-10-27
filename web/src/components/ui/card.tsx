import type { ReactNode } from 'react';
import { cx } from '@/lib/cx';

/** A white panel with radius 14, with an optional title in Century Gothic and something on the right of it. */
export function Card({
  title,
  aside,
  children,
  className,
  bodyClassName,
  dark,
}: {
  title?: ReactNode;
  aside?: ReactNode;
  children: ReactNode;
  className?: string;
  bodyClassName?: string;
  /** The desktop's DarkCard: charcoal with white text. */
  dark?: boolean;
}) {
  return (
    <section className={cx('rounded-card shadow-card', dark ? 'bg-charcoal text-white' : 'bg-card', className)}>
      {(title || aside) && (
        <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-5 pb-1 pt-4">
          {title && <h2 className="font-heading text-section">{title}</h2>}
          {aside && <div className={cx('text-note', dark ? 'text-cream' : 'text-muted')}>{aside}</div>}
        </div>
      )}
      <div className={cx(title || aside ? 'px-5 pb-5 pt-2' : 'p-5', bodyClassName)}>{children}</div>
    </section>
  );
}
