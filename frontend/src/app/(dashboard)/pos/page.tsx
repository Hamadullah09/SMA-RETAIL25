'use client';

import { useEffect, useRef, useState } from 'react';
import { HotkeyProvider, useHotkey } from '@/lib/hotkeys';
import { usePosStore } from '@/stores/pos-store';
import { posApi, PosApiError } from '@/lib/pos-api';
import { printPdf } from '@/lib/print';
import { toast } from '@/components/ui/toaster';
import {
  CartList,
  ConnectionBanner,
  FunctionKeyBar,
  PosMessageBanner,
  ScanBox,
  SidePanel,
  StatusBar,
} from '@/components/pos/panels';
import { readStationOverride } from '@/lib/station-override';
import { TagFeed } from '@/components/pos/tag-feed';
import { ProductGrid } from '@/components/pos/product-grid';
import {
  CheatSheetDialog,
  ClientDialog,
  CreditsDialog,
  StaffSwitchDialog,
  SupervisorApprovalDialog,
  SerialPickerDialog,
  VariantPickerDialog,
  DrawerDialog,
  FindDialog,
  LineDetailDialog,
  PaymentDialog,
  SpecialDialog,
  SuspendedCartsDialog,
  UnknownItemDialog,
} from '@/components/pos/dialogs';

/**
 * The point of sale (doc 08).
 *
 * Five functional groups are visible at rest — cart, totals, payment, customer, status — and
 * everything else sits behind an explicit key. That is Miller's Law applied literally: a cashier
 * under queue pressure holds about five things, and a screen offering fifteen is a screen they will
 * learn three of.
 *
 * The station is a per-till fact, not a user choice, so nobody can ring a sale against the wrong
 * station by picking the wrong item in a dropdown. Which machine this is comes from the agent
 * installed on it — see {@link resolveStation}.
 */
// Environment variables are strings; a station and a location are rows. Number() rather than a
// cast, and NaN rather than 0 when unset — 0 is a real value elsewhere, and a till that silently
// registered itself as station 0 would ring sales against nothing.
const STATION_ID = Number(process.env.NEXT_PUBLIC_STATION_ID);
const LOCATION_ID = Number(process.env.NEXT_PUBLIC_LOCATION_ID);

const AGENT = process.env.NEXT_PUBLIC_AGENT_URL ?? 'http://127.0.0.1:8477';

/**
 * Which till this machine is, asked of the machine itself.
 *
 * These used to be only the build-time environment values, which is wrong the moment a shop has more
 * than one till: the front end is built once and served to every browser, so every machine claimed
 * the same station, and moving a reader to another PC did not move the station with it. Baking it in
 * also meant adding a till required a rebuild.
 *
 * The agent is the thing actually installed per machine, and it already knows its own station —
 * it is configured with one at install time and the server confirms it on every profile refresh. So
 * the till asks the agent, and falls back to the build-time value for a machine with no agent
 * (a back-office browser, or a shop that sells only by barcode).
 *
 * Deliberately not remembered between loads: a stale station id in localStorage would outlive the
 * agent being reconfigured, and a till quietly ringing sales against the till next door is worse
 * than one that asks again on each start.
 */
async function resolveStation(): Promise<number> {
  // An administrator's explicit choice outranks the machine's own opinion, and is checked before the
  // agent is even asked. That order is the point: the reason to pin a browser is usually that the
  // agent's answer is wrong for it — a self-checkout counter read by a handheld, or a PC watching a
  // till it is not installed on. See lib/station-override.
  const pinned = readStationOverride();
  if (pinned !== null) return pinned;

  try {
    const controller = new AbortController();
    const timer = window.setTimeout(() => controller.abort(), 1500);

    const response = await fetch(`${AGENT}/status`, { signal: controller.signal, cache: 'no-store' });
    window.clearTimeout(timer);

    if (!response.ok) return STATION_ID;

    const status = (await response.json()) as { stationId?: number };

    return Number.isFinite(status.stationId) && (status.stationId ?? 0) > 0 ? status.stationId! : STATION_ID;
  } catch {
    // No agent on this machine, or it is not answering yet. The environment value is the answer for
    // a single-till shop and the only one available for a browser with no agent behind it.
    return STATION_ID;
  }
}

/** The location still comes from the build: one deployment serves one shop. */
const CONFIGURED = Number.isFinite(LOCATION_ID) && LOCATION_ID > 0;

export default function PosPage() {
  return (
    <HotkeyProvider>
      <PosScreen />
    </HotkeyProvider>
  );
}

