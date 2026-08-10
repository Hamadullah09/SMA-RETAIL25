'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  AlertTriangle,
  CircleDot,
  Clock,
  Download,
  GraduationCap,
  Minus,
  Plus,
  RotateCcw,
  Trash2,
  X,
} from 'lucide-react';
import { DataGrid, type DataGridColumn } from '@/components/shell/data-grid';
import { BrowseFormShell, FormSection } from '@/components/masters/browse-form';
import { RecordPicker } from '@/components/masters/record-picker';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import { cn, formatCurrency , recordIdFrom} from '@/lib/utils';
import type {
  CommissionReportResult,
  CommissionRule,
  CommissionType,
  HoursReportResult,
  StaffRow,
} from '@/types/masters';

const inputClass =
  'pos-input';

const thText = 'px-2 py-2 text-left text-label font-medium text-ink-muted';
const thNum = 'px-2 py-2 text-right text-label font-medium text-ink-muted';
const td = 'px-2 py-2 align-middle';
const tdNum = 'px-2 py-2 text-right align-middle tabular-nums';

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
      { key: 'code', header: 'Code', width: 80, render: (r) => <span className="pos-amount">{r.staffCode}</span> },
      { key: 'name', header: 'Name', width: 200, render: (r) => r.fullName },
      {
        key: 'level',
        header: 'Level',
        width: 190,
        render: (r) => (
          <span className={cn('inline-flex items-center gap-1.5', r.accessLevel === 0 && 'text-warning')}>
            {r.accessLevel === 0 ? <GraduationCap className="h-3.5 w-3.5 shrink-0" aria-hidden /> : null}
            {accessLevelLabel[r.accessLevel] ?? `Level ${r.accessLevel}`}
          </span>
        ),
        sortValue: (r) => r.accessLevel,
      },
      {
        key: 'clocked',
        header: 'On the clock',
        width: 140,
        // Words and a glyph, not a dot: "since 09:14" tells a supervisor what a green light cannot.
        render: (r) =>
          r.isClockedIn && r.clockedInAt ? (
            <span className="pos-badge text-positive">
              <Clock className="h-3 w-3" aria-hidden />
              since {new Date(r.clockedInAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
            </span>
          ) : (
            <span className="text-ink-faint">—</span>
          ),
      },
      {
        key: 'active',
        header: 'Active',
        width: 100,
        render: (r) =>
          r.isActive ? (
            <span className="pos-badge text-ink-muted">
              <CircleDot className="h-3 w-3" aria-hidden />
              Yes
            </span>
          ) : (
            <span className="pos-badge text-ink-faint">
              <Minus className="h-3 w-3" aria-hidden />
              Left
            </span>
          ),
      },
    ],
    [],
  );

  return (
    <BrowseFormShell
      title="Staff"
      description="Who works here, what they are allowed to ring up, and what each sale earns them. Double-click a person to open their commission rules."
      toolbar={
        <button type="button" className="pos-button" disabled={loading} onClick={() => void load()}>
          <RotateCcw className="h-3.5 w-3.5" aria-hidden />
          {loading ? 'Loading…' : 'Refresh'}
        </button>
      }
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
          emptyMessage={
            loading
              ? 'Loading…'
              : includeInactive
                ? 'No staff records for this location yet.'
                : 'Nobody is currently active here. Tick “Include people who have left” to see past staff.'
          }
        />
      }
      form={
        selectedId !== null && locationId ? (
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
        <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span className="tabular-nums">{rows.length} on the books</span>
          <span aria-hidden>·</span>
          <span>Double-click a row to open it</span>
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
      <div className="mb-2 flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-body-lg font-semibold text-ink">{staff.fullName}</h2>
          <p className="pos-amount mt-0.5 text-body text-ink-muted">{staff.staffCode}</p>
        </div>
        <button type="button" className="pos-button shrink-0" onClick={onClose}>
          <X className="h-3.5 w-3.5" aria-hidden />
          Close
        </button>
      </div>

      <FormSection title="Role">
        <p className="text-body text-ink">{accessLevelLabel[staff.accessLevel] ?? `Level ${staff.accessLevel}`}</p>

        {staff.accessLevel === 0 ? (
          <div className="flex items-start gap-2.5 rounded border border-warning/35 bg-warning/10 p-3">
            <GraduationCap className="mt-0.5 h-4 w-4 shrink-0 text-warning" aria-hidden />
            <div className="min-w-0">
              <p className="text-body font-semibold text-warning">Practice mode</p>
              <p className="mt-0.5 text-body text-ink-muted">
                Everything this person rings is a practice sale: it moves no stock, no drawer, no loyalty and no
                money, it earns no commission, and every report leaves it out.
              </p>
            </div>
          </div>
        ) : null}
      </FormSection>

      <FormSection
        title="Commission rules"
        hint="The most specific rule wins — an item rate beats a department rate, which beats the rate on everything else."
      >
        {rules.length === 0 ? (
          <p className="rounded border border-subtle bg-panel-sunken px-3 py-4 text-center text-body text-ink-muted">
            No rules yet — this person earns no commission.
            {canWrite ? ' Add one below to start paying them on what they sell.' : ''}
          </p>
        ) : (
          <div className="overflow-x-auto rounded border border-subtle">
            <table className="w-full border-collapse text-body">
              <thead className="border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" className={thText}>Applies to</th>
                  <th scope="col" className={thText}>Pays</th>
                  <th scope="col" className={thNum}>Cap</th>
                  {canWrite ? (
                    <th scope="col" className={thNum}>
                      <span className="sr-only">Action</span>
                    </th>
                  ) : null}
                </tr>
              </thead>
              <tbody>
                {rules.map((rule) => (
                  <tr
                    key={String(rule.id)}
                    className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                  >
                    <td className={td}>
                      {rule.productName ?? rule.departmentName ?? 'Everything they sell'}
                      {!rule.isActive ? (
                        <span className="ml-1.5 pos-badge text-ink-faint">Off</span>
                      ) : null}
                    </td>
                    <td className={cn(td, 'tabular-nums')}>
                      {rule.commissionType === 'Fixed'
                        ? `${formatCurrency(rule.value)} per unit`
                        : `${rule.value}% ${rule.commissionType === 'PercentOfProfit' ? 'of margin' : 'of takings'}`}
                    </td>
                    <td className={tdNum} data-numeric="">
                      {rule.maxCommission ? formatCurrency(rule.maxCommission) : '—'}
                    </td>
                    {canWrite ? (
                      <td className={cn(td, 'text-right')}>
                        <button
                          type="button"
                          className="pos-button-danger"
                          disabled={busy}
                          onClick={() => void remove(rule.id)}
                          title="Stops this rule paying from now on. Commission already earned is unaffected."
                        >
                          <Trash2 className="h-3.5 w-3.5" aria-hidden />
                          Remove
                        </button>
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {canWrite && rules.length > 0 ? (
          <p className="text-body text-ink-muted">
            Removing a rule stops it paying from the next sale onwards. Commission already earned is read off
            the ledger and is not affected.
          </p>
        ) : null}

        {canWrite ? (
          <div className="space-y-3 rounded border border-subtle p-3">
            <p className="pos-nav-section px-0 pt-0">Add a rule</p>

            <div className="flex flex-wrap items-end gap-2">
              <label className="flex flex-col gap-1 text-label text-ink-muted">
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
                <label className="flex flex-col gap-1 text-label text-ink-muted">
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

              <label className="flex flex-col gap-1 text-label text-ink-muted">
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

              <label className="flex flex-col gap-1 text-label text-ink-muted">
                {type === 'Fixed' ? 'Amount' : 'Percent'}
                <input
                  type="number"
                  step="0.01"
                  className={`${inputClass} w-24 text-right`}
                  value={value}
                  onChange={(event) => setValue(Number(event.target.value) || 0)}
                />
              </label>

              <label className="flex flex-col gap-1 text-label text-ink-muted">
                Cap per line
                <input
                  type="number"
                  step="0.01"
                  placeholder="none"
                  className={`${inputClass} w-24 text-right`}
                  value={max}
                  onChange={(event) => setMax(event.target.value)}
                />
              </label>

              <button
                type="button"
                className="pos-button-primary"
                disabled={busy || (scope === 'product' && !productId) || (scope === 'department' && !departmentId)}
                onClick={() => void add()}
              >
                <Plus className="h-3.5 w-3.5" aria-hidden />
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

            <p className="text-body text-ink-muted">
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
                <Download className="h-3.5 w-3.5" aria-hidden />
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
                <Download className="h-3.5 w-3.5" aria-hidden />
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
      <div className="mb-2">
        <h2 className="text-body-lg font-semibold text-ink">Hours and commission</h2>
        <p className="mt-0.5 text-body text-ink-muted">Everyone at this location over the period below.</p>
      </div>

      <FormSection title="Period">
        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col gap-1 text-label text-ink-muted">
            From
            <input type="date" className={inputClass} value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1 text-label text-ink-muted">
            To
            <input type="date" className={inputClass} value={to} onChange={(e) => setTo(e.target.value)} />
          </label>
          <button type="button" className="pos-button" disabled={loading} onClick={() => void run()}>
            <RotateCcw className="h-3.5 w-3.5" aria-hidden />
            {loading ? 'Loading…' : 'Refresh'}
          </button>
        </div>
      </FormSection>

      {canSeeHours && hours ? (
        <FormSection
          title="Hours"
          actions={
            <a
              className="pos-button"
              href={mastersApi.staff.hoursExportUrl(locationId, from, to)}
              target="_blank"
              rel="noopener noreferrer"
            >
              <Download className="h-3.5 w-3.5" aria-hidden />
              CSV
            </a>
          }
        >
          {hours.rows.length === 0 ? (
            <p className="rounded border border-subtle bg-panel-sunken px-3 py-4 text-center text-body text-ink-muted">
              Nobody clocked in between these dates. Widen the period above.
            </p>
          ) : (
            <div className="overflow-x-auto rounded border border-subtle">
              <table className="w-full border-collapse text-body">
                <thead className="border-b border-subtle bg-panel-sunken">
                  <tr>
                    <th scope="col" className={thText}>Code</th>
                    <th scope="col" className={thText}>Name</th>
                    <th scope="col" className={thNum}>Shifts</th>
                    <th scope="col" className={thNum}>Hours</th>
                    <th scope="col" className={thNum}>Still on</th>
                  </tr>
                </thead>
                <tbody>
                  {hours.rows.map((row) => (
                    <tr
                      key={row.staffId}
                      className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                    >
                      <td className={cn(td, 'pos-amount')}>{row.staffCode}</td>
                      <td className={td}>{row.staffName}</td>
                      <td className={tdNum} data-numeric="">{row.shifts}</td>
                      <td className={tdNum} data-numeric="">{row.hoursWorked}</td>
                      <td className={tdNum} data-numeric="">{row.openShifts || '—'}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="border-t border-strong bg-panel-sunken">
                  <tr>
                    <td className={cn(td, 'font-semibold')} colSpan={2}>
                      Total
                    </td>
                    <td className={cn(tdNum, 'font-semibold')} data-numeric="">{hours.totalShifts}</td>
                    <td className={cn(tdNum, 'font-semibold')} data-numeric="">{hours.totalHours}</td>
                    <td className={cn(tdNum, 'font-semibold')} data-numeric="">{hours.totalOpenShifts || '—'}</td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}

          {hours.totalOpenShifts > 0 ? (
            <div className="flex items-start gap-2.5 rounded border border-warning/35 bg-warning/10 p-3">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-warning" aria-hidden />
              <p className="text-body text-ink-muted">
                <span className="font-semibold text-warning">
                  {hours.totalOpenShifts} shift(s) have no clock-out
                </span>
                , so their hours are not in that total. Correct them before running payroll.
              </p>
            </div>
          ) : null}
        </FormSection>
      ) : null}

      {canSeeCommissions && commissions ? (
        <FormSection
          title="Commission"
          hint="Read off the commission ledger, so editing a rate afterwards cannot restate what was already earned."
          actions={
            <a
              className="pos-button"
              href={mastersApi.staff.commissionsExportUrl(locationId, from, to)}
              target="_blank"
              rel="noopener noreferrer"
            >
              <Download className="h-3.5 w-3.5" aria-hidden />
              CSV
            </a>
          }
        >
          {commissions.rows.length === 0 ? (
            <p className="rounded border border-subtle bg-panel-sunken px-3 py-4 text-center text-body text-ink-muted">
              No commission earned in this period. Either nothing sold, or nobody has a rule — open a person to
              give them one.
            </p>
          ) : (
            <div className="overflow-x-auto rounded border border-subtle">
              <table className="w-full border-collapse text-body">
                <thead className="border-b border-subtle bg-panel-sunken">
                  <tr>
                    <th scope="col" className={thText}>Code</th>
                    <th scope="col" className={thText}>Name</th>
                    <th scope="col" className={thNum}>Lines</th>
                    <th scope="col" className={thNum}>Sales</th>
                    <th scope="col" className={thNum}>Commission</th>
                  </tr>
                </thead>
                <tbody>
                  {commissions.rows.map((row) => (
                    <tr
                      key={row.staffId}
                      className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                    >
                      <td className={cn(td, 'pos-amount')}>{row.staffCode}</td>
                      <td className={td}>{row.staffName}</td>
                      <td className={tdNum} data-numeric="">
                        {row.lines}
                        {row.cappedLines > 0 ? (
                          <span className="text-label text-ink-muted"> ({row.cappedLines} capped)</span>
                        ) : null}
                      </td>
                      <td className={tdNum} data-numeric="">{formatCurrency(row.salesNet)}</td>
                      <td className={cn(tdNum, 'font-medium')} data-numeric="">{formatCurrency(row.commission)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="border-t border-strong bg-panel-sunken">
                  <tr>
                    <td className={cn(td, 'font-semibold')} colSpan={4}>
                      Owed in total
                    </td>
                    <td className={cn(tdNum, 'font-semibold')} data-numeric="">
                      {formatCurrency(commissions.totalCommission)}
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
          )}
        </FormSection>
      ) : null}
    </div>
  );
}
