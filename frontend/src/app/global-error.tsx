'use client';

/**
 * The boundary of last resort: the root layout itself failed.
 *
 * It has to render its own <html> and <body>, because the ones in layout.tsx are what did not
 * survive. Nothing here may depend on providers, fonts or the stylesheet — those are exactly what
 * might be broken — so the styles are inline and the words carry the whole message.
 */
export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <html lang="en">
      <body
        style={{
          margin: 0,
          minHeight: '100vh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: '#f5f8f9',
          color: '#0d1720',
          fontFamily: 'system-ui, -apple-system, "Segoe UI", sans-serif',
          padding: '2rem',
        }}
      >
        <main style={{ maxWidth: '32rem', textAlign: 'center' }}>
          <h1 style={{ fontSize: '1.75rem', margin: '0 0 0.75rem', lineHeight: 1.2 }}>
            SMA Retail could not start
          </h1>

          <p style={{ fontSize: '1rem', lineHeight: 1.6, margin: '0 0 1.25rem', color: '#59687a' }}>
            Nothing recorded in the shop has been affected — this is the program failing to open, not
            the records. Try again, and if it keeps happening tell whoever looks after this system.
          </p>

          {error.digest ? (
            <p style={{ fontSize: '0.875rem', color: '#59687a', margin: '0 0 1.25rem' }}>
              Reference <code>{error.digest}</code>
            </p>
          ) : null}

          <button
            type="button"
            onClick={reset}
            style={{
              minHeight: '3rem',
              padding: '0 1.25rem',
              fontSize: '1rem',
              fontWeight: 600,
              color: '#ffffff',
              background: '#3f51d4',
              border: 0,
              borderRadius: '0.625rem',
              cursor: 'pointer',
            }}
          >
            Try again
          </button>
        </main>
      </body>
    </html>
  );
}
