'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Ban, HandCoins, Plus, Save, Send, Trash2, UserCog, X } from 'lucide-react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useId, useState } from 'react';
import { AccountSelect, amountOf, Attachments, LINK, MoneyInput, ReasonDialog, StatusChip, StoreSelect, today, useAccounts, useMoney, useSuppliers } from '@/components/finance';
import { ConflictDialog } from '@/components/masters';
import { Button, IconButton, LinkButton } from '@/components/ui/button';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Detail, PageHeader, Toolbar } from '@/components/ui/page';
import { ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { billLine, billTotals, dueDate, label } from '@/lib/finance';
import { formatDate } from '@/lib/format';
import { numberText } from '@/lib/forms';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { BillDetail, BillKind, BillRequest, SupplierFinance, WithholdingKind } from '@/lib/types';

export default function BillEditPage() {
  return (
    <Suspense>
      <BillEdit />
    </Suspense>
  );
}

function BillEdit() {
  const params = useSearchParams();
  const id = params.get('id');
  const detail = useQuery({ queryKey: ['bill', id], queryFn: () => api.bill(id!), enabled: !!id, staleTime: 0 });
  if (id && detail.error) {
    return (
      <div className="card">
        <ErrorState error={detail.error} onRetry={() => void detail.refetch()} />
      </div>
    );
  }
  if (id && !detail.data) return <Skeleton className="h-64" />;
  return <Editor key={detail.data?.version ?? 'new'} bill={detail.data ?? null} kind={(params.get('kind') as BillKind) || 'Bill'} />;
}

interface Line {
  key: number;
  type: 'account' | 'item';
  accountId: string | null;
  itemCode: string;
  itemId: string | null;
  itemName: string | null;
  description: string;
  quantity: string;
  unitPrice: string;
  taxRate: string;
}

let nextKey = 1;
const blank = (): Line => ({ key: nextKey++, type: 'account', accountId: null, itemCode: '', itemId: null, itemName: null, description: '', quantity: '1', unitPrice: '', taxRate: '0' });

/** A supplier's bill or debit note: lines for expenses or stock, tax, totals; posted to the ledger, paid, voided. */
function Editor({ bill, kind: newKind }: { bill: BillDetail | null; kind: BillKind }) {
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can, canChangeShared } = useSession();
  const { defaultStoreId } = useStore();
  const money = useMoney();
  const accounts = useAccounts();
  const suppliers = useSuppliers();
  const rules = useQuery({ queryKey: ['tax-rules'], queryFn: () => api.taxRules(), staleTime: 5 * 60_000 });
  const ids = { supplier: useId(), store: useId(), date: useId(), due: useId(), ref: useId(), currency: useId(), rate: useId(), notes: useId() };
  const kind = bill?.kind ?? newKind;
  const editable = (!bill || bill.status === 'Draft') && can(P.financePayables);
  const [supplierId, setSupplierId] = useState<string | null>(bill?.supplierId ?? null);
  const [storeId, setStoreId] = useState<string | null>(bill?.storeId ?? defaultStoreId);
  const [billDate, setBillDate] = useState(bill?.billDate ?? today());
  const [due, setDue] = useState(bill?.dueDate ?? '');
  const [reference, setReference] = useState(bill?.supplierReference ?? '');
  const [currency, setCurrency] = useState(bill?.currency ?? '');
  const [rate, setRate] = useState(bill ? numberText(bill.exchangeRate) : '1');
  const [notes, setNotes] = useState(bill?.notes ?? '');
  const [lines, setLines] = useState<Line[]>(() =>
    bill
      ? bill.lines.map((l) => ({
          key: nextKey++,
          type: l.itemId ? 'item' : 'account',
          accountId: l.accountId,
          itemCode: '',
          itemId: l.itemId,
          itemName: l.itemId ? l.description : null,
          description: l.description,
          quantity: numberText(l.quantity),
          unitPrice: numberText(l.unitPrice),
          taxRate: numberText(l.taxRate),
        }))
      : [blank()],
  );
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [taxDetails, setTaxDetails] = useState(false);
  const profile = useQuery({ queryKey: ['supplier-finance', supplierId], queryFn: () => api.supplierFinance(supplierId!), enabled: !!supplierId });
  const defaultRate = rules.data?.[0]?.salesTaxRates.find((r) => r.isDefault)?.rate;

  useEffect(() => {
    if (!storeId && defaultStoreId) setStoreId(defaultStoreId);
  }, [storeId, defaultStoreId]);

  const parsed = lines.map((l) => ({ quantity: amountOf(l.quantity), unitPrice: amountOf(l.unitPrice), taxRate: amountOf(l.taxRate) }));
  const totals = billTotals(parsed);
  const shownDue = due || (profile.data ? dueDate(billDate, profile.data.paymentTermsDays) : '');

  const done = (saved: BillDetail, message: string) => {
    toast.ok(message);
    void queryClient.invalidateQueries({ queryKey: ['bills'] });
    void queryClient.invalidateQueries({ queryKey: ['bills-due'] });
    queryClient.setQueryData(['bill', saved.id], saved);
    router.replace(`/finance/bills/edit/?id=${encodeURIComponent(saved.id)}`);
  };
  const save = useMutation({
    mutationFn: async ({ body, post }: { body: BillRequest; post: boolean }) => {
      const saved = bill ? await api.saveBill(bill.id, body) : await api.addBill(body);
      return post ? api.postBill(saved.id) : saved;
    },
    onSuccess: (saved) => done(saved, saved.status === 'Draft' ? 'The bill is saved as a draft.' : `${label(saved.kind)} ${saved.number} is posted.`),
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });
  const post = useMutation({ mutationFn: () => api.postBill(bill!.id), onSuccess: (saved) => done(saved, `${label(saved.kind)} ${saved.number} is posted.`) });
  const voidBill = useMutation({
    mutationFn: ({ on, reason }: { on: string; reason: string }) => api.voidBill(bill!.id, on, reason),
    onSuccess: (saved) => {
      setVoiding(false);
      done(saved, `${label(saved.kind)} ${saved.number} is void.`);
    },
  });
  const remove = useMutation({
    mutationFn: () => api.deleteBill(bill!.id),
    onSuccess: () => {
      toast.ok('The draft is deleted.');
      void queryClient.invalidateQueries({ queryKey: ['bills'] });
      router.push('/finance/bills/');
    },
  });

  function submit(andPost: boolean) {
    if (!supplierId) return setProblem('Choose the supplier.');
    if (!storeId) return setProblem('Choose the store the bill is for.');
    if (lines.some((l) => (l.type === 'account' ? !l.accountId : !l.itemId))) return setProblem('Each line needs an account, or an item found by its code.');
    if (totals.total <= 0) return setProblem('The bill’s total must be more than zero.');
    setProblem(null);
    save.mutate({
      post: andPost,
      body: {
        kind,
        supplierId,
        storeId,
        billDate,
        dueDate: due || null,
        supplierReference: reference.trim() || null,
        currency: currency.trim().toUpperCase() || null,
        exchangeRate: amountOf(rate) || 1,
        notes: notes.trim() || null,
        originalBillId: bill?.originalBillId ?? null,
        lines: lines.map((l) => ({
          accountId: l.type === 'account' ? l.accountId : null,
          itemId: l.type === 'item' ? l.itemId : null,
          description: l.description.trim() || (l.itemName ?? ''),
          quantity: amountOf(l.quantity),
          unitPrice: amountOf(l.unitPrice),
          taxRate: amountOf(l.taxRate),
        })),
        version: bill?.version ?? null,
      },
    });
  }

  const setLine = (key: number, change: Partial<Line>) => setLines((all) => all.map((l) => (l.key === key ? { ...l, ...change } : l)));
  async function findItem(line: Line) {
    const code = line.itemCode.trim();
    if (!code) return;
    try {
      const found = await api.items({ search: code, storeId, pageSize: 10 });
      const item = found.items.find((i) => i.plu.toUpperCase() === code.toUpperCase()) ?? (found.items.length === 1 ? found.items[0] : undefined);
      setLine(line.key, item ? { itemId: item.id, itemName: `${item.plu} ${item.name}`, description: line.description || item.name } : { itemId: null, itemName: null });
      if (!item) toast.error(`No item has the code ${code}.`);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : 'The item was not found.');
    }
  }

  const title = bill ? `${label(bill.kind)} ${bill.number || '(draft)'}` : kind === 'DebitNote' ? 'New debit note' : 'New bill';
  return (
    <div>
      <Toolbar>
        <LinkButton href="/finance/bills/" icon={ArrowLeft}>
          Bills
        </LinkButton>
      </Toolbar>
      <PageHeader title={title} aside={bill && <StatusChip status={bill.status} />} />
      <form
        className="card space-y-4"
        onSubmit={(e) => {
          e.preventDefault();
          submit(false);
        }}
      >
        <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
          <Field label="Supplier" htmlFor={ids.supplier} className="md:col-span-2">
            <div className="flex gap-2">
              <Select id={ids.supplier} value={supplierId ?? ''} disabled={!editable} onChange={(e) => setSupplierId(e.target.value || null)}>
                <option value="">Choose the supplier</option>
                {(suppliers.data?.items ?? []).map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.company}
                  </option>
                ))}
              </Select>
              {supplierId && <IconButton icon={UserCog} label="Supplier’s tax details" onClick={() => setTaxDetails(true)} />}
            </div>
          </Field>
          <Field label="Store" htmlFor={ids.store}>
            <StoreSelect id={ids.store} value={storeId} onChange={setStoreId} disabled={!editable} />
          </Field>
          <Field label="Their number" htmlFor={ids.ref}>
            <Input id={ids.ref} value={reference} maxLength={40} readOnly={!editable} onChange={(e) => setReference(e.target.value)} />
          </Field>
          <Field label="Bill date" htmlFor={ids.date}>
            <Input id={ids.date} type="date" value={billDate} readOnly={!editable} onChange={(e) => setBillDate(e.target.value)} />
          </Field>
          <Field label="Due" htmlFor={ids.due} hint={!due && profile.data ? `${profile.data.paymentTermsDays} days’ terms` : undefined}>
            <Input id={ids.due} type="date" value={shownDue} readOnly={!editable} onChange={(e) => setDue(e.target.value)} />
          </Field>
          <Field label="Currency" htmlFor={ids.currency} hint="Empty for the books’ own">
            <Input id={ids.currency} value={currency} maxLength={3} readOnly={!editable} onChange={(e) => setCurrency(e.target.value.toUpperCase())} />
          </Field>
          {currency.trim() && (
            <Field label="Exchange rate" htmlFor={ids.rate} hint="The books’ currency for one unit">
              <MoneyInput id={ids.rate} value={rate} disabled={!editable} onChange={setRate} />
            </Field>
          )}
        </div>

        <div className="grid-card">
          <div className="grid-scroll">
            <table className="grid-table" aria-label="Lines">
              <thead>
                <tr>
                  <th scope="col">For</th>
                  <th scope="col">Description</th>
                  <th scope="col" className="num">
                    Quantity
                  </th>
                  <th scope="col" className="num">
                    Price
                  </th>
                  <th scope="col" className="num">
                    Tax %
                  </th>
                  <th scope="col" className="num">
                    Amount
                  </th>
                  {editable && <th scope="col" aria-label="Remove" />}
                </tr>
              </thead>
              <tbody>
                {lines.map((l, i) => {
                  const t = billLine(parsed[i]);
                  return (
                    <tr key={l.key}>
                      <td className="min-w-[260px] space-y-1.5">
                        {editable && (
                          <Select aria-label={`Kind of line ${i + 1}`} value={l.type} onChange={(e) => setLine(l.key, { type: e.target.value as Line['type'] })}>
                            <option value="account">An expense account</option>
                            <option value="item">A stock item</option>
                          </Select>
                        )}
                        {l.type === 'account' ? (
                          <AccountSelect
                            ariaLabel={`Account of line ${i + 1}`}
                            value={l.accountId}
                            accounts={accounts.data}
                            disabled={!editable}
                            onChange={(accountId) => setLine(l.key, { accountId })}
                          />
                        ) : editable ? (
                          <Input
                            aria-label={`Item code of line ${i + 1}`}
                            placeholder={l.itemName ?? 'Item code, then Tab'}
                            value={l.itemCode}
                            onChange={(e) => setLine(l.key, { itemCode: e.target.value })}
                            onBlur={() => void findItem(l)}
                          />
                        ) : (
                          l.itemName ?? 'Stock item'
                        )}
                        {l.type === 'item' && l.itemName && editable && <p className="text-note text-muted">{l.itemName}</p>}
                      </td>
                      <td className="min-w-[180px]">
                        <Input aria-label={`Description of line ${i + 1}`} value={l.description} readOnly={!editable} maxLength={200} onChange={(e) => setLine(l.key, { description: e.target.value })} />
                      </td>
                      <td className="num w-[110px]">
                        <MoneyInput ariaLabel={`Quantity of line ${i + 1}`} value={l.quantity} disabled={!editable} onChange={(quantity) => setLine(l.key, { quantity })} />
                      </td>
                      <td className="num w-[140px]">
                        <MoneyInput ariaLabel={`Price of line ${i + 1}`} value={l.unitPrice} disabled={!editable} onChange={(unitPrice) => setLine(l.key, { unitPrice })} />
                      </td>
                      <td className="num w-[100px]">
                        <MoneyInput ariaLabel={`Tax rate of line ${i + 1}`} value={l.taxRate} disabled={!editable} onChange={(taxRate) => setLine(l.key, { taxRate })} />
                      </td>
                      <td className="num">{money(t.total)}</td>
                      {editable && (
                        <td className="w-[60px]">
                          <IconButton icon={X} label={`Remove line ${i + 1}`} disabled={lines.length <= 1} onClick={() => setLines((all) => all.filter((x) => x.key !== l.key))} />
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
              <tfoot>
                <tr>
                  <td colSpan={5}>
                    Before tax {money(totals.subtotal)} · tax {money(totals.tax)}
                  </td>
                  <td className="num" data-testid="bill-total">
                    {money(totals.total)}
                  </td>
                  {editable && <td />}
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
        {editable && (
          <div className="flex flex-wrap gap-2.5">
            <Button icon={Plus} onClick={() => setLines((all) => [...all, { ...blank(), taxRate: defaultRate !== undefined ? numberText(defaultRate) : '0' }])}>
              Add a line
            </Button>
          </div>
        )}
        <Field label="Notes" htmlFor={ids.notes}>
          <TextArea id={ids.notes} value={notes} readOnly={!editable} maxLength={400} onChange={(e) => setNotes(e.target.value)} />
        </Field>

        {bill && bill.status !== 'Draft' && (
          <dl className="grid grid-cols-2 gap-3 md:grid-cols-4">
            <Detail label="Paid">{money(bill.paid)}</Detail>
            <Detail label="Open">{money(bill.open)}</Detail>
            <Detail label="Posted by">{bill.postedBy ?? ''}</Detail>
            <Detail label="Ledger entry">
              {bill.journalEntryId && (
                <Link className={LINK} href={`/finance/journals/edit/?id=${encodeURIComponent(bill.journalEntryId)}`}>
                  Open the entry
                </Link>
              )}
            </Detail>
          </dl>
        )}
        {bill?.voidReason && <p className="text-danger">Void: {bill.voidReason}</p>}
        {(problem || save.error || post.error) && (problem ? <p className="text-danger">{problem}</p> : <FormError error={save.error ?? post.error} />)}

        <div className="flex flex-wrap gap-2.5">
          {editable && (
            <>
              <Button type="submit" icon={Save} shortcut="Ctrl+S" busy={save.isPending}>
                Save as draft
              </Button>
              <Button variant="sun" icon={Send} onClick={() => submit(true)} busy={save.isPending}>
                Save and post
              </Button>
            </>
          )}
          {bill?.status === 'Draft' && can(P.financePayables) && (
            <Button variant="danger" icon={Trash2} onClick={() => setDeleting(true)}>
              Delete draft
            </Button>
          )}
          {bill && (bill.status === 'Posted' || bill.status === 'PartPaid') && can(P.financePayables) && (
            <LinkButton href={`/finance/payments/?run=1&bills=${encodeURIComponent(bill.id)}`} variant="sun" icon={HandCoins}>
              Pay
            </LinkButton>
          )}
          {bill?.status === 'Posted' && bill.paid === 0 && can(P.financePayables) && (
            <Button variant="danger" icon={Ban} onClick={() => setVoiding(true)}>
              Void
            </Button>
          )}
        </div>
      </form>

      {bill && bill.payments.length > 0 && (
        <div className="card mt-4">
          <h3 className="section-title mb-2">Payments</h3>
          <ul className="space-y-1">
            {bill.payments.map((p) => (
              <li key={p.paymentId}>
                Payment {p.number} on {formatDate(p.date)}: {money(p.amount)} {p.status === 'Void' && <StatusChip status="Void" />}
              </li>
            ))}
          </ul>
        </div>
      )}
      {bill && (
        <div className="card mt-4">
          <Attachments owner="bills" ownerId={bill.id} files={bill.attachments} canChange={can(P.financePayables)} queryKey={['bill', bill.id]} />
        </div>
      )}

      {voiding && (
        <ReasonDialog
          title={`Void ${label(bill!.kind).toLowerCase()} ${bill!.number}?`}
          message="Its ledger entry is reversed on the date you give."
          confirmLabel="Void"
          busy={voidBill.isPending}
          error={voidBill.error}
          onConfirm={(on, reason) => voidBill.mutate({ on, reason })}
          onClose={() => setVoiding(false)}
        />
      )}
      {taxDetails && supplierId && <SupplierTaxDialog supplierId={supplierId} readOnly={!(can(P.financePayables) && canChangeShared)} onClose={() => setTaxDetails(false)} />}
      <ConfirmDialog open={deleting} title="Delete this draft?" confirmLabel="Delete" danger busy={remove.isPending} onConfirm={() => remove.mutate()} onClose={() => setDeleting(false)} />
      <ConflictDialog
        open={conflict}
        what="bill"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['bill', bill?.id] });
        }}
        onClose={() => setConflict(false)}
      />
    </div>
  );
}

