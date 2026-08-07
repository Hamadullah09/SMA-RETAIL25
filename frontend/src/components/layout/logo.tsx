/* eslint-disable @next/next/no-img-element */

/**
 * The SMA mark.
 *
 * The supplied artwork, cropped to the figure and its glass and squared, with the JPEG's white
 * ground turned transparent so it sits on a panel, a dark theme or the orange tile alike. The
 * "SMA Technology" wordmark beside it in the original is deliberately not here: this appears at
 * 36 pixels in a rail and 44 in a card, where the words would be an unreadable smudge, and the
 * product's own name is already set in type next to it.
 *
 * `gradientId` is accepted and ignored. It was needed while this was an inline SVG — two copies on
 * one page both resolved to whichever gradient definition came first, and below `lg` that was the
 * one inside the hidden hero, so the visible mark rendered grey. An image has no such problem, but
 * the callers that pass it are correct to and there is no reason to make them change.
 */
export function SmaMark({
  className,
}: {
  className?: string;
  gradientId?: string;
}) {
  return (
    <img
      src="/sma-logo.png"
      alt="SMA Retail"
      /* Square source, so `object-contain` only ever letterboxes the rounding, never the figure. */
      className={className ? `${className} object-contain` : 'h-9 w-9 object-contain'}
    />
  );
}
