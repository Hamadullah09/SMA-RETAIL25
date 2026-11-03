'use client';

import { useQuery } from '@tanstack/react-query';
import { ArrowLeft, Printer } from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense } from 'react';
import { Button, LinkButton } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Detail, Toolbar } from '@/components/ui/page';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui/states';
import { api } from '@/lib/api';
import { formatDate, formatHours, formatMoney, formatQuantity } from '@/lib/format';
import { formatDays, payslipSections } from '@/lib/hr';
import { useHotkey } from '@/lib/hotkeys';
import { useStore } from '@/lib/store';
import type { PayslipDetail } from '@/lib/types';

export default function PayslipPage() {
  return (
    <Suspense>
      <PayslipLoader />
    </Suspense>
  );
}

function PayslipLoader() {
  const id = useSearchParams().get('id') ?? '';
  const query = useQuery({ queryKey: ['hr-payslip', id], queryFn: () => api.payslip(id), enabled: !!id });
  if (!id) {
    return (
      <div className="card">
        <EmptyState title="No payslip chosen">Open one from a payroll run.</EmptyState>
      </div>
    );
  }
  if (query.error) {
    return (
      <div className="card">
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      </div>
    );
  }
  if (!query.data) return <Skeleton className="h-[60vh] w-full rounded-card" />;
  return <Payslip slip={query.data} />;
}

function Payslip({ slip }: { slip: PayslipDetail }) {
  const router = useRouter();
  const { currencySymbol: symbol } = useStore();
  const money = (n: number) => formatMoney(n, symbol);
  const back = `/hr/payroll/run/?id=${encodeURIComponent(slip.runId)}`;
  useHotkey('Esc', () => router.push(back));
  const row = slip.row;
  const sections = payslipSections(slip.lines);

  return (
    <div>
      <Toolbar>
        <LinkButton href={back} icon={ArrowLeft} shortcut="Esc">
          Payroll #{slip.runNumber}
        </LinkButton>
        <Button variant="sun" icon={Printer} shortcut="Ctrl+P" onClick={() => window.print()}>
          Print
        </Button>
      </Toolbar>

      <article className="payslip mx-auto max-w-[900px] space-y-4" aria-label="Payslip">
        <header className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="text-muted">{[slip.companyName, slip.storeName].filter(Boolean).join(' · ')}</p>
            <h1 className="page-title">Payslip, {slip.runTitle}</h1>
            <p className="text-muted">
              {formatDate(slip.periodStart)} to {formatDate(slip.periodEnd)} · payroll #{slip.runNumber} ({slip.runStatus})
            </p>
          </div>
          <div className="text-right">
            <p className="font-heading text-section">{row.name}</p>
            <p className="text-muted">
              {row.code}
              {row.designation ? ` · ${row.designation}` : ''}
              {row.department ? ` · ${row.department}` : ''}
            </p>
          </div>
        </header>

        <Card>
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Detail label="CNIC">{slip.cnic}</Detail>
            <Detail label="Paid by">{row.paymentMethod === 'Bank' ? `${slip.bankName ?? 'Bank'} ${slip.iban ?? ''}` : 'Cash'}</Detail>
            <Detail label="Monthly basic">{money(slip.monthlyBasic)}</Detail>
            <Detail label="Days employed">{formatQuantity(row.daysEmployed)}</Detail>
            <Detail label="Days worked">{formatDays(row.daysPresent)}</Detail>
            <Detail label="Paid leave">{formatDays(row.paidLeaveDays)}</Detail>
            <Detail label="Unpaid leave">{formatDays(row.unpaidLeaveDays)}</Detail>
            <Detail label="Absent">{formatDays(row.absentDays)}</Detail>
            <Detail label="Late">{`${row.lateDays} times`}</Detail>
            <Detail label="Overtime hours">{formatHours(row.overtimeHours)}</Detail>
          </dl>
        </Card>

        <div className="grid gap-4 md:grid-cols-2">
          {sections.map((section) => (
            <Card key={section.kind} title={section.title} className={section.kind === 'EmployerContribution' ? 'md:col-span-2' : undefined}>
              <table className="w-full">
                <tbody>
                  {section.lines.map((l, i) => (
                    <tr key={i} className="border-b border-line last:border-0">
                      <td className="py-1.5 pr-3">
                        {l.name}
                        {l.quantity !== null && l.rate !== null && l.code !== 'BASIC' && (
                          <span className="block text-note text-muted">
                            {formatQuantity(l.quantity)} × {money(l.rate)}
                          </span>
                        )}
                      </td>
                      <td className="num py-1.5 text-right tabular-nums">{money(l.amount)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <td className="pt-2 font-semibold">Total</td>
                    <td className="num pt-2 text-right font-semibold tabular-nums">{money(section.total)}</td>
                  </tr>
                </tfoot>
              </table>
            </Card>
          ))}
        </div>

        <Card dark>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="font-heading text-section">Net pay</p>
            <p className="font-heading text-value tabular-nums" data-testid="net-pay">
              {money(row.net)}
            </p>
          </div>
        </Card>

        <Card title="Income tax this tax year">
          <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <Detail label="Taxable before this payslip">{money(slip.taxableBefore)}</Detail>
            <Detail label="Tax deducted before">{money(slip.taxBefore)}</Detail>
            <Detail label="Taxable this payslip">{money(slip.taxable)}</Detail>
            <Detail label="The year's taxable income, projected">{money(slip.projectedAnnualTaxable)}</Detail>
            <Detail label="Tax on it for the year">{money(slip.projectedAnnualTax)}</Detail>
            <Detail label="Deducted now">{money(row.tax)}</Detail>
          </dl>
        </Card>
      </article>
    </div>
  );
}
