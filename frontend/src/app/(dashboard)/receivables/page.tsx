'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, Field, FormSection, NumberField } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { posApi, PosApiError } from '@/lib/pos-api';
import type {
  ArLedgerEntryRow,
  CustomerAccountRow,
  CustomerStatement,
  GiftCard,
  InvoiceRow,
  LoyaltyBalance,
  LoyaltyLedgerEntryRow,
  LoyaltyPolicy,
  ReceivablesAgingRow,
  TenderSettings,
} from '@/types/masters';

const selectClass =
  'pos-input';

/** Customer accounts, invoices, payments, void/refund and aging (guide p.51–58). */
export default function ReceivablesPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canPay = auth.can('ar.payment');
  const canVoid = auth.can('ar.void_invoice');
  const canRefund = auth.can('ar.refund');

  const [search, setSearch] = useState('');
  const [withBalanceOnly, setWithBalanceOnly] = useState(true);
  const [rows, setRows] = useState<CustomerAccountRow[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [view, setView] = useState<'accounts' | 'aging' | 'giftCards' | 'loyalty'>('accounts');
  const [aging, setAging] = useState<ReceivablesAgingRow[]>([]);
  const [tenders, setTenders] = useState<TenderSettings[]>([]);

  const load = useCallback(
    async (append: boolean, from: string | null) => {
      if (!locationId) return;
      setLoading(true);

      try {
        const page = await mastersApi.receivables.browseAccounts(locationId, {
          search,
          withBalanceOnly,
          cursor: from ?? undefined,
          pageSize: 100,
        });

        setRows((current) => (append ? [...current, ...page.items] : page.items));
        setCursor(page.nextCursor);
        setHasMore(page.hasMore);
      } catch (error) {
        toast({ title: 'Could not load accounts', description: describe(error), variant: 'destructive' });
      } finally {
        setLoading(false);
      }
    },
    [locationId, search, withBalanceOnly],
  );

  useEffect(() => {
    const timer = window.setTimeout(() => void load(false, null), 200);
    return () => window.clearTimeout(timer);
  }, [load]);

  useEffect(() => {
    if (!locationId) return;
    void mastersApi.settings.get(locationId).then((snapshot) => setTenders(snapshot.tenders));
  }, [locationId]);

  const loadAging = async () => {
    if (!locationId) return;
    try {
      setAging(await mastersApi.receivables.aging(locationId));
      setView('aging');
    } catch (error) {
      toast({ title: 'Could not load the aging report', description: describe(error), variant: 'destructive' });
    }
  };

  const columns = useMemo<DataGridColumn<CustomerAccountRow>[]>(
    () => [
      { key: 'account', header: 'Account #', width: 100, render: (r) => r.accountNumber, sortValue: (r) => r.accountNumber },
      { key: 'name', header: 'Customer', width: 220, render: (r) => r.customerName, sortValue: (r) => r.customerName },
      { key: 'balance', header: 'Balance due', width: 120, numeric: true, render: (r) => currency(r.balanceDue), sortValue: (r) => r.balanceDue },
      { key: 'limit', header: 'Credit limit', width: 120, numeric: true, render: (r) => (r.creditLimit > 0 ? currency(r.creditLimit) : 'Unlimited') },
      { key: 'open', header: 'Open invoices', width: 110, numeric: true, render: (r) => r.openInvoiceCount },
    ],
    [],
  );

  return (
    <BrowseFormShell
      title="Receivables"
      toolbar={
        <>
          <button type="button" className="pos-button" onClick={() => void loadAging()}>
            Aging report
          </button>
          <button type="button" className="pos-button" onClick={() => setView('giftCards')}>
            Gift cards
          </button>
          <button type="button" className="pos-button" onClick={() => setView('loyalty')}>
            Loyalty
          </button>
        </>
      }
      filters={
        <>
          <input
            className="pos-input w-64"
            placeholder="Customer name"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          <label className="flex items-center gap-1 text-label">
            <input type="checkbox" checked={withBalanceOnly} onChange={(event) => setWithBalanceOnly(event.target.checked)} />
            With a balance only
          </label>
        </>
      }
      grid={
        view === 'aging' ? (
          <AgingReport rows={aging} onClose={() => setView('accounts')} />
        ) : view === 'giftCards' ? (
          <GiftCardsPanel onClose={() => setView('accounts')} />
        ) : view === 'loyalty' ? (
          <LoyaltyPanel locationId={locationId ?? ''} onClose={() => setView('accounts')} />
        ) : (
          <DataGrid
            gridId="receivables-accounts"
            rows={rows}
            columns={columns}
            rowKey={(row) => row.customerId}
            onRowActivate={(row) => setSelectedId(row.customerId)}
            emptyMessage={loading ? 'Loading…' : 'No accounts match these filters.'}
          />
        )
      }
      form={
        selectedId ? (
          <StatementPanel
            key={selectedId}
            customerId={selectedId}
            tenders={tenders}
            canPay={canPay}
            canVoid={canVoid}
            canRefund={canRefund}
            onClose={() => setSelectedId(null)}
            onChanged={() => void load(false, null)}
          />
        ) : null
      }
      status={
        <span className="flex items-center gap-3">
          <span>{rows.length} loaded{hasMore ? ' of more' : ''}</span>
          {hasMore ? (
            <button type="button" className="underline" onClick={() => void load(true, cursor)} disabled={loading}>
              Load more
            </button>
          ) : null}
        </span>
      }
    />
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}

function currency(value: number): string {
  return value.toLocaleString(undefined, { style: 'currency', currency: 'USD' });
}

function AgingReport({ rows, onClose }: { rows: ReceivablesAgingRow[]; onClose: () => void }) {
  return (
    <div className="pos-panel h-full overflow-y-auto">
      <header className="pos-panel-header flex items-center justify-between">
        <span>Receivables aging</span>
        <button type="button" className="underline normal-case" onClick={onClose}>
          Back to accounts
        </button>
      </header>
      <table className="w-full text-body">
        <thead className="text-left text-ink-muted">
          <tr>
            <th className="p-2">Customer</th>
            <th className="p-2 text-right">Current</th>
            <th className="p-2 text-right">1–30</th>
            <th className="p-2 text-right">31–60</th>
            <th className="p-2 text-right">61+</th>
            <th className="p-2 text-right">Total</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.customerId} className="border-t border-subtle">
              <td className="p-2">{r.customerName}</td>
              <td className="p-2 text-right pos-amount">{currency(r.current)}</td>
              <td className="p-2 text-right pos-amount">{currency(r.days30)}</td>
              <td className="p-2 text-right pos-amount">{currency(r.days60)}</td>
              <td className="p-2 text-right pos-amount">{currency(r.days90Plus)}</td>
              <td className="p-2 text-right pos-amount font-medium">{currency(r.total)}</td>
            </tr>
          ))}
          {rows.length === 0 ? (
            <tr>
              <td colSpan={6} className="p-4 text-center text-ink-muted">
                Nothing outstanding.
              </td>
            </tr>
          ) : null}
        </tbody>
      </table>
    </div>
  );
}

