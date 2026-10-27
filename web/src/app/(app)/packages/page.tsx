'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { FileArchive, FolderOpen, RefreshCw, UploadCloud } from 'lucide-react';
import { useRef, useState, type DragEvent } from 'react';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/field';
import { Chip, PageHeader, Toolbar, type ChipTone } from '@/components/ui/page';
import { EmptyState } from '@/components/ui/states';
import { DataTable } from '@/components/ui/table';
import { useToast } from '@/components/ui/toast';
import { api, ApiError } from '@/lib/api';
import { describeUpload } from '@/lib/packages';
import { cx } from '@/lib/cx';
import { formatCount, formatShortUtc } from '@/lib/format';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { BatchInfo, SyncBatchStatus } from '@/lib/types';

const STATUS: Record<SyncBatchStatus, { label: string; tone: ChipTone }> = {
  Applied: { label: 'Applied', tone: 'ok' },
  Duplicate: { label: 'Sent before', tone: 'plain' },
  Failed: { label: 'Failed', tone: 'danger' },
  Waiting: { label: 'Waiting', tone: 'warn' },
  Sent: { label: 'Sent', tone: 'ok' },
};

type Upload = { name: string; progress: number; state: 'sending' | 'done' | 'failed'; message?: string; notes?: string[] };

