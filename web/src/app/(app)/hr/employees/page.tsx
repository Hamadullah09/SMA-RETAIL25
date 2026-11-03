'use client';

import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import { useHrRights } from '@/components/hr/common';
import { LinkButton } from '@/components/ui/button';
import { Select } from '@/components/ui/field';
import { Chip, PageHeader, Pager, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { api } from '@/lib/api';
import { formatCount, formatDate } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { EMPLOYMENT_TYPES } from '@/lib/hr';
import { useStore } from '@/lib/store';
import type { EmployeeRow } from '@/lib/types';

const PAGE_SIZE = 50;

export default function EmployeesPage() {
  const router = useRouter();
  const rights = useHrRights();
  const { storeId } = useStore();
  const [text, setText] = useState('');
  const [departmentId, setDepartmentId] = useState('');
  const [status, setStatus] = useState('active');
  const [page, setPage] = useState(1);
  const search = useDebounced(text.trim());
  const filters = { search, departmentId, status, storeId };
  useEffect(() => setPage(1), [search, departmentId, status, storeId]);

  const query = useQuery({
    queryKey: ['hr-employees', filters, page],
    queryFn: ({ signal }) => api.employees({ ...filters, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });
  const departments = useQuery({ queryKey: ['hr-departments'], queryFn: () => api.hrDepartments(), staleTime: 5 * 60_000 });

  return (
    <div>
      <PageHeader title="Employees" aside={query.data && `${formatCount(query.data.items.length)} shown · ${formatCount(query.data.total)} employees`} />
      <Toolbar>
        <SearchBox value={text} onChange={setText} placeholder="Name, code, CNIC or phone" />
        <label className="sr-only" htmlFor="emp-dept">
          Department
        </label>
        <Select id="emp-dept" className="w-[220px]" value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
          <option value="">Every department</option>
          {(departments.data ?? []).map((d) => (
            <option key={d.id ?? d.name} value={d.id ?? ''}>
              {d.name}
            </option>
          ))}
        </Select>
        <label className="sr-only" htmlFor="emp-status">
          Status
        </label>
        <Select id="emp-status" className="w-[200px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="active">Working now</option>
          <option value="left">Left</option>
          <option value="all">Everyone</option>
        </Select>
        {rights.edit && (
          <LinkButton href="/hr/employees/edit/" variant="sun" icon={Plus} shortcut="Ctrl+N">
            New employee
          </LinkButton>
        )}
      </Toolbar>

      <DataTable<EmployeeRow>
        rows={query.data?.items}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(e) => e.id}
        onRowClick={(e) => router.push(`/hr/employees/edit/?id=${encodeURIComponent(e.id)}`)}
        caption="Employees"
        empty={
          <EmptyState title={search ? 'Nobody matches' : 'No employees yet'}>
            {rights.edit ? 'Add the people who work for the company with New employee.' : 'Employees added in HR show here.'}
          </EmptyState>
        }
        columns={[
          { key: 'code', header: 'Code', cell: (e) => <span className="font-semibold">{e.code}</span> },
          { key: 'name', header: 'Name', cell: (e) => e.fullName },
          { key: 'job', header: 'Designation', cell: (e) => e.designation ?? '', hideBelow: 'md' },
          { key: 'dept', header: 'Department', cell: (e) => e.department ?? '', hideBelow: 'lg' },
          { key: 'store', header: 'Store', cell: (e) => e.storeCode ?? 'Head office', hideBelow: 'lg' },
          { key: 'type', header: 'Type', cell: (e) => EMPLOYMENT_TYPES[e.employmentType] ?? e.employmentType, hideBelow: 'xl' },
          { key: 'joined', header: 'Joined', cell: (e) => formatDate(e.joiningDate), hideBelow: 'md' },
          { key: 'user', header: 'Till user', cell: (e) => e.userName ?? '', hideBelow: 'xl' },
          {
            key: 'status',
            header: 'Status',
            cell: (e) => (e.active ? <Chip tone="ok">Working</Chip> : <Chip>Left {e.leavingDate ? formatDate(e.leavingDate) : ''}</Chip>),
            hideBelow: 'sm',
          },
        ]}
        footer={query.data && <Pager page={page} pageSize={PAGE_SIZE} total={query.data.total} onPage={setPage} noun="employees" />}
      />
    </div>
  );
}
