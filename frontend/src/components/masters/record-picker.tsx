'use client';

import { useEffect, useRef, useState } from 'react';
import { Field } from '@/components/masters/browse-form';

/**
 * Picks one record by searching for it.
 *
 * A plain id field would be unusable — nobody knows a GUID, and the legacy screens let you type the
 * first characters of a stock code and pick from what narrows. The current value is shown as its
 * code and name rather than its id, so a form that is holding a link says what the link is.
 */

export interface PickerOption {
  id: string;
  code: string;
  name: string;
}

export function RecordPicker({
  label,
  value,
  onChange,
  search,
  hint,
  disabled,
  placeholder = 'Type to search',
}: {
  label: string;
  value: PickerOption | null;
  onChange: (value: PickerOption | null) => void;
  search: (term: string) => Promise<PickerOption[]>;
  hint?: string;
  disabled?: boolean;
  placeholder?: string;
}) {
  const [term, setTerm] = useState('');
  const [results, setResults] = useState<PickerOption[]>([]);
  const [open, setOpen] = useState(false);

  // Held in a ref so a slow response for an old term cannot overwrite a newer one — typing fast
  // otherwise shows results for something you have already stopped searching for.
  const latest = useRef(0);

  useEffect(() => {
    if (term.trim().length < 2) {
      setResults([]);
      return undefined;
    }

    const ticket = ++latest.current;

    const timer = window.setTimeout(() => {
      void search(term.trim())
        .then((found) => {
          if (ticket === latest.current) setResults(found);
        })
        .catch(() => {
          if (ticket === latest.current) setResults([]);
        });
    }, 200);

    return () => window.clearTimeout(timer);
  }, [term, search]);

  return (
    <Field label={label} hint={hint}>
      {value ? (
        <div className="flex items-center justify-between gap-2 rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1 text-sm">
          <span className="truncate">
            <span className="pos-amount">{value.code}</span> — {value.name}
          </span>
          {!disabled ? (
            <button
              type="button"
              className="text-xs underline"
              onClick={() => {
                onChange(null);
                setTerm('');
                setOpen(false);
              }}
            >
              Clear
            </button>
          ) : null}
        </div>
      ) : (
        <div className="relative">
          <input
            className="w-full rounded-[var(--radius-dense)] border border-[rgb(var(--border))] bg-[rgb(var(--panel))] px-2 py-1 outline-none focus:border-[rgb(var(--accent))]"
            value={term}
            placeholder={placeholder}
            disabled={disabled}
            onChange={(event) => {
              setTerm(event.target.value);
              setOpen(true);
            }}
            onFocus={() => setOpen(true)}
            // A blur that fires before the click would close the list under the pointer.
            onBlur={() => window.setTimeout(() => setOpen(false), 150)}
          />

          {open && results.length > 0 ? (
            <ul className="absolute z-10 mt-0.5 max-h-56 w-full overflow-y-auto border border-[rgb(var(--border))] bg-[rgb(var(--panel))] text-sm shadow-md">
              {results.map((option) => (
                <li key={option.id}>
                  <button
                    type="button"
                    className="block w-full px-2 py-1 text-left hover:bg-[rgb(var(--surface))]"
                    onClick={() => {
                      onChange(option);
                      setTerm('');
                      setOpen(false);
                    }}
                  >
                    <span className="pos-amount">{option.code}</span> — {option.name}
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      )}
    </Field>
  );
}
