'use client';

import { useEffect, useRef, useState } from 'react';
import { HotkeyProvider, useHotkey } from '@/lib/hotkeys';
import { usePosStore } from '@/stores/pos-store';
import { posApi } from '@/lib/pos-api';
import {
  CartList,
  ConnectionBanner,
  CustomerPanel,
  FunctionKeyBar,
  PaymentMatrix,
  PosMessageBanner,
  ScanBox,
  StatusBar,
  TotalsPanel,
  money,
} from '@/components/pos/panels';
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
 * The station and location come from this machine's environment. They are per-till facts, not user
 * choices, so a till set up once keeps working after a browser restart with nobody selecting anything
 * — and nobody can ring a sale against the wrong station by picking the wrong item in a dropdown.
 */
// Environment variables are strings; a station and a location are rows. Number() rather than a
// cast, and NaN rather than 0 when unset — 0 is a real value elsewhere, and a till that silently
// registered itself as station 0 would ring sales against nothing.
const STATION_ID = Number(process.env.NEXT_PUBLIC_STATION_ID);
const LOCATION_ID = Number(process.env.NEXT_PUBLIC_LOCATION_ID);

/** Both must be real numbers before this till may do anything. */
const CONFIGURED = Number.isFinite(STATION_ID) && Number.isFinite(LOCATION_ID) && STATION_ID > 0 && LOCATION_ID > 0;

export default function PosPage() {
  return (
    <HotkeyProvider>
      <PosScreen />
    </HotkeyProvider>
  );
}

function PosScreen() {
  const {
    cart,
    dialog,
    lastSale,
    policy,
    stationId,
    initialise,
    teardown,
    openDialog,
    removeLastLine,
  } = usePosStore();

  const scanRef = useRef<HTMLInputElement>(null);

  /**
   * Whether the product picker is showing. A till that mostly scans wants the room for the cart; a
   * counter-service till lives in the picker. It is per-machine rather than per-user because it is a
   * property of how that till is worked, and it survives a browser restart for the same reason the
   * station id does.
   */
  const [gridOpen, setGridOpen] = useState(false);

  useEffect(() => {
    setGridOpen(window.localStorage.getItem('retail25.pos.grid-open') !== 'false');
  }, []);

  const toggleGrid = () => {
    setGridOpen((open) => {
      window.localStorage.setItem('retail25.pos.grid-open', String(!open));
      return !open;
    });
  };

  useEffect(() => {
    if (!CONFIGURED) return undefined;

    void initialise(STATION_ID, LOCATION_ID);
    return () => void teardown();
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
          Set <code>NEXT_PUBLIC_STATION_ID</code> and <code>NEXT_PUBLIC_LOCATION_ID</code> for this machine. They
          identify the physical till, so they belong in its environment rather than in a picker a cashier could get
          wrong.
        </p>
      </div>
    );
  }

  return (
    <div className="pos-layout bg-surface text-ink" data-grid={gridOpen ? 'open' : 'closed'}>
      <div className="pos-area-status space-y-2">
        <ConnectionBanner />
        <StatusBar />
        <ScanBox inputRef={scanRef} />
        <PosMessageBanner />
        {lastSale ? (
          <p className="px-1 text-label text-ink-muted" role="status">
            Sale #{lastSale.transactionNumber} saved
            {lastSale.changeGiven > 0 ? ` · change ${money(lastSale.changeGiven, policy?.currencySymbol)}` : ''}
          </p>
        ) : null}
      </div>

      <div className="pos-area-cart min-h-0">
        <CartList />
      </div>

      {gridOpen ? (
        <div className="pos-area-grid min-h-0">
          <ProductGrid />
        </div>
      ) : null}

      <div className="pos-area-side flex min-h-0 flex-col gap-2 overflow-y-auto">
        <CustomerPanel />
        <TotalsPanel />

        {/*
          Above the payment keys, below the money. A cashier looks here when a tag does not read, and
          that is a mid-sale moment — putting it off-screen would mean scrolling while holding an item.
        */}
        {CONFIGURED ? <TagFeed stationId={STATION_ID} locationId={LOCATION_ID} /> : null}

        <PaymentMatrix onPay={() => openDialog('payment')} />
      </div>

      <div className="pos-area-keys">
        <FunctionKeyBar
          keys={[
            { key: 'Ctrl+G', label: gridOpen ? 'Hide items' : 'Show items', onSelect: toggleGrid },
            { key: 'F4', label: 'Pay', onSelect: () => openDialog('payment'), disabled: !hasLines },
            { key: 'F5', label: 'Client', onSelect: () => openDialog('client') },
            { key: 'F6', label: 'Delete', onSelect: () => void removeLastLine(), disabled: !hasLines },
            { key: 'F7', label: 'Reprint', onSelect: () => stationId && void posApi.reprintLast(stationId) },
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
