import { describe, expect, it } from 'vitest';
import type { NetworkScene } from './contract';
import { drawWater } from './draw';

/** A stand-in canvas context that records the calls drawWater makes, in order. */
function recordingContext(): { context: CanvasRenderingContext2D; calls: string[] } {
  const calls: string[] = [];
  const context = {
    fillStyle: '',
    beginPath: () => calls.push('beginPath'),
    moveTo: (pixelX: number, pixelY: number) => calls.push(`moveTo ${pixelX},${pixelY}`),
    lineTo: (pixelX: number, pixelY: number) => calls.push(`lineTo ${pixelX},${pixelY}`),
    closePath: () => calls.push('closePath'),
    fill: (rule?: string) => calls.push(`fill ${rule}`),
  };
  return { context: context as unknown as CanvasRenderingContext2D, calls };
}

function sceneWith(water: NetworkScene['water']): NetworkScene {
  return { artifactVersion: 'test@00000000', extent: { aspect: 1, coreRadiusKm: 22, spanKm: 10 }, lines: [], water };
}

/** One pixel per hundredth of a unit, no offset: 0.5 lands on pixel 50. */
const fit = { scale: 100, originX: 0, originY: 0 };

describe('drawWater', () => {
  it('fills each area on its own, so overlapping areas never cut holes in each other', () => {
    const { context, calls } = recordingContext();
    const square = [0.1, 0.1, 0.2, 0.1, 0.2, 0.2];

    drawWater(context, sceneWith([{ rings: [square] }, { rings: [square] }]), fit);

    expect(calls.filter((call) => call === 'beginPath')).toHaveLength(2);
    expect(calls.filter((call) => call === 'fill evenodd')).toHaveLength(2);
  });

  it('draws an island as a second closed ring in the same fill', () => {
    const { context, calls } = recordingContext();
    const lake = [0, 0, 1, 0, 1, 1];
    // Quarters and halves are exact in binary, so the expected pixels are exact too.
    const island = [0.25, 0.25, 0.75, 0.25, 0.5, 0.75];

    drawWater(context, sceneWith([{ rings: [lake, island] }]), fit);

    expect(calls).toEqual([
      'beginPath',
      'moveTo 0,0', 'lineTo 100,0', 'lineTo 100,100', 'closePath',
      'moveTo 25,25', 'lineTo 75,25', 'lineTo 50,75', 'closePath',
      'fill evenodd',
    ]);
  });

  it('skips a ring with too few points to enclose anything', () => {
    const { context, calls } = recordingContext();

    drawWater(context, sceneWith([{ rings: [[0.1, 0.1, 0.2, 0.2]] }]), fit);

    expect(calls).toEqual(['beginPath', 'fill evenodd']);
  });
});
