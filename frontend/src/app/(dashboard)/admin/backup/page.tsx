'use client';

import { useCallback, useEffect, useState, type ReactNode } from 'react';
import {
  AlertTriangle,
  Database,
  DatabaseBackup,
  Download,
  HardDriveDownload,
  Lock,
  RotateCcw,
  ShieldAlert,
  type LucideIcon,
} from 'lucide-react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { apiClient } from '@/lib/api-client';
import { cn } from '@/lib/utils';
import { PageHeader as SharedPageHeader } from '@/components/shell/page-header';

type BackupFile = {
  fileName: string;
  sizeBytes: number;
  createdAt: string;
};

const thText = 'px-3 py-2 text-left text-label font-medium text-ink-muted';
const thNum = 'px-3 py-2 text-right text-label font-medium text-ink-muted';
const td = 'px-3 py-2 align-middle';

function formatSize(bytes: number): string {
  if (bytes >= 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

function describeError(error: unknown): string {
  const err = error as { response?: { data?: { detail?: string; title?: string } } };
  return err.response?.data?.detail ?? err.response?.data?.title ?? 'Something went wrong.';
}

/**
 * Whole-database backup and restore.
 *
 * The legacy system's nightly ritual, without the query window. Restore is deliberately loud: it
 * replaces every table at once and drops every signed-in session, so the page says exactly that
 * before it lets anyone click it.
 */
export default function BackupPage() {
  const auth = useAuth();
  const allowed = auth.can('system.backup');

  const [files, setFiles] = useState<BackupFile[]>([]);
  const [loading, setLoading] = useState(false);
  const [working, setWorking] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);

    try {
      const response = await apiClient.get<BackupFile[]>('/maintenance/backups');
      setFiles(response.data);
    } catch (error) {
      toast({ title: 'Could not list backups', description: describeError(error), variant: 'destructive' });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (allowed) void load();
  }, [allowed, load]);

  const backupNow = async () => {
    setWorking(true);

    try {
      const response = await apiClient.post<BackupFile>('/maintenance/backups');
      toast({
        title: 'Backup taken',
        description: `${response.data.fileName} (${formatSize(response.data.sizeBytes)})`,
      });
      await load();
    } catch (error) {
      toast({ title: 'Backup failed', description: describeError(error), variant: 'destructive' });
    } finally {
      setWorking(false);
    }
  };

  const restore = async (file: BackupFile) => {
    const confirmed = window.confirm(
      `Restore ${file.fileName}?\n\n` +
        'This replaces EVERYTHING in the database with the contents of this backup. ' +
        'Every sale, item and change made since it was taken will be gone, and every ' +
        'signed-in user (including you) will be thrown out while it runs.',
    );

    if (!confirmed) return;

    setWorking(true);

    try {
      await apiClient.post('/maintenance/backups/restore', { fileName: file.fileName });
      toast({
        title: 'Restore complete',
        description: 'The database was replaced. Sign in again if the session was dropped.',
      });
    } catch (error) {
      toast({ title: 'Restore failed', description: describeError(error), variant: 'destructive' });
    } finally {
      setWorking(false);
    }
  };

  if (!allowed) {
    return (
      <div className="p-4 lg:p-6">
        <PageHeader title="Backup and restore" lede="A backup is the whole database in one file." />
        <section className="pos-panel mt-4">
          <EmptyState
            icon={Lock}
            title="You do not have permission to manage backups"
            hint="Backing up and restoring the database needs the system.backup permission. Ask an administrator to grant it on your role."
          />
        </section>
      </div>
    );
  }

  return (
    <div className="space-y-4 p-4 lg:p-6">
      <PageHeader
        title="Backup and restore"
        lede="A backup is the whole database in one file — every item, sale, customer and setting. Take one before anything risky, and on a schedule that matches how much work you can bear to lose."
      >
        <button type="button" className="pos-button" onClick={() => void load()} disabled={loading || working}>
          <RotateCcw className="h-3.5 w-3.5" aria-hidden />
          Refresh
        </button>
        <button type="button" className="pos-button-primary" onClick={() => void backupNow()} disabled={working}>
          <DatabaseBackup className="h-3.5 w-3.5" aria-hidden />
          {working ? 'Working…' : 'Back up now'}
        </button>
      </PageHeader>

      <Panel
        title="Backups on this server"
        icon={Database}
        action={loading ? 'Loading…' : `${files.length} file${files.length === 1 ? '' : 's'}`}
      >
        {/* The consequence is stated once, in words, above the buttons that carry it — not only in
            the confirm() that appears after the click. */}
        <div className="flex items-start gap-2.5 border-b border-subtle bg-negative/5 px-4 py-3">
          <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-negative" aria-hidden />
          <div className="min-w-0">
            <p className="text-body font-semibold text-negative">Restoring replaces the entire database</p>
            <p className="mt-0.5 max-w-[72ch] text-body text-ink-muted">
              Every sale, item and change made since the backup was taken is gone, and every signed-in
              user — including you — is thrown out while it runs. There is no undo. Take a fresh backup
              first if there is anything since the one you are about to restore.
            </p>
          </div>
        </div>

        {files.length === 0 ? (
          <EmptyState
            icon={HardDriveDownload}
            title={loading ? 'Loading…' : 'No backups yet'}
            hint={
              loading
                ? 'Reading the server’s backup folder.'
                : 'Press “Back up now” to write the first one. It takes a copy of the whole database into the server’s backup folder.'
            }
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-body">
              <thead className="border-b border-subtle bg-panel-sunken">
                <tr>
                  <th scope="col" className={thText}>File</th>
                  <th scope="col" className={thText}>Taken</th>
                  <th scope="col" className={thNum}>Size</th>
                  <th scope="col" className={thNum}>
                    <span className="sr-only">Action</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {files.map((file) => (
                  <tr
                    key={file.fileName}
                    className="border-b border-subtle transition-colors last:border-0 hover:bg-panel-hover"
                  >
                    <td className={cn(td, 'font-mono text-ink')}>{file.fileName}</td>
                    <td className={cn(td, 'tabular-nums text-ink-muted')}>
                      {new Date(file.createdAt).toLocaleString()}
                    </td>
                    <td className={cn(td, 'text-right tabular-nums')} data-numeric="">
                      {formatSize(file.sizeBytes)}
                    </td>
                    <td className={cn(td, 'text-right')}>
                      <div className="flex items-center justify-end gap-2">
                        {/*
                          A plain link, not a fetch. The browser streams it straight to disk, so a
                          year of sales never has to be held in the page — and this is the step that
                          makes the rest of it a backup: a copy sitting on the same machine as the
                          database it protects is one power supply away from being nothing.
                        */}
                        <a
                          className="pos-button"
                          href={`/api/proxy/maintenance/backups/${encodeURIComponent(file.fileName)}`}
                          download={file.fileName}
                          title={`Save ${file.fileName} to this computer`}
                        >
                          <Download className="h-3.5 w-3.5" aria-hidden />
                          Download
                        </a>

                        <button
                          type="button"
                          className="pos-button-danger"
                          onClick={() => void restore(file)}
                          disabled={working}
                          title={`Replace the whole database with ${file.fileName}`}
                        >
                          <AlertTriangle className="h-3.5 w-3.5" aria-hidden />
                          Restore over everything
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <p className="border-t border-subtle px-4 py-3 text-body text-ink-muted">
          Files live in the server&apos;s backup folder. Copy them somewhere that is not this machine —
          a backup on the disk that fails with the database is not a backup.
        </p>
      </Panel>
    </div>
  );
}

/* ------------------------------------------------------------------ page furniture */

/**
 * Delegates to the shared header.
 *
 * This was copy-pasted verbatim into six admin screens, and had already drifted: year-end
 * aligned its actions to the bottom while the other five centred them. Kept behind the local
 * name so the call sites in this file do not change.
 */
function PageHeader({ title, lede, children }: { title: string; lede: string; children?: ReactNode }) {
  return <SharedPageHeader title={title} description={lede} actions={children} />;
}

function Panel({
  title,
  icon: Icon,
  action,
  children,
}: {
  title: string;
  icon?: LucideIcon;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="pos-panel overflow-hidden">
      <header className="pos-panel-header">
        <span className="pos-panel-title">
          {Icon ? <Icon /> : null}
          <span className="truncate">{title}</span>
        </span>
        {action ? <span className="pos-panel-header-action">{action}</span> : null}
      </header>
      {children}
    </section>
  );
}

function EmptyState({ icon: Icon, title, hint }: { icon: LucideIcon; title: string; hint: string }) {
  return (
    <div className="flex flex-col items-center gap-1.5 px-4 py-12 text-center">
      <Icon className="mb-1 h-6 w-6 text-ink-faint" aria-hidden />
      <p className="text-body-lg font-medium text-ink">{title}</p>
      <p className="max-w-[52ch] text-body text-ink-muted">{hint}</p>
    </div>
  );
}
