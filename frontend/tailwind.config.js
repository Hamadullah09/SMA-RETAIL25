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
    /**
     * The two widths this product actually changes shape at, named so they stop being magic numbers
     * repeated in globals.css media queries. Tailwind's defaults are kept underneath; these two are
     * the ones that mean something here — below `tablet` the POS unpins, below `desk` the product
     * picker is not shown beside the cart.
     */
    screens: {
      sm: '640px',
      md: '768px',
      tablet: '1024px',
      lg: '1024px',
      desk: '1280px',
      xl: '1280px',
      '2xl': '1536px',
    },

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

        /** The edge of an operable control, which WCAG asks to be 3:1 rather than merely quiet. */
        control: 'rgb(var(--border-control) / <alpha-value>)',

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

        /**
         * The only four meanings colour is allowed to carry.
         *
         * Each has a fill and a text tone, the way `accent` does. The plain token fills; the `-text`
         * token is the same meaning at a lightness that can be read as words on its own tint.
         */
        positive: 'oklch(var(--positive) / <alpha-value>)',
        warning: 'oklch(var(--warning) / <alpha-value>)',
        negative: 'oklch(var(--negative) / <alpha-value>)',
        live: 'oklch(var(--live) / <alpha-value>)',
        'positive-text': 'oklch(var(--positive-text) / <alpha-value>)',
        'warning-text': 'oklch(var(--warning-text) / <alpha-value>)',
        'negative-text': 'oklch(var(--negative-text) / <alpha-value>)',
        'live-text': 'oklch(var(--live-text) / <alpha-value>)',
      },

      /**
       * A real scale. The old one ran 14 / 16 / 18px, which made a page title 1.29x body text — not
       * enough for a screen to have a reading order at a glance. Line heights travel with the size
       * so a heading never has to be corrected at the call site.
       */
      fontSize: {
        /*
         * Re-pitched for the people who actually use this: retail staff, often older, on a cheap
         * panel at arm's length, under pressure. Nothing in the scale is below 14px any more, so
         * "secondary information stays readable" is a property of the system rather than something
         * review has to police at every call site.
         *
         * The names are unchanged, so all 539 call sites keep working and the whole shift is these
         * ten values.
         */
        caption: ['0.875rem', { lineHeight: '1.25rem', letterSpacing: '0.005em' }],
        label: ['0.9375rem', { lineHeight: '1.375rem', letterSpacing: '0.01em' }],
        body: ['1rem', { lineHeight: '1.5rem' }],
        'body-lg': ['1.125rem', { lineHeight: '1.625rem' }],
        h3: ['1.25rem', { lineHeight: '1.75rem', letterSpacing: '-0.005em' }],
        h2: ['1.5rem', { lineHeight: '2rem', letterSpacing: '-0.01em' }],
        h1: ['1.875rem', { lineHeight: '2.375rem', letterSpacing: '-0.02em' }],

        /*
         * The band the scale did not have. A figure that is the point of its tile — a day's takings,
         * an amount due, a balance — belongs between a heading and the POS grand total, and there
         * was nothing between h1 and display, so tiles reached for the 36px display and overflowed
         * at four-up, or for h1 and read as a heading rather than a number.
         */
        value: ['1.5rem', { lineHeight: '1.75rem', letterSpacing: '-0.01em' }],
        'value-lg': ['1.75rem', { lineHeight: '2rem', letterSpacing: '-0.015em' }],

        display: ['2.25rem', { lineHeight: '2.5rem', letterSpacing: '-0.03em' }],
      },

      fontFamily: {
        sans: ['var(--font-sans)', 'ui-sans-serif', 'system-ui', 'sans-serif'],

        // Figures, keyboard hints and stock codes. Previously fell through to whatever the browser
        // calls monospace, which on Windows is Courier New and reads badly beside the UI face.
        mono: ['var(--font-mono)', 'ui-monospace', 'SFMono-Regular', 'Consolas', 'monospace'],
      },

      /**
       * 8px base, with the half-steps a dense grid genuinely needs, plus four steps that say what
       * they are for. A page margin chosen by name is one a second page can match.
       */
      spacing: {
        4.5: '1.125rem',
        18: '4.5rem',
        page: 'var(--space-page)',
        section: 'var(--space-section)',
        panel: 'var(--space-panel)',
        field: 'var(--space-field)',
        control: 'var(--control-height)',
        tap: 'var(--tap-min)',
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

      /**
       * Drives the shell so a header height change cannot silently break a page's scroll box.
       *
       * dvh rather than vh: on a tablet or phone, 100vh is the viewport as though the browser
       * chrome were not there, so a box sized against it runs under the address bar. The vh value
       * stays as the fallback for anything that has not heard of dvh.
       */
      height: {
        header: 'var(--header-height)',
        'below-header': ['calc(100vh - var(--header-height))', 'calc(100dvh - var(--header-height))'],
        'below-chrome': [
          'calc(100vh - var(--header-height) - var(--toolbar-height))',
          'calc(100dvh - var(--header-height) - var(--toolbar-height))',
        ],
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

      /**
       * Four steps, in the order things actually stack.
       *
       * The drawer's scrim and the app header were both z-30, so the header painted over the very
       * overlay meant to cover it — the shop's own name sitting on top of a dimmed screen. Naming
       * the layers is what stops the next such collision being settled by whichever number somebody
       * typed last.
       */
      zIndex: {
        base: '0',
        sticky: '10',
        drawer: '30',
        overlay: '50',
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
