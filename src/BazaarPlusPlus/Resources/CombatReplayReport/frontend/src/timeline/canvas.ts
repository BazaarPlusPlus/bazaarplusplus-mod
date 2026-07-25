const MAX_CANVAS_SIDE = 32_000;
const MAX_CANVAS_AREA = 240_000_000;

export interface LogicalCanvasSize {
  width: number;
  height: number;
}

export function canvasBackingScale(
  cssWidth: number,
  cssHeight: number,
): number {
  const width = Math.max(1, cssWidth);
  const height = Math.max(1, cssHeight);
  const devicePixels = Math.max(
    1,
    Math.min(3, window.devicePixelRatio || 1),
  );
  return Math.max(
    1,
    Math.min(
      devicePixels,
      MAX_CANVAS_SIDE / width,
      MAX_CANVAS_SIDE / height,
      Math.sqrt(MAX_CANVAS_AREA / (width * height)),
    ),
  );
}

export function beginLogicalDraw(
  canvas: HTMLCanvasElement,
  context: CanvasRenderingContext2D,
): LogicalCanvasSize {
  const cssWidth = Number.parseFloat(canvas.style.width) || canvas.width;
  const scale = canvas.width / Math.max(1, cssWidth);
  context.setTransform(scale, 0, 0, scale, 0, 0);
  return {
    width: canvas.width / scale,
    height: canvas.height / scale,
  };
}

export function resizeLogicalCanvas(
  canvas: HTMLCanvasElement,
  cssWidth: number,
  cssHeight: number,
): number {
  const width = Math.max(1, Math.round(cssWidth));
  const height = Math.max(1, Math.round(cssHeight));
  const scale = canvasBackingScale(width, height);
  const backingWidth = Math.max(1, Math.round(width * scale));
  const backingHeight = Math.max(1, Math.round(height * scale));
  canvas.style.width = `${width}px`;
  canvas.style.height = `${height}px`;
  if (canvas.width !== backingWidth) canvas.width = backingWidth;
  if (canvas.height !== backingHeight) canvas.height = backingHeight;
  return scale;
}
