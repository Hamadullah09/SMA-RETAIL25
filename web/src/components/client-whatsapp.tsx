'use client';

/**
 * A client's WhatsApp messages in the client editor, and "Send on WhatsApp": one message from a template, only to a
 * client who agreed and has a number.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MessageCircle, Send } from 'lucide-react';
import { useState } from 'react';
import { Button } from './ui/button';
import { Dialog } from './ui/dialog';
import { Field, Select } from './ui/field';
import { Chip } from './ui/page';
import { ErrorState, FormError, Skeleton } from './ui/states';
import { useToast } from './ui/toast';
import { api } from '@/lib/api';
import { formatShortUtc } from '@/lib/format';
import { P } from '@/lib/nav';
import { useSession } from '@/lib/session';
import { useStore } from '@/lib/store';
import type { ClientDetail } from '@/lib/types';
import { MESSAGE_STATUS, previewBody } from '@/lib/whatsapp';

export function ClientWhatsApp({ client }: { client: ClientDetail }) {
  const { can } = useSession();
  const [sending, setSending] = useState(false);
  const read = can(P.crmView);
  const send = can(P.crmEdit);
  const messages = useQuery({ queryKey: ['client-whatsapp', client.id], queryFn: () => api.clientMessages(client.id!), enabled: read && !!client.id });
  if (!read && !send) return null;
  const consent = !!client.whatsAppOptIn && !!client.whatsAppNumber;

  return (
    <section className="mt-5 border-t border-line pt-4" aria-labelledby="client-whatsapp-title">
      <div className="mb-2 flex flex-wrap items-center justify-between gap-3">
        <h3 id="client-whatsapp-title" className="flex items-center gap-2 font-heading text-section">
          <MessageCircle className="h-5 w-5" aria-hidden="true" />
          WhatsApp messages
        </h3>
        {send && (
          <Button icon={Send} disabled={!consent} onClick={() => setSending(true)} title={consent ? undefined : 'Only to a client who agreed and has a number.'}>
            Send on WhatsApp
          </Button>
        )}
      </div>
      {!consent && <p className="mb-2 text-muted">{client.whatsAppNumber ? 'This client has not agreed to WhatsApp messages.' : 'No WhatsApp number yet.'}</p>}
      {read &&
        (messages.error ? (
          <ErrorState error={messages.error} onRetry={() => void messages.refetch()} className="py-4" />
        ) : !messages.data ? (
          <Skeleton className="h-12" />
        ) : messages.data.length === 0 ? (
          <p className="text-muted">No messages sent yet.</p>
        ) : (
          <ul className="max-h-[220px] divide-y divide-line overflow-y-auto" aria-label="Messages sent">
            {messages.data.map((m) => (
              <li key={m.id} className="flex items-start gap-3 py-2">
                <Chip tone={MESSAGE_STATUS[m.status]?.tone ?? 'plain'}>{MESSAGE_STATUS[m.status]?.label ?? m.status}</Chip>
                <span className="min-w-0 flex-1">
                  <span className="block truncate">{m.body}</span>
                  <span className="block text-note text-muted">
                    {m.template} · {formatShortUtc(m.sentAtUtc ?? m.createdAtUtc)}
                    {m.error ? ` · ${m.error}` : ''}
                  </span>
                </span>
              </li>
            ))}
          </ul>
        ))}
      {sending && <SendDialog client={client} onClose={() => setSending(false)} />}
    </section>
  );
}

function SendDialog({ client, onClose }: { client: ClientDetail; onClose(): void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { defaultStoreId } = useStore();
  const templates = useQuery({ queryKey: ['templates'], queryFn: () => api.templates() });
  const active = (templates.data ?? []).filter((t) => t.isActive);
  const [templateId, setTemplateId] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const chosen = active.find((t) => t.id === templateId);
  const name = [client.firstName, client.lastName].filter(Boolean).join(' ') || client.company || 'the client';
  const send = useMutation({
    mutationFn: () => api.sendWhatsApp(client.id!, { templateId, storeId: defaultStoreId }),
    onSuccess: (m) => {
      toast.ok(m.status === 'Error' ? `Not sent: ${m.error ?? 'WhatsApp refused it.'}` : `Sent to ${m.toNumber}.`);
      void queryClient.invalidateQueries({ queryKey: ['client-whatsapp', client.id] });
      onClose();
    },
  });
  const samples = { FirstName: client.firstName ?? '', LastName: client.lastName ?? '', Name: name, Points: String(client.points), Balance: client.balance.toFixed(2) };

  return (
    <Dialog
      open
      title={`WhatsApp to ${name}`}
      onClose={onClose}
      onSubmit={() => {
        if (!templateId) return setProblem('Choose the template.');
        setProblem(null);
        send.mutate();
      }}
      footer={
        <>
          <Button type="submit" variant="sun" icon={Send} shortcut="Enter" busy={send.isPending}>
            Send
          </Button>
          <Button onClick={onClose} shortcut="Esc">
            Cancel
          </Button>
        </>
      }
    >
      {templates.error ? (
        <ErrorState error={templates.error} onRetry={() => void templates.refetch()} />
      ) : !templates.data ? (
        <Skeleton className="h-24" />
      ) : (
        <div className="space-y-3">
          <p>
            To <strong>{client.whatsAppNumber}</strong>. WhatsApp takes only messages from approved templates.
          </p>
          <Field label="Template" htmlFor="send-template">
            <Select id="send-template" value={templateId} onChange={(e) => setTemplateId(e.target.value)}>
              <option value="">{active.length ? 'Choose…' : 'No template is on'}</option>
              {active.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name} ({t.language})
                </option>
              ))}
            </Select>
          </Field>
          {chosen && (
            <p className="whitespace-pre-wrap rounded-card bg-ok-light px-4 py-3 text-ink" data-testid="send-preview">
              {previewBody(chosen.body, samples)}
            </p>
          )}
        </div>
      )}
      <FormError error={problem ?? send.error} />
    </Dialog>
  );
}
