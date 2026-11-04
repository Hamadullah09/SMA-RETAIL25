'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Download, FileUp, LogOut, Plus, Save, Trash2, X } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useMemo, useRef, useState } from 'react';
import { employeeLabel, useEmployeeChoices, useHrRights } from '@/components/hr/common';
import { ConflictDialog } from '@/components/masters';
import { Button, LinkButton } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { ConfirmDialog, Dialog } from '@/components/ui/dialog';
import { Checkbox, Field, Input, Select, TextArea } from '@/components/ui/field';
import { Chip, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, FormError, Skeleton } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { Tabs } from '@/components/ui/tabs';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { isBusinessDate, toBusinessDate } from '@/lib/dates';
import { checkEmail, checkNumber, numberText, parseNumber, textOrNull } from '@/lib/forms';
import { formatCount, formatDate, formatMoney, formatShortUtc } from '@/lib/format';
import { EMPLOYMENT_TYPES, formatDays, GENDERS, LEAVE_STATUS, MARITAL_STATUS, PAYMENT_METHODS, saveBlob } from '@/lib/hr';
import { useHotkey } from '@/lib/hotkeys';
import { useStaff, staffName } from '@/lib/staff';
import { useStore } from '@/lib/store';
import type {
  EmployeeDetail,
  EmployeeDocumentRow,
  EmploymentType,
  Gender,
  LeaveBalanceRow,
  LeaveRequestRow,
  MaritalStatus,
  RuleSetInfo,
  SalaryComponentInfo,
  SalaryInfo,
  SalaryPaymentMethod,
} from '@/lib/types';

export default function EmployeePage() {
  return (
    <Suspense>
      <EmployeeLoader />
    </Suspense>
  );
}

function EmployeeLoader() {
  const id = useSearchParams().get('id');
  const query = useQuery({ queryKey: ['hr-employee', id], queryFn: () => api.employee(id!), enabled: !!id });
  if (id && query.error) {
    return (
      <div className="card">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    );
  }
  if (id && !query.data) {
    return (
      <div className="space-y-4" aria-busy="true">
        <Skeleton className="h-11 w-72" />
        <Skeleton className="h-[50vh] w-full rounded-card" />
      </div>
    );
  }
  return <EmployeeEditor key={query.data?.version ?? 'new'} employee={query.data ?? null} />;
}

type Tab = 'personal' | 'job' | 'pay' | 'salary' | 'documents' | 'leave';

interface Form {
  code: string;
  fullName: string;
  fatherOrHusbandName: string;
  cnic: string;
  dateOfBirth: string;
  gender: string;
  maritalStatus: string;
  address: string;
  city: string;
  phone: string;
  email: string;
  emergencyContactName: string;
  emergencyContactPhone: string;
  emergencyContactRelation: string;
  joiningDate: string;
  confirmationDate: string;
  leavingDate: string;
  leavingReason: string;
  employmentType: EmploymentType;
  departmentId: string;
  designationId: string;
  storeId: string;
  managerId: string;
  userId: string;
  shiftId: string;
  tracksAttendance: boolean;
  overtimeEligible: boolean;
  eobiApplies: boolean;
  socialSecurityApplies: boolean;
  paymentMethod: SalaryPaymentMethod;
  bankName: string;
  iban: string;
  ntn: string;
  eobiNumber: string;
  socialSecurityNumber: string;
  priorTaxYear: string;
  priorTaxableIncome: string;
  priorTaxDeducted: string;
  notes: string;
}

function formOf(e: EmployeeDetail | null, storeId: string | null): Form {
  return {
    code: e?.code ?? '',
    fullName: e?.fullName ?? '',
    fatherOrHusbandName: e?.fatherOrHusbandName ?? '',
    cnic: e?.cnic ?? '',
    dateOfBirth: e?.dateOfBirth ?? '',
    gender: e?.gender ?? '',
    maritalStatus: e?.maritalStatus ?? '',
    address: e?.address ?? '',
    city: e?.city ?? '',
    phone: e?.phone ?? '',
    email: e?.email ?? '',
    emergencyContactName: e?.emergencyContactName ?? '',
    emergencyContactPhone: e?.emergencyContactPhone ?? '',
    emergencyContactRelation: e?.emergencyContactRelation ?? '',
    joiningDate: e?.joiningDate ?? toBusinessDate(new Date()),
    confirmationDate: e?.confirmationDate ?? '',
    leavingDate: e?.leavingDate ?? '',
    leavingReason: e?.leavingReason ?? '',
    employmentType: e?.employmentType ?? 'Permanent',
    departmentId: e?.departmentId ?? '',
    designationId: e?.designationId ?? '',
    storeId: e ? e.storeId ?? '' : storeId ?? '',
    managerId: e?.managerId ?? '',
    userId: e?.userId ?? '',
    shiftId: e?.shiftId ?? '',
    tracksAttendance: e?.tracksAttendance ?? true,
    overtimeEligible: e?.overtimeEligible ?? true,
    eobiApplies: e?.eobiApplies ?? true,
    socialSecurityApplies: e?.socialSecurityApplies ?? true,
    paymentMethod: e?.paymentMethod ?? 'Cash',
    bankName: e?.bankName ?? '',
    iban: e?.iban ?? '',
    ntn: e?.ntn ?? '',
    eobiNumber: e?.eobiNumber ?? '',
    socialSecurityNumber: e?.socialSecurityNumber ?? '',
    priorTaxYear: e?.priorTaxYear ? String(e.priorTaxYear) : '',
    priorTaxableIncome: numberText(e?.priorTaxableIncome ?? 0),
    priorTaxDeducted: numberText(e?.priorTaxDeducted ?? 0),
    notes: e?.notes ?? '',
  };
}

