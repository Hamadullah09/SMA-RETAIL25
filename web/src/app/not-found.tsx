import Link from 'next/link';

export default function NotFound() {
  return (
    <main className="grid min-h-screen place-items-center p-6">
      <div className="card w-full max-w-lg p-8 text-center">
        <h1 className="page-title mb-2">Page not found</h1>
        <p className="mb-6 text-muted">There is no page at this address. It may have moved.</p>
        <Link href="/" className="btn btn-sun">
          Go to the dashboard
        </Link>
      </div>
    </main>
  );
}
