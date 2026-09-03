import type { Metadata } from 'next';
import { Onest, JetBrains_Mono } from 'next/font/google';
import { Providers } from '@/components/providers';
import './globals.css';

/**
 * The UI face.
 *
 * Onest rather than Inter. Both are neutral grotesques, but Onest carries slightly more character
 * at the small sizes this application actually uses — a back office runs at 12 to 13px, where Inter
 * flattens into something that reads as "a form" rather than as a product.
 */
const sans = Onest({
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
  title: 'SMA Retail',
  description: 'Point of sale, inventory and accounts in one place.',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${sans.variable} ${mono.variable}`} suppressHydrationWarning>
      <head>
      </head>
      <body className="font-sans">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
