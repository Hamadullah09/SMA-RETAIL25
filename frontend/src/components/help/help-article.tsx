import type { HelpTopic } from '@/lib/help-content';

/**
 * A guide's body, wherever it is being read.
 *
 * Both the help page and the overlay render this. They were going to be two copies of the same
 * markup, which is the failure mode this whole exercise exists to remove: the page would grow a
 * definition list style the overlay never got, and the guide would look like two different guides
 * depending on how it was opened.
 *
 * Deliberately plain — long measure, large type, numbered steps. This is what somebody reads when
 * they are already stuck, so it is the last place to be clever with layout.
 */
export function HelpArticle({ topic }: { topic: HelpTopic }) {
  return (
    <div className="space-y-8">
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
                  {/* Numbered by hand rather than by list-style, because the marker has to line up
                      with the first line of a step that wraps onto three. */}
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
    </div>
  );
}
