'use client';

/*
 * Plain <img>, not next/image: the preview is a blob: URL the optimiser cannot fetch, and the stored
 * picture is behind the BFF proxy's session cookie, which a server-side fetch does not carry.
 */
/* eslint-disable @next/next/no-img-element */

import { useEffect, useRef, useState } from 'react';
import { apiClient } from '@/lib/api-client';
import { toast } from '@/components/ui/toaster';
import { FormSection } from '@/components/masters/browse-form';

/**
 * The item's picture, for the till's product grid.
 *
 * Uploading is deliberately the only thing this does — no cropping, no rotation, no gallery. A shop
 * photographing its catalogue does it once with a phone, and every control added here is one more
 * thing to get wrong on the day the pictures are being loaded in bulk.
 *
 * The file is validated on the server against its own magic number, not its declared type, so this
 * side checks only what it can check instantly: whether the size is worth sending at all.
 */

const MAXIMUM_BYTES = 2 * 1024 * 1024;
const ACCEPTED = 'image/png,image/jpeg,image/webp';

export function ProductImageField({
  productId,
  hasImage,
  disabled,
  onChanged,
}: {
  productId: number;
  hasImage: boolean;
  disabled: boolean;
  onChanged: (hasImage: boolean) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);

  /**
   * Bumped after every upload and appended to the image URL. The server's ETag would eventually get
   * a browser to the new picture, but "eventually" here means the clerk uploads a photo, sees the old
   * one, and uploads it again.
   */
  const [version, setVersion] = useState(0);

  const [preview, setPreview] = useState<string | null>(null);

  // The object URL is revoked when it is replaced or the panel closes; a preview per upload attempt
  // otherwise leaks the whole file for as long as the tab is open.
  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview); }, [preview]);

  const upload = async (file: File) => {
    if (file.size > MAXIMUM_BYTES) {
      toast({
        title: 'That picture is too large',
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
      const body = new FormData();
      body.append('file', file);

      // No explicit Content-Type: the browser has to set the multipart boundary itself, and naming
      // the type here overwrites it with one that has none.
      await apiClient.put(`/products/${productId}/image`, body);

      setVersion((v) => v + 1);
      onChanged(true);
      toast({ title: 'Picture saved' });
    } catch (error) {
      setPreview((previous) => {
        if (previous) URL.revokeObjectURL(previous);
        return null;
      });
      toast({
        title: 'Picture not saved',
        description: error instanceof Error ? error.message : 'The upload was refused.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
      if (inputRef.current) inputRef.current.value = '';
    }
  };

  const remove = async () => {
    setBusy(true);

    try {
      await apiClient.delete(`/products/${productId}/image`);
      setPreview((previous) => {
        if (previous) URL.revokeObjectURL(previous);
        return null;
      });
      setVersion((v) => v + 1);
      onChanged(false);
      toast({ title: 'Picture removed', description: 'The item shows as a coloured tile instead.' });
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

  const source = preview ?? (hasImage ? `/api/proxy/products/${productId}/image?v=${version}` : null);

  return (
    <FormSection
      title="Picture"
      hint="Shown on the till's product grid. Items without one appear as a coloured tile, and a catalogue with no pictures at all is listed row by row instead."
    >
      <div className="flex items-start gap-4">
        <div className="flex h-28 w-28 shrink-0 items-center justify-center overflow-hidden rounded border border-subtle bg-panel-sunken">
          {source ? (
            <img src={source} alt="" className="h-full w-full object-cover" />
          ) : (
            <span className="px-2 text-center text-caption text-ink-faint">No picture</span>
          )}
        </div>

        <div className="flex flex-col gap-2">
          <input
            ref={inputRef}
            type="file"
            accept={ACCEPTED}
            className="sr-only"
            disabled={disabled || busy}
            onChange={(event) => {
              const file = event.target.files?.[0];
              if (file) void upload(file);
            }}
          />

          <button
            type="button"
            className="pos-button"
            disabled={disabled || busy}
            onClick={() => inputRef.current?.click()}
          >
            {busy ? 'Uploading…' : hasImage ? 'Replace picture' : 'Add picture'}
          </button>

          {hasImage ? (
            <button
              type="button"
              className="pos-button"
              disabled={disabled || busy}
              onClick={() => void remove()}
            >
              Remove
            </button>
          ) : null}

          <p className="max-w-xs text-caption text-ink-faint">PNG, JPEG or WebP, up to 2 MB. Square crops best.</p>
        </div>
      </div>
    </FormSection>
  );
}
