'use client';

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { BookOpen, ExternalLink } from 'lucide-react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { HelpArticle } from './help-article';
import { helpTopic } from '@/lib/help-content';
import { helpTopicFor } from '@/lib/route-match';
import { useHotkey, useHotkeyScope } from '@/lib/hotkeys';

interface HelpControls {
  open: (slug?: string) => void;
  /** The slug for the screen the reader is standing on, so a button can say what it will open. */
  topicForHere: string | undefined;
}

const HelpContext = createContext<HelpControls | null>(null);

/**
 * Help that does not take the screen away.
 *
 * The Help button used to be a link. On a back-office form that means leaving a half-filled form to
 * read how to fill it in, and on the till it is worse than that: navigating away from `/pos` tears
 * down the SignalR connection and drops the cart. So the one place somebody goes when they are
 * stuck was also a place that could lose their work.
 *
 * An overlay solves both, and solving it once means Help behaves the same everywhere — which is the
 * point. The guide is still a real page underneath (`/help/<topic>`), for printing, for a link
 * somebody sends a colleague, and for anyone who would rather read it full-width; the overlay links
 * out to it rather than reimplementing it.
 */
export function HelpProvider({ children }: { children: ReactNode }) {
  const pathname = usePathname() ?? '/';
  const [slug, setSlug] = useState<string | null>(null);

  const topicForHere = helpTopicFor(pathname);

  const open = useCallback(
    (requested?: string) => {
      // An empty string, not a guess. A screen the registry does not know still opens the panel —
      // it says so and offers the index — because a shortcut that does nothing at all reads as
      // broken, and the person pressing it is already stuck.
      setSlug(requested ?? topicForHere ?? '');
    },
    [topicForHere],
  );

  const value = useMemo<HelpControls>(() => ({ open, topicForHere }), [open, topicForHere]);

  return (
    <HelpContext.Provider value={value}>
      {children}
      <HelpHotkeys />
      <HelpDialog slug={slug} onClose={() => setSlug(null)} />
    </HelpContext.Provider>
  );
}

/**
 * Ctrl+H, and F1 alongside it.
 *
 * Ctrl+H is what the rest of the application documents, and F1 is what somebody who has used a
 * computer since the nineties will actually press. They are two doors into one room, not two
 * features — and F1 costs nothing, because nothing else in this application binds it.
 *
 * Registered in the `global` scope so it survives an open dialog. Somebody with the payment dialog
 * up and a question about it is precisely the person who needs this, and a shortcut that stops
 * working exactly when it is wanted is a shortcut nobody trusts.
 */
function HelpHotkeys() {
  const { open } = useHelp();

  useHotkey('Ctrl+H', () => open(), { scope: 'global', label: 'Help for this screen', group: 'Help' });
  useHotkey('F1', () => open(), { scope: 'global', label: 'Help for this screen', group: 'Help', hidden: true });

  return null;
}

function HelpDialog({ slug, onClose }: { slug: string | null; onClose: () => void }) {
  // `helpTopic` itself falls back to what the route registry knows — the screen's name, what it is
  // for, how to reach it — for a topic nobody has written a guide for yet. That is thin, but true.
  const topic = slug ? helpTopic(slug) : undefined;

  /*
    While the panel is up, the screen behind it stops answering its own keys.

    Radix traps focus, but the hotkey registry listens on `window` in the capture phase and does not
    care where focus is. Without this, reading the till's guide with F4 in it would mean that
    pressing F4 to see what it does opens the payment dialog underneath the thing explaining it.

    Ctrl+H and F1 are bound in the `global` scope, so they still reach here.
  */
  useHotkeyScope('dialog', slug !== null);

  return (
    <DialogPrimitive.Root open={slug !== null} onOpenChange={(next) => !next && onClose()}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-overlay bg-black/40 data-[state=open]:animate-fade-in" />

        {/*
          A side panel rather than a centred box. The reader is comparing the guide against the
          screen it describes — "press the Post button" is only useful while the Post button is
          still visible — so the thing being explained stays on screen beside the explanation.
        */}
        <DialogPrimitive.Content
          className="fixed inset-y-0 right-0 z-overlay flex w-full max-w-xl flex-col border-l border-subtle bg-panel shadow-overlay data-[state=open]:animate-slide-up sm:w-[32rem]"
          aria-describedby={undefined}
        >
          <div className="flex shrink-0 items-start justify-between gap-4 border-b border-subtle px-5 py-4">
            <div className="min-w-0">
              <p className="flex items-center gap-2 text-label font-medium text-accent-text">
                <BookOpen className="h-5 w-5 shrink-0" aria-hidden />
                Help
              </p>
              <DialogPrimitive.Title className="mt-1 text-h2 font-semibold">
                {topic?.title ?? 'Help'}
              </DialogPrimitive.Title>
              {topic?.summary ? <p className="mt-1 text-body text-ink-muted">{topic.summary}</p> : null}
            </div>

            {/* A real target, not a glyph to aim at. */}
            <DialogPrimitive.Close className="pos-button shrink-0">Close</DialogPrimitive.Close>
          </div>

          <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5">
            {topic ? (
              <HelpArticle topic={topic} />
            ) : (
              <p className="text-body leading-relaxed text-ink-muted">
                There is no guide for this screen yet. All guides are listed below, and pressing{' '}
                <kbd className="font-mono">Ctrl</kbd>+<kbd className="font-mono">K</kbd> searches every
                screen by name.
              </p>
            )}
          </div>

          <div className="flex shrink-0 flex-wrap items-center gap-2 border-t border-subtle px-5 py-4">
            {topic ? (
              <Link href={`/help/${topic.slug}`} className="pos-button" onClick={onClose}>
                <ExternalLink className="h-5 w-5 shrink-0" aria-hidden />
                Open as a page
              </Link>
            ) : null}
            <Link href="/help" className="pos-button" onClick={onClose}>
              All guides
            </Link>
            <p className="ml-auto text-caption text-ink-muted">
              Press <kbd className="font-mono">Ctrl</kbd>+<kbd className="font-mono">H</kbd> on any screen.
            </p>
          </div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}

/**
 * Opening help from a button.
 *
 * Returns a no-op outside the provider rather than throwing. Help is not load-bearing: a screen
 * rendered in a test harness without the shell should still render, and a Help button that does
 * nothing there is better than a page that will not mount.
 */
export function useHelp(): HelpControls {
  return useContext(HelpContext) ?? NO_HELP;
}

const NO_HELP: HelpControls = { open: () => {}, topicForHere: undefined };
