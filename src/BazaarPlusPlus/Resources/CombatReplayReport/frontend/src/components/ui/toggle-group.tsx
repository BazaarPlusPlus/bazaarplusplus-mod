import * as React from "react";
import { type VariantProps } from "class-variance-authority";
import { ToggleGroup as ToggleGroupPrimitive } from "radix-ui";

import { cn } from "@/lib/utils";
import { toggleVariants } from "@/components/ui/toggle";

const ToggleGroupContext = React.createContext<
  VariantProps<typeof toggleVariants> & { spacing?: number }
>({
  size: "default",
  variant: "default",
  spacing: 0,
});

function ToggleGroup({
  className,
  variant,
  size,
  spacing = 0,
  children,
  ...props
}: React.ComponentProps<typeof ToggleGroupPrimitive.Root>
  & VariantProps<typeof toggleVariants>
  & { spacing?: number }) {
  return (
    <ToggleGroupPrimitive.Root
      className={cn(
        "flex w-fit items-center rounded-panel",
        spacing > 0 && "gap-1",
        className,
      )}
      data-size={size}
      data-slot="toggle-group"
      data-spacing={spacing}
      data-variant={variant}
      {...props}
    >
      <ToggleGroupContext.Provider value={{ variant, size, spacing }}>
        {children}
      </ToggleGroupContext.Provider>
    </ToggleGroupPrimitive.Root>
  );
}

function ToggleGroupItem({
  className,
  variant,
  size,
  ...props
}: React.ComponentProps<typeof ToggleGroupPrimitive.Item>
  & VariantProps<typeof toggleVariants>) {
  const context = React.useContext(ToggleGroupContext);
  return (
    <ToggleGroupPrimitive.Item
      className={cn(
        toggleVariants({
          variant: context.variant || variant,
          size: context.size || size,
        }),
        "min-w-0 shrink-0 focus-visible:z-10",
        context.spacing === 0
          && "rounded-none first:rounded-l-panel last:rounded-r-panel",
        className,
      )}
      data-size={context.size || size}
      data-slot="toggle-group-item"
      data-variant={context.variant || variant}
      {...props}
    />
  );
}

export { ToggleGroup, ToggleGroupItem };
