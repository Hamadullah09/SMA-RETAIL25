/**
 * The held shape while a screen's code and data arrive.
 *
 * A blank area between navigations reads as a broken link — somebody presses the menu item again,
 * and on a slow shop connection that is how two of something get created. A held frame says the
 * press registered.
 */
export default function DashboardLoading() {
  return (
    <div className="flex h-below-header flex-col gap-3 px-page py-panel" aria-busy="true">
      <span className="sr-only" role="status">
        Loading the screen.
      </span>

      <div className="h-10 w-64 animate-pulse rounded-sm bg-panel-sunken" />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="h-28 animate-pulse rounded-lg bg-panel-sunken" />
        ))}
      </div>

      <div className="min-h-0 flex-1 animate-pulse rounded-lg bg-panel-sunken" />
    </div>
  );
}
