'use client';

/*
 * Plain <img>, not next/image: the preview is a blob: URL the optimiser cannot fetch, and the stored
 * image is behind the BFF proxy's session cookie, which a server-side fetch does not carry.
 */
/* eslint-disable @next/next/no-img-element */

import { useEffect, useRef, useState } from 'react';
import { FormSection } from '@/components/masters/browse-form';
import { toast } from '@/components/ui/toaster';
import { useBranding } from '@/components/layout/branding';
import { brandingApi, brandingImageUrl, type BrandingSlot } from '@/lib/branding-api';

/**
 * The Branding tab: the two marks that make an installation belong to this shop.
 *
 * Both are uploaded here and nowhere else. That is what white-labelling means in practice — a
 * reseller opening a new customer changes these two images and ships no code, and nothing in the
 * bundle names any particular shop.
 *
 * The file is validated on the server against its own magic number rather than its declared type,
 * so this side checks only what it can check instantly: whether the size is worth sending at all.
 */

const MAXIMUM_BYTES = 2 * 1024 * 1024;
const ACCEPTED = 'image/png,image/jpeg,image/webp';

interface SlotCopy {
  slot: BrandingSlot;
  title: string;
  hint: string;
  /** A watermark is judged against a busy screen; a corner logo is judged against the header. */
  showsOpacity: boolean;
  preview: string;
}

const SLOTS: SlotCopy[] = [
  {
    slot: 'Watermark',
    title: 'Screen watermark',
    hint:
      'Sits in the centre of every screen, behind the working area. Faint on purpose — it is there to say the system is running, not to be read.',
    showsOpacity: true,
    preview: 'h-28 w-40',
  },
  {
    slot: 'CompanyLogo',
    title: 'Company logo',
    hint:
      'Appears in the corner of the header. A wide wordmark and a square badge both work; it is sized by height so nothing needs cropping to a template.',
    showsOpacity: false,
    preview: 'h-14 w-40',
  },
];

export function BrandingTab({ locationId, canWrite }: { locationId: number; canWrite: boolean }) {
  return (
    <div className="flex flex-col gap-6">
      {SLOTS.map((copy) => (
        <BrandingSlotField key={copy.slot} locationId={locationId} canWrite={canWrite} copy={copy} />
      ))}
    </div>
  );
}

