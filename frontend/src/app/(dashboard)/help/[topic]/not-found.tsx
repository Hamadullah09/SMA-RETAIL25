import Link from 'next/link';
import { BookOpen } from 'lucide-react';
import { WRITTEN_TOPICS } from '@/lib/help-content';

/** A guide that has not been written. Says which ones have. */
export default function HelpTopicNotFound() {
  return (
    <div className="flex h-below-header flex-col items-center justify-center gap-3 px-page text-center">
      <span
        className="flex h-14 w-14 items-center justify-center rounded-full bg-accent-soft text-accent-text"
        aria-hidden
      >
        <BookOpen className="h-7 w-7" />
      </span>

      <h1 className="text-h1 font-semibold text-ink">There is no guide for that yet</h1>

      <p className="max-w-[52ch] text-body leading-relaxed text-ink-muted">
        The guides written so far are listed below. Ask a colleague or an administrator rather than
        guessing on a screen that moves stock or money.
      </p>

      <ul className="mt-2 flex flex-wrap items-center justify-center gap-2">
        {WRITTEN_TOPICS.map((topic) => (
          <li key={topic.slug}>
            <Link href={`/help/${topic.slug}`} className="pos-button">
              {topic.title}
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
