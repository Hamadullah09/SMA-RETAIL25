'use client';

import { useCallback, useEffect, useState } from 'react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { apiClient } from '@/lib/api-client';

type BackupFile = {
  fileName: string;
  sizeBytes: number;
  createdAt: string;
};

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
    return <p className="text-body text-ink-muted">You do not have permission to manage backups.</p>;
  }

  return (
    <div className="space-y-3">
      <h1 className="text-h3 font-semibold">Backup and restore</h1>

      <p className="max-w-2xl text-body text-ink-muted">
        A backup is the whole database in one file — every item, sale, customer and setting. Take one
        before anything risky, and on a schedule that matches how much work you can bear to lose.
      </p>

      <div className="flex items-center gap-2">
        <button type="button" className="pos-button-primary" onClick={() => void backupNow()} disabled={working}>
          {working ? 'Working…' : 'Back up now'}
        </button>
        <button type="button" className="pos-button" onClick={() => void load()} disabled={loading || working}>
          Refresh
        </button>
      </div>

      <div className="pos-panel overflow-x-auto">
        <table className="w-full text-body">
          <thead>
            <tr className="text-left text-ink-muted">
              <th className="p-2 font-medium">File</th>
              <th className="p-2 font-medium">Taken</th>
              <th className="p-2 font-medium">Size</th>
              <th className="p-2" />
            </tr>
          </thead>
          <tbody>
            {files.length === 0 && (
              <tr>
                <td className="p-2 text-ink-muted" colSpan={4}>
                  {loading ? 'Loading…' : 'No backups yet. The first one starts with the button above.'}
                </td>
              </tr>
            )}
            {files.map((file) => (
              <tr key={file.fileName} className="border-t border-subtle">
                <td className="p-2 font-mono">{file.fileName}</td>
                <td className="p-2">{new Date(file.createdAt).toLocaleString()}</td>
                <td className="p-2">{formatSize(file.sizeBytes)}</td>
                <td className="p-2 text-right">
                  <button
                    type="button"
                    className="pos-button-danger"
                    onClick={() => void restore(file)}
                    disabled={working}
                  >
                    Restore
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="max-w-2xl text-caption text-ink-faint">
        Files live in the server&apos;s backup folder. Copy them somewhere that is not this machine —
        a backup on the disk that fails with the database is not a backup.
      </p>
    </div>
  );
}
