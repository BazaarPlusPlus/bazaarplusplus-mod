import {
  CircleHelp,
  Diamond,
  Package,
  Sparkles,
  UserRound,
} from "lucide-react";
import { useState } from "react";
import { cn } from "../../lib/utils.ts";
import { safeAssetUrl } from "../../model/asset-paths.ts";
import type { NormalizedEntity } from "../../model/normalize.ts";
import {
  entityArtDimensions,
  type EntityArtSize,
} from "./entity-art-geometry.ts";

function Fallback({
  type,
  className,
}: {
  type: string;
  className?: string;
}): React.JSX.Element {
  const Icon =
    type === "hero"
      ? UserRound
      : type === "item"
        ? Package
        : type === "skill"
          ? Sparkles
          : type === "effect"
            ? Diamond
            : CircleHelp;
  return <Icon className={cn("size-icon-md text-muted-foreground", className)} />;
}

export function EntityArt({
  entity,
  size = "default",
  squareSlot = false,
  testId,
}: {
  entity: NormalizedEntity;
  size?: EntityArtSize;
  squareSlot?: boolean;
  testId?: string;
}): React.JSX.Element {
  const [failed, setFailed] = useState(false);
  const type = entity.type.toLowerCase();
  const asset = safeAssetUrl(entity.asset);
  const dimensions = entityArtDimensions(type, entity.span, size);

  return (
    <span
      className={cn(
        "relative inline-flex shrink-0 items-center justify-center overflow-hidden",
        type === "skill" || type === "hero"
          ? "rounded-full"
          : type === "item"
            ? "rounded-art"
            : "rounded-art border border-border/60 bg-muted/30",
      )}
      data-bpp-test-id={testId}
      data-entity-art-size={size}
      data-entity-span={dimensions.span}
      data-entity-type={type}
      data-asset-status={asset && !failed ? "ready" : "missing"}
      style={{
        width: squareSlot ? dimensions.height : dimensions.width,
        height: dimensions.height,
      }}
    >
      {asset && !failed ? (
        <img
          alt=""
          className="block size-full object-contain [transform:none]"
          loading="lazy"
          onError={() => setFailed(true)}
          src={asset}
        />
      ) : (
        <span className="grid size-full place-items-center border border-brand/35 bg-surface-raised">
          <Fallback
            className={
              size === "activity" || size === "inspector"
                ? "size-icon-lg"
                : undefined
            }
            type={type}
          />
        </span>
      )}
    </span>
  );
}
