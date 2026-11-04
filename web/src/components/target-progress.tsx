import { cx } from '@/lib/cx';
import { formatPercent } from '@/lib/format';
import { barWidth, paceTone } from '@/lib/targets';
import { Chip } from './ui/page';

/** A bar of the target reached, coloured by the pace, with a mark where the month should be by now. */
export function TargetBar({ percent, expected, label }: { percent: number; expected: number; label: string }) {
  const tone = paceTone(percent, expected);
  return (
    <div className="flex items-center gap-3">
      <div className="relative h-3 flex-1 overflow-hidden rounded-full bg-stone-light" role="meter" aria-label={label} aria-valuenow={percent} aria-valuemin={0} aria-valuemax={100}>
        <div className={cx('h-full rounded-full', tone === 'ok' ? 'bg-ok' : tone === 'warn' ? 'bg-sun-dark' : 'bg-danger')} style={{ width: `${barWidth(percent)}%` }} />
        {expected > 0 && expected < 100 && <div className="absolute inset-y-0 w-0.5 bg-ink" style={{ left: `${expected}%` }} aria-hidden="true" />}
      </div>
      <Chip tone={tone} className="min-w-[72px] justify-center tabular-nums">
        {formatPercent(percent)}
      </Chip>
    </div>
  );
}
