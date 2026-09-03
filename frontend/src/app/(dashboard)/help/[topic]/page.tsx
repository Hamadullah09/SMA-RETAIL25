import Link from 'next/link';
import { notFound } from 'next/navigation';
import { ArrowLeft } from 'lucide-react';
import { PageHeader } from '@/components/shell/page-header';
import { helpTopic } from '@/lib/help-content';

/**
 * One guide.
 *
 * Deliberately plain: long measure, large type, numbered steps. This is the page somebody opens
 * when they are already stuck, so it is the last place to be clever with layout.
 */
export default function HelpTopicPage({ params }: { params: { topic: string } }) {
  const topic = helpTopic(params.topic);

  if (!topic) notFound();

  return (
    <div className="flex h-below-header min-h-0 flex-col">
      {/* No Help button on the help page itself — it would link to where you already are. */}
      <PageHeader title={topic.title} description={topic.summary} help={false} />

      <div className="min-h-0 flex-1 overflow-auto px-page py-panel">
        <div className="max-w-2xl space-y-8">
          {topic.sections.map((section) => (
            <section key={section.heading} className="space-y-3">
              <h2 className="text-h2 font-semibold">{section.heading}</h2>

              {section.body?.map((paragraph) => (
                <p key={paragraph} className="text-body leading-relaxed text-ink">
                  {paragraph}
                </p>
              ))}

              {section.steps ? (
                <ol className="space-y-2 text-body leading-relaxed text-ink">
                  {section.steps.map((step, index) => (
                    <li key={step} className="flex gap-3">
                      <span
                        className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-accent-soft text-label font-semibold text-accent-text"
                        aria-hidden
                      >
                        {index + 1}
                      </span>
                      <span>{step}</span>
                    </li>
                  ))}
                </ol>
              ) : null}

              {section.definitions ? (
                <dl className="grid gap-x-6 gap-y-2 text-body sm:grid-cols-[minmax(8rem,auto)_1fr]">
                  {section.definitions.map((entry) => (
                    <div key={entry.term} className="contents">
                      <dt className="font-semibold text-ink">{entry.term}</dt>
                      <dd className="text-ink-muted">{entry.meaning}</dd>
                    </div>
                  ))}
                </dl>
              ) : null}
            </section>
          ))}

          <Link href="/help" className="pos-button">
            <ArrowLeft className="h-5 w-5" aria-hidden />
            All guides
          </Link>
        </div>
      </div>
    </div>
  );
}
