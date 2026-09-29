/** Where the normalized scene lands on screen: pixel = origin + normalized * scale. */
export interface Fit {
  scale: number;
  originX: number;
  originY: number;
}

/**
 * Contain-fit of the normalized scene into a viewport, centred and inset by a margin.
 * The scene's long axis spans [0, 1] and its short axis [0, shorter / longer], so the map
 * measures 1 by 1/aspect units when wide and aspect by 1 when tall.
 */
export function containFit(viewportWidth: number, viewportHeight: number, aspect: number, marginPx: number): Fit {
  const mapWidthUnits = Math.min(1, aspect);
  const mapHeightUnits = Math.min(1, 1 / aspect);
  const availableWidth = Math.max(0, viewportWidth - 2 * marginPx);
  const availableHeight = Math.max(0, viewportHeight - 2 * marginPx);
  const scale = Math.min(availableWidth / mapWidthUnits, availableHeight / mapHeightUnits);
  return {
    scale,
    originX: (viewportWidth - mapWidthUnits * scale) / 2,
    originY: (viewportHeight - mapHeightUnits * scale) / 2,
  };
}

export function toPixel(fit: Fit, normalizedX: number, normalizedY: number): [number, number] {
  return [fit.originX + normalizedX * fit.scale, fit.originY + normalizedY * fit.scale];
}
