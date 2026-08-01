import { AuthLink, AuthShell } from '@/components/auth/auth-shell';

/**
 * A page of its own rather than a message on the form.
 *
 * The form is gone once it has succeeded, and leaving a filled-in sign-up form on screen behind a
 * green banner invites a second submission. This also gives the browser a URL that means "it worked",
 * which is what someone lands on if they hit back.
 */
export default function SignUpDonePage() {
  return (
    <AuthShell
      title="Account created"
      lead="You can sign in now."
      footer={<AuthLink href="/">Go to sign in</AuthLink>}
    >
      <div className="space-y-3 text-body text-ink-muted">
        <p>
          Your account starts in <strong className="font-medium text-ink">training mode</strong>. You
          can open the till, ring items through and practise the whole flow — none of it touches real
          stock, the drawer, or a customer&rsquo;s account.
        </p>
        <p>
          An administrator turns that off when they are ready to give you a real role. Until then,
          nothing you do can go wrong.
        </p>
      </div>
    </AuthShell>
  );
}
