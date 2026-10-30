import { describe, expect, it } from 'vitest';
import type { NetworkScene, StationMarker } from './contract';
import { drawStations } from './stations';

/** A stand-in canvas context that records what drawStations draws. Every character is 6 px wide. */
function recordingContext(): { context: CanvasRenderingContext2D; calls: string[] } {
  const calls: string[] = [];
  const context = {
    font: '',
    fillStyle: '',
    strokeStyle: '',
    lineWidth: 0,
    textAlign: '',
    textBaseline: '',
    beginPath: () => undefined,
    arc: (pixelX: number, pixelY: number, radiusPx: number) => calls.push(`arc ${pixelX},${pixelY} r${radiusPx}`),
    fill: () => undefined,
    stroke: () => calls.push('stroke'),
    measureText: (text: string) => ({ width: text.length * 6 }),
    fillText: (text: string, pixelX: number, pixelY: number) => calls.push(`text ${text} ${pixelX},${pixelY}`),
  };
  return { context: context as unknown as CanvasRenderingContext2D, calls };
}

function sceneWith(stations: StationMarker[], lines: NetworkScene['lines'] = []): NetworkScene {
  return { artifactVersion: 'test@00000000', extent: { aspect: 1, coreRadiusKm: 22, spanKm: 10 }, lines, stations, water: [] };
}

/** One pixel per thousandth of a unit, no offset: 0.5 lands on pixel 500. */
const fit = { scale: 1000, originX: 0, originY: 0 };
const canvas = { left: 0, top: 0, right: 1000, bottom: 1000 };

describe('drawStations', () => {
  it('draws a dot for an ordinary station and no name', () => {
    const { context, calls } = recordingContext();

    drawStations(context, sceneWith([{ x: 0.5, y: 0.25, name: 'Midway', rank: 1 }]), fit, canvas);

    expect(calls).toEqual(['arc 500,250 r1.5']);
  });

  it('draws a ring and a name for a terminus and for an interchange', () => {
    const { context, calls } = recordingContext();

    drawStations(
      context,
      sceneWith([
        { x: 0.25, y: 0.25, name: 'North End', rank: 2 },
        { x: 0.5, y: 0.5, name: 'Crossing', rank: 3 },
      ]),
      fit,
      canvas,
    );

    expect(calls).toEqual([
      'arc 250,250 r3.5', 'stroke',
      'arc 500,500 r3.5', 'stroke',
      // Each name starts 6 px right of its marker, centred on it.
      'text North End 256,250',
      'text Crossing 506,500',
    ]);
  });

  it('moves a name off the line its station is on', () => {
    const { context, calls } = recordingContext();
    // A terminus with its line running away to the east.
    const line = { id: 'Red', name: 'Red Line', color: '#DA291C', shapes: [{ id: 'shape-red', points: [0.5, 0.5, 0.9, 0.5], lengthM: 1000 }] };

    drawStations(context, sceneWith([{ x: 0.5, y: 0.5, name: 'Alewife', rank: 2 }], [line]), fit, canvas);

    // 'Alewife' is 42 px wide, so on the left it starts at 500 - 6 - 42.
    expect(calls).toContain('text Alewife 452,500');
  });

  it('gives the place to the interchange when two names want it', () => {
    const { context, calls } = recordingContext();

    drawStations(
      context,
      sceneWith([
        { x: 0.5, y: 0.5, name: 'Terminus', rank: 2 },
        { x: 0.5, y: 0.501, name: 'Crossing', rank: 3 },
      ]),
      fit,
      canvas,
    );

    expect(calls).toContain('text Crossing 506,501');
    expect(calls).toContain('text Terminus 446,500');
  });

  it('keeps the marker when the name has nowhere to go', () => {
    const { context, calls } = recordingContext();
    // No name fits inside bounds this small.
    const cramped = { left: 495, top: 495, right: 505, bottom: 505 };

    drawStations(context, sceneWith([{ x: 0.5, y: 0.5, name: 'Crossing', rank: 3 }]), fit, cramped);

    expect(calls).toEqual(['arc 500,500 r3.5', 'stroke']);
  });

  it('draws a ring without a name for a ranked station the feed left unnamed', () => {
    const { context, calls } = recordingContext();

    drawStations(context, sceneWith([{ x: 0.5, y: 0.5, name: '', rank: 2 }]), fit, canvas);

    expect(calls).toEqual(['arc 500,500 r3.5', 'stroke']);
  });
});
