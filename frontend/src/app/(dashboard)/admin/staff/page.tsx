'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, FormSection } from '@/components/masters/browse-form';
import { RecordPicker } from '@/components/masters/record-picker';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { formatCurrency , recordIdFrom} from '@/lib/utils';
import type {
  CommissionReportResult,
  CommissionRule,
  CommissionType,
  HoursReportResult,
  StaffRow,
} from '@/types/masters';

const inputClass =
  'pos-input';

const commissionTypeLabel: Record<CommissionType, string> = {
  Percentage: '% of the takings',
  Fixed: 'a fixed amount per unit',
  PercentOfProfit: '% of the margin',
};

/** Legacy 0–4 (guide p.82). Level 0 is why a trainee's sales are practice rather than real. */
const accessLevelLabel: Record<number, string> = {
  0: 'Trainee — sales are practice',
  1: 'Cashier',
  2: 'Senior',
  3: 'Supervisor',
  4: 'Manager',
};

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/**
 * Staff, their commission rules, and the two reports that come off them (guide p.33, p.75–76).
 */
export default function StaffPage() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canWrite = auth.can('staff.write');
  const canSeeHours = auth.can('reports.hours');
  const canSeeCommissions = auth.can('reports.commissions');

  const [rows, setRows] = useState<StaffRow[]>([]);
  const [includeInactive, setIncludeInactive] = useState(false);
  const [loading, setLoading] = useState(false);
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const load = useCallback(async () => {
    if (!locationId) return;
    setLoading(true);

    try {
      setRows(await mastersApi.staff.browse(locationId, includeInactive));
    } catch (error) {
      toast({ title: 'Could not load staff', description: describe(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId, includeInactive]);

  useEffect(() => {
    void load();
  }, [load]);

  const columns = useMemo<DataGridColumn<StaffRow>[]>(
    () => [
      { key: 'code', header: 'Code', width: 80, render: (r) => r.staffCode },
      { key: 'name', header: 'Name', width: 200, render: (r) => r.fullName },
      {
        key: 'level',
        header: 'Level',
        width: 190,
        render: (r) => accessLevelLabel[r.accessLevel] ?? `Level ${r.accessLevel}`,
        sortValue: (r) => r.accessLevel,
      },
      {
        key: 'clocked',
        header: 'On the clock',
        width: 130,
        // Words, not a dot: "since 09:14" tells a supervisor what a green light cannot.
        render: (r) =>
          r.isClockedIn && r.clockedInAt
            ? `since ${new Date(r.clockedInAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
            : '—',
      },
      { key: 'active', header: 'Active', width: 70, render: (r) => (r.isActive ? 'Yes' : 'No') },
    ],
    [],
  );

  return (
    <BrowseFormShell
      title="Staff"
      filters={
        <label className="flex items-center gap-1.5">
          <input
            type="checkbox"
            checked={includeInactive}
            onChange={(event) => setIncludeInactive(event.target.checked)}
          />
          Include people who have left
        </label>
      }
      grid={
        <DataGrid
          gridId="staff"
          rows={rows}
          columns={columns}
          rowKey={(row) => row.id}
          onRowActivate={(row) => setSelectedId(row.id)}
          emptyMessage={loading ? 'Loading…' : 'No staff records.'}
        />
      }
      form={
        selectedId && locationId ? (
          <StaffPanel
            key={String(selectedId)}
            staff={rows.find((r) => r.id === selectedId)!}
            locationId={locationId}
            canWrite={canWrite}
            canSeeHours={canSeeHours}
            canSeeCommissions={canSeeCommissions}
            onClose={() => setSelectedId(null)}
          />
        ) : locationId ? (
          <ReportsPanel
            locationId={locationId}
            canSeeHours={canSeeHours}
            canSeeCommissions={canSeeCommissions}
          />
        ) : null
      }
      status={
        <span className="flex items-center gap-3">
          <span>{rows.length} on the books</span>
          <span>Double-click a row to open it.</span>
        </span>
      }
    />
  );
}

function describe(error: unknown): string {
  return error instanceof PosApiError ? error.problem.detail : 'Something went wrong.';
}

function StaffPanel({
  staff,
  locationId,
  canWrite,
  canSeeHours,
  canSeeCommissions,
  onClose,
}: {
  staff: StaffRow;
  locationId: number;
  canWrite: boolean;
  canSeeHours: boolean;
  canSeeCommissions: boolean;
  onClose: () => void;
}) {
  const [rules, setRules] = useState<CommissionRule[]>([]);
  const [busy, setBusy] = useState(false);

  const [type, setType] = useState<CommissionType>('Percentage');
  const [value, setValue] = useState(5);
  const [max, setMax] = useState('');
  const [scope, setScope] = useState<'all' | 'department' | 'product'>('all');
  const [departmentId, setDepartmentId] = useState<number | ''>('');
  const [productId, setProductId] = useState<number | null>(null);

  const { data: departments = [] } = useQuery({
    queryKey: ['departments', locationId],
    queryFn: () => mastersApi.departments.list(locationId),
  });

  const refresh = useCallback(async () => {
    try {
      setRules(await mastersApi.staff.commissionRules(staff.id));
    } catch (error) {
      toast({ title: 'Could not load the rules', description: describe(error), variant: 'destructive' });
    }
  }, [staff.id]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const add = async () => {
    setBusy(true);

    try {
      await mastersApi.staff.saveCommissionRule({
        staffId: staff.id,
        commissionType: type,
        value,
        productId: scope === 'product' ? productId : null,
        departmentId: scope === 'department' ? departmentId || null : null,
        maxCommission: max === '' ? null : Number(max),
        isActive: true,
      });

      setProductId(null);
      await refresh();
      toast({ title: 'Rule added' });
    } catch (error) {
      toast({ title: 'Not added', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const remove = async (id: number) => {
    setBusy(true);

    try {
      await mastersApi.staff.deleteCommissionRule(id);
      await refresh();
      toast({ title: 'Rule removed', description: 'Commission already earned is unaffected.' });
    } catch (error) {
      toast({ title: 'Not removed', description: describe(error), variant: 'destructive' });
    } finally {
      setBusy(false);
    }
  };

  const today = isoDate(new Date());
  const monthStart = isoDate(new Date(new Date().getFullYear(), new Date().getMonth(), 1));

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="text-body font-semibold">
          {staff.staffCode} — {staff.fullName}
        </h2>
        <button type="button" className="pos-button" onClick={onClose}>
          Close
        </button>
      </div>

      <FormSection title="Role">
        <p className="text-body">{accessLevelLabel[staff.accessLevel] ?? `Level ${staff.accessLevel}`}</p>
        {staff.accessLevel === 0 ? (
          <p className="text-label text-warning">
            Everything this person rings is a practice sale: it moves no stock, no drawer, no loyalty and no
            money, it earns no commission, and every report leaves it out.
          </p>
        ) : null}
      </FormSection>

      <FormSection
        title="Commission rules"
        hint="The most specific rule wins — an item rate beats a department rate, which beats the rate on everything else."
      >
        <table className="w-full text-body">
          <thead className="text-label">
            <tr>
              <th className="py-1 text-left">Applies to</th>
              <th className="py-1 text-left">Pays</th>
              <th className="py-1 text-right">Cap</th>
              {canWrite ? <th /> : null}
            </tr>
          </thead>
          <tbody>
            {rules.map((rule) => (
              <tr key={String(rule.id)} className="border-t border-subtle">
                <td className="py-1">
                  {rule.productName ?? rule.departmentName ?? 'Everything they sell'}
                  {!rule.isActive ? <span className="ml-1 text-label">(off)</span> : null}
                </td>
                <td className="py-1">
                  {rule.commissionType === 'Fixed'
                    ? `${formatCurrency(rule.value)} per unit`
                    : `${rule.value}% ${rule.commissionType === 'PercentOfProfit' ? 'of margin' : 'of takings'}`}
                </td>
                <td className="py-1 text-right">{rule.maxCommission ? formatCurrency(rule.maxCommission) : '—'}</td>
                {canWrite ? (
                  <td className="py-1 text-right">
                    <button
                      type="button"
                      className="text-label underline"
                      disabled={busy}
                      onClick={() => void remove(rule.id)}
                    >
                      Remove
                    </button>
                  </td>
                ) : null}
              </tr>
            ))}
          </tbody>
        </table>

        {rules.length === 0 ? (
          <p className="text-label text-ink-muted">
            No rules — this person earns no commission.
          </p>
        ) : null}

        {canWrite ? (
          <div className="space-y-2 border-t border-subtle pt-2">
            <div className="flex flex-wrap items-end gap-2">
              <label className="flex flex-col gap-1 text-label">
                Applies to
                <select
                  className={inputClass}
                  value={scope}
                  onChange={(event) => setScope(event.target.value as typeof scope)}
                >
                  <option value="all">Everything they sell</option>
                  <option value="department">One department</option>
                  <option value="product">One item</option>
                </select>
              </label>

              {scope === 'department' ? (
                <label className="flex flex-col gap-1 text-label">
                  Department
                  <select
                    className={inputClass}
                    value={departmentId}
                    onChange={(event) => setDepartmentId(recordIdFrom(event.target.value))}
                  >
                    <option value="">Choose…</option>
                    {departments.map((department) => (
                      <option key={String(department.id)} value={department.id}>
                        {department.name}
                      </option>
                    ))}
                  </select>
                </label>
              ) : null}

              <label className="flex flex-col gap-1 text-label">
                Pays
                <select
                  className={inputClass}
                  value={type}
                  onChange={(event) => setType(event.target.value as CommissionType)}
                >
                  {(Object.keys(commissionTypeLabel) as CommissionType[]).map((option) => (
                    <option key={option} value={option}>
                      {commissionTypeLabel[option]}
                    </option>
                  ))}
                </select>
              </label>

              <label className="flex flex-col gap-1 text-label">
                {type === 'Fixed' ? 'Amount' : 'Percent'}
                <input
                  type="number"
                  step="0.01"
                  className={`${inputClass} w-24`}
                  value={value}
                  onChange={(event) => setValue(Number(event.target.value) || 0)}
                />
              </label>

              <label className="flex flex-col gap-1 text-label">
                Cap per line
                <input
                  type="number"
                  step="0.01"
                  placeholder="none"
                  className={`${inputClass} w-24`}
                  value={max}
                  onChange={(event) => setMax(event.target.value)}
                />
              </label>

              <button
                type="button"
                className="pos-button"
                disabled={busy || (scope === 'product' && !productId) || (scope === 'department' && !departmentId)}
                onClick={() => void add()}
              >
                Add
              </button>
            </div>

            {scope === 'product' ? (
              <RecordPicker
                label="Item"
                value={null}
                placeholder="Code or description"
                search={(term) =>
                  mastersApi.products
                    .browse(locationId, { search: term, pageSize: 15 })
                    .then((page) => page.items.map((i) => ({ id: i.id, code: i.stockCode, name: i.name })))
                }
                onChange={(picked) => setProductId(picked?.id ?? null)}
              />
            ) : null}

            <p className="text-label text-ink-muted">
              Paying on margin pays nothing on a line sold at or below cost. A fixed amount pays per unit, so
              three of an item pays three times.
            </p>
          </div>
        ) : null}
      </FormSection>

      {canSeeHours || canSeeCommissions ? (
        <FormSection title="This month" hint="Their own figures, from the first of the month to today.">
          <div className="flex flex-wrap gap-2">
            {canSeeHours ? (
              <a
                className="pos-button"
                href={mastersApi.staff.hoursExportUrl(locationId, monthStart, today, staff.id)}
                target="_blank"
                rel="noopener noreferrer"
              >
                Download their hours
              </a>
            ) : null}
            {canSeeCommissions ? (
              <a
                className="pos-button"
                href={mastersApi.staff.commissionsExportUrl(locationId, monthStart, today, staff.id)}
                target="_blank"
                rel="noopener noreferrer"
              >
                Download their commission
              </a>
            ) : null}
          </div>
        </FormSection>
      ) : null}
    </div>
  );
}

/** The two reports, shown when nobody in particular is selected. */
function ReportsPanel({
  locationId,
  canSeeHours,
  canSeeCommissions,
}: {
  locationId: number;
  canSeeHours: boolean;
  canSeeCommissions: boolean;
}) {
  const [from, setFrom] = useState(() => isoDate(new Date(new Date().getFullYear(), new Date().getMonth(), 1)));
  const [to, setTo] = useState(() => isoDate(new Date()));
  const [hours, setHours] = useState<HoursReportResult | null>(null);
  const [commissions, setCommissions] = useState<CommissionReportResult | null>(null);
  const [loading, setLoading] = useState(false);

  const run = useCallback(async () => {
    setLoading(true);

    try {
      const [h, c] = await Promise.all([
        canSeeHours ? mastersApi.staff.hours(locationId, from, to) : Promise.resolve(null),
        canSeeCommissions ? mastersApi.staff.commissions(locationId, from, to) : Promise.resolve(null),
      ]);

      setHours(h);
      setCommissions(c);
    } catch (error) {
      toast({ title: 'Could not run that', description: describe(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, [locationId, from, to, canSeeHours, canSeeCommissions]);

  useEffect(() => {
    void run();
  }, [run]);

  if (!canSeeHours && !canSeeCommissions) {
    return null;
  }

  return (
    <div>
      <h2 className="mb-2 text-body font-semibold">Hours and commission</h2>

      <FormSection title="Period">
        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col gap-1 text-label">
            From
            <input type="date" className={inputClass} value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1 text-label">
            To
            <input type="date" className={inputClass} value={to} onChange={(e) => setTo(e.target.value)} />
          </label>
          <button type="button" className="pos-button" disabled={loading} onClick={() => void run()}>
            Refresh
          </button>
        </div>
      </FormSection>

      {canSeeHours && hours ? (
        <FormSection
          title="Hours"
          actions={
            <a
              className="text-label underline"
              href={mastersApi.staff.hoursExportUrl(locationId, from, to)}
              target="_blank"
              rel="noopener noreferrer"
            >
              CSV
            </a>
          }
        >
          <table className="w-full text-body">
            <thead className="text-label">
              <tr>
                <th className="py-1 text-left">Code</th>
                <th className="py-1 text-left">Name</th>
                <th className="py-1 text-right">Shifts</th>
                <th className="py-1 text-right">Hours</th>
                <th className="py-1 text-right">Still on</th>
              </tr>
            </thead>
            <tbody>
              {hours.rows.map((row) => (
                <tr key={row.staffId} className="border-t border-subtle">
                  <td className="py-1">{row.staffCode}</td>
                  <td className="py-1">{row.staffName}</td>
                  <td className="py-1 text-right">{row.shifts}</td>
                  <td className="py-1 text-right">{row.hoursWorked}</td>
                  <td className="py-1 text-right">{row.openShifts || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <p className="text-body font-semibold">{hours.totalHours}h over {hours.totalShifts} shift(s)</p>

          {hours.totalOpenShifts > 0 ? (
            <p className="text-label text-warning">
              {hours.totalOpenShifts} shift(s) have no clock-out, so their hours are not in that total. Correct
              them before running payroll.
            </p>
          ) : null}
        </FormSection>
      ) : null}

      {canSeeCommissions && commissions ? (
        <FormSection
          title="Commission"
          hint="Read off the commission ledger, so editing a rate afterwards cannot restate what was already earned."
          actions={
            <a
              className="text-label underline"
              href={mastersApi.staff.commissionsExportUrl(locationId, from, to)}
              target="_blank"
              rel="noopener noreferrer"
            >
              CSV
            </a>
          }
        >
          <table className="w-full text-body">
            <thead className="text-label">
              <tr>
                <th className="py-1 text-left">Code</th>
                <th className="py-1 text-left">Name</th>
                <th className="py-1 text-right">Lines</th>
                <th className="py-1 text-right">Sales</th>
                <th className="py-1 text-right">Commission</th>
              </tr>
            </thead>
            <tbody>
              {commissions.rows.map((row) => (
                <tr key={row.staffId} className="border-t border-subtle">
                  <td className="py-1">{row.staffCode}</td>
                  <td className="py-1">{row.staffName}</td>
                  <td className="py-1 text-right">
                    {row.lines}
                    {row.cappedLines > 0 ? (
                      <span className="text-label text-ink-muted"> ({row.cappedLines} capped)</span>
                    ) : null}
                  </td>
                  <td className="py-1 text-right">{formatCurrency(row.salesNet)}</td>
                  <td className="py-1 text-right font-medium">{formatCurrency(row.commission)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {commissions.rows.length === 0 ? (
            <p className="text-label text-ink-muted">
              No commission earned in this period.
            </p>
          ) : (
            <p className="text-body font-semibold">{formatCurrency(commissions.totalCommission)} owed in total</p>
          )}
        </FormSection>
      ) : null}
    </div>
  );
}
