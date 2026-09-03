import { ROUTES, type AppRoute } from './routes';

/**
 * Which declared route a pathname is currently on.
 *
 * Longest matching prefix, which is the rule a router already uses. `startsWith` on its own lights
 * up two rows at once — `/purchasing/suppliers` sits inside `/purchasing`, and `/inventory/counts`
 * inside `/inventory`. That was survivable when the active state was a faint tint; against a solid
 * accent fill, two filled rows is simply wrong.
 *
 * Extracted from sidebar.tsx so that nav highlighting and help resolution answer "where am I?" with
 * the same code. Two implementations of that question will eventually disagree, and the failure is
 * quiet: the rail highlights one screen while Ctrl+H opens the guide for another.
 */
export function matchRoute(pathname: string): AppRoute | undefined {
  return ROUTES.filter((route) => pathname === route.href || pathname.startsWith(`${route.href}/`))
    .sort((a, b) => b.href.length - a.href.length)[0];
}

/**
 * The rail row to fill in for a pathname — the top-level section, not the child.
 *
 * A child page keeps its parent lit: standing on `/inventory/counts`, the row that should look
 * current is "Stock", because that is the thing the reader is inside.
 */
export function activeNavHref(pathname: string): string | undefined {
  const match = matchRoute(pathname);

  if (!match) return undefined;

  return match.parent ?? match.href;
}

/**
 * Where Ctrl+H goes from here.
 *
 * Falls back to the help index rather than guessing. A guide for the wrong screen is worse than an
 * index, because somebody following it will do the wrong thing confidently.
 */
export function helpTopicFor(pathname: string): string {
  const topic = matchRoute(pathname)?.helpTopic;

  return topic ? `/help/${topic}` : '/help';
}

/**
 * The trail from the rail down to here, for a breadcrumb.
 *
 * Empty for a top-level screen — a breadcrumb with one entry is decoration.
 */
export function breadcrumbFor(pathname: string): AppRoute[] {
  const match = matchRoute(pathname);

  if (!match) return [];

  const parent = match.parent ? ROUTES.find((route) => route.href === match.parent) : undefined;

  return parent ? [parent, match] : [match];
}
