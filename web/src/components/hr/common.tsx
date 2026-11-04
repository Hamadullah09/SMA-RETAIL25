'use client';

import { useQuery } from '@tanstack/react-query';
import { Select } from '@/components/ui/field';
import { api } from '@/lib/api';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { EmployeeRow } from '@/lib/types';

/** The employees of the chosen store for pickers (up to 500), still employed unless `all`. */
export function useEmployeeChoices(all = false) {
  const { storeId } = useStore();
  return useQuery({
    queryKey: ['hr-employee-choices', storeId, all],
    queryFn: () => api.employees({ storeId, status: all ? 'all' : 'active', pageSize: 500 }),
    staleTime: 60_000,
    select: (page) => page.items,
  });
}

export function employeeLabel(e: Pick<EmployeeRow, 'code' | 'fullName'>): string {
  return `${e.code} · ${e.fullName}`;
}

/** A dropdown of employees. `empty` is the first choice's text ("Everyone", "Choose…"). */
export function EmployeeSelect({
  id,
  value,
  onChange,
  empty = 'Choose…',
  all,
  className,
  label,
}: {
  id: string;
  value: string;
  onChange(value: string): void;
  empty?: string;
  all?: boolean;
  className?: string;
  label?: string;
}) {
  const employees = useEmployeeChoices(all);
  return (
    <Select id={id} aria-label={label} className={className} value={value} onChange={(e) => onChange(e.target.value)}>
      <option value="">{employees.isLoading ? 'Loading…' : empty}</option>
      {(employees.data ?? []).map((e) => (
        <option key={e.id} value={e.id}>
          {employeeLabel(e)}
        </option>
      ))}
    </Select>
  );
}

/** What the signed-in user may do with HR and payroll. */
export function useHrRights() {
  const { can, canChangeShared } = useSession();
  const payrollRun = can(P.payrollRun);
  const payrollApprove = can(P.payrollApprove);
  return {
    view: can(P.hrView),
    edit: can(P.hrEdit),
    attendance: can(P.hrAttendance),
    leave: can(P.hrLeave),
    payrollRun,
    payrollApprove,
    readPayroll: payrollRun || payrollApprove,
    /** Departments, designations, leave types, holidays and rule sets are shared by every store. */
    editShared: can(P.hrEdit) && canChangeShared,
  };
}