export default function PackagesPage() {
  const { can } = useSession();
  const { storeId, stores } = useStore();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<string>('');
  const [upload, setUpload] = useState<Upload | null>(null);
  const [dragging, setDragging] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const mayUpload = can(P.syncSend);

  const query = useQuery({
    queryKey: ['batches', storeId, status],
    queryFn: () => api.batches({ storeId, status, take: 200 }),
    refetchInterval: 30_000,
  });

  const storeCode = (id: string | null) => stores.find((s) => s.id === id)?.code ?? '';

  async function send(file: File) {
    if (!file.name.toLowerCase().endsWith('.smapkg')) {
      setUpload({ name: file.name, progress: 0, state: 'failed', message: 'Choose a .smapkg file: the package a till saves for the server.' });
      return;
    }
    setUpload({ name: file.name, progress: 0, state: 'sending' });
    try {
      const result = await api.uploadPackage(file, (progress) => setUpload((u) => (u ? { ...u, progress } : u)));
      const { failed, message } = describeUpload(result);
      setUpload({ name: file.name, progress: 1, state: failed ? 'failed' : 'done', message, notes: result?.notes ?? [] });
      if (failed) toast.error(message);
      else toast.ok(message);
    } catch (e) {
      const message = e instanceof ApiError ? e.message : 'The upload did not finish.';
      setUpload({ name: file.name, progress: 0, state: 'failed', message });
    } finally {
      void queryClient.invalidateQueries({ queryKey: ['batches'] });
      void queryClient.invalidateQueries({ queryKey: ['tills'] });
      if (fileInput.current) fileInput.current.value = '';
    }
  }

  function onDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setDragging(false);
    const file = e.dataTransfer.files?.[0];
    if (file && mayUpload && upload?.state !== 'sending') void send(file);
  }

  return (
    <div>
      <PageHeader title="Packages" aside="Data a till saved to a USB stick, sent from here" />

      {mayUpload ? (
        <div
          className={cx(
            'card mb-5 flex flex-col items-center gap-4 border-2 border-dashed px-6 py-8 text-center transition-colors sm:flex-row sm:text-left',
            dragging ? 'border-sun-dark bg-sun-light' : 'border-line',
          )}
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
          data-testid="drop-zone"
        >
          <span className="grid h-16 w-16 shrink-0 place-items-center rounded-full bg-sun-light">
            <UploadCloud className="h-8 w-8 text-ink" strokeWidth={1.6} />
          </span>
          <div className="min-w-0 flex-1">
            <p className="font-heading text-section">Drop a .smapkg file here</p>
            <p className="text-muted">
              When a till cannot reach the server, End of Day saves its package to a USB stick. Send it from here; the server checks it with the till&apos;s key, and sending it twice does no harm.
            </p>
            {upload && (
              <div className="mt-4" aria-live="polite">
                <div className="flex items-center gap-2">
                  <FileArchive className="h-5 w-5 shrink-0 text-muted" />
                  <span className="truncate font-semibold">{upload.name}</span>
                  <span className="ml-auto text-muted">{upload.state === 'sending' ? `${Math.round(upload.progress * 100)}%` : ''}</span>
                </div>
                <div
                  className="mt-2 h-3 overflow-hidden rounded-full bg-stone-light"
                  role="progressbar"
                  aria-label="Upload"
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={Math.round(upload.progress * 100)}
                >
                  <div
                    className={cx('h-full rounded-full transition-[width]', upload.state === 'failed' ? 'bg-danger' : upload.state === 'done' ? 'bg-ok' : 'bg-sun-dark')}
                    style={{ width: `${upload.state === 'failed' ? 100 : Math.max(4, upload.progress * 100)}%` }}
                  />
                </div>
                {upload.message && (
                  <p className={cx('mt-2 font-semibold', upload.state === 'failed' ? 'text-danger' : 'text-ok')} role={upload.state === 'failed' ? 'alert' : 'status'}>
                    {upload.message}
                  </p>
                )}
                {upload.notes && upload.notes.length > 0 && (
                  <ul className="mt-1 list-disc pl-5 text-ink">
                    {upload.notes.map((note, i) => (
                      <li key={i}>{note}</li>
                    ))}
                  </ul>
                )}
              </div>
            )}
          </div>
          <input
            ref={fileInput}
            type="file"
            accept=".smapkg"
            className="sr-only"
            aria-label="Package file"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) void send(file);
            }}
          />
          <Button variant="sun" icon={FolderOpen} shortcut="Ctrl+O" onClick={() => fileInput.current?.click()} busy={upload?.state === 'sending'}>
            Choose file
          </Button>
        </div>
      ) : (
        <p className="mb-4 text-ink">Your role can see the packages but not send one (it needs “Send this till’s data to the server”).</p>
      )}

      <Toolbar>
        <h2 className="mr-auto font-heading text-section">History</h2>
        <label className="sr-only" htmlFor="batch-status">
          Status
        </label>
        <Select id="batch-status" className="w-[200px]" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Every status</option>
          {Object.entries(STATUS).map(([key, s]) => (
            <option key={key} value={key}>
              {s.label}
            </option>
          ))}
        </Select>
        <Button icon={RefreshCw} shortcut="F5" onClick={() => void query.refetch()}>
          Refresh
        </Button>
      </Toolbar>

      <DataTable<BatchInfo>
        rows={query.data}
        loading={query.isLoading}
        error={query.error}
        onRetry={() => void query.refetch()}
        rowKey={(b) => b.id}
        empty={<EmptyState title="No packages yet">Packages appear here as tills send them, by themselves or from here.</EmptyState>}
        columns={[
          { key: 'at', header: 'Received', cell: (b) => formatShortUtc(b.receivedAtUtc) },
          { key: 'store', header: 'Store', cell: (b) => storeCode(b.storeId), hideBelow: 'md' },
          { key: 'till', header: 'Till', cell: (b) => String(b.stationNumber).padStart(3, '0') },
          { key: 'file', header: 'File', cell: (b) => <span className="break-all">{b.fileName ?? (b.direction === 'Download' ? 'Download' : '')}</span>, hideBelow: 'lg' },
          { key: 'rows', header: 'Rows', numeric: true, cell: (b) => formatCount(b.rows), hideBelow: 'sm' },
          { key: 'status', header: 'Status', cell: (b) => <Chip tone={STATUS[b.status]?.tone ?? 'plain'}>{STATUS[b.status]?.label ?? b.status}</Chip> },
          { key: 'error', header: 'Problem', cell: (b) => (b.error ? <span className="text-danger">{b.error}</span> : ''), className: 'wrap min-w-[220px]' },
        ]}
      />
    </div>
  );
}
