import * as React from "react";

import { cn } from "@/lib/utils";

function Card({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      className={cn(
        "flex flex-col gap-4 rounded-panel border border-border bg-card text-card-foreground shadow-panel",
        className,
      )}
      data-slot="card"
      {...props}
    />
  );
}

export { Card };
