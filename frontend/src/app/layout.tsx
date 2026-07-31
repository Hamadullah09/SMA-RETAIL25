import type { Metadata } from 'next';
import { Inter, JetBrains_Mono } from 'next/font/google';
import { Providers } from '@/components/providers';
import './globals.css';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-sans',
  display: 'swap',
});

/**
 * Figures, stock codes and keyboard hints.
 *
 * Previously the mono stack fell through to whatever the browser calls monospace, which on Windows
 * is Courier New — noticeably lighter and wider than Inter, so every keyboard hint and stock code
 * looked like it had come from a different application.
 */
const mono = JetBrains_Mono({
  subsets: ['latin'],
  variable: '--font-mono',
  display: 'swap',
});

export const metadata: Metadata = {
  title: 'Retail 25 POS',
  description: 'Enterprise Point of Sale & ERP',
};

/**
 * Sets the theme class before the first paint.
 *
 * Without this, a dark-mode user gets a white flash on every navigation to a server-rendered page,
 * because React only applies the class after hydration. Inline and synchronous on purpose — it has
 * to run before the browser paints, and it is small enough to read.
 */
const themeBootstrap = `
(function () {
  try {
    var stored = localStorage.getItem('r25.theme') || 'system';
    var dark = stored === 'dark'
      || (stored === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
    document.documentElement.classList.toggle('dark', dark);
  } catch (e) {
    // Storage disabled. Light mode is the safe default, not a failed page.
  }
})();
`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${inter.variable} ${mono.variable}`} suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeBootstrap }} />
      </head>
      <body className="font-sans">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
