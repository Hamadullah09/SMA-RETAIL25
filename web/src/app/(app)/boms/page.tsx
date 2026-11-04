'use client';

import { useQuery } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { LinkButton } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/field';
import { Chip, PageHeader, SearchBox, Toolbar } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { formatCount, formatDate, formatQuantity } from '@/lib/format';
import { useDebounced } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { productionApi } from '@/lib/production-api';
import type { BomListRow } from '@/lib/production-types';
import { useSession } from '@/lib/session';

/** Every bill of materials, each version on its own line, the one in force marked. */
export default function BomsPage() {
  const router = useRouter();
  const { can, canChangeShared } = useSession();
  const [search, setSearch] = useState('');
  const [activeOnly, setActiveOnly] = useState(false);
  const text = useDebounced(search.trim());
  const query = useQuery({ queryKey: ['boms', text, activeOnly], queryFn: () => productionApi.boms({ search: text, active: activeOnly || undefined }) });
  const edit = can(P.productionPlan) && canChangeShared;
  return (
    <div>
      <PageHeader title="Bills of materials" aside={query.data && `${formatCount(query.data.length)} versions`} />
      <Toolbar>
        <SearchBox value={search} onChange={setSearch} placeholder="Product code or name" label="Find a bill" />
        <Checkbox label="Active only" checked={activeOnly} onChange={setActiveOnly} />
        {edit && (
          <LinkButton href="/boms/edit/" variant="sun" icon={Plus} shortcut="Ctrl+N">
            New bill
          </LinkButton>
        )}
        {can(P.productionPlan) && !canChangeShared && <span className="text-ink">Bills are shared by every store; a super administrator changes them.</span>}
      </Toolbar>
      <DataTable<BomListRow>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(b) => b.id}
        caption="Bills of materials"
        onRowClick={(b) => router.push(`/boms/edit/?id=${b.id}`)}
        empty={<EmptyState title="No bills of materials yet">A bill says what a product is made of and how it is worked.</EmptyState>}
        columns={[
          { key: 'code', header: 'Product', cell: (b) => <span className="font-semibold">{b.productCode}</span> },
          { key: 'name', header: 'Name', cell: (b) => b.productName },
          { key: 'rev', header: 'Version', numeric: true, cell: (b) => b.revision },
          {
            key: 'state',
            header: 'State',
            cell: (b) => (b.inForce ? <Chip tone="ok">In force</Chip> : b.isActive ? <Chip tone="warn">Active, not in force</Chip> : <Chip>Inactive</Chip>),
          },
          { key: 'yield', header: 'Makes', numeric: true, cell: (b) => formatQuantity(b.yieldQuantity), hideBelow: 'sm' },
          { key: 'lines', header: 'Components', numeric: true, cell: (b) => b.lines, hideBelow: 'md' },
          { key: 'ops', header: 'Operations', numeric: true, cell: (b) => b.operations, hideBelow: 'md' },
          { key: 'from', header: 'From', cell: (b) => (b.effectiveFrom ? formatDate(b.effectiveFrom) : ''), hideBelow: 'lg' },
          { key: 'to', header: 'To', cell: (b) => (b.effectiveTo ? formatDate(b.effectiveTo) : ''), hideBelow: 'lg' },
        ]}
      />
    </div>
  );
}
