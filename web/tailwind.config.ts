import type { Config } from 'tailwindcss';
import { colors, cssVar, fonts, radius, type ColorToken } from './src/lib/tokens';

/** Every colour token as `rgb(var(--rp-x) / <alpha-value>)`, so `bg-sun`, `bg-sun/40` and `hover:bg-sun-dark` all work. */
function colorScale(): Record<string, string> {
  const scale: Record<string, string> = {};
  for (const token of Object.keys(colors) as ColorToken[]) {
    const name = cssVar(token).slice('--rp-'.length);
    scale[name] = `rgb(var(${cssVar(token)}) / <alpha-value>)`;
  }
  return scale;
}

const config: Config = {
  content: ['./src/**/*.{ts,tsx}'],
  theme: {
    // The desktop's widths: below 768 a phone (drawer), 768 to 1279 a tablet (icons only), 1280 and up the desktop.
    screens: {
      sm: '480px',
      md: '768px',
      lg: '1024px',
      xl: '1280px',
      '2xl': '1600px',
    },
    colors: {
      transparent: 'transparent',
      current: 'currentColor',
      white: '#FFFFFF',
      black: '#000000',
      ...colorScale(),
    },
    // One scale for the whole app, from the desktop styles. Tailwind's own sizes are switched off so none sneak in.
    fontSize: {
      key: ['12.5px', { lineHeight: '16px' }],
      note: ['14px', { lineHeight: '20px' }],
      small: ['15px', { lineHeight: '22px' }],
      body: ['17px', { lineHeight: '26px' }],
      lead: ['18px', { lineHeight: '26px' }],
      section: ['20px', { lineHeight: '28px' }],
      value: ['28px', { lineHeight: '34px' }],
      title: ['34px', { lineHeight: '42px' }],
      big: ['46px', { lineHeight: '52px' }],
    },
    fontFamily: {
      heading: fonts.heading.split(/,\s*/),
      body: fonts.body.split(/,\s*/),
      mono: ['Consolas', '"Cascadia Mono"', 'ui-monospace', 'monospace'],
    },
    extend: {
      borderRadius: {
        card: radius.card,
        control: radius.control,
      },
      minHeight: {
        control: '48px',
        input: '46px',
      },
      height: {
        control: '48px',
        input: '46px',
      },
      width: {
        nav: '300px',
        'nav-icons': '84px',
      },
      boxShadow: {
        card: '0 1px 2px rgb(30 30 30 / 0.04), 0 8px 24px rgb(30 30 30 / 0.05)',
        dialog: '0 24px 64px rgb(30 30 30 / 0.22)',
        focus: '0 0 0 2px rgb(var(--rp-sun-dark) / 1)',
      },
      backgroundImage: {
        page: 'linear-gradient(120deg, rgb(var(--rp-page0)) 0%, rgb(var(--rp-page45)) 45%, rgb(var(--rp-page80)) 80%, rgb(var(--rp-page100)) 100%)',
        stripes:
          'repeating-linear-gradient(135deg, rgb(var(--rp-stone-light)) 0 10px, rgb(var(--rp-card)) 10px 20px)',
      },
      zIndex: {
        nav: '30',
        overlay: '50',
        toast: '60',
      },
      keyframes: {
        'fade-in': { from: { opacity: '0' }, to: { opacity: '1' } },
        'rise-in': { from: { opacity: '0', transform: 'translateY(6px)' }, to: { opacity: '1', transform: 'none' } },
        'slide-in': { from: { transform: 'translateX(-100%)' }, to: { transform: 'none' } },
        'slide-left': { from: { transform: 'translateX(100%)' }, to: { transform: 'none' } },
        shimmer: { '0%': { backgroundPosition: '-400px 0' }, '100%': { backgroundPosition: '400px 0' } },
      },
      animation: {
        'fade-in': 'fade-in 120ms ease-out',
        'rise-in': 'rise-in 140ms ease-out',
        'slide-in': 'slide-in 160ms ease-out',
        'slide-left': 'slide-left 160ms ease-out',
        shimmer: 'shimmer 1.4s linear infinite',
      },
    },
  },
  plugins: [],
};

export default config;
