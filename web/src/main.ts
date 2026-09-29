import type { NetworkScene } from './contract';
import { drawNetwork } from './draw';
import { containFit } from './fit';

const MARGIN_PX = 32;
const BACKGROUND = '#07090c';

function requireElement<TElement extends Element>(selector: string): TElement {
  const element = document.querySelector<TElement>(selector);
  if (!element) {
    throw new Error(`index.html has no element matching ${selector}.`);
  }
  return element;
}

const container = requireElement<HTMLDivElement>('#map');
const canvas = requireElement<HTMLCanvasElement>('#map canvas');
const statusLine = requireElement<HTMLDivElement>('#status');
const context = canvas.getContext('2d');
if (!context) {
  throw new Error('Canvas 2D is not available.');
}
const drawing: CanvasRenderingContext2D = context;

function render(scene: NetworkScene, widthPx: number, heightPx: number): void {
  const pixelRatio = window.devicePixelRatio || 1;
  canvas.width = Math.round(widthPx * pixelRatio);
  canvas.height = Math.round(heightPx * pixelRatio);
  // Draw in CSS pixels; the transform maps them onto device pixels so lines stay crisp.
  drawing.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
  drawing.fillStyle = BACKGROUND;
  drawing.fillRect(0, 0, widthPx, heightPx);

  // Not laid out yet. ResizeObserver calls again once the container has a size.
  if (widthPx === 0 || heightPx === 0) {
    return;
  }
  drawNetwork(drawing, scene, containFit(widthPx, heightPx, scene.extent.aspect, MARGIN_PX));
}

async function loadScene(): Promise<NetworkScene> {
  const response = await fetch('/api/network');
  if (!response.ok) {
    throw new Error(`GET /api/network returned HTTP ${response.status}.`);
  }
  return (await response.json()) as NetworkScene;
}

loadScene()
  .then((scene) => {
    statusLine.textContent = `${scene.lines.length} lines · ${scene.artifactVersion}`;
    new ResizeObserver((entries) => {
      const size = entries[0].contentRect;
      render(scene, size.width, size.height);
    }).observe(container);
  })
  .catch((error: unknown) => {
    statusLine.textContent = `Network unavailable: ${error instanceof Error ? error.message : String(error)}`;
  });
