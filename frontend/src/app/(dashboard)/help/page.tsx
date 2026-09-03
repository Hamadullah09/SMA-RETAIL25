import Link from 'next/link';
import { PageHeader } from '@/components/shell/page-header';
import { WRITTEN_TOPICS } from '@/lib/help-content';

/**
 * The index of guides.
 *
 * Only the written ones are listed. A list padded out with entries that turn out to say "not
 * written yet" teaches people that this section is not worth opening.
 */
export default function HelpIndexPage() {
  return (
    <div className="flex h-below-header min-h-0 flex-col">
      <PageHeader
        title="Help"
        description="How each screen works, in plain words. Press Ctrl+H on any screen to open its guide."
        help={false}
      />

      <div className="min-h-0 flex-1 overflow-auto px-page py-panel">
        <ul className="grid max-w-4xl gap-3 sm:grid-cols-2">
          {WRITTEN_TOPICS.map((topic) => (
            <li key={topic.slug}>
              <Link
                href={`/help/${topic.slug}`}
                className="block rounded-lg border border-subtle bg-panel p-panel transition-colors hover:border-strong hover:bg-panel-hover"
              >
                <span className="block text-body-lg font-semibold text-ink">{topic.title}</span>
                <span className="mt-1 block text-body text-ink-muted">{topic.summary}</span>
              </Link>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
