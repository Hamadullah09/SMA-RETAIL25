'use client';

import { useQuery } from '@tanstack/react-query';
import { api } from './api';
import type { StaffMember } from './types';

/**
 * The people to filter by (`GET /api/staff`): the store's users and those of every store, or everyone for "All stores".
 * Readable with reports.view, reports.staff, audit.view or setup.users, so every page that filters by staff may ask.
 */
export function useStaff(storeId: string | null) {
  return useQuery({ queryKey: ['staff', storeId], queryFn: () => api.staff({ storeId }), staleTime: 5 * 60_000 });
}

export function staffName(member: StaffMember): string {
  return member.fullName?.trim() || member.userName;
}
