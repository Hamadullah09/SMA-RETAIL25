'use client';

/**
 * A store's RFID hub keys, on the Stores page: a hub that reads the store's readers from the server signs in with one.
 * The key is shown once, when it is made; afterwards only its first characters are known.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, Copy, KeyRound, ShieldAlert } from 'lucide-react';
import { useState } from 'react';
import { api } from '@/lib/api';
import { formatAgo, formatUtc } from '@/lib/format';
import { useNow } from '@/lib/hooks';
import { P } from '@/lib/nav';
import { checkHubName, hubCommand, hubSettings, keyHint } from '@/lib/rfid';
import { useSession } from '@/lib/session';
import type { NewRfidHubResponse, RfidHubInfo, StoreInfo } from '@/lib/types';
import { Button } from './ui/button';
import { ConfirmDialog, Dialog } from './ui/dialog';
import { Field, Input } from './ui/field';
import { Chip } from './ui/page';
import { FormError } from './ui/states';
import { DataTable } from './ui/table';
import { useToast } from './ui/toast';

export function RfidHubs({ store }: { store: StoreInfo }) {
  const { can } = useSession();
  const manage = can(P.storesManage);
  const now = useNow(60_000);
  const toast = useToast();
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [made, setMade] = useState<NewRfidHubResponse | null>(null);
  const [revoking, setRevoking] = useState<RfidHubInfo | null>(null);
  const hubs = useQuery({ queryKey: ['rfid-hubs', store.id], queryFn: () => api.rfidHubs(store.id) });

  const revoke = useMutation({
    mutationFn: (hub: RfidHubInfo) => api.revokeRfidHub(hub.id),
    onSuccess: (_, hub) => {
      toast.ok(`The key of ${hub.name} is revoked. That hub can no longer read the readers.`);
      setRevoking(null);
      void queryClient.invalidateQueries({ queryKey: ['rfid-hubs', store.id] });
    },
    onError: (e: Error) => {
      setRevoking(null);
      toast.error(e.message);
    },
  });

  return (
    <div className="border-t border-line" aria-label={`RFID hubs of ${store.name}`} role="region">
      <div className="flex flex-wrap items-center justify-between gap-3 px-5 pb-2 pt-4">
        <div>
          <h3 className="section-title my-0">RFID hubs</h3>
          <p className="text-muted">A hub that reads this store&apos;s readers from the server signs in with a key of its own.</p>
        </div>
        {manage && (
          <Button icon={KeyRound} onClick={() => setAdding(true)}>
            Add a hub key
          </Button>
        )}
      </div>
      <DataTable<RfidHubInfo>
        className="rounded-t-none shadow-none"
        rows={hubs.data}
        loading={hubs.isLoading}
        error={hubs.error}
        onRetry={() => void hubs.refetch()}
        rowKey={(h) => h.id}
        caption={`RFID hubs of ${store.name}`}
        empty={<p className="px-5 pb-5 text-muted">No hub keys yet.{manage ? ' Add one for each PC that runs an RFID hub.' : ''}</p>}
        columns={[
          { key: 'name', header: 'Hub', cell: (h) => <span className="font-semibold">{h.name}</span> },
          { key: 'hint', header: 'Key', cell: (h) => <span className="font-mono">{keyHint(h.keyHint)}</span> },
          {
            key: 'made',
            header: 'Made by',
            cell: (h) => (
              <span title={formatUtc(h.createdAtUtc)}>
                {h.createdBy ?? ''}
                {now ? ` · ${formatAgo(h.createdAtUtc, now)}` : ''}
              </span>
            ),
            hideBelow: 'md',
          },
          {
            key: 'seen',
            header: 'Last seen',
            cell: (h) => (now ? `${formatAgo(h.lastSeenAtUtc, now)}${h.lastAddress ? ` · ${h.lastAddress}` : ''}` : ''),
            hideBelow: 'sm',
          },
          {
            key: 'state',
            header: 'State',
            cell: (h) =>
              h.revoked ? (
                <span title={h.revokedAtUtc ? `Revoked ${formatUtc(h.revokedAtUtc)}` : undefined}>
                  <Chip>Revoked</Chip>
                </span>
              ) : (
                <Chip tone="ok">In use</Chip>
              ),
          },
          ...(manage
            ? [
                {
                  key: 'revoke',
                  header: '',
                  cell: (h: RfidHubInfo) =>
                    h.revoked ? null : (
                      <Button icon={Ban} variant="danger" className="min-h-[40px] px-4" onClick={() => setRevoking(h)} aria-label={`Revoke the key of ${h.name}`}>
                        Revoke
                      </Button>
                    ),
                  className: 'text-right',
                },
              ]
            : []),
        ]}
      />

      {adding && (
        <AddHubDialog
          store={store}
          onClose={() => setAdding(false)}
          onMade={(response) => {
            setAdding(false);
            setMade(response);
            void queryClient.invalidateQueries({ queryKey: ['rfid-hubs', store.id] });
          }}
        />
      )}
      <HubKeyDialog made={made} onClose={() => setMade(null)} />
      <ConfirmDialog
        open={!!revoking}
        title={`Revoke the key of ${revoking?.name ?? ''}?`}
        message={`The hub with key ${revoking ? keyHint(revoking.keyHint) : ''} is refused from now on and keeps only the readers it last read. Make a new key to set it up again.`}
        confirmLabel="Revoke"
        cancelLabel="Keep it"
        danger
        busy={revoke.isPending}
        onConfirm={() => revoking && revoke.mutate(revoking)}
        onClose={() => setRevoking(null)}
      />
    </div>
  );
}

function AddHubDialog({ store, onClose, onMade }: { store: StoreInfo; onClose(): void; onMade(response: NewRfidHubResponse): void }) {
  const [name, setName] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const add = useMutation({ mutationFn: () => api.addRfidHub(store.id, name.trim()), onSuccess: onMade });
  return (
    <Dialog
      open
      title="Add a hub key"
      onClose={onClose}
      onSubmit={() => {
        const text = !name.trim() ? 'Say where the hub runs, for example Back office PC.' : checkHubName(name) ?? null;
        setProblem(text);
        if (!text) add.mutate();
      }}
      footer={
        <>
          <Button type="submit" variant="sun" icon={KeyRound} shortcut="Enter" busy={add.isPending}>
            Make the key
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      <p className="mb-4 text-ink">
        One key for each PC that runs an RFID hub for {store.code} · {store.name}, so one can be revoked without the others.
      </p>
      <Field label="Hub name" htmlFor="hub-name" hint="Where the hub runs, at most 40 characters.">
        <Input id="hub-name" value={name} maxLength={40} placeholder="Back office PC" onChange={(e) => setName(e.target.value)} />
      </Field>
      <FormError error={problem ?? add.error} />
    </Dialog>
  );
}

/** The new key, shown this once, with how to give it to the hub. */
function HubKeyDialog({ made, onClose }: { made: NewRfidHubResponse | null; onClose(): void }) {
  const toast = useToast();
  if (!made) return null;
  const origin = typeof window === 'undefined' ? '' : window.location.origin;
  const copy = (text: string, what: string) =>
    void navigator.clipboard?.writeText(text).then(
      () => toast.ok(`The ${what} is copied.`),
      () => toast.error(`The browser did not let the ${what} be copied.`),
    );
  return (
    <Dialog
      open
      width="lg"
      title={`Key for ${made.hub.name}`}
      onClose={onClose}
      footer={
        <>
          <Button icon={Copy} onClick={() => copy(made.key, 'key')}>
            Copy the key
          </Button>
          <Button variant="sun" onClick={onClose} shortcut="Esc" data-autofocus>
            Done
          </Button>
        </>
      }
    >
      <p className="text-ink">Give this key to the hub:</p>
      <p
        className="my-4 select-all break-all rounded-card bg-card px-4 py-5 text-center font-mono text-[22px] font-bold leading-8 tracking-wide text-ink shadow-card sm:text-[26px]"
        data-testid="hub-key"
      >
        {made.key}
      </p>
      <p className="mb-2 text-ink">On the hub PC, start the hub with the server and the key:</p>
      <pre className="overflow-x-auto whitespace-pre-wrap break-all rounded-control border border-line bg-cream px-3 py-2 font-mono text-note text-ink" data-testid="hub-command">
        {hubCommand(origin, made.key)}
      </pre>
      <p className="mb-2 mt-3 text-ink">
        or keep the key off the command line in an <code className="font-mono">appsettings.json</code> next to the hub:
      </p>
      <pre className="overflow-x-auto whitespace-pre-wrap break-all rounded-control border border-line bg-cream px-3 py-2 font-mono text-note text-ink">{hubSettings(origin, made.key)}</pre>
      <p className="mt-4 flex items-start gap-2 font-semibold text-ink">
        <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0" />
        The key is not shown again: the server keeps only a fingerprint of it. If it is lost, revoke it and make a new one.
      </p>
    </Dialog>
  );
}
