import { describe, expect, it } from 'vitest';
import { LABEL_GAP_PX, placeLabels, segmentCrossesBox, type Box, type LabelRequest, type LabelSurroundings } from './labels';

const wideOpen: Box = { left: 0, top: 0, right: 1000, bottom: 1000 };

function surroundings(overrides: Partial<LabelSurroundings> = {}): LabelSurroundings {
  return { bounds: wideOpen, obstacles: [], polylines: [], ...overrides };
}

/** A name 40 px wide and 10 px tall beside a marker at the given point. */
function request(anchorX: number, anchorY: number, priority = 1): LabelRequest {
  return { anchorX, anchorY, widthPx: 40, heightPx: 10, priority };
}

describe('placeLabels', () => {
  it('puts a name to the right of its marker when nothing is in the way', () => {
    const [box] = placeLabels([request(500, 500)], surroundings());

    expect(box).toEqual({ left: 500 + LABEL_GAP_PX, top: 495, right: 546, bottom: 505 });
  });

  it('moves a name to the left when the right would cross a line', () => {
    // A line running east from the marker, as a terminus has on one side.
    const [box] = placeLabels([request(500, 500)], surroundings({ polylines: [[500, 500, 900, 500]] }));

    expect(box).toEqual({ left: 454, top: 495, right: 494, bottom: 505 });
  });

  it('goes above or below a line that runs through the marker both ways', () => {
    const [box] = placeLabels([request(500, 500)], surroundings({ polylines: [[100, 500, 900, 500]] }));

    // Right and above: the third position tried.
    expect(box).toEqual({ left: 506, top: 484, right: 546, bottom: 494 });
  });

  it('accepts crossing a line when every position crosses one', () => {
    // A grid of lines 8 px apart leaves no clear gap for a 10 px tall name.
    const grid: number[][] = [];
    for (let lineY = 400; lineY <= 600; lineY += 8) {
      grid.push([0, lineY, 1000, lineY]);
    }

    const [box] = placeLabels([request(500, 500)], surroundings({ polylines: grid }));

    // Falls back to the first position, on the right.
    expect(box).toEqual({ left: 506, top: 495, right: 546, bottom: 505 });
  });

  it('never covers another marker', () => {
    const neighbour: Box = { left: 516, top: 496, right: 524, bottom: 504 };

    const [box] = placeLabels([request(500, 500)], surroundings({ obstacles: [neighbour] }));

    expect(box).toEqual({ left: 454, top: 495, right: 494, bottom: 505 });
  });

  it('keeps two names from overlapping', () => {
    // Markers 4 px apart: both names want the same place on the right.
    const [first, second] = placeLabels([request(500, 500), request(500, 504)], surroundings());

    expect(first).toEqual({ left: 506, top: 495, right: 546, bottom: 505 });
    expect(second).toEqual({ left: 454, top: 499, right: 494, bottom: 509 });
  });

  it('places the higher priority name first, whatever the order they arrive in', () => {
    const [lowPriority, highPriority] = placeLabels([request(500, 500, 2), request(500, 504, 3)], surroundings());

    // The later, more important name takes the right-hand place.
    expect(highPriority).toEqual({ left: 506, top: 499, right: 546, bottom: 509 });
    expect(lowPriority).toEqual({ left: 454, top: 495, right: 494, bottom: 505 });
  });

  it('gives up on a name with nowhere to go, without disturbing the others', () => {
    // Room for one name only: the bounds are barely larger than a name beside its marker.
    const tight: Box = { left: 494, top: 490, right: 550, bottom: 510 };

    const placements = placeLabels([request(500, 500), request(500, 500)], surroundings({ bounds: tight }));

    expect(placements[0]).toEqual({ left: 506, top: 495, right: 546, bottom: 505 });
    expect(placements[1]).toBeNull();
  });

  it('keeps a name wholly on screen', () => {
    // A marker near the right edge: the name would run off, so it goes left.
    const [box] = placeLabels([request(980, 500)], surroundings());

    expect(box).toEqual({ left: 934, top: 495, right: 974, bottom: 505 });
  });

  it('returns nothing for no requests', () => {
    expect(placeLabels([], surroundings())).toEqual([]);
  });
});

describe('segmentCrossesBox', () => {
  const box: Box = { left: 0, top: 0, right: 100, bottom: 100 };

  it('finds a segment that passes through with both ends outside', () => {
    expect(segmentCrossesBox(-50, 50, 150, 50, box)).toBe(true);
    // Across a corner.
    expect(segmentCrossesBox(60, -20, 120, 40, box)).toBe(true);
  });

  it('finds a segment that lies wholly inside', () => {
    expect(segmentCrossesBox(10, 10, 20, 20, box)).toBe(true);
  });

  it('rejects a segment that stays to one side', () => {
    expect(segmentCrossesBox(-50, -10, 150, -10, box)).toBe(false);
    expect(segmentCrossesBox(110, 0, 110, 100, box)).toBe(false);
    // Passes the corner without reaching it.
    expect(segmentCrossesBox(90, -30, 130, 10, box)).toBe(false);
  });

  it('treats a point as a segment of no length', () => {
    expect(segmentCrossesBox(50, 50, 50, 50, box)).toBe(true);
    expect(segmentCrossesBox(150, 50, 150, 50, box)).toBe(false);
  });
});
