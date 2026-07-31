import * as React from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
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
        'inline-flex min-h-control items-center justify-center gap-1.5 rounded px-2 text-body font-medium text-ink-muted transition-colors hover:bg-panel-hover hover:text-ink disabled:cursor-not-allowed disabled:opacity-40',
      link: 'pos-link text-body font-medium',
    },
    size: {
      default: '',
      sm: 'min-h-0 px-2 py-1 text-label',
      lg: 'px-4 text-body-lg',
      icon: 'w-8 px-0',
    },
  },
  defaultVariants: { variant: 'outline', size: 'default' },
});

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean;
}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, ...props }, ref) => {
    const Comp = asChild ? Slot : 'button';
    return <Comp className={cn(buttonVariants({ variant, size }), className)} ref={ref} {...props} />;
  },
);

Button.displayName = 'Button';

export { Button, buttonVariants };