/** A supplier's tax number, filer status, withholding kind and payment terms (shared by every store). */
function SupplierTaxDialog({ supplierId, readOnly, onClose }: { supplierId: string; readOnly: boolean; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const accounts = useAccounts();
  const current = useQuery({ queryKey: ['supplier-finance', supplierId], queryFn: () => api.supplierFinance(supplierId) });
  const [form, setForm] = useState<SupplierFinance | null>(null);
  const ids = { ntn: useId(), strn: useId(), kind: useId(), terms: useId(), account: useId() };
  useEffect(() => {
    if (current.data && !form) setForm(current.data);
  }, [current.data, form]);
  const save = useMutation({
    mutationFn: (body: SupplierFinance) => api.saveSupplierFinance(supplierId, body),
    onSuccess: (saved) => {
      toast.ok('The supplier’s tax details are saved.');
      queryClient.setQueryData(['supplier-finance', supplierId], saved);
      onClose();
    },
  });
  const set = <K extends keyof SupplierFinance>(key: K, value: SupplierFinance[K]) => setForm((f) => (f ? { ...f, [key]: value } : f));
  return (
    <Dialog
      open
      title="Supplier’s tax details"
      onClose={onClose}
      onSubmit={() => (readOnly || !form ? onClose() : save.mutate(form))}
      footer={
        <>
          {!readOnly && (
            <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
              Save
            </Button>
          )}
          <Button onClick={onClose} shortcut="Esc">
            {readOnly ? 'Close' : 'Cancel'}
          </Button>
        </>
      }
    >
      {!form ? (
        <Skeleton className="h-40" />
      ) : (
        <div className="space-y-3">
          <Field label="Tax number" htmlFor={ids.ntn}>
            <Input id={ids.ntn} value={form.taxNumber ?? ''} readOnly={readOnly} maxLength={20} onChange={(e) => set('taxNumber', e.target.value || null)} />
          </Field>
          <Field label="Sales tax number" htmlFor={ids.strn}>
            <Input id={ids.strn} value={form.salesTaxNumber ?? ''} readOnly={readOnly} maxLength={20} onChange={(e) => set('salesTaxNumber', e.target.value || null)} />
          </Field>
          <Checkbox label="On the active taxpayers list (filer)" checked={form.isFiler} disabled={readOnly} onChange={(v) => set('isFiler', v)} />
          <Checkbox label="Exempt from withholding" checked={form.withholdingExempt} disabled={readOnly} onChange={(v) => set('withholdingExempt', v)} />
          <Field label="Withholding as payments for" htmlFor={ids.kind}>
            <Select id={ids.kind} value={form.withholdingKind} disabled={readOnly} onChange={(e) => set('withholdingKind', e.target.value as WithholdingKind)}>
              {(['Goods', 'Services', 'Rent', 'Other'] as const).map((k) => (
                <option key={k} value={k}>
                  {k}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="Payment terms (days)" htmlFor={ids.terms}>
            <Input id={ids.terms} inputMode="numeric" value={String(form.paymentTermsDays)} readOnly={readOnly} onChange={(e) => set('paymentTermsDays', Number(e.target.value) || 0)} />
          </Field>
          <Field label="Usual account" htmlFor={ids.account}>
            <AccountSelect id={ids.account} value={form.defaultAccountId} accounts={accounts.data} allowEmpty emptyLabel="None" disabled={readOnly} onChange={(v) => set('defaultAccountId', v)} />
          </Field>
          {save.error ? <FormError error={save.error} /> : null}
        </div>
      )}
    </Dialog>
  );
}