function PosScreen() {
  const { cart, dialog, lastSale, stationId, initialise, teardown, openDialog, removeLastLine } = usePosStore();

  const scanRef = useRef<HTMLInputElement>(null);

  /**
   * Whether the product picker is showing. A till that mostly scans wants the room for the cart; a
   * counter-service till lives in the picker. It is per-machine rather than per-user because it is a
   * property of how that till is worked, and it survives a browser restart for the same reason the
   * station id does.
   */
  const [gridOpen, setGridOpen] = useState(false);

  // Closed unless this machine has been told otherwise. A till that mostly scans wants every pixel
  // of the left column for the sale, and the picker is one key away — so the default is the one that
  // costs a counter-service till a keystroke rather than the one that costs a scanning till its list.
  useEffect(() => {
    setGridOpen(window.localStorage.getItem('retail25.pos.grid-open') === 'true');
  }, []);

  const toggleGrid = () => {
    setGridOpen((open) => {
      window.localStorage.setItem('retail25.pos.grid-open', String(!open));
      return !open;
    });
  };

  useEffect(() => {
    if (!CONFIGURED) return undefined;

    let cancelled = false;

    void (async () => {
      const station = await resolveStation();

      // The screen may have been left while the agent was being asked. Starting a till that is no
      // longer on screen would join a cart nothing is watching and never leave it.
      if (!cancelled) await initialise(station, LOCATION_ID);
    })();

    return () => {
      cancelled = true;
      void teardown();
    };
  }, [initialise, teardown]);

  // Focus returns to the scan box whenever a dialog closes, so the next barcode just works.
  useEffect(() => {
    if (dialog === null) scanRef.current?.focus();
  }, [dialog]);

  const hasLines = Boolean(cart && cart.lines.length > 0);

  // F1 to F3 are deliberately unused. On the laptops and compact keyboards a shop actually buys,
  // that row is media and brightness and needs Fn held down, and the browser claims F1 for help and
  // F3 for its own find bar — so a till key there is a key that sometimes does something else.
  // Everything therefore lives on F4 and up. Pay, Client, Delete and Reprint keep the positions
  // they have always had; Find and Credits took over the two keys that were only doing something
  // another key already did (F9 opened the payment dialog exactly as F4 does, and F12 closed a
  // dialog exactly as Escape does).
  /**
   * The just-finished sale, on paper, through the browser's own print dialog.
   *
   * The till's F7 reprint sends ESC/POS to the thermal printer through the agent and shows the
   * cashier nothing at all — correct when that printer works, useless when it does not. This is the
   * fallback that was missing: any printer the browser can see, including a PDF writer.
   */
  const printReceipt = async () => {
    if (!lastSale) return;

    try {
      const outcome = await printPdf(await posApi.receiptPdf(lastSale.transactionId));

      if (outcome === 'blocked') {
        toast({
          title: 'Pop-up blocked',
          description: 'Allow pop-ups for this site to print the receipt.',
          variant: 'destructive',
        });
      }
    } catch (error) {
      toast({
        title: 'Could not print the receipt',
        description: error instanceof PosApiError ? error.problem.detail : 'Something went wrong.',
        variant: 'destructive',
      });
    }
  };

  useHotkey('F4', () => hasLines && openDialog('payment'), { label: 'Pay', group: 'Sale', disabled: !hasLines });
  useHotkey('F5', () => openDialog('client'), { label: 'Client menu', group: 'Sale' });
  useHotkey('F6', () => void removeLastLine(), { label: 'Delete last line', group: 'Sale', disabled: !hasLines });
  useHotkey('F7', () => stationId && void posApi.reprintLast(stationId), {
    label: 'Reprint last sale',
    group: 'Documents',
  });
  useHotkey('F8', () => openDialog('credits'), { label: 'Credits menu', group: 'Sale', disabled: !cart });
  useHotkey('F9', () => openDialog('find'), { label: 'Find item', group: 'Sale' });
  useHotkey('F10', () => openDialog('drawer'), { label: 'Drawer menu', group: 'Drawer' });
  useHotkey('F11', () => openDialog('special'), { label: 'Special menu', group: 'Sale' });
  useHotkey('F12', () => lastSale && stationId && void posApi.packingSlip(lastSale.transactionId, stationId), {
    label: 'Packing slip',
    group: 'Documents',
    disabled: !lastSale,
  });
  // Ctrl+P, deliberately taking the key the browser would otherwise use for its own print dialog.
  // Printing the browser's rendering of the POS screen is never what anybody standing at a till
  // wants, and a cashier who reaches for the familiar shortcut should get the receipt.
  useHotkey('Ctrl+P', () => void printReceipt(), {
    label: 'Print receipt',
    group: 'Documents',
    disabled: !lastSale,
  });
  useHotkey('Ctrl+I', () => openDialog('staffSwitch'), { label: 'Enter staff ID', group: 'Session' });
  useHotkey('Ctrl+G', () => toggleGrid(), { label: 'Show / hide products', group: 'Sale' });
  useHotkey('Ctrl+/', () => openDialog('cheatSheet'), {
    label: 'Shortcut cheat sheet',
    group: 'Help',
    scope: 'global',
  });

  if (!CONFIGURED) {
    return (
      <div className="p-8">
        <h1 className="text-h3 font-medium">This till is not configured</h1>
        <p className="mt-2 max-w-prose text-body text-ink-muted">
          Set <code>NEXT_PUBLIC_LOCATION_ID</code> for this deployment — one shop per deployment, so it belongs in the
          build. Which till a machine is comes from the agent installed on it, not from here.
        </p>
      </div>
    );
  }

  return (
    <div className="pos-layout bg-surface text-ink" data-grid={gridOpen ? 'open' : 'closed'}>
      {/*
        The state of the till and the way into it, as one object rather than four stacked cards. The
        banners are inside it because a warning about the connection belongs *on* the thing whose
        readings it makes doubtful, not floating above it.
      */}
      <div className="pos-area-status">
        <div className="pos-panel overflow-hidden">
          <ConnectionBanner />
          <StatusBar />
          <ScanBox inputRef={scanRef} />
          <PosMessageBanner />
        </div>
      </div>

      <div className="pos-area-cart min-h-0">
        <CartList />
      </div>

      {gridOpen ? (
        <div className="pos-area-grid min-h-0">
          <ProductGrid />
        </div>
      ) : null}

      <div className="pos-area-side flex min-h-0 flex-col gap-1.5 overflow-y-auto">
        <SidePanel onPay={() => openDialog('payment')} />

        {/*
          Below the sale group, and given whatever height is left. A cashier looks here when a tag
          does not read, and that is a mid-sale moment — so it takes the slack rather than being
          pushed off the bottom, and it is the panel that shrinks when a customer's details grow.
        */}
        {/*
          The resolved station, not the build-time one. This panel opens its own RFID hub
          connection and joins a station group with it, so passing STATION_ID subscribed it to
          whichever till the bundle was built for while the rest of the screen ran on the station
          the agent reported. On a shop where those differ the panel showed a reader that was
          plainly working as offline, and no tag ever appeared in the feed, because the reads were
          being announced to a group this browser had never joined.

          Gating on stationId rather than CONFIGURED also delays the connection until resolution
          has finished, instead of opening one to NaN and reconnecting.
        */}
        {stationId ? <TagFeed stationId={stationId} locationId={LOCATION_ID} /> : null}
      </div>

      <div className="pos-area-keys">
        <FunctionKeyBar
          keys={[
            { key: 'Ctrl+G', label: gridOpen ? 'Hide items' : 'Show items', onSelect: toggleGrid },
            { key: 'F4', label: 'Pay', onSelect: () => openDialog('payment'), disabled: !hasLines },
            { key: 'F5', label: 'Client', onSelect: () => openDialog('client') },
            { key: 'F6', label: 'Delete', onSelect: () => void removeLastLine(), disabled: !hasLines },
            { key: 'F7', label: 'Reprint', onSelect: () => stationId && void posApi.reprintLast(stationId) },
            { key: 'Ctrl+P', label: 'Print', onSelect: () => void printReceipt(), disabled: !lastSale },
            { key: 'F8', label: 'Credits', onSelect: () => openDialog('credits'), disabled: !cart },
            { key: 'F9', label: 'Find', onSelect: () => openDialog('find') },
            { key: 'F10', label: 'Drawer', onSelect: () => openDialog('drawer') },
            { key: 'F11', label: 'More', onSelect: () => openDialog('special') },
          ]}
        />
      </div>

      {dialog === 'lineDetail' ? <LineDetailDialog /> : null}
      {dialog === 'payment' ? <PaymentDialog /> : null}
      {dialog === 'credits' ? <CreditsDialog /> : null}
      {dialog === 'special' ? <SpecialDialog /> : null}
      {dialog === 'drawer' ? <DrawerDialog /> : null}
      {dialog === 'client' ? <ClientDialog /> : null}
      {dialog === 'find' ? <FindDialog /> : null}
      {dialog === 'suspended' ? <SuspendedCartsDialog /> : null}
      {dialog === 'unknownItem' ? <UnknownItemDialog /> : null}
      {dialog === 'variantPicker' ? <VariantPickerDialog /> : null}
      {dialog === 'serialPicker' ? <SerialPickerDialog /> : null}
      {dialog === 'staffSwitch' ? <StaffSwitchDialog /> : null}
      {dialog === 'supervisorApproval' ? <SupervisorApprovalDialog /> : null}
      {dialog === 'cheatSheet' ? <CheatSheetDialog /> : null}
    </div>
  );
}
