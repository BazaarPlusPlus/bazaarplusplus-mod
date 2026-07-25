import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { Slot } from "radix-ui";

import { cn } from "@/lib/utils";

const badgeVariants = cva(
  "inline-flex w-fit shrink-0 items-center justify-center gap-1 overflow-hidden whitespace-nowrap rounded-full border px-2 py-0.5 text-micro font-semibold transition-colors focus-visible:ring-2 focus-visible:ring-ring/50 [&>svg]:pointer-events-none [&>svg]:size-3",
  {
    variants: {
      variant: {
        default: "border-primary/60 bg-primary/12 text-primary",
        secondary: "border-secondary bg-secondary text-secondary-foreground",
        destructive:
          "border-destructive/45 bg-destructive/12 text-destructive",
        outline: "border-border bg-transparent text-foreground",
        success: "border-success/40 bg-success/10 text-success",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  },
);

function Badge({
  className,
  variant = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"span">
  & VariantProps<typeof badgeVariants>
  & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "span";
  return (
    <Comp
      className={cn(badgeVariants({ variant }), className)}
      data-slot="badge"
      data-variant={variant}
      {...props}
    />
  );
}

export { Badge, badgeVariants };
