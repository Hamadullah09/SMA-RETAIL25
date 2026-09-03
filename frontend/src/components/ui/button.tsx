import * as React from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { Loader2 } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * The button, wrapping the same `.pos-button` classes the rest of the app uses.
 *
 * It used to be an independent shadcn-style definition on the old HSL tokens, with its own heights
 * (h-10) and radius (rounded-md) — so the one place it was used looked subtly unlike the seventy
 * places `.pos-button` was. Wrapping rather than redefining means there is one answer to "how tall
 * is a button" and changing it changes both.
 */
const buttonVariants = cva('', {
  variants: {
    variant: {
      default: 'pos-button-primary',
      outline: 'pos-button',
      destructive: 'pos-button-danger',

      // No border and no fill until hovered — for icon controls sitting inside other chrome, where
      // a bordered button would draw a box around something that is not a region.
      ghost:
        'inline-flex min-h-control items-center justify-center gap-1.5 rounded px-2 text-body font-medium text-ink-muted transition-colors duration-150 hover:bg-panel-hover hover:text-ink disabled:cursor-not-allowed disabled:opacity-40',
      link: 'pos-link text-body font-medium',
    },
    size: {
      default: '',

      // No min-h-0. It cancelled the control height the whole design system is built on, so every
      // "small" button was a 22px target on a screen where the floor is 48 — and small is exactly
      // where a button is hardest to hit.
      sm: 'px-3 text-label',
      lg: 'px-5 text-body-lg',

      // Square at the tap floor, not 32px wide. An icon-only control is the one that most needs the
      // room, because there is no label to aim at either.
      icon: 'w-control px-0',
    },
  },
  defaultVariants: { variant: 'outline', size: 'default' },
});

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean;
  /**
   * Shows a spinner and disables the button.
   *
   * Held here rather than at each call site, because every call site that did it by hand forgot at
   * least one of the two — a button that spins but can still be pressed submits twice.
   */
  loading?: boolean;
}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, loading = false, disabled, children, ...props }, ref) => {
    const Comp = asChild ? Slot : 'button';

    return (
      <Comp
        className={cn(buttonVariants({ variant, size }), className)}
        ref={ref}
        disabled={disabled || loading}
        aria-busy={loading || undefined}
        {...props}
      >
        {loading ? <Loader2 className="h-5 w-5 shrink-0 animate-spin" aria-hidden /> : null}
        {children}
      </Comp>
    );
  },
);

Button.displayName = 'Button';

export { Button, buttonVariants };
