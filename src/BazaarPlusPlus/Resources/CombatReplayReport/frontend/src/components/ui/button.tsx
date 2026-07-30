import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { Slot } from "radix-ui";

import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "inline-flex shrink-0 items-center justify-center gap-2 whitespace-nowrap text-body font-medium leading-none transition-colors outline-none focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:opacity-40 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-icon-md",
  {
    variants: {
      variant: {
        default:
          "rounded-panel border border-primary/70 bg-primary text-primary-foreground shadow-brand-glow hover:bg-primary/90",
        destructive:
          "rounded-panel border border-destructive/70 bg-destructive text-destructive-foreground hover:bg-destructive/88",
        outline:
          "rounded-panel border border-input bg-background/45 text-foreground hover:bg-accent hover:text-accent-foreground",
        secondary:
          "rounded-panel border border-secondary bg-secondary text-secondary-foreground hover:bg-secondary/80",
        ghost:
          "rounded-panel border border-transparent bg-transparent text-muted-foreground hover:border-border/70 hover:bg-accent hover:text-accent-foreground",
        link:
          "rounded-panel text-primary underline-offset-4 hover:underline",
        tableHeader:
          "rounded-none border-0 bg-transparent text-muted-foreground hover:bg-accent hover:text-foreground",
      },
      size: {
        default: "h-control-md px-4",
        xs: "h-control-xs gap-1 px-2 text-micro [&_svg:not([class*='size-'])]:size-icon-xs",
        sm: "h-control-sm gap-1.5 px-3 text-compact",
        lg: "h-control-lg px-6",
        icon: "size-control-md",
        "icon-xs": "size-control-xs [&_svg:not([class*='size-'])]:size-icon-xs",
        "icon-sm": "size-control-sm",
        "icon-lg": "size-control-lg",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
);

function Button({
  className,
  variant = "default",
  size = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"button">
  & VariantProps<typeof buttonVariants>
  & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "button";
  return (
    <Comp
      className={cn(buttonVariants({ variant, size, className }))}
      data-size={size}
      data-slot="button"
      data-variant={variant}
      {...props}
    />
  );
}

export { Button };
