/**
 * The design tokens from doc 08, registered as real theme values.
 *
 * Everything is declared as a bare `R G B` triplet in globals.css and consumed through
 * `rgb(var(--x) / <alpha-value>)` here. That one indirection is what makes `bg-panel`,
 * `hover:bg-panel-hover`, `text-ink-muted`, `border-subtle`, `bg-live/10` and `dark:` all work —
 * none of which is possible when a token is only reachable as an arbitrary value like
 * `bg-[rgb(var(--panel))]`, which Tailwind cannot vary by state, opacity or colour scheme.
 *
 * @type {import('tailwindcss').Config}
 */
module.exports = {
  // Driven by a class on <html>, so a toggle can switch it and a preference can persist.
  darkMode: 'class',

  content: [
    './src/pages/**/*.{js,ts,jsx,tsx,mdx}',
    './src/components/**/*.{js,ts,jsx,tsx,mdx}',
    './src/app/**/*.{js,ts,jsx,tsx,mdx}',
  ],

  theme: {
    extend: {
      colors: {
        /** The page behind everything. */
        surface: 'rgb(var(--surface) / <alpha-value>)',

        /** A panel sitting on the surface. Panels never nest (doc 08). */
        panel: {
          DEFAULT: 'rgb(var(--panel) / <alpha-value>)',
          hover: 'rgb(var(--panel-hover) / <alpha-value>)',
          sunken: 'rgb(var(--panel-sunken) / <alpha-value>)',
        },

        /** The 1px border that does the work cards would otherwise do. */
        subtle: 'rgb(var(--border) / <alpha-value>)',
        strong: 'rgb(var(--border-strong) / <alpha-value>)',

        ink: {
          DEFAULT: 'rgb(var(--text) / <alpha-value>)',
          muted: 'rgb(var(--text-muted) / <alpha-value>)',
          faint: 'rgb(var(--text-faint) / <alpha-value>)',
        },

        /**
         * The primary action.
         *
         * oklch rather than rgb: the token holds `L C H`, and the alpha slot stays free so
         * `bg-accent/10` still works. `soft` is the tint an active nav item or a chip sits on, and
         * `text` is the accent at a lightness that reads *on* that tint — one colour, three jobs,
         * rather than three colours that have to be kept in step by hand.
         */
        accent: {
          DEFAULT: 'oklch(var(--accent) / <alpha-value>)',
          strong: 'oklch(var(--accent-strong) / <alpha-value>)',
          soft: 'oklch(var(--accent-soft) / <alpha-value>)',
          text: 'oklch(var(--accent-text) / <alpha-value>)',
          foreground: 'rgb(var(--accent-foreground) / <alpha-value>)',
        },

        /** The only four meanings colour is allowed to carry. */
        positive: 'oklch(var(--positive) / <alpha-value>)',
        warning: 'oklch(var(--warning) / <alpha-value>)',
        negative: 'oklch(var(--negative) / <alpha-value>)',
        live: 'oklch(var(--live) / <alpha-value>)',
      },

      /**
       * A real scale. The old one ran 14 / 16 / 18px, which made a page title 1.29x body text — not
       * enough for a screen to have a reading order at a glance. Line heights travel with the size
       * so a heading never has to be corrected at the call site.
       */
      fontSize: {
        caption: ['0.6875rem', { lineHeight: '1rem', letterSpacing: '0.01em' }],
        label: ['0.75rem', { lineHeight: '1.125rem', letterSpacing: '0.02em' }],
        body: ['0.8125rem', { lineHeight: '1.25rem' }],
        'body-lg': ['0.875rem', { lineHeight: '1.375rem' }],
        h3: ['1rem', { lineHeight: '1.5rem', letterSpacing: '-0.005em' }],
        h2: ['1.25rem', { lineHeight: '1.75rem', letterSpacing: '-0.01em' }],
        h1: ['1.625rem', { lineHeight: '2rem', letterSpacing: '-0.02em' }],
        display: ['2.25rem', { lineHeight: '2.5rem', letterSpacing: '-0.03em' }],
      },

      fontFamily: {
        sans: ['var(--font-sans)', 'ui-sans-serif', 'system-ui', 'sans-serif'],

        // Figures, keyboard hints and stock codes. Previously fell through to whatever the browser
        // calls monospace, which on Windows is Courier New and reads badly beside the UI face.
        mono: ['var(--font-mono)', 'ui-monospace', 'SFMono-Regular', 'Consolas', 'monospace'],
      },

      /** 8px base, with the half-steps a dense grid genuinely needs. */
      spacing: {
        4.5: '1.125rem',
        18: '4.5rem',
      },

      borderRadius: {
        sm: 'var(--radius-dense)',
        DEFAULT: 'var(--radius)',
        md: 'var(--radius)',
        lg: 'var(--radius-lg)',
      },

      /**
       * Three steps, and they still mean "this is above the page" rather than decoration. Borders do
       * the work between things that sit *on* the page; these are for a raised panel, a popover and a
       * dialog respectively. Each is defined per colour scheme, because a black blur on a near-black
       * ground is invisible and needs to be deeper to read at all.
       */
      boxShadow: {
        raised: 'var(--shadow-1)',
        popover: 'var(--shadow-2)',
        overlay: 'var(--shadow-3)',
      },

      /** Drives the shell so a header height change cannot silently break a page's scroll box. */
      height: {
        header: 'var(--header-height)',
        'below-header': 'calc(100vh - var(--header-height))',
        'below-chrome': 'calc(100vh - var(--header-height) - var(--toolbar-height))',
      },

      minHeight: {
        control: 'var(--control-height)',
      },

      width: {
        sidebar: 'var(--sidebar-width)',
        'sidebar-collapsed': 'var(--sidebar-collapsed-width)',
      },

      margin: {
        sidebar: 'var(--sidebar-width)',
        'sidebar-collapsed': 'var(--sidebar-collapsed-width)',
      },

      keyframes: {
        'fade-in': {
          from: { opacity: '0' },
          to: { opacity: '1' },
        },
        'slide-up': {
          from: { opacity: '0', transform: 'translateY(4px)' },
          to: { opacity: '1', transform: 'translateY(0)' },
        },
      },

      animation: {
        // Short on purpose. A back office that animates is one nobody can read at speed.
        'fade-in': 'fade-in 120ms ease-out',
        'slide-up': 'slide-up 140ms ease-out',
      },
    },
  },

  plugins: [],
};