function GiftCardsPanel({ onClose }: { onClose: () => void }) {
  const [value, setValue] = useState(25);
  const [serialNumber, setSerialNumber] = useState('');
  const [issued, setIssued] = useState<GiftCard | null>(null);

  const [lookupSerial, setLookupSerial] = useState('');
  const [found, setFound] = useState<GiftCard | null>(null);
  const [busy, setBusy] = useState(false);

  const issue = async () => {
    if (value <= 0) {
      toast({ title: 'Enter a value greater than zero', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      const card = await mastersApi.giftCards.issue({ value, serialNumber: serialNumber || undefined });
      setIssued(card);
      setSerialNumber('');
      toast({ title: `Gift card ${card.serialNumber} issued for ${currency(card.originalValue)}` });
    } catch (error) {
      toast({ title: 'Could not issue the gift card', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const lookup = async () => {
    if (!lookupSerial.trim()) return;

    setBusy(true);
    try {
      setFound(await mastersApi.giftCards.balance(lookupSerial.trim()));
    } catch (error) {
      setFound(null);
      toast({ title: 'Could not find that gift card', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="h-full overflow-y-auto">
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">Gift cards</h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Back to accounts
        </button>
      </div>

      <FormSection
        title="Issue a gift card"
        hint="Leave the serial blank to generate one — used when the card is really just a line on the receipt."
        actions={
          <button type="button" className="underline" disabled={busy} onClick={() => void issue()}>
            Issue
          </button>
        }
      >
        <NumberField label="Value" value={value} onChange={setValue} />
        <Field label="Serial number (optional)">
          <input
            className="w-full pos-input"
            value={serialNumber}
            onChange={(event) => setSerialNumber(event.target.value)}
            placeholder="Printed on a physical card, if any"
          />
        </Field>
        {issued ? (
          <p className="pos-amount text-body">
            Issued: <span className="font-semibold">{issued.serialNumber}</span> — {currency(issued.remainingValue)} remaining
          </p>
        ) : null}
      </FormSection>

      <FormSection
        title="Check a balance"
        actions={
          <button type="button" className="underline" disabled={busy} onClick={() => void lookup()}>
            Look up
          </button>
        }
      >
        <Field label="Serial number">
          <input
            className="w-full pos-input"
            value={lookupSerial}
            onChange={(event) => setLookupSerial(event.target.value)}
          />
        </Field>
        {found ? (
          <p className="pos-amount text-body">
            {currency(found.remainingValue)} of {currency(found.originalValue)} remaining
            {found.isActive ? '' : ' — inactive'}
          </p>
        ) : null}
      </FormSection>
    </div>
  );
}

function LoyaltyPanel({ locationId, onClose }: { locationId: string; onClose: () => void }) {
  const [policy, setPolicy] = useState<LoyaltyPolicy | null>(null);
  const [busy, setBusy] = useState(false);

  const [term, setTerm] = useState('');
  const [results, setResults] = useState<{ id: string; customerNumber: number; fullName: string }[]>([]);
  const [customerId, setCustomerId] = useState<string | null>(null);
  const [balance, setBalance] = useState<LoyaltyBalance | null>(null);
  const [ledger, setLedger] = useState<LoyaltyLedgerEntryRow[]>([]);
  const [delta, setDelta] = useState(0);
  const [reason, setReason] = useState('');

  useEffect(() => {
    if (!locationId) return;
    void mastersApi.loyalty.getPolicy(locationId).then(setPolicy);
  }, [locationId]);

  useEffect(() => {
    if (term.trim().length < 2) {
      setResults([]);
      return;
    }

    const timer = window.setTimeout(() => {
      void posApi.searchCustomers(term, locationId).then(setResults).catch(() => setResults([]));
    }, 200);

    return () => window.clearTimeout(timer);
  }, [term, locationId]);

  const savePolicy = async () => {
    if (!policy) return;

    setBusy(true);
    try {
      setPolicy(await mastersApi.loyalty.savePolicy(policy));
      toast({ title: 'Loyalty policy saved' });
    } catch (error) {
      toast({ title: 'Could not save the policy', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const pickCustomer = async (id: string) => {
    setCustomerId(id);
    setResults([]);
    setTerm('');
    const [b, l] = await Promise.all([mastersApi.loyalty.balance(id), mastersApi.loyalty.ledger(id)]);
    setBalance(b);
    setLedger(l);
  };

  const adjust = async () => {
    if (!customerId || delta === 0 || !reason.trim()) {
      toast({ title: 'Choose a customer, a non-zero amount, and a reason', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      setBalance(await mastersApi.loyalty.adjust(customerId, delta, reason.trim()));
      setLedger(await mastersApi.loyalty.ledger(customerId));
      setDelta(0);
      setReason('');
      toast({ title: 'Points adjusted' });
    } catch (error) {
      toast({ title: 'Could not adjust points', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="h-full overflow-y-auto">
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">Loyalty</h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Back to accounts
        </button>
      </div>

      {policy ? (
        <FormSection
          title="Policy"
          hint="Points per dollar, minimum to redeem, and the reward it converts to (guide p.83–84)."
          actions={
            <button type="button" className="underline" disabled={busy} onClick={() => void savePolicy()}>
              Save
            </button>
          }
        >
          <label className="flex items-center gap-1 text-label">
            <input type="checkbox" checked={policy.isEnabled} onChange={(e) => setPolicy({ ...policy, isEnabled: e.target.checked })} />
            Enabled
          </label>
          <NumberField label="Points per dollar" value={policy.pointsPerDollar} onChange={(v) => setPolicy({ ...policy, pointsPerDollar: v })} />
          <NumberField label="Minimum points to redeem" value={policy.minimumRequired} onChange={(v) => setPolicy({ ...policy, minimumRequired: v })} step="1" />
          <label className="flex items-center gap-1 text-label">
            <input type="checkbox" checked={policy.percentEnabled} onChange={(e) => setPolicy({ ...policy, percentEnabled: e.target.checked })} />
            Reward as a percentage of subtotal
          </label>
          <NumberField label="Reward percent" value={policy.rewardPercent} onChange={(v) => setPolicy({ ...policy, rewardPercent: v })} />
          <label className="flex items-center gap-1 text-label">
            <input type="checkbox" checked={policy.fixedEnabled} onChange={(e) => setPolicy({ ...policy, fixedEnabled: e.target.checked })} />
            Reward as a fixed amount
          </label>
          <NumberField label="Fixed reward amount" value={policy.rewardFixedAmount} onChange={(v) => setPolicy({ ...policy, rewardFixedAmount: v })} />
          <label className="flex items-center gap-1 text-label">
            <input
              type="checkbox"
              checked={policy.suppressIfSubtotalDiscountApplied}
              onChange={(e) => setPolicy({ ...policy, suppressIfSubtotalDiscountApplied: e.target.checked })}
            />
            No reward if a subtotal discount already applied
          </label>
        </FormSection>
      ) : null}

      <FormSection title="Customer balance" hint="Look up a customer to see and adjust their points.">
        <Field label="Customer">
          <input
            className="w-full pos-input"
            value={term}
            onChange={(event) => setTerm(event.target.value)}
            placeholder="Name or customer number"
          />
          {results.length > 0 ? (
            <ul className="mt-1 max-h-40 overflow-y-auto rounded-sm border border-subtle">
              {results.map((r) => (
                <li key={r.id}>
                  <button
                    type="button"
                    className="block w-full px-2 py-1 text-left text-label hover:bg-panel-hover"
                    onClick={() => void pickCustomer(r.id)}
                  >
                    #{r.customerNumber} — {r.fullName}
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </Field>

        {balance ? (
          <>
            <p className="pos-amount text-body font-medium">{balance.customerName}: {balance.rewardPoints} points</p>

            <NumberField label="Adjustment (+/-)" value={delta} onChange={setDelta} step="1" />
            <Field label="Reason">
              <input
                className="w-full pos-input"
                value={reason}
                onChange={(event) => setReason(event.target.value)}
              />
            </Field>
            <button type="button" className="underline text-body" disabled={busy} onClick={() => void adjust()}>
              Apply adjustment
            </button>

            {ledger.length > 0 ? (
              <table className="mt-2 w-full text-label">
                <thead className="text-left text-ink-muted">
                  <tr>
                    <th className="pb-1">When</th>
                    <th className="pb-1">Type</th>
                    <th className="pb-1 text-right">Points</th>
                  </tr>
                </thead>
                <tbody>
                  {ledger.map((entry) => (
                    <tr key={entry.id} className="border-t border-subtle">
                      <td className="py-1">{new Date(entry.occurredAt).toLocaleString()}</td>
                      <td className="py-1">{entry.entryType}</td>
                      <td className="py-1 text-right pos-amount">{entry.points}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : null}
          </>
        ) : null}
      </FormSection>
    </div>
  );
}

function StatementPanel({
  customerId,
  tenders,
  canPay,
  canVoid,
  canRefund,
  onClose,
  onChanged,
}: {
  customerId: string;
  tenders: TenderSettings[];
  canPay: boolean;
  canVoid: boolean;
  canRefund: boolean;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [statement, setStatement] = useState<CustomerStatement | null>(null);
  const [amount, setAmount] = useState(0);
  const [tenderTypeId, setTenderTypeId] = useState('');
  const [busy, setBusy] = useState(false);

  const reload = useCallback(async () => {
    try {
      setStatement(await mastersApi.receivables.statement(customerId));
    } catch (error) {
      toast({ title: 'Could not open the statement', description: describe(error), variant: 'destructive' });
    }
  }, [customerId]);

  useEffect(() => {
    void reload();
  }, [reload]);

  useEffect(() => {
    if (!tenderTypeId && tenders.length > 0) setTenderTypeId(tenders[0]!.id);
  }, [tenders, tenderTypeId]);

  if (!statement) {
    return <p className="text-body text-ink-muted">Loading…</p>;
  }

  const takePayment = async () => {
    if (amount <= 0 || !tenderTypeId) {
      toast({ title: 'Enter an amount and choose a tender', variant: 'destructive' });
      return;
    }

    setBusy(true);
    try {
      const result = await mastersApi.receivables.takePayment({ customerId, amount, tenderTypeId });
      toast({
        title: 'Payment applied',
        description:
          result.amountUnapplied > 0
            ? `${currency(result.amountUnapplied)} left over — no open balance to apply it to.`
            : undefined,
      });
      setAmount(0);
      await reload();
      onChanged();
    } catch (error) {
      toast({ title: 'Could not take the payment', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const voidInvoice = async (invoiceId: string) => {
    setBusy(true);
    try {
      await mastersApi.receivables.voidInvoice(invoiceId);
      toast({ title: 'Invoice voided' });
      await reload();
      onChanged();
    } catch (error) {
      toast({ title: 'Could not void the invoice', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const refundInvoice = async (invoiceId: string, invoiceTotal: number) => {
    const value = window.prompt(`Refund how much of this invoice (up to ${currency(invoiceTotal)})?`);
    const parsed = value ? Number.parseFloat(value) : NaN;
    if (!parsed || parsed <= 0) return;

    setBusy(true);
    try {
      await mastersApi.receivables.refundInvoice(invoiceId, parsed);
      toast({ title: 'Refund recorded' });
      await reload();
      onChanged();
    } catch (error) {
      toast({ title: 'Could not refund the invoice', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">
          #{statement.accountNumber} — {statement.customerName}
        </h2>
        <div className="flex gap-2">
          {/* The window envelope for posting this statement out (guide App. L). */}
          <a
            className="pos-button"
            href={mastersApi.documents.statementEnvelopeUrl(customerId)}
            target="_blank"
            rel="noopener noreferrer"
          >
            Print envelope
          </a>
          <button type="button" className="pos-button" onClick={onClose}>
            Close
          </button>
        </div>
      </div>

      <FormSection title="Account" hint={`Credit limit: ${statement.creditLimit > 0 ? currency(statement.creditLimit) : 'unlimited'}`}>
        <p className="text-h3 font-semibold pos-amount">{currency(statement.balanceDue)} due</p>
      </FormSection>

      {canPay ? (
        <FormSection
          title="Take a payment"
          hint="Applied oldest invoice first, penalty before principal on each one."
          actions={
            <button type="button" className="underline" disabled={busy} onClick={() => void takePayment()}>
              Apply
            </button>
          }
        >
          <NumberField label="Amount" value={amount} onChange={setAmount} />
          <Field label="Tender">
            <select className={selectClass + ' w-full'} value={tenderTypeId} onChange={(event) => setTenderTypeId(event.target.value)}>
              {tenders.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.displayName}
                </option>
              ))}
            </select>
          </Field>
        </FormSection>
      ) : null}

      <FormSection title="Invoices">
        <table className="w-full text-label">
          <thead className="text-left text-ink-muted">
            <tr>
              <th className="pb-1">Invoice</th>
              <th className="pb-1">Due</th>
              <th className="pb-1 text-right">Total</th>
              <th className="pb-1 text-right">Penalty</th>
              <th className="pb-1 text-right">Balance</th>
              <th className="pb-1">Status</th>
              {canVoid || canRefund ? <th /> : null}
            </tr>
          </thead>
          <tbody>
            {statement.invoices.map((invoice) => (
              <InvoiceRowView
                key={invoice.id}
                invoice={invoice}
                canVoid={canVoid}
                canRefund={canRefund}
                busy={busy}
                onVoid={() => void voidInvoice(invoice.id)}
                onRefund={() => void refundInvoice(invoice.id, invoice.invoiceTotal)}
              />
            ))}
          </tbody>
        </table>
      </FormSection>

      <FormSection title="Ledger">
        <LedgerTable entries={statement.ledger} />
      </FormSection>
    </div>
  );
}

function InvoiceRowView({
  invoice,
  canVoid,
  canRefund,
  busy,
  onVoid,
  onRefund,
}: {
  invoice: InvoiceRow;
  canVoid: boolean;
  canRefund: boolean;
  busy: boolean;
  onVoid: () => void;
  onRefund: () => void;
}) {
  return (
    <tr className="border-t border-subtle">
      <td className="py-1">#{invoice.invoiceNumber}</td>
      <td className="py-1">{invoice.dueOn}</td>
      <td className="py-1 text-right pos-amount">{currency(invoice.invoiceTotal)}</td>
      <td className="py-1 text-right pos-amount">{currency(invoice.penaltyAccrued)}</td>
      <td className="py-1 text-right pos-amount">{currency(invoice.balanceDue)}</td>
      <td className="py-1">{invoice.status}</td>
      {canVoid || canRefund ? (
        <td className="py-1 text-right whitespace-nowrap">
          {canVoid && invoice.status !== 'Void' ? (
            <button type="button" className="underline mr-2" disabled={busy} onClick={onVoid}>
              Void
            </button>
          ) : null}
          {canRefund && invoice.status !== 'Void' ? (
            <button type="button" className="underline" disabled={busy} onClick={onRefund}>
              Refund
            </button>
          ) : null}
        </td>
      ) : null}
    </tr>
  );
}

function LedgerTable({ entries }: { entries: ArLedgerEntryRow[] }) {
  if (entries.length === 0) {
    return <p className="text-label text-ink-muted">No activity yet.</p>;
  }

  return (
    <table className="w-full text-label">
      <thead className="text-left text-ink-muted">
        <tr>
          <th className="pb-1">When</th>
          <th className="pb-1">Type</th>
          <th className="pb-1 text-right">Amount</th>
        </tr>
      </thead>
      <tbody>
        {entries.map((entry) => (
          <tr key={entry.id} className="border-t border-subtle">
            <td className="py-1">{new Date(entry.occurredAt).toLocaleString()}</td>
            <td className="py-1">{entry.entryType}</td>
            <td className="py-1 text-right pos-amount">{currency(entry.amount)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
