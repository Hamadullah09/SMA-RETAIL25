import Link from 'next/link';
import { notFound } from 'next/navigation';
import { ArrowLeft } from 'lucide-react';
import { PageHeader } from '@/components/shell/page-header';
import { HelpArticle } from '@/components/help/help-article';
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
        {/* A long measure, not the full width of a 1920px monitor. Prose set across 200 characters
            is prose whose next line the eye cannot find. */}
        <div className="max-w-2xl space-y-8">
          <HelpArticle topic={topic} />

          <Link href="/help" className="pos-button">
            <ArrowLeft className="h-5 w-5" aria-hidden />
            All guides
          </Link>
        </div>
      </div>
    </div>
  );
}
