'use client';

import { useEffect, useState } from 'react';
import { Monitor, Moon, Sun } from 'lucide-react';
import { cn } from '@/lib/utils';

type Theme = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'r25.theme';

/**
 * Reads the stored choice, or falls back to following the operating system.
 * <p>Exported so the inline boot script and this component cannot disagree about the key.</p>
 */
export function resolveTheme(theme: Theme): 'light' | 'dark' {
  if (theme !== 'system') {
    return theme;
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function applyTheme(theme: Theme): void {
  document.documentElement.classList.toggle('dark', resolveTheme(theme) === 'dark');
}

/**
 * Light / dark / follow the system.
 *
 * Dark mode has been in the tokens since the beginning and could never activate: `darkMode` was not
 * configured in the Tailwind build, and nothing anywhere put the class on the document. Doc 08 calls
 * dark mode first-class because tills sit under fluorescent light all day, so this is the control
 * that makes the tokens that were already written actually reachable.
 *
 * Three states rather than two, because "follow the system" is what most people want and a two-way
 * switch cannot express it — once you toggle, you are pinned forever.
 */
export function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>('system');

  useEffect(() => {
    const stored = (localStorage.getItem(STORAGE_KEY) as Theme | null) ?? 'system';
    setTheme(stored);
    applyTheme(stored);
  }, []);

  // Following the system means following it as it changes, not only at load.
  useEffect(() => {
    if (theme !== 'system') return;

    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => applyTheme('system');

    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, [theme]);

  const choose = (next: Theme) => {
    setTheme(next);
    applyTheme(next);

    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // A browser with storage disabled loses the preference on reload, not the ability to switch.
    }
  };

  const options: Array<{ value: Theme; label: string; Icon: typeof Sun }> = [
    { value: 'light', label: 'Light', Icon: Sun },
    { value: 'dark', label: 'Dark', Icon: Moon },
    { value: 'system', label: 'Match the system', Icon: Monitor },
  ];

  return (
    <div
      role="radiogroup"
      aria-label="Colour theme"
      className="hidden items-center gap-0.5 rounded border border-subtle p-0.5 md:inline-flex"
    >
      {options.map(({ value, label, Icon }) => (
        <button
          key={value}
          type="button"
          role="radio"
          aria-checked={theme === value}
          aria-label={label}
          title={label}
          onClick={() => choose(value)}
          className={cn(
            'inline-flex h-6 w-6 items-center justify-center rounded-sm transition-colors',
            theme === value ? 'bg-accent text-accent-foreground' : 'text-ink-muted hover:bg-panel-hover hover:text-ink',
          )}
        >
          <Icon className="h-3.5 w-3.5" aria-hidden />
        </button>
      ))}
    </div>
  );
}
