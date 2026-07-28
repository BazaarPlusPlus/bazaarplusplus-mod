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
  intrinsicItemArtGeometry,
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
  contentAlign = "center",
  entity,
  itemFit = "slot",
  size = "default",
  squareSlot = false,
  testId,
}: {
  contentAlign?: "center" | "start";
  entity: NormalizedEntity;
  itemFit?: "intrinsic" | "slot";
  size?: EntityArtSize;
  squareSlot?: boolean;
  testId?: string;
}): React.JSX.Element {
  const [failedAsset, setFailedAsset] = useState("");
  const [loadedAsset, setLoadedAsset] = useState<{
    asset: string;
    height: number;
    width: number;
  } | null>(null);
  const type = entity.type.toLowerCase();
  const asset = safeAssetUrl(entity.asset);
  const failed = failedAsset === asset;
  const dimensions = entityArtDimensions(type, entity.span, size);
  const intrinsicItem = type === "item" && itemFit === "intrinsic";
  const loadedGeometry =
    loadedAsset?.asset === asset
      ? { width: loadedAsset.width, height: loadedAsset.height }
      : undefined;
  const intrinsicDimensions = intrinsicItem
    ? intrinsicItemArtGeometry(entity.span, size, loadedGeometry)
    : null;

  return (
    <span
      className={cn(
        "relative inline-flex shrink-0 items-center justify-center",
        intrinsicItem ? "overflow-visible" : "overflow-hidden",
        type === "skill" || type === "hero"
          ? "rounded-full"
          : type === "item"
            ? intrinsicItem
              ? "rounded-none"
              : "rounded-art"
            : "rounded-art border border-border/60 bg-muted/30",
      )}
      data-bpp-test-id={testId}
      data-entity-art-align={contentAlign}
      data-entity-art-fit={intrinsicItem ? "intrinsic" : "slot"}
      data-entity-art-size={size}
      data-entity-span={dimensions.span}
      data-entity-type={type}
      data-asset-status={asset && !failed ? "ready" : "missing"}
      style={{
        width:
          intrinsicDimensions?.width
          ?? (squareSlot ? dimensions.height : dimensions.width),
        height: intrinsicDimensions?.height ?? dimensions.height,
      }}
    >
      {asset && !failed ? (
        <img
          alt=""
          className={cn(
            "block size-full object-contain [transform:none]",
            contentAlign === "start" ? "object-left" : "object-center",
          )}
          loading="lazy"
          onError={() => setFailedAsset(asset)}
          onLoad={
            intrinsicItem
              ? (event) => {
                  const image = event.currentTarget;
                  setLoadedAsset({
                    asset,
                    width: image.naturalWidth,
                    height: image.naturalHeight,
                  });
                }
              : undefined
          }
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
