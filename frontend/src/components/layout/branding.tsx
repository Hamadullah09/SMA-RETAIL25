'use client';

/*
 * Plain <img>, not next/image: these are behind the BFF proxy's session cookie, which the image
 * optimiser's server-side fetch does not carry.
 */
/* eslint-disable @next/next/no-img-element */

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useAuth } from '@/lib/auth-config';
import {
  brandingApi,
  brandingImageUrl,
  type Branding,
  type BrandingSlot,
  type BrandingSlotState,
} from '@/lib/branding-api';

/**
 * The shop's marks, fetched once per session and shared by every screen.
 *
 * One fetch, in a provider, rather than a hook per consumer: the watermark and the corner logo want
 * the same row, and the till re-renders its chrome on every cart change. A hook that fetched per
 * mount would put this request on the hot path of ringing a sale.
 *
 * A failure here is silent by design. Branding is decoration; a store whose logo endpoint is down
 * still has to be able to sell, and an error toast on every page load would be the more visible
 * fault of the two.
 */

interface BrandingState {
  branding: Branding | null;
  slot: (slot: BrandingSlot) => BrandingSlotState | null;
  imageUrl: (slot: BrandingSlot) => string | null;
  reload: () => Promise<void>;
}

const BrandingContext = createContext<BrandingState | null>(null);

export function BrandingProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const locationId = user?.locationId ?? null;

  const [branding, setBranding] = useState<Branding | null>(null);

  const reload = useCallback(async () => {
    if (locationId === null) {
      setBranding(null);
      return;
    }

    try {
      setBranding(await brandingApi.get(locationId));
    } catch {
      setBranding(null);
    }
  }, [locationId]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const value = useMemo<BrandingState>(() => {
    const find = (slot: BrandingSlot) => branding?.slots.find((s) => s.slot === slot) ?? null;

    return {
      branding,
      slot: find,
      imageUrl: (slot) => {
        const state = find(slot);
        return state && branding ? brandingImageUrl(branding.locationId, state) : null;
      },
      reload,
    };
  }, [branding, reload]);

  return <BrandingContext.Provider value={value}>{children}</BrandingContext.Provider>;
}

export function useBranding(): BrandingState {
  const context = useContext(BrandingContext);

  if (!context) {
    throw new Error('useBranding must be used inside a BrandingProvider');
  }

  return context;
}

/**
 * The large mark behind the working area.
 *
 * Fixed to the viewport rather than to the page, so it stays put while a long browse scrolls over
 * it — a watermark that scrolls reads as a stray picture in the content.
 *
 * `pointer-events-none` is not cosmetic. Without it this element sits over the whole application and
 * swallows every click, which is a total loss of function in exchange for a decoration. `aria-hidden`
 * for the same reason in the other direction: a screen reader announcing the company logo before
 * every screen is noise, and the shop's name is already in the header.
 */
export function Watermark() {
  const { slot, imageUrl } = useBranding();

  const state = slot('Watermark');
  const source = imageUrl('Watermark');

  if (!state?.present || !source) return null;

  return (
    <div
      aria-hidden="true"
      className="pointer-events-none fixed inset-0 z-0 flex select-none items-center justify-center overflow-hidden"
    >
      <img
        src={source}
        alt=""
        /*
         * A share of the shorter viewport edge, so it is the same size relative to the screen on a
         * 1366x768 till and on a 27-inch back-office monitor. A fixed pixel width fills one and
         * disappears on the other.
         */
        className="h-auto w-auto max-h-[45vmin] max-w-[60vmin] object-contain"
        style={{ opacity: state.opacityPct / 100 }}
      />
    </div>
  );
}

/**
 * The shop's own mark, in the corner.
 *
 * Sized by height alone so a wide wordmark and a square badge both sit on the same baseline without
 * anyone having to crop them to a template.
 */
export function CompanyLogo({ className }: { className?: string }) {
  const { branding, slot, imageUrl } = useBranding();

  const state = slot('CompanyLogo');
  const source = imageUrl('CompanyLogo');

  if (!state?.present || !source) return null;

  return (
    <img
      src={source}
      alt={branding?.businessName ?? ''}
      className={className ?? 'h-8 w-auto max-w-[160px] object-contain'}
      style={{ opacity: state.opacityPct / 100 }}
    />
  );
}
