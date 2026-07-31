'use client';

import { useCallback, useEffect, useState } from 'react';
import { toast } from '@/components/ui/toaster';
import { useAuth } from '@/lib/auth-config';
import { mastersApi } from '@/lib/masters-api';
import { PosApiError } from '@/lib/pos-api';
import type { TimeClockState } from '@/types/masters';

/**
 * The punch clock, in the shell header (guide p.75–76).
 *
 * It lives here rather than on a page of its own because clocking on is the first thing someone does
 * when they arrive and the last thing before they leave — neither is a moment to go looking through
 * a menu for it.
 */
export function PunchClock() {
  const auth = useAuth();
  const locationId = auth.user?.locationId;
  const canClock = auth.can('staff.time_clock');

  const [state, setState] = useState<TimeClockState | null>(null);
  const [busy, setBusy] = useState(false);
  const [unavailable, setUnavailable] = useState(false);

  const refresh = useCallback(async () => {
    if (!locationId || !canClock) return;

    try {
      setState(await mastersApi.staff.myTimeClock(locationId));
    } catch {
      // A sign-in with no staff record behind it cannot use the clock. That is a normal state for
      // an admin account, so the widget disappears rather than showing an error in the header.
      setUnavailable(true);
    }
  }, [locationId, canClock]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // The elapsed figure is a clock face — it has to tick, or someone glancing at it after an hour
  // reads a stale number as the truth.
  useEffect(() => {
    if (!state?.isClockedIn) return;

    const timer = window.setInterval(() => void refresh(), 60_000);
    return () => window.clearInterval(timer);
  }, [state?.isClockedIn, refresh]);

  if (!canClock || unavailable || !locationId || !state) {
    return null;
  }

  const punch = async (going: 'in' | 'out') => {
    setBusy(true);

    try {
      const updated = going === 'in'
        ? await mastersApi.staff.clockIn(locationId)
        : await mastersApi.staff.clockOut(locationId);

      setState(updated);
      toast({ title: going === 'in' ? 'Clocked in' : `Clocked out — ${updated.hoursToday}h today` });
    } catch (error) {
      toast({
        title: 'Not done',
        description: error instanceof PosApiError ? error.problem.detail : 'Something went wrong.',
        variant: 'destructive',
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex items-center gap-2 text-xs">
      {/* Stated in words as well as by which button is offered — "on the clock" is not a colour. */}
      <span className="text-muted-foreground">
        {state.isClockedIn ? `On since ${time(state.clockedInAt)} · ${state.hoursSoFar}h` : `${state.hoursToday}h today`}
      </span>

      <button
        type="button"
        className="pos-button"
        disabled={busy}
        onClick={() => void punch(state.isClockedIn ? 'out' : 'in')}
      >
        {state.isClockedIn ? 'Clock out' : 'Clock in'}
      </button>
    </div>
  );
}

function time(iso: string | null): string {
  return iso ? new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '—';
}
