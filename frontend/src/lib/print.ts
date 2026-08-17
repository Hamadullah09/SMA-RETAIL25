'use client';

/**
 * Printing a PDF the operator has just asked for.
 *
 * The point of this file is that "print" and "download" are different intentions and the software
 * kept conflating them. Pressing Print produced a file in Downloads and no dialog: the server sent
 * every document as `Content-Disposition: attachment`, which a browser saves however it is opened,
 * and the front end then opened it in a tab and hoped. Somebody printing shelf labels had to find
 * the file, open it, and press Ctrl+P — three steps, twice a day, forever.
 *
 * So the server now sends `inline` and this raises the dialog directly. Saving is still available
 * from the viewer for anyone who wants the file, which is the right way round: printing is the
 * common case and should be one press, and keeping the file is the rarer one and can be two.
 */

/** How the document was presented, so a caller can tell the operator something true. */
export type PrintOutcome = 'printed' | 'opened' | 'blocked';

const CLEANUP_DELAY_MS = 60_000;

/**
 * Raises the print dialog for a PDF.
 *
 * A hidden same-origin iframe rather than a pop-up: `window.open` is blocked by default in a lot of
 * configurations, and a blocked pop-up is indistinguishable to the operator from a button that does
 * nothing. The iframe is not blocked, and printing from it targets the PDF rather than the POS
 * screen behind it.
 *
 * Falls back to a tab when the iframe cannot print — some browsers refuse `print()` on an embedded
 * PDF viewer — so the worst case is the old behaviour rather than a dead button.
 */
export async function printPdf(pdf: Blob): Promise<PrintOutcome> {
  const url = URL.createObjectURL(pdf);

  const revoke = () => window.setTimeout(() => URL.revokeObjectURL(url), CLEANUP_DELAY_MS);

  try {
    const printed = await printInFrame(url);

    if (printed) {
      revoke();
      return 'printed';
    }
  } catch {
    // Fall through to the tab.
  }

  const opened = window.open(url, '_blank', 'noopener');
  revoke();

  return opened ? 'opened' : 'blocked';
}

/**
 * Loads the PDF into an off-screen frame and prints it.
 *
 * `display: none` is deliberately avoided — a frame with no box is not rendered, and a PDF that was
 * never rendered has nothing to print. It is positioned off-screen instead, which keeps it invisible
 * while remaining a real, laid-out frame.
 *
 * The frame is left in the document rather than removed on the next line: removing it cancels the
 * dialog it just opened, because the dialog belongs to that frame's window.
 */
function printInFrame(url: string): Promise<boolean> {
  return new Promise((resolve, reject) => {
    const frame = document.createElement('iframe');

    frame.setAttribute('aria-hidden', 'true');
    frame.style.position = 'fixed';
    frame.style.right = '100%';
    frame.style.bottom = '100%';
    frame.style.width = '1px';
    frame.style.height = '1px';
    frame.style.border = '0';

    // A browser that never fires load would otherwise leave the caller waiting on a promise that
    // cannot settle, and the operator looking at a button that has not visibly done anything.
    const giveUp = window.setTimeout(() => {
      cleanUp();
      resolve(false);
    }, 10_000);

    const cleanUp = () => {
      window.clearTimeout(giveUp);
      window.setTimeout(() => frame.remove(), CLEANUP_DELAY_MS);
    };

    frame.onload = () => {
      try {
        const view = frame.contentWindow;

        if (!view) {
          cleanUp();
          resolve(false);
          return;
        }

        view.focus();
        view.print();

        cleanUp();
        resolve(true);
      } catch (error) {
        cleanUp();
        reject(error instanceof Error ? error : new Error('The document could not be printed.'));
      }
    };

    frame.onerror = () => {
      cleanUp();
      resolve(false);
    };

    frame.src = url;
    document.body.appendChild(frame);
  });
}

/** Saves the PDF instead, for the operator who wanted the file rather than the printer. */
export function downloadPdf(pdf: Blob, fileName: string): void {
  const url = URL.createObjectURL(pdf);
  const link = document.createElement('a');

  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();

  window.setTimeout(() => URL.revokeObjectURL(url), CLEANUP_DELAY_MS);
}
