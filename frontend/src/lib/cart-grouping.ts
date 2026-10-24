import type { Cart, CartLine } from '@/types/pos';

/**
 * How many of a product the open sale is already taking off the shelf.
 *
 * Stock on hand only falls when a sale completes, so a product with one left still showed one left
 * while that one was sitting in the customer's basket — and the next customer was told it was there.
 * Returns and trade-ins put stock back rather than taking it, so they are not counted.
 */
export function quantityInSale(cart: Cart | null, productId: number): number {
  if (!cart) return 0;

  return cart.lines
    .filter((line) => line.productId === productId && line.lineType === 'Sale')
    .reduce((sum, line) => sum + line.quantity, 0);
}

/** How many characters of an EPC a chip on the till shows. */
const EPC_LABEL_LENGTH = 6;

/**
 * A short label for each tag that actually tells them apart.
 *
 * The till showed the last six characters of each EPC, on the assumption that the serial sits at
 * the end. On a shop's real tags it did not: six units of one blouse all read "…6B630D", because
 * their EPCs share the same ending and differ further in. So the labels are cut from where the
 * group's EPCs stop agreeing — after the characters they all share at the start and before the
 * characters they all share at the end.
 */
export function epcLabels(epcs: readonly string[]): string[] {
  if (epcs.length < 2) return epcs.map((epc) => `…${epc.slice(-EPC_LABEL_LENGTH)}`);

  const shortest = Math.min(...epcs.map((epc) => epc.length));

  let prefix = 0;
  while (prefix < shortest && epcs.every((epc) => epc[prefix] === epcs[0][prefix])) prefix++;

  let suffix = 0;
  while (
    suffix < shortest - prefix &&
    epcs.every((epc) => epc[epc.length - 1 - suffix] === epcs[0][epcs[0].length - 1 - suffix])
  ) {
    suffix++;
  }

  return epcs.map((epc) => {
    const differing = epc.slice(prefix, epc.length - suffix);

    // Identical tags (which the server refuses anyway) have nothing that differs; show the end.
    if (differing.length === 0) return `…${epc.slice(-EPC_LABEL_LENGTH)}`;

    const shown = differing.slice(0, EPC_LABEL_LENGTH);
    const before = prefix > 0 ? '…' : '';
    const after = prefix + shown.length < epc.length ? '…' : '';

    return `${before}${shown}${after}`;
  });
}

/** One row of the sale as the cashier sees it: possibly several tagged units of the same item. */
export interface GroupedLine {
  /** Stable across renders, for React. */
  key: string;
  /** The units behind the row, in the order they were added. Never empty. */
  lines: CartLine[];
  first: CartLine;
  /** The unit added last — the one a cashier who mis-read something means to take off. */
  last: CartLine;
  quantity: number;
  extendedNet: number;
  lineWeight: number;
  /** The tags behind the row, when it stands for tagged units. */
  epcs: string[];
}

/**
 * The sale's lines, with tagged units of one item gathered into one row.
 *
 * A tagged unit is its own line on the server, deliberately — it is what lets one of three identical
 * shirts be taken off, or later returned, on its own. But three of a shirt drawn as three rows reads as
 * three different things, and that is what a cashier reported: pick quantity three, see three items.
 * They are now one row with the quantity and the tags beneath it, exactly as the receipt prints them.
 *
 * Only tagged units are gathered, and only when everything the customer would see as different
 * matches: price, discount, price level, taxes, line type and note. Untagged items already merge into
 * one line on the server, and the lines that stay separate do so for a reason — two weighed packs are
 * two packs. Rows keep the order in which their first unit was added.
 */
export function groupCartLines(lines: readonly CartLine[]): GroupedLine[] {
  const groups = new Map<string, CartLine[]>();

  for (const line of lines) {
    const key = line.epc
      ? [
          'tagged',
          line.productId,
          line.variantId ?? '',
          line.unitPrice,
          line.discountPct,
          line.tax1Applies,
          line.tax2Applies,
          line.lineType,
          line.priceOrigin,
          line.note ?? '',
        ].join('|')
      : `line|${line.sequence}`;

    const existing = groups.get(key);
    if (existing) existing.push(line);
    else groups.set(key, [line]);
  }

  return [...groups.entries()].map(([key, members]) => ({
    key,
    lines: members,
    first: members[0],
    last: members[members.length - 1],
    quantity: members.reduce((sum, line) => sum + line.quantity, 0),
    // A plain sum. The display formats it at the currency's own scale, which is not this module's to
    // assume; rounding to cents here would be wrong for a currency with none.
    extendedNet: members.reduce((sum, line) => sum + line.extendedNet, 0),
    lineWeight: members.reduce((sum, line) => sum + line.lineWeight, 0),
    epcs: members.map((line) => line.epc).filter((epc): epc is string => Boolean(epc)),
  }));
}
