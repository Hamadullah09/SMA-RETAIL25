'use client';

/**
 * What the finance pages share: the chart and the bank accounts (fetched once), pickers for accounts, stores and
 * suppliers, money boxes, status chips, the reason-and-date dialog of voids and reversals, and a record's attachments.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Paperclip, Trash2, Upload } from 'lucide-react';
import { useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { Button, IconButton } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { DateInput, Field, Input, Select } from '@/components/ui/field';
import { Chip } from '@/components/ui/page';
import { FormError } from '@/components/ui/states';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { cx } from '@/lib/cx';
import { toBusinessDate } from '@/lib/dates';
import { fileSize, label, postingAccounts, statusTone } from '@/lib/finance';
import { formatMoney, formatUtc } from '@/lib/format';
import { parseNumber } from '@/lib/forms';
import { useStore } from '@/lib/store';
import type { AccountType, AttachmentInfo, BankAccountRow, FinanceAccount } from '@/lib/types';

/** A link in running text, as the dashboard writes them. */
export const LINK = 'font-semibold text-ink underline decoration-sun-dark decoration-2 underline-offset-4';

/** The chart of accounts (for the store chosen in the header: the balances are its). */
export function useAccounts() {
  const { storeId } = useStore();
  return useQuery({ queryKey: ['finance-accounts', storeId], queryFn: () => api.financeAccounts({ storeId }), staleTime: 60_000 });
}

/** The bank and cash accounts the user may use. */
export function useBankAccounts() {
  const { storeId } = useStore();
  return useQuery({ queryKey: ['bank-accounts', storeId], queryFn: () => api.bankAccounts({ storeId }), staleTime: 30_000 });
}

/** The suppliers, for pickers (the first 500). */
export function useSuppliers() {
  return useQuery({ queryKey: ['suppliers', 'all'], queryFn: () => api.suppliers({ pageSize: 500 }), staleTime: 60_000 });
}

/** Today in the browser, as a business date. */
export function today(): string {
  return toBusinessDate(new Date());
}

/** A select of the accounts that are posted to, grouped by type. */
export function AccountSelect({
  id,
  value,
  onChange,
  accounts,
  types,
  allowEmpty,
  emptyLabel = 'Choose an account',
  disabled,
  invalid,
  ariaLabel,
}: {
  id?: string;
  value: string | null;
  onChange(accountId: string | null): void;
  accounts: readonly FinanceAccount[] | undefined;
  types?: readonly AccountType[];
  allowEmpty?: boolean;
  emptyLabel?: string;
  disabled?: boolean;
  invalid?: boolean;
  ariaLabel?: string;
}) {
  const choices = useMemo(() => postingAccounts(accounts ?? [], types), [accounts, types]);
  const groups = useMemo(() => {
    const out = new Map<AccountType, FinanceAccount[]>();
    for (const a of choices) out.set(a.type, [...(out.get(a.type) ?? []), a]);
    return [...out.entries()];
  }, [choices]);
  return (
    <Select
      id={id}
      aria-label={ariaLabel}
      value={value ?? ''}
      disabled={disabled}
      aria-invalid={invalid || undefined}
      onChange={(e) => onChange(e.target.value || null)}
    >
      {(allowEmpty || !value) && <option value="">{emptyLabel}</option>}
      {groups.map(([type, list]) => (
        <optgroup key={type} label={type}>
          {list.map((a) => (
            <option key={a.id} value={a.id}>
              {a.code} {a.name}
            </option>
          ))}
        </optgroup>
      ))}
    </Select>
  );
}

/** A select of the stores the user sees (with "The company" for those who see every store, when allowed). */
export function StoreSelect({
  id,
  value,
  onChange,
  allowCompany,
  disabled,
}: {
  id?: string;
  value: string | null;
  onChange(storeId: string | null): void;
  allowCompany?: boolean;
  disabled?: boolean;
}) {
  const { stores, canSwitch } = useStore();
  return (
    <Select id={id} value={value ?? ''} disabled={disabled} onChange={(e) => onChange(e.target.value || null)}>
      {allowCompany && canSwitch && <option value="">The company</option>}
      {!allowCompany && !value && <option value="">Choose a store</option>}
      {stores.map((s) => (
        <option key={s.id} value={s.id}>
          {s.code} {s.name}
        </option>
      ))}
    </Select>
  );
}

/** A select of the bank and cash accounts. */
export function BankSelect({
  id,
  value,
  onChange,
  accounts,
  allowEmpty,
  emptyLabel = 'Choose an account',
}: {
  id?: string;
  value: string | null;
  onChange(accountId: string | null): void;
  accounts: readonly BankAccountRow[] | undefined;
  allowEmpty?: boolean;
  emptyLabel?: string;
}) {
  return (
    <Select id={id} value={value ?? ''} onChange={(e) => onChange(e.target.value || null)}>
      {(allowEmpty || !value) && <option value="">{emptyLabel}</option>}
      {(accounts ?? [])
        .filter((a) => a.isActive)
        .map((a) => (
          <option key={a.id} value={a.id}>
            {a.name} ({a.currency})
          </option>
        ))}
    </Select>
  );
}

