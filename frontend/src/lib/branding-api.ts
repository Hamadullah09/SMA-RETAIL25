import { apiClient } from '@/lib/api-client';

/**
 * The two marks that make an installation belong to a shop.
 *
 * Both are uploaded through the application rather than built into the bundle, which is the whole
 * point: a reseller standing up a new customer changes them here and ships nothing.
 */

export type BrandingSlot = 'Watermark' | 'CompanyLogo';

export interface BrandingSlotState {
  slot: BrandingSlot;
  present: boolean;
  eTag: string | null;
  opacityPct: number;
}

export interface Branding {
  locationId: number;
  businessName: string;
  slots: BrandingSlotState[];
}

/**
 * The image URL, tagged with the slot's ETag.
 *
 * The tag is in the query string as well as on the response, because the server's own ETag only
 * gets a browser to the new logo on its next revalidation — and "next revalidation" for a
 * `max-age=86400` response is tomorrow. An administrator who uploads a new logo and still sees the
 * old one concludes the upload failed and does it again.
 */
export function brandingImageUrl(locationId: number, state: BrandingSlotState): string | null {
  if (!state.present) return null;

  return `/api/proxy/locations/${locationId}/branding/${state.slot}?v=${state.eTag ?? ''}`;
}

export const brandingApi = {
  get: async (locationId: number): Promise<Branding> =>
    (await apiClient.get<Branding>(`/locations/${locationId}/branding`)).data,

  upload: async (locationId: number, slot: BrandingSlot, file: File): Promise<BrandingSlotState> => {
    const body = new FormData();
    body.append('file', file);

    // No explicit Content-Type: the browser has to set the multipart boundary itself, and naming
    // the type here overwrites it with one that has none.
    return (await apiClient.put<BrandingSlotState>(`/locations/${locationId}/branding/${slot}`, body)).data;
  },

  setOpacity: async (locationId: number, slot: BrandingSlot, opacityPct: number): Promise<BrandingSlotState> =>
    (await apiClient.patch<BrandingSlotState>(`/locations/${locationId}/branding/${slot}/opacity`, { opacityPct }))
      .data,

  remove: async (locationId: number, slot: BrandingSlot): Promise<void> => {
    await apiClient.delete(`/locations/${locationId}/branding/${slot}`);
  },
};
