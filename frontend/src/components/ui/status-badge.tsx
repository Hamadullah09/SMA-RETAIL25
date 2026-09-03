import type { LucideIcon } from 'lucide-react';
import {
  AlertTriangle,
  Ban,
  CheckCircle2,
  CircleDot,
  Clock,
  Info,
  PackageX,
  Radio,
  TriangleAlert,
  Truck,
  XCircle,
} from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * The six meanings colour is allowed to carry, and the shape and word that go with each.
 *
 * Status was being drawn five different ways across the application: raw enum text, a label map,
 * hard-coded emerald and amber, a bare `text-negative` on a number, and an arrow with bold. Somebody
 * learning what "amber" means on one screen learned nothing about the next.
 *
 * Every tone carries three carriers at once — a glyph, a word, and a hue — because colour on its own
 * fails for the eight percent of men who cannot separate red from green, and fails again on a cheap
 * panel at an angle in daylight. The hue is the fastest of the three to read and the least reliable,
 * so it is never the only one present.
 */
export type StatusTone = 'neutral' | 'positive' | 'warning' | 'negative' | 'live' | 'special';

const TONE_CLASSES: Record<StatusTone, string> = {
  neutral: 'bg-panel-sunken text-ink-muted ring-subtle',
  positive: 'bg-positive-soft text-positive-text ring-positive/25',
  warning: 'bg-warning-soft text-warning-text ring-warning/25',
  negative: 'bg-negative-soft text-negative-text ring-negative/25',
  live: 'bg-live-soft text-live-text ring-live/25',
  special: 'bg-special-soft text-special-text ring-special/25',
};

const TONE_ICONS: Record<StatusTone, LucideIcon> = {
  neutral: CircleDot,
  positive: CheckCircle2,
  warning: TriangleAlert,
  negative: XCircle,
  live: Radio,
  special: Info,
};

/**
 * A status, drawn the same way everywhere.
 *
 * The label is not optional and there is no icon-only variant, deliberately: a coloured dot with no
 * word is a thing you have to be taught, and this system is used by people who were shown it once.
 */
export function StatusBadge({
  label,
  tone = 'neutral',
  icon,
  className,
}: {
  label: string;
  tone?: StatusTone;
  /** Overrides the tone's default glyph where a more specific one says more — a van for dispatched. */
  icon?: LucideIcon;
  className?: string;
}) {
  const Icon = icon ?? TONE_ICONS[tone];

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-1 text-label font-medium ring-1 ring-inset',
        TONE_CLASSES[tone],
        className,
      )}
    >
      <Icon className="h-4 w-4 shrink-0" aria-hidden />
      {label}
    </span>
  );
}

/**
 * How much of something there is, said in words as well as a number.
 *
 * The products screen signalled low stock with `text-warning` on a bare quantity — colour as the
 * only carrier, on the single figure somebody scans a list for. A number that is merely orange
 * tells a colour-blind reader nothing, and tells everyone else nothing at a glance about whether
 * orange is bad here or just notable.
 */
export function StockBadge({ onHand, reorderPoint }: { onHand: number; reorderPoint?: number | null }) {
  if (onHand <= 0) {
    return <StatusBadge tone="negative" icon={PackageX} label="Out of stock" />;
  }

  // No reorder point means nobody asked to be told about this item, so there is no "low" to be at.
  if (reorderPoint != null && reorderPoint > 0 && onHand <= reorderPoint) {
    return <StatusBadge tone="warning" label={`Low stock · ${onHand}`} />;
  }

  return <StatusBadge tone="positive" label={`In stock · ${onHand}`} />;
}

/**
 * The vocabulary for the states this application actually has.
 *
 * Kept as one map rather than a switch at each call site, so a state added to the domain is named
 * once. Anything unmapped falls through to neutral with its own text, which is worse than a proper
 * label and much better than a blank cell.
 */
const STATUS_VOCABULARY: Record<string, { label: string; tone: StatusTone; icon?: LucideIcon }> = {
  // Purchase orders
  Draft: { label: 'Draft', tone: 'neutral' },
  Ordered: { label: 'Ordered', tone: 'live' },
  Posted: { label: 'Posted', tone: 'live' },
  PartiallyReceived: { label: 'Part received', tone: 'warning' },
  Received: { label: 'Received', tone: 'positive' },
  Cancelled: { label: 'Cancelled', tone: 'negative', icon: Ban },
  Closed: { label: 'Closed', tone: 'neutral' },

  // Customer orders and layaways
  New: { label: 'New', tone: 'live' },
  Confirmed: { label: 'Confirmed', tone: 'live' },
  Ready: { label: 'Ready', tone: 'positive' },
  Dispatched: { label: 'Dispatched', tone: 'positive', icon: Truck },
  Completed: { label: 'Completed', tone: 'positive' },
  Active: { label: 'Active', tone: 'special' },
  Forfeited: { label: 'Forfeited', tone: 'negative' },

  // Quotes and invoices
  Open: { label: 'Open', tone: 'live' },
  Accepted: { label: 'Accepted', tone: 'positive' },
  Declined: { label: 'Declined', tone: 'negative' },
  Expired: { label: 'Expired', tone: 'warning', icon: Clock },
  Paid: { label: 'Paid', tone: 'positive' },
  PartPaid: { label: 'Part paid', tone: 'warning' },
  Overdue: { label: 'Overdue', tone: 'negative', icon: AlertTriangle },
  Void: { label: 'Void', tone: 'negative', icon: Ban },

  // Sales
  Completed_Sale: { label: 'Completed', tone: 'positive' },
  Suspended: { label: 'Held', tone: 'special' },
  Refunded: { label: 'Refunded', tone: 'warning' },
};

/** Renders a domain status by its enum name. */
export function DomainStatusBadge({ status }: { status: string }) {
  const known = STATUS_VOCABULARY[status];

  if (known) {
    return <StatusBadge tone={known.tone} icon={known.icon} label={known.label} />;
  }

  // Spaced out, so an unmapped PartiallyReceived reads as words rather than as one long token.
  return <StatusBadge tone="neutral" label={status.replace(/([a-z])([A-Z])/g, '$1 $2')} />;
}
