import { describe, expect, it } from 'vitest';
import { containFit, toPixel } from './fit';

describe('containFit', () => {
  it('fills the width with a wide map and centres it vertically', () => {
    // Aspect 2: the map is 1 unit wide and 0.5 tall, so width is the constraint.
    expect(containFit(1000, 1000, 2, 0)).toEqual({ scale: 1000, originX: 0, originY: 250 });
  });

  it('fills the height with a tall map and centres it horizontally', () => {
    // Aspect 0.5: the map is 0.5 units wide and 1 tall, so height is the constraint.
    expect(containFit(1000, 500, 0.5, 0)).toEqual({ scale: 500, originX: 375, originY: 0 });
  });

  it('keeps the margin clear on the constraining axis', () => {
    expect(containFit(1000, 1000, 1, 50)).toEqual({ scale: 900, originX: 50, originY: 50 });
  });

  it('returns a zero scale for a container that has not been laid out', () => {
    expect(containFit(0, 0, 1.2, 24)).toEqual({ scale: 0, originX: 0, originY: 0 });
  });
});

describe('toPixel', () => {
  it('maps normalized coordinates through the fit', () => {
    expect(toPixel({ scale: 900, originX: 50, originY: 100 }, 0.5, 0.25)).toEqual([500, 325]);
  });
});