function BrandingSlotField({
  locationId,
  canWrite,
  copy,
}: {
  locationId: number;
  canWrite: boolean;
  copy: SlotCopy;
}) {
  const { slot: read, reload } = useBranding();
  const state = read(copy.slot);

  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState<string | null>(null);

  // Local while the slider is being dragged, so the control does not lag behind the thumb waiting
  // for a round trip. Committed on release.
  const [opacity, setOpacity] = useState(state?.opacityPct ?? 100);

  useEffect(() => {
    if (state) setOpacity(state.opacityPct);
  }, [state?.opacityPct]); // eslint-disable-line react-hooks/exhaustive-deps

  // The object URL is revoked when it is replaced or the panel closes; a preview per upload attempt
  // otherwise leaks the whole file for as long as the tab is open.
  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);

  const upload = async (file: File) => {
    if (file.size > MAXIMUM_BYTES) {
      toast({
        title: 'That image is too large',
        description: `It is ${(file.size / 1024 / 1024).toFixed(1)} MB; the limit is 2 MB.`,
        variant: 'destructive',
      });
      return;
    }

    const local = URL.createObjectURL(file);
    setPreview((previous) => {
      if (previous) URL.revokeObjectURL(previous);
      return local;
    });

    setBusy(true);

    try {
      await brandingApi.upload(locationId, copy.slot, file);
      await reload();
      toast({ title: `${copy.title} saved`, description: 'It is on every screen from now on.' });
    } catch (error) {
      setPreview((previous) => {
        if (previous) URL.revokeObjectURL(previous);
        return null;
      });
      toast({
        title: 'Not saved',
        description: error instanceof Error ? error.message : 'The upload was refused.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
      if (inputRef.current) inputRef.current.value = '';
    }
  };

  const commitOpacity = async (value: number) => {
    setBusy(true);

    try {
      await brandingApi.setOpacity(locationId, copy.slot, value);
      await reload();
    } catch (error) {
      setOpacity(state?.opacityPct ?? 100);
      toast({
        title: 'Opacity not changed',
        description: error instanceof Error ? error.message : 'The request was refused.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setBusy(true);

    try {
      await brandingApi.remove(locationId, copy.slot);
      setPreview((previous) => {
        if (previous) URL.revokeObjectURL(previous);
        return null;
      });
      await reload();
      toast({ title: `${copy.title} removed` });
    } catch (error) {
      toast({
        title: 'Not removed',
        description: error instanceof Error ? error.message : 'The request was refused.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  };

  const stored = state ? brandingImageUrl(locationId, state) : null;
  const source = preview ?? stored;
  const present = state?.present ?? false;

  return (
    <FormSection title={copy.title} hint={copy.hint}>
      <div className="flex flex-wrap items-start gap-4">
        {/*
          Checkerboard behind the preview. Every one of these images is a logo on a transparent
          background, and on a plain panel a white mark is indistinguishable from an empty box.
        */}
        <div
          className={`flex shrink-0 items-center justify-center overflow-hidden rounded border border-subtle ${copy.preview}`}
          style={{
            backgroundImage:
              'linear-gradient(45deg, rgb(0 0 0 / 0.06) 25%, transparent 25%, transparent 75%, rgb(0 0 0 / 0.06) 75%), linear-gradient(45deg, rgb(0 0 0 / 0.06) 25%, transparent 25%, transparent 75%, rgb(0 0 0 / 0.06) 75%)',
            backgroundSize: '12px 12px',
            backgroundPosition: '0 0, 6px 6px',
          }}
        >
          {source ? (
            <img
              src={source}
              alt=""
              className="max-h-full max-w-full object-contain"
              style={{ opacity: opacity / 100 }}
            />
          ) : (
            <span className="px-2 text-center text-caption text-ink-faint">Nothing uploaded</span>
          )}
        </div>

        <div className="flex flex-col gap-2">
          <input
            ref={inputRef}
            type="file"
            accept={ACCEPTED}
            className="sr-only"
            disabled={!canWrite || busy}
            onChange={(event) => {
              const file = event.target.files?.[0];
              if (file) void upload(file);
            }}
          />

          <button
            type="button"
            className="pos-button"
            disabled={!canWrite || busy}
            onClick={() => inputRef.current?.click()}
          >
            {busy ? 'Working…' : present ? 'Replace image' : 'Upload image'}
          </button>

          {present ? (
            <button type="button" className="pos-button" disabled={!canWrite || busy} onClick={() => void remove()}>
              Remove
            </button>
          ) : null}

          <p className="max-w-xs text-caption text-ink-faint">
            PNG, JPEG or WebP, up to 2 MB. Transparent PNG looks best.
          </p>
        </div>

        {copy.showsOpacity && present ? (
          <div className="flex min-w-[220px] flex-col gap-1">
            <label htmlFor={`opacity-${copy.slot}`} className="text-caption font-medium text-ink-muted">
              Opacity — {opacity}%
            </label>

            <input
              id={`opacity-${copy.slot}`}
              type="range"
              min={0}
              max={100}
              step={5}
              value={opacity}
              disabled={!canWrite || busy}
              onChange={(event) => setOpacity(Number(event.target.value))}
              onPointerUp={() => void commitOpacity(opacity)}
              onKeyUp={() => void commitOpacity(opacity)}
              className="w-full accent-accent"
            />

            <p className="text-caption text-ink-faint">
              20% is the default and about as strong as a mark can be behind a screen people read all
              day. A pale logo may need more, a dark one less.
            </p>
          </div>
        ) : null}
      </div>
    </FormSection>
  );
}