/** A box for an amount: typed as text, read with the app's number rules. */
export function MoneyInput({
  id,
  value,
  onChange,
  invalid,
  ariaLabel,
  placeholder,
  disabled,
}: {
  id?: string;
  value: string;
  onChange(text: string): void;
  invalid?: boolean;
  ariaLabel?: string;
  placeholder?: string;
  disabled?: boolean;
}) {
  return (
    <Input
      id={id}
      inputMode="decimal"
      className="text-right tabular-nums"
      aria-label={ariaLabel}
      value={value}
      placeholder={placeholder}
      invalid={invalid}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
    />
  );
}

/** The number a money box holds (0 when empty or not a number). */
export function amountOf(text: string): number {
  return parseNumber(text) ?? 0;
}

/** A status as a chip in its tone. */
export function StatusChip({ status }: { status: string }) {
  return <Chip tone={statusTone(status)}>{label(status)}</Chip>;
}

/** Money in the chosen store's currency. */
export function useMoney() {
  const { currencySymbol } = useStore();
  return (value: number | null | undefined) => formatMoney(value ?? 0, currencySymbol);
}

/**
 * The dialog of a void or a reversal: the date it takes effect (an open period) and the reason, both needed.
 */
export function ReasonDialog({
  title,
  message,
  confirmLabel,
  busy,
  error,
  onConfirm,
  onClose,
}: {
  title: string;
  message?: ReactNode;
  confirmLabel: string;
  busy?: boolean;
  error?: unknown;
  onConfirm(date: string, reason: string): void;
  onClose(): void;
}) {
  const [date, setDate] = useState(today());
  const [reason, setReason] = useState('');
  const [missing, setMissing] = useState(false);
  const reasonId = useId();
  return (
    <Dialog
      open
      title={title}
      onClose={onClose}
      onSubmit={() => {
        if (!reason.trim()) return setMissing(true);
        onConfirm(date, reason.trim());
      }}
      footer={
        <>
          <Button type="submit" variant="danger" shortcut="Enter" busy={busy}>
            {confirmLabel}
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        {message && <p className="text-ink">{message}</p>}
        <DateInput label="Date" value={date} onChange={setDate} />
        <Field label="Reason" htmlFor={reasonId} error={missing ? 'Say why.' : null}>
          <Input id={reasonId} value={reason} invalid={missing} maxLength={200} onChange={(e) => setReason(e.target.value)} />
        </Field>
        {error ? <FormError error={error} /> : null}
      </div>
    </Dialog>
  );
}

/** A record's receipts and documents: open, delete, and add a picture or PDF. */
export function Attachments({
  owner,
  ownerId,
  files,
  canChange,
  queryKey,
}: {
  owner: 'journals' | 'expenses' | 'bills';
  ownerId: string;
  files: readonly AttachmentInfo[];
  canChange: boolean;
  /** Refetched after a change. */
  queryKey: readonly unknown[];
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const add = useMutation({
    mutationFn: (file: File) => api.attach(owner, ownerId, file, setProgress),
    onSuccess: (saved) => {
      toast.ok(`${saved.fileName} is attached.`);
      void queryClient.invalidateQueries({ queryKey });
    },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : 'The file was not attached.'),
    onSettled: () => setProgress(null),
  });
  const remove = useMutation({
    mutationFn: (fileId: string) => api.deleteAttachment(fileId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey }),
    onError: (e) => toast.error(e instanceof ApiError ? e.message : 'The file was not removed.'),
  });
  return (
    <section aria-label="Attachments" className="space-y-2">
      <h3 className="section-title">Receipts and documents</h3>
      {files.length === 0 ? (
        <p className="text-muted">None yet.</p>
      ) : (
        <ul className="space-y-1.5">
          {files.map((f) => (
            <li key={f.id} className="flex items-center gap-2">
              <Paperclip className="h-4 w-4 text-muted" aria-hidden="true" />
              <a className={cx(LINK, 'truncate')} href={api.attachmentUrl(f.id)} target="_blank" rel="noreferrer">
                {f.fileName}
              </a>
              <span className="text-note text-muted">
                {fileSize(f.size)} · {formatUtc(f.createdAtUtc)}
              </span>
              {canChange && <IconButton icon={Trash2} label={`Remove ${f.fileName}`} onClick={() => remove.mutate(f.id)} />}
            </li>
          ))}
        </ul>
      )}
      {canChange && (
        <>
          <input
            ref={input}
            type="file"
            accept=".jpg,.jpeg,.png,.gif,.webp,.pdf"
            className="sr-only"
            aria-label="Receipt file"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) add.mutate(file);
              e.target.value = '';
            }}
          />
          <Button icon={Upload} onClick={() => input.current?.click()} busy={add.isPending}>
            {progress !== null && progress < 1 ? `Attaching ${Math.round(progress * 100)}%` : 'Attach a receipt'}
          </Button>
        </>
      )}
    </section>
  );
}

/** Downloads a blob as a file. */
export function saveBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
