'use client';

import { useEffect, useRef } from 'react';
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
const STATION_ID = process.env.NEXT_PUBLIC_STATION_ID ?? '';
const LOCATION_ID = process.env.NEXT_PUBLIC_LOCATION_ID ?? '';

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
    closeDialog,
    removeLastLine,
  } = usePosStore();

  const scanRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!STATION_ID || !LOCATION_ID) return undefined;

    void initialise(STATION_ID, LOCATION_ID);
    return () => void teardown();
  }, [initialise, teardown]);

  // Focus returns to the scan box whenever a dialog closes, so the next barcode just works.
  useEffect(() => {
    if (dialog === null) scanRef.current?.focus();
  }, [dialog]);

  const hasLines = Boolean(cart && cart.lines.length > 0);

  useHotkey('F2', () => openDialog('find'), { label: 'Find item', group: 'Sale' });
  useHotkey('F3', () => openDialog('credits'), { label: 'Credits menu', group: 'Sale', disabled: !cart });
  useHotkey('F4', () => hasLines && openDialog('payment'), { label: 'Pay', group: 'Sale', disabled: !hasLines });
  useHotkey('F5', () => openDialog('client'), { label: 'Client menu', group: 'Sale' });
  useHotkey('F6', () => void removeLastLine(), { label: 'Delete last line', group: 'Sale', disabled: !hasLines });
  useHotkey('F7', () => stationId && void posApi.reprintLast(stationId), {
    label: 'Reprint last sale',
    group: 'Documents',
  });
  useHotkey('F8', () => lastSale && stationId && void posApi.packingSlip(lastSale.transactionId, stationId), {
    label: 'Packing slip',
    group: 'Documents',
    disabled: !lastSale,
  });
  useHotkey('F9', () => hasLines && openDialog('payment'), { label: 'Save sale', group: 'Sale', disabled: !hasLines });
  useHotkey('F10', () => openDialog('drawer'), { label: 'Drawer menu', group: 'Drawer' });
  useHotkey('F11', () => openDialog('special'), { label: 'Special menu', group: 'Sale' });
  useHotkey('F12', () => closeDialog(), { label: 'Close / cancel', group: 'Sale' });
  useHotkey('Ctrl+I', () => openDialog('staffSwitch'), { label: 'Enter staff ID', group: 'Session' });
  useHotkey('Ctrl+/', () => openDialog('cheatSheet'), {
    label: 'Shortcut cheat sheet',
    group: 'Help',
    scope: 'global',
  });

  if (!STATION_ID || !LOCATION_ID) {
    return (
      <div className="p-8">
        <h1 className="text-lg font-medium">This till is not configured</h1>
        <p className="mt-2 max-w-prose text-sm text-[rgb(var(--text-muted))]">
          Set <code>NEXT_PUBLIC_STATION_ID</code> and <code>NEXT_PUBLIC_LOCATION_ID</code> for this machine. They
          identify the physical till, so they belong in its environment rather than in a picker a cashier could get
          wrong.
        </p>
      </div>
    );
  }

  return (
    <div className="pos-layout bg-[rgb(var(--surface))] text-[rgb(var(--text))]">
      <div className="pos-area-status space-y-2">
        <ConnectionBanner />
        <StatusBar />
        <ScanBox inputRef={scanRef} />
        <PosMessageBanner />
        {lastSale ? (
          <p className="px-1 text-xs text-[rgb(var(--text-muted))]" role="status">
            Sale #{lastSale.transactionNumber} saved
            {lastSale.changeGiven > 0 ? ` · change ${money(lastSale.changeGiven, policy?.currencySymbol)}` : ''}
          </p>
        ) : null}
      </div>

      <div className="pos-area-cart min-h-0">
        <CartList />
      </div>

      <div className="pos-area-side flex min-h-0 flex-col gap-2 overflow-y-auto">
        <CustomerPanel />
        <TotalsPanel />
        <PaymentMatrix onPay={() => openDialog('payment')} />
      </div>

      <div className="pos-area-keys">
        <FunctionKeyBar
          keys={[
            { key: 'F2', label: 'Find', onSelect: () => openDialog('find') },
            { key: 'F3', label: 'Credits', onSelect: () => openDialog('credits'), disabled: !cart },
            { key: 'F4', label: 'Pay', onSelect: () => openDialog('payment'), disabled: !hasLines },
            { key: 'F5', label: 'Client', onSelect: () => openDialog('client') },
            { key: 'F6', label: 'Delete', onSelect: () => void removeLastLine(), disabled: !hasLines },
            { key: 'F7', label: 'Reprint', onSelect: () => stationId && void posApi.reprintLast(stationId) },
            { key: 'F9', label: 'Save', onSelect: () => openDialog('payment'), disabled: !hasLines },
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