/**
 * The rule set of a country in effect today: the latest that has started. When none is (a tax year ended before the next
 * one was added), the latest of all, as the server does: the formats of identity numbers and IBANs do not change with it.
 */
function inEffect(sets: RuleSetInfo[] | undefined, today: string): RuleSetInfo | undefined {
  const latest = [...(sets ?? [])].sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom));
  return latest.find((s) => s.effectiveFrom <= today && (!s.effectiveTo || s.effectiveTo >= today)) ?? latest[0];
}

function EmployeeEditor({ employee }: { employee: EmployeeDetail | null }) {
  const router = useRouter();
  const toast = useToast();
  const queryClient = useQueryClient();
  const rights = useHrRights();
  const { stores, storeId } = useStore();
  const [tab, setTab] = useState<Tab>('personal');
  const [form, setForm] = useState<Form>(() => formOf(employee, storeId));
  const [problem, setProblem] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const isNew = !employee?.id;
  const readOnly = !rights.edit;

  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((f) => ({ ...f, [key]: value }));

  const departments = useQuery({ queryKey: ['hr-departments'], queryFn: () => api.hrDepartments(), staleTime: 5 * 60_000 });
  const designations = useQuery({ queryKey: ['hr-designations'], queryFn: () => api.designations(), staleTime: 5 * 60_000 });
  const shifts = useQuery({ queryKey: ['hr-shifts', form.storeId], queryFn: () => api.shifts({ storeId: form.storeId || null }), staleTime: 60_000 });
  const settings = useQuery({ queryKey: ['hr-settings'], queryFn: () => api.hrSettings(), staleTime: 5 * 60_000 });
  const staff = useStaff(form.storeId || null);
  const managers = useEmployeeChoices(true);
  const country = form.storeId ? settings.data?.stores.find((s) => s.storeId === form.storeId)?.country : settings.data?.companyCountry;
  const ruleSets = useQuery({ queryKey: ['hr-rule-sets', country], queryFn: () => api.ruleSets(country), enabled: !!country, staleTime: 5 * 60_000 });
  const rules = inEffect(ruleSets.data, toBusinessDate(new Date()));
  const nextCode = useQuery({ queryKey: ['hr-next-code'], queryFn: () => api.nextEmployeeCode(), enabled: isNew && rights.edit });

  useEffect(() => {
    if (isNew && nextCode.data && !form.code) set('code', nextCode.data.code);
    // Only when the suggestion arrives.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nextCode.data]);

  useHotkey('Esc', () => router.push('/hr/employees/'));

  const save = useMutation({
    mutationFn: (body: EmployeeDetail) => (employee?.id ? api.saveEmployee(employee.id, body) : api.addEmployee(body)),
    onSuccess: (saved) => {
      toast.ok(`${saved.fullName} is saved.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-employees'] });
      void queryClient.invalidateQueries({ queryKey: ['hr-employee-choices'] });
      queryClient.setQueryData(['hr-employee', saved.id], saved);
      if (isNew && saved.id) router.replace(`/hr/employees/edit/?id=${encodeURIComponent(saved.id)}`);
    },
    onError: (e) => e instanceof ApiError && e.isConflict && setConflict(true),
  });

  const remove = useMutation({
    mutationFn: () => api.deleteEmployee(employee!.id!),
    onSuccess: () => {
      toast.ok(`${employee?.fullName ?? 'The employee'} is deleted.`);
      void queryClient.invalidateQueries({ queryKey: ['hr-employees'] });
      router.push('/hr/employees/');
    },
    onError: (e: Error) => {
      setDeleting(false);
      toast.error(e.message);
    },
  });

  const settle = useMutation({
    mutationFn: () => api.addSettlement({ employeeId: employee!.id!, notes: null }),
    onSuccess: (run) => router.push(`/hr/payroll/run/?id=${encodeURIComponent(run.id)}`),
    onError: (e: Error) => toast.error(e.message),
  });

  function submit() {
    const number = (text: string, label: string) => checkNumber(text, { label });
    const problemText = !form.fullName.trim()
      ? 'Give the full name.'
      : !isBusinessDate(form.joiningDate)
        ? 'Give the date the employee joined.'
        : form.leavingDate && form.leavingDate < form.joiningDate
          ? 'The leaving date is before the joining date.'
          : form.paymentMethod === 'Bank' && !form.iban.trim()
            ? 'A salary paid by bank transfer needs the IBAN.'
            : checkEmail(form.email) ?? number(form.priorTaxableIncome, 'Income from a previous employer') ?? number(form.priorTaxDeducted, 'Tax from a previous employer') ?? null;
    setProblem(problemText);
    if (problemText) return;
    save.mutate({
      id: employee?.id ?? null,
      version: employee?.version ?? null,
      code: textOrNull(form.code),
      fullName: form.fullName.trim(),
      fatherOrHusbandName: textOrNull(form.fatherOrHusbandName),
      cnic: textOrNull(form.cnic),
      dateOfBirth: isBusinessDate(form.dateOfBirth) ? form.dateOfBirth : null,
      gender: (form.gender || null) as Gender | null,
      maritalStatus: (form.maritalStatus || null) as MaritalStatus | null,
      address: textOrNull(form.address),
      city: textOrNull(form.city),
      phone: textOrNull(form.phone),
      email: textOrNull(form.email),
      emergencyContactName: textOrNull(form.emergencyContactName),
      emergencyContactPhone: textOrNull(form.emergencyContactPhone),
      emergencyContactRelation: textOrNull(form.emergencyContactRelation),
      joiningDate: form.joiningDate,
      confirmationDate: isBusinessDate(form.confirmationDate) ? form.confirmationDate : null,
      leavingDate: isBusinessDate(form.leavingDate) ? form.leavingDate : null,
      leavingReason: textOrNull(form.leavingReason),
      employmentType: form.employmentType,
      departmentId: form.departmentId || null,
      designationId: form.designationId || null,
      storeId: form.storeId || null,
      managerId: form.managerId || null,
      userId: form.userId || null,
      shiftId: form.shiftId || null,
      tracksAttendance: form.tracksAttendance,
      overtimeEligible: form.overtimeEligible,
      eobiApplies: form.eobiApplies,
      socialSecurityApplies: form.socialSecurityApplies,
      paymentMethod: form.paymentMethod,
      bankName: textOrNull(form.bankName),
      iban: textOrNull(form.iban),
      ntn: textOrNull(form.ntn),
      eobiNumber: textOrNull(form.eobiNumber),
      socialSecurityNumber: textOrNull(form.socialSecurityNumber),
      priorTaxYear: form.priorTaxYear ? Number(form.priorTaxYear) : null,
      priorTaxableIncome: parseNumber(form.priorTaxableIncome) ?? 0,
      priorTaxDeducted: parseNumber(form.priorTaxDeducted) ?? 0,
      notes: textOrNull(form.notes),
    });
  }

  const tabs = useMemo(() => {
    const list: Array<{ key: Tab; label: string }> = [
      { key: 'personal', label: 'Personal' },
      { key: 'job', label: 'Job' },
      { key: 'pay', label: 'Pay and statutory' },
    ];
    if (!isNew) {
      if (rights.readPayroll) list.push({ key: 'salary', label: 'Salary' });
      list.push({ key: 'documents', label: 'Documents' }, { key: 'leave', label: 'Leave' });
    }
    return list;
  }, [isNew, rights.readPayroll]);

  const text = (key: keyof Form, label: string, props: { maxLength?: number; hint?: string; type?: string; placeholder?: string } = {}) => (
    <Field label={label} htmlFor={`emp-${key}`} hint={props.hint}>
      <Input
        id={`emp-${key}`}
        type={props.type}
        value={String(form[key] ?? '')}
        maxLength={props.maxLength}
        placeholder={props.placeholder}
        disabled={readOnly}
        onChange={(e) => set(key, e.target.value as never)}
      />
    </Field>
  );

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault();
        if (!readOnly && ['personal', 'job', 'pay'].includes(tab)) submit();
      }}
    >
      <Toolbar>
        <LinkButton href="/hr/employees/" icon={ArrowLeft} shortcut="Esc">
          Employees
        </LinkButton>
        {!readOnly && (
          <Button type="submit" variant="sun" icon={Save} shortcut="Ctrl+S" busy={save.isPending}>
            Save
          </Button>
        )}
        {!readOnly && !isNew && (
          <Button icon={Trash2} onClick={() => setDeleting(true)}>
            Delete
          </Button>
        )}
        {!isNew && rights.payrollRun && employee?.leavingDate && (
          <Button icon={LogOut} busy={settle.isPending} onClick={() => settle.mutate()}>
            Final settlement
          </Button>
        )}
      </Toolbar>
      <header className="mb-3">
        <h1 className="page-title">{isNew ? 'New employee' : employee?.fullName}</h1>
        {!isNew && (
          <p className="text-muted">
            {employee?.code}
            {employee?.leavingDate ? ` · left ${formatDate(employee.leavingDate)}` : ` · joined ${formatDate(employee?.joiningDate)}`}
            {country ? ` · rules of ${country}` : ''}
          </p>
        )}
      </header>
      <Tabs label="Employee" tabs={tabs} value={tab} onChange={setTab} className="mb-4" />

      {tab === 'personal' && (
        <Card>
          <div className="grid max-w-3xl gap-3">
            {text('fullName', 'Full name', { maxLength: 100 })}
            {text('fatherOrHusbandName', 'Father or husband name', { maxLength: 100 })}
            {text('cnic', 'CNIC', { maxLength: 30, hint: rules?.cnicExample ? `Like ${rules.cnicExample}` : undefined, placeholder: rules?.cnicExample ?? undefined })}
            {text('dateOfBirth', 'Date of birth', { type: 'date' })}
            <Field label="Gender" htmlFor="emp-gender">
              <Select id="emp-gender" value={form.gender} disabled={readOnly} onChange={(e) => set('gender', e.target.value)}>
                <option value="">Not given</option>
                {Object.entries(GENDERS).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Marital status" htmlFor="emp-marital">
              <Select id="emp-marital" value={form.maritalStatus} disabled={readOnly} onChange={(e) => set('maritalStatus', e.target.value)}>
                <option value="">Not given</option>
                {Object.entries(MARITAL_STATUS).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </Select>
            </Field>
            {text('address', 'Address', { maxLength: 200 })}
            {text('city', 'City', { maxLength: 60 })}
            {text('phone', 'Phone', { maxLength: 30 })}
            {text('email', 'Email', { maxLength: 100, type: 'email' })}
            {text('emergencyContactName', 'Emergency contact', { maxLength: 100 })}
            {text('emergencyContactRelation', 'Relation', { maxLength: 40 })}
            {text('emergencyContactPhone', 'Emergency phone', { maxLength: 30 })}
          </div>
        </Card>
      )}

      {tab === 'job' && (
        <Card>
          <div className="grid max-w-3xl gap-3">
            {text('code', 'Employee code', { maxLength: 20, hint: isNew ? 'Made for you; change it if you number staff your own way.' : undefined })}
            {text('joiningDate', 'Joined', { type: 'date' })}
            {text('confirmationDate', 'Confirmed', { type: 'date' })}
            <Field label="Employment" htmlFor="emp-type">
              <Select id="emp-type" value={form.employmentType} disabled={readOnly} onChange={(e) => set('employmentType', e.target.value as EmploymentType)}>
                {Object.entries(EMPLOYMENT_TYPES).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Department" htmlFor="emp-dept">
              <Select id="emp-dept" value={form.departmentId} disabled={readOnly} onChange={(e) => set('departmentId', e.target.value)}>
                <option value="">None</option>
                {(departments.data ?? []).filter((d) => d.isActive || d.id === form.departmentId).map((d) => (
                  <option key={d.id ?? d.name} value={d.id ?? ''}>
                    {d.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Designation" htmlFor="emp-designation">
              <Select id="emp-designation" value={form.designationId} disabled={readOnly} onChange={(e) => set('designationId', e.target.value)}>
                <option value="">None</option>
                {(designations.data ?? []).filter((d) => d.isActive || d.id === form.designationId).map((d) => (
                  <option key={d.id ?? d.name} value={d.id ?? ''}>
                    {d.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Store" htmlFor="emp-store">
              <Select id="emp-store" value={form.storeId} disabled={readOnly} onChange={(e) => set('storeId', e.target.value)}>
                <option value="">Head office</option>
                {stores.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.code} · {s.name}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Reports to" htmlFor="emp-manager">
              <Select id="emp-manager" value={form.managerId} disabled={readOnly} onChange={(e) => set('managerId', e.target.value)}>
                <option value="">Nobody</option>
                {employee?.managerId && !managers.data?.some((m) => m.id === employee.managerId) && <option value={employee.managerId}>{employee.managerName}</option>}
                {(managers.data ?? []).filter((m) => m.id !== employee?.id).map((m) => (
                  <option key={m.id} value={m.id}>
                    {employeeLabel(m)}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Till user" htmlFor="emp-user" hint="Their punches make the attendance and their sales the commission.">
              <Select id="emp-user" value={form.userId} disabled={readOnly} onChange={(e) => set('userId', e.target.value)}>
                <option value="">None</option>
                {employee?.userId && !staff.data?.some((u) => u.id === employee.userId) && <option value={employee.userId}>{employee.userName}</option>}
                {(staff.data ?? []).map((u) => (
                  <option key={u.id} value={u.id}>
                    {staffName(u)} ({u.userName})
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="Usual shift" htmlFor="emp-shift">
              <Select id="emp-shift" value={form.shiftId} disabled={readOnly} onChange={(e) => set('shiftId', e.target.value)}>
                <option value="">The rule set&apos;s working week</option>
                {(shifts.data ?? []).filter((s) => s.isActive || s.id === form.shiftId).map((s) => (
                  <option key={s.id ?? s.name} value={s.id ?? ''}>
                    {s.name} ({s.startTime}–{s.endTime})
                  </option>
                ))}
              </Select>
            </Field>
            <Field label="" htmlFor="emp-tracks">
              <div>
                <Checkbox label="Attendance from the time clock" hint="Off: presumed present on working days." checked={form.tracksAttendance} disabled={readOnly} onChange={(v) => set('tracksAttendance', v)} />
                <Checkbox label="Paid for overtime" checked={form.overtimeEligible} disabled={readOnly} onChange={(v) => set('overtimeEligible', v)} />
              </div>
            </Field>
            {text('leavingDate', 'Left on', { type: 'date' })}
            {form.leavingDate && text('leavingReason', 'Why they left', { maxLength: 200 })}
            <Field label="Notes" htmlFor="emp-notes">
              <TextArea id="emp-notes" value={form.notes} maxLength={1000} disabled={readOnly} onChange={(e) => set('notes', e.target.value)} />
            </Field>
          </div>
        </Card>
      )}

      {tab === 'pay' && (
        <Card>
          <div className="grid max-w-3xl gap-3">
            <Field label="Paid by" htmlFor="emp-method">
              <Select id="emp-method" value={form.paymentMethod} disabled={readOnly} onChange={(e) => set('paymentMethod', e.target.value as SalaryPaymentMethod)}>
                {Object.entries(PAYMENT_METHODS).map(([k, v]) => (
                  <option key={k} value={k}>
                    {v}
                  </option>
                ))}
              </Select>
            </Field>
            {text('bankName', 'Bank', { maxLength: 60 })}
            {text('iban', 'IBAN', { maxLength: 34, hint: rules?.ibanExample ? `Like ${rules.ibanExample}` : undefined, placeholder: rules?.ibanExample ?? undefined })}
            {text('ntn', 'NTN', { maxLength: 20 })}
            {text('eobiNumber', 'EOBI number', { maxLength: 30 })}
            {text('socialSecurityNumber', 'Social security number', { maxLength: 30 })}
            <Field label="" htmlFor="emp-eobi">
              <div>
                <Checkbox label="EOBI contributions" checked={form.eobiApplies} disabled={readOnly} onChange={(v) => set('eobiApplies', v)} />
                <Checkbox label="Social security contributions" checked={form.socialSecurityApplies} disabled={readOnly} onChange={(v) => set('socialSecurityApplies', v)} />
              </div>
            </Field>
            {text('priorTaxYear', 'Previous employer, tax year', { maxLength: 4, hint: 'The year the tax year starts in, when they joined during it.' })}
            {text('priorTaxableIncome', 'Taxable income there')}
            {text('priorTaxDeducted', 'Tax deducted there')}
          </div>
        </Card>
      )}

      {tab === 'salary' && employee?.id && <SalaryTab employeeId={employee.id} rules={rules} />}
      {tab === 'documents' && employee?.id && <DocumentsTab employeeId={employee.id} canEdit={rights.edit} />}
      {tab === 'leave' && employee?.id && <LeaveTab employeeId={employee.id} />}

      <FormError error={problem ?? (save.error && !(save.error instanceof ApiError && save.error.isConflict) ? save.error : null)} />
      <ConflictDialog
        open={conflict}
        what="employee"
        onReload={() => {
          setConflict(false);
          void queryClient.invalidateQueries({ queryKey: ['hr-employee', employee?.id] });
        }}
        onClose={() => setConflict(false)}
      />
      <ConfirmDialog
        open={deleting}
        title={`Delete ${employee?.fullName ?? 'this employee'}?`}
        message="Only someone added by mistake is deleted; anyone who was paid keeps their record with a leaving date."
        confirmLabel="Delete"
        cancelLabel="Keep"
        danger
        busy={remove.isPending}
        onConfirm={() => remove.mutate()}
        onClose={() => setDeleting(false)}
      />
    </form>
  );
}

// Salary -------------------------------------------------------------------------------------------------------------

function SalaryTab({ employeeId, rules }: { employeeId: string; rules: RuleSetInfo | undefined }) {
  const rights = useHrRights();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { currencySymbol: symbol } = useStore();
  const [adding, setAdding] = useState<SalaryInfo | 'new' | null>(null);
  const [deleting, setDeleting] = useState<SalaryInfo | null>(null);
  const history = useQuery({ queryKey: ['hr-salary', employeeId], queryFn: () => api.salaryHistory(employeeId) });
  const money = (n: number | undefined) => formatMoney(n ?? 0, symbol);
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteSalary(id),
    onSuccess: () => {
      toast.ok('The salary is deleted.');
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ['hr-salary', employeeId] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  return (
    <>
      {rights.payrollRun && (
        <Toolbar>
          <Button variant="sun" icon={Plus} onClick={() => setAdding(history.data?.[0] ?? 'new')}>
            New salary
          </Button>
        </Toolbar>
      )}
      <DataTable<SalaryInfo>
        rows={history.data}
        loading={history.isLoading}
        error={history.error}
        onRetry={() => void history.refetch()}
        rowKey={(s) => s.id ?? s.effectiveFrom}
        caption="Salary history"
        empty={<EmptyState title="No salary yet">Payroll leaves out employees without a salary.</EmptyState>}
        columns={[
          { key: 'from', header: 'From', cell: (s, i) => <span className="font-semibold">{formatDate(s.effectiveFrom)}{i === 0 && <Chip tone="ok" className="ml-2">Latest</Chip>}</span> },
          { key: 'basic', header: 'Basic', numeric: true, cell: (s) => money(s.basic) },
          { key: 'allow', header: 'Allowances', numeric: true, cell: (s) => money((s.gross ?? s.basic) - s.basic), hideBelow: 'md' },
          { key: 'gross', header: 'Gross a month', numeric: true, cell: (s) => <span className="font-semibold">{money(s.gross)}</span> },
          { key: 'ded', header: 'Fixed deductions', numeric: true, cell: (s) => money(s.fixedDeductions), hideBelow: 'lg' },
          { key: 'lines', header: 'Made of', cell: (s) => s.components.map((c) => `${c.name} ${money(c.amount)}`).join(', '), hideBelow: 'xl', className: 'wrap' },
          { key: 'by', header: 'Changed by', cell: (s) => s.changedBy ?? '', hideBelow: 'lg' },
          {
            key: 'act',
            header: '',
            cell: (s) =>
              rights.payrollRun && s.id ? (
                <button type="button" className="btn h-10 min-h-0 px-3" onClick={() => setDeleting(s)} aria-label={`Delete the salary from ${s.effectiveFrom}`}>
                  <Trash2 className="h-4 w-4" />
                </button>
              ) : null,
          },
        ]}
      />
      <p className="mt-2 px-1 text-ink">A change is a new salary from a date, so the history stays. Payroll uses the salary in effect on the last day it pays.</p>
      {adding && <SalaryDialog employeeId={employeeId} from={adding === 'new' ? null : adding} rules={rules} onClose={() => setAdding(null)} />}
      <ConfirmDialog
        open={!!deleting}
        title={`Delete the salary from ${deleting ? formatDate(deleting.effectiveFrom) : ''}?`}
        message="Only a salary no approved payroll used can go."
        confirmLabel="Delete"
        cancelLabel="Keep"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting?.id && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
}

interface Line {
  kind: SalaryComponentInfo['kind'];
  code: string;
  name: string;
  amount: string;
  accountCode: string;
}

function SalaryDialog({ employeeId, from, rules, onClose }: { employeeId: string; from: SalaryInfo | null; rules: RuleSetInfo | undefined; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [effectiveFrom, setEffectiveFrom] = useState(toBusinessDate(new Date()).slice(0, 8) + '01');
  const [basic, setBasic] = useState(numberText(from?.basic ?? 0));
  const [note, setNote] = useState('');
  const [lines, setLines] = useState<Line[]>(() =>
    (from?.components ?? []).map((c) => ({ kind: c.kind, code: c.code, name: c.name, amount: numberText(c.amount), accountCode: c.accountCode ?? '' })),
  );
  const [problem, setProblem] = useState<string | null>(null);
  const allowances = rules?.allowances ?? [];
  const save = useMutation({
    mutationFn: (body: SalaryInfo) => api.saveSalary(employeeId, body),
    onSuccess: () => {
      toast.ok('The salary is saved.');
      void queryClient.invalidateQueries({ queryKey: ['hr-salary', employeeId] });
      onClose();
    },
  });
  const change = (i: number, patch: Partial<Line>) => setLines((list) => list.map((l, j) => (j === i ? { ...l, ...patch } : l)));

  function submit() {
    const p =
      !isBusinessDate(effectiveFrom)
        ? 'Give the date the salary starts.'
        : checkNumber(basic, { required: true, label: 'The basic pay' }) ??
          lines.map((l) => (!l.code.trim() || !l.name.trim() ? 'Every line has a code and a name.' : checkNumber(l.amount, { required: true, label: l.name || 'An amount' }))).find(Boolean) ??
          null;
    setProblem(p ?? null);
    if (p) return;
    save.mutate({
      id: null,
      effectiveFrom,
      basic: parseNumber(basic) ?? 0,
      note: textOrNull(note),
      changedBy: null,
      components: lines.map((l) => ({ kind: l.kind, code: l.code.trim().toUpperCase(), name: l.name.trim(), amount: parseNumber(l.amount) ?? 0, accountCode: l.kind === 'Deduction' ? textOrNull(l.accountCode) : null })),
    });
  }

  return (
    <Dialog
      open
      width="lg"
      title="New salary"
      onClose={onClose}
      onSubmit={submit}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Save} shortcut="Enter" busy={save.isPending}>
            Save
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <Field label="From" htmlFor="sal-from">
          <Input id="sal-from" type="date" className="max-w-[200px]" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} />
        </Field>
        <Field label="Basic a month" htmlFor="sal-basic" hint="For a daily wage, the day's wage.">
          <Input id="sal-basic" inputMode="decimal" className="max-w-[200px]" value={basic} onChange={(e) => setBasic(e.target.value)} />
        </Field>
        <div className="rounded-control border border-line p-3">
          <p className="mb-2 font-semibold">Allowances and fixed deductions, a month</p>
          {lines.length === 0 && <p className="mb-2 text-muted">None yet.</p>}
          {lines.map((l, i) => (
            <div key={i} className="mb-2 grid grid-cols-1 gap-2 sm:grid-cols-[130px_150px_minmax(0,1fr)_130px_110px_auto] sm:items-center">
              <Select aria-label="Kind" value={l.kind} onChange={(e) => change(i, { kind: e.target.value as Line['kind'] })}>
                <option value="Allowance">Allowance</option>
                <option value="Deduction">Deduction</option>
              </Select>
              {l.kind === 'Allowance' && allowances.length > 0 ? (
                <Select
                  aria-label="Allowance"
                  value={allowances.some((a) => a.code === l.code) ? l.code : ''}
                  onChange={(e) => {
                    const rule = allowances.find((a) => a.code === e.target.value);
                    change(i, { code: e.target.value, name: rule?.name ?? l.name });
                  }}
                >
                  <option value="">Choose…</option>
                  {allowances.map((a) => (
                    <option key={a.code} value={a.code}>
                      {a.name}
                    </option>
                  ))}
                </Select>
              ) : (
                <Input aria-label="Code" placeholder="Code" value={l.code} maxLength={20} onChange={(e) => change(i, { code: e.target.value })} />
              )}
              <Input aria-label="Name" placeholder="Name" value={l.name} maxLength={60} onChange={(e) => change(i, { name: e.target.value })} />
              <Input aria-label="Amount" placeholder="Amount" inputMode="decimal" value={l.amount} onChange={(e) => change(i, { amount: e.target.value })} />
              {l.kind === 'Deduction' ? (
                <Input aria-label="Account" placeholder="Account" value={l.accountCode} maxLength={20} onChange={(e) => change(i, { accountCode: e.target.value })} />
              ) : (
                <span className="text-note text-muted">{allowances.find((a) => a.code === l.code) ? (allowances.find((a) => a.code === l.code)!.isTaxable ? 'Taxable' : 'Exempt') : ''}</span>
              )}
              <button type="button" className="btn h-10 min-h-0 px-3" aria-label="Remove the line" onClick={() => setLines((list) => list.filter((_, j) => j !== i))}>
                <X className="h-4 w-4" />
              </button>
            </div>
          ))}
          <div className="flex flex-wrap gap-2">
            <Button icon={Plus} onClick={() => setLines((list) => [...list, { kind: 'Allowance', code: '', name: '', amount: '', accountCode: '' }])}>
              Allowance
            </Button>
            <Button icon={Plus} onClick={() => setLines((list) => [...list, { kind: 'Deduction', code: '', name: '', amount: '', accountCode: '' }])}>
              Fixed deduction
            </Button>
          </div>
        </div>
        <Field label="Note" htmlFor="sal-note">
          <Input id="sal-note" value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} />
        </Field>
      </div>
      <FormError error={problem ?? save.error} />
    </Dialog>
  );
}

// Documents ----------------------------------------------------------------------------------------------------------

function DocumentsTab({ employeeId, canEdit }: { employeeId: string; canEdit: boolean }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const file = useRef<HTMLInputElement>(null);
  const [title, setTitle] = useState('');
  const [deleting, setDeleting] = useState<EmployeeDocumentRow | null>(null);
  const documents = useQuery({ queryKey: ['hr-documents', employeeId], queryFn: () => api.employeeDocuments(employeeId) });
  const upload = useMutation({
    mutationFn: (f: File) => api.uploadEmployeeDocument(employeeId, f, textOrNull(title)),
    onSuccess: (d) => {
      toast.ok(`${d.title} is kept.`);
      setTitle('');
      if (file.current) file.current.value = '';
      void queryClient.invalidateQueries({ queryKey: ['hr-documents', employeeId] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  const remove = useMutation({
    mutationFn: (id: string) => api.deleteEmployeeDocument(id),
    onSuccess: () => {
      toast.ok('The document is deleted.');
      setDeleting(null);
      void queryClient.invalidateQueries({ queryKey: ['hr-documents', employeeId] });
    },
    onError: (e: Error) => toast.error(e.message),
  });
  async function download(d: EmployeeDocumentRow) {
    try {
      saveBlob(await api.employeeDocument(d.id), d.fileName);
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'The document did not download.');
    }
  }
  return (
    <>
      {canEdit && (
        <Toolbar>
          <label className="sr-only" htmlFor="doc-title">
            Title
          </label>
          <Input id="doc-title" className="w-[260px]" placeholder="Title (CNIC copy, contract…)" value={title} maxLength={100} onChange={(e) => setTitle(e.target.value)} />
          <input
            ref={file}
            type="file"
            aria-label="Document file"
            className="sr-only"
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) upload.mutate(f);
            }}
          />
          <Button variant="sun" icon={FileUp} busy={upload.isPending} onClick={() => file.current?.click()}>
            Add a document
          </Button>
          <span className="text-muted">At most 10 MB.</span>
        </Toolbar>
      )}
      <DataTable<EmployeeDocumentRow>
        rows={documents.data}
        loading={documents.isLoading}
        error={documents.error}
        onRetry={() => void documents.refetch()}
        rowKey={(d) => d.id}
        caption="Documents"
        empty={<EmptyState title="No documents">Keep copies of the CNIC, the contract and certificates here.</EmptyState>}
        columns={[
          { key: 'title', header: 'Title', cell: (d) => <span className="font-semibold">{d.title}</span> },
          { key: 'file', header: 'File', cell: (d) => d.fileName, hideBelow: 'md' },
          { key: 'size', header: 'Size', numeric: true, cell: (d) => `${formatCount(Math.max(1, Math.round(d.size / 1024)))} KB`, hideBelow: 'lg' },
          { key: 'when', header: 'Added', cell: (d) => formatShortUtc(d.uploadedAtUtc), hideBelow: 'md' },
          { key: 'by', header: 'By', cell: (d) => d.uploadedBy ?? '', hideBelow: 'xl' },
          {
            key: 'act',
            header: '',
            cell: (d) => (
              <span className="flex justify-end gap-2">
                <button type="button" className="btn h-10 min-h-0 px-3" onClick={() => void download(d)} aria-label={`Download ${d.title}`}>
                  <Download className="h-4 w-4" />
                </button>
                {canEdit && (
                  <button type="button" className="btn h-10 min-h-0 px-3" onClick={() => setDeleting(d)} aria-label={`Delete ${d.title}`}>
                    <Trash2 className="h-4 w-4" />
                  </button>
                )}
              </span>
            ),
          },
        ]}
      />
      <ConfirmDialog
        open={!!deleting}
        title={`Delete ${deleting?.title ?? 'the document'}?`}
        confirmLabel="Delete"
        cancelLabel="Keep"
        danger
        busy={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
}

// Leave --------------------------------------------------------------------------------------------------------------

function LeaveTab({ employeeId }: { employeeId: string }) {
  const year = new Date().getFullYear();
  const balances = useQuery({ queryKey: ['hr-balances', year, employeeId], queryFn: () => api.leaveBalances({ year, employeeId }) });
  const requests = useQuery({ queryKey: ['hr-leave', { employeeId }], queryFn: () => api.leaveRequests({ employeeId, pageSize: 100 }) });
  return (
    <div className="space-y-4">
      <DataTable<LeaveBalanceRow>
        rows={balances.data}
        loading={balances.isLoading}
        error={balances.error}
        onRetry={() => void balances.refetch()}
        rowKey={(b) => b.leaveTypeId}
        caption={`Leave balances ${year}`}
        empty={<EmptyState title="No leave types for this employee's country" />}
        columns={[
          { key: 'type', header: `Leave ${year}`, cell: (b) => <span className="font-semibold">{b.leaveType}</span> },
          { key: 'ent', header: 'A year', numeric: true, cell: (b) => (b.limited ? formatDays(b.entitled) : 'No limit') },
          { key: 'earn', header: 'Earned', numeric: true, cell: (b) => (b.limited ? formatDays(b.earned) : ''), hideBelow: 'md' },
          { key: 'in', header: 'Carried in', numeric: true, cell: (b) => (b.limited ? formatDays(b.carriedIn) : ''), hideBelow: 'lg' },
          { key: 'taken', header: 'Taken', numeric: true, cell: (b) => formatDays(b.taken) },
          { key: 'pend', header: 'Waiting', numeric: true, cell: (b) => formatDays(b.pending), hideBelow: 'md' },
          { key: 'left', header: 'Left', numeric: true, cell: (b) => <span className="font-semibold">{b.available === null ? '' : formatDays(b.available)}</span> },
        ]}
      />
      <DataTable<LeaveRequestRow>
        rows={requests.data?.items}
        loading={requests.isLoading}
        error={requests.error}
        onRetry={() => void requests.refetch()}
        rowKey={(r) => r.id}
        caption="Leave requests"
        empty={<EmptyState title="No leave asked for" />}
        columns={[
          { key: 'type', header: 'Leave', cell: (r) => <span className="font-semibold">{r.leaveType}</span> },
          { key: 'from', header: 'From', cell: (r) => formatDate(r.fromDate) + (r.firstDayHalf ? ' (½)' : '') },
          { key: 'to', header: 'To', cell: (r) => formatDate(r.toDate) + (r.lastDayHalf ? ' (½)' : '') },
          { key: 'days', header: 'Days', numeric: true, cell: (r) => formatDays(r.days) },
          { key: 'status', header: 'Status', cell: (r) => <Chip tone={LEAVE_STATUS[r.status].tone}>{LEAVE_STATUS[r.status].label}</Chip> },
        ]}
      />
    </div>
  );
}
