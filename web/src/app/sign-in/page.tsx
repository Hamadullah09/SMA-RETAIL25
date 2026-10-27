'use client';

import { useQueryClient } from '@tanstack/react-query';
import { Eye, EyeOff, LogIn } from 'lucide-react';
import Image from 'next/image';
import { useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { FormError } from '@/components/ui/states';
import { api, ApiError, safeNext } from '@/lib/api';
import { homeFor } from '@/lib/nav';
import { ME_KEY } from '@/lib/session';

export default function SignInPage() {
  return (
    <Suspense>
      <SignIn />
    </Suspense>
  );
}

/** The sign-in window, like the desktop's login: the SMA mark, user name and password, Log in (Enter). */
function SignIn() {
  const router = useRouter();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nameBox = useRef<HTMLInputElement>(null);

  useEffect(() => {
    document.title = 'Sign in · SMA RETAIL';
    nameBox.current?.focus();
  }, []);

  async function submit() {
    if (!userName.trim() || !password) {
      setError('Type your user name and password.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const { user } = await api.signIn({ userName: userName.trim(), password });
      queryClient.clear();
      queryClient.setQueryData(ME_KEY, user);
      const next = safeNext(params.get('next'));
      router.replace(user.mustChangePassword ? '/account/' : next ?? homeFor(user.permissions));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'The sign-in did not work. Try again.');
      setBusy(false);
    }
  }

  return (
    <main className="grid min-h-[100dvh] place-items-center p-4 sm:p-8">
      <form
        className="card w-full max-w-[620px] animate-rise-in p-6 sm:p-9"
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <div className="mb-7 flex items-center gap-5">
          <Image src="/sma-mark.png" alt="SMA" width={96} height={96} priority className="h-20 w-20 sm:h-24 sm:w-24" />
          <div>
            <p className="font-heading text-[32px] font-bold leading-10 tracking-wide sm:text-[34px]">SMA RETAIL</p>
            <p className="text-muted">by SMA Technology</p>
          </div>
        </div>

        <h1 className="font-heading text-[28px] leading-9">Welcome to the back office</h1>
        <p className="mb-6 text-muted">Sign in with your name and password.</p>

        <label htmlFor="userName" className="mb-1.5 block px-0.5">
          User name
        </label>
        <input
          ref={nameBox}
          id="userName"
          name="username"
          className="input mb-4"
          autoComplete="username"
          autoCapitalize="none"
          spellCheck={false}
          value={userName}
          onChange={(e) => setUserName(e.target.value)}
        />

        <label htmlFor="password" className="mb-1.5 block px-0.5">
          Password
        </label>
        <div className="relative">
          <input
            id="password"
            name="password"
            type={show ? 'text' : 'password'}
            className="input pr-14"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <button
            type="button"
            onClick={() => setShow((s) => !s)}
            className="absolute right-1.5 top-1/2 grid h-10 w-10 -translate-y-1/2 place-items-center rounded-full text-muted hover:bg-stone-light hover:text-ink"
            aria-label={show ? 'Hide the password' : 'Show the password'}
          >
            {show ? <EyeOff className="h-5 w-5" /> : <Eye className="h-5 w-5" />}
          </button>
        </div>

        <FormError error={error} />

        <div className="mt-7 flex justify-end">
          <Button type="submit" variant="sun" icon={LogIn} shortcut="Enter" busy={busy} className="min-w-[170px]">
            {busy ? 'Signing in…' : 'Log in'}
          </Button>
        </div>
        <p className="mt-6 text-note text-muted">The tills keep selling without the web. Ask your administrator if you cannot sign in.</p>
      </form>
    </main>
  );
}
