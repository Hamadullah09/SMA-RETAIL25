/**
 * The SMA Retail mark.
 *
 * It inherits the two things that make the parent brand recognisable — the orange and the
 * magnifying glass — and drops the rest. The original is a rendered 3D figure holding a glass,
 * which cannot survive being drawn at sixteen pixels in a browser tab, so this is the same idea
 * built as flat geometry: a lens, and inside it the thing this product looks at, which is stock.
 *
 * Deliberately wordless. A mark that carries the parent company's name cannot be used as the
 * customer's own logo, and this system is white-labelled — the shop's name and logo come from the
 * branding rows, and this stands in only where the product itself is speaking.
 */
/**
 * `gradientId` exists because an SVG gradient is referenced by a document-wide id, not a local one.
 * Two marks on the same page therefore both resolve to whichever def comes first — and when that
 * first one sits inside a `display:none` subtree, as it does on the account screens where the hero
 * is hidden below `lg`, the visible mark loses its fill and renders grey. Callers that can put two
 * marks in one document pass a distinct id; a single mark never has to think about it.
 */
export function SmaMark({
  className,
  gradientId = 'sma-mark-tile',
}: {
  className?: string;
  gradientId?: string;
}) {
  return (
    <svg viewBox="0 0 64 64" className={className} role="img" aria-label="SMA Retail">
      <defs>
        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#FB9A2E" />
          <stop offset="1" stopColor="#E8620E" />
        </linearGradient>
      </defs>

      <rect width="64" height="64" rx="15" fill={`url(#${gradientId})`} />

      {/* The glass. Its handle is heavier than its rim so the silhouette still reads once the
          detail inside the lens is too small to see. */}
      <circle cx="27" cy="27" r="14" fill="none" stroke="#fff" strokeWidth="4.5" />
      <path d="M37.5 37.5 L50 50" fill="none" stroke="#fff" strokeWidth="6" strokeLinecap="round" />

      {/* What is under the glass: a shopping bag. */}
      <path d="M24.2 23.5 v-2.2 a2.8 2.8 0 0 1 5.6 0 v2.2" fill="none" stroke="#fff" strokeWidth="2" />
      <rect x="21" y="23.5" width="12" height="10.5" rx="2.2" fill="#fff" />
    </svg>
  );
}
