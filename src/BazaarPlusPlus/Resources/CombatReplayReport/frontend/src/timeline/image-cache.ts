interface CachedImage {
  image: HTMLImageElement;
  state: "loading" | "ready" | "failed";
  listeners: Set<() => void>;
}

const imageCache = new Map<string, CachedImage>();

export function cachedTimelineImage(
  url: string,
  onReady: () => void,
): HTMLImageElement | null {
  if (!url) return null;
  let entry = imageCache.get(url);
  if (!entry) {
    const image = new Image();
    entry = {
      image,
      state: "loading",
      listeners: new Set(),
    };
    imageCache.set(url, entry);
    image.addEventListener(
      "load",
      () => {
        if (!entry) return;
        entry.state = "ready";
        for (const listener of entry.listeners) listener();
        entry.listeners.clear();
      },
      { once: true },
    );
    image.addEventListener(
      "error",
      () => {
        if (!entry) return;
        entry.state = "failed";
        entry.listeners.clear();
      },
      { once: true },
    );
    image.src = url;
  }
  if (entry.state === "loading") entry.listeners.add(onReady);
  return entry.state === "ready" ? entry.image : null;
}
