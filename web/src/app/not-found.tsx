'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { pageForPath } from '@/lib/nav';

/**
 * The export's 404.html. The server answers `/tills` (no slash) with it, since the page is `/tills/index.html`: when
 * adding the slash names a page, go there instead of saying it is not found.
 */
export default function NotFound() {
  const [moving, setMoving] = useState(true);
  useEffect(() => {
    const { pathname, search, hash } = window.location;
    const withSlash = pathname + '/';
    if (!pathname.endsWith('/') && (pageForPath(withSlash) || withSlash === '/sign-in/')) window.location.replace(withSlash + search + hash);
    else setMoving(false);
  }, []);

  return (
    <main className="grid min-h-screen place-items-center p-6">
      {!moving && (
        <div className="card w-full max-w-lg p-8 text-center">
          <h1 className="page-title mb-2">Page not found</h1>
          <p className="mb-6 text-muted">There is no page at this address. It may have moved.</p>
          <Link href="/" className="btn btn-sun">
            Go to the dashboard
          </Link>
        </div>
      )}
    </main>
  );
}
