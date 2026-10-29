/** A rectangle in CSS pixels. */
export interface Box {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

/** A name that wants a place beside its marker. */
export interface LabelRequest {
  /** Centre of the marker the name belongs to. */
  anchorX: number;
  anchorY: number;
  widthPx: number;
  heightPx: number;
  /** Names with a higher priority are placed first, so they get the better positions. */
  priority: number;
}

/** What a name has to fit among. */
export interface LabelSurroundings {
  /** A name must lie wholly inside this. */
  bounds: Box;
  /** Markers. A name never covers one. */
  obstacles: Box[];
  /** Lines, each a flat [x0, y0, x1, y1, ...] array in pixels. A name avoids crossing one when it can. */
  polylines: number[][];
}

/** Clear space between a marker's centre and its name. Larger than any marker's radius. */
export const LABEL_GAP_PX = 6;

/**
 * Finds a place for each name, highest priority first. A name takes the first position around
 * its marker that overlaps no name already placed and no marker, preferring one that crosses
 * no line.
 * Returns one entry per request, in the same order: the name's box, or null when it has
 * nowhere to go.
 */
export function placeLabels(requests: LabelRequest[], surroundings: LabelSurroundings): (Box | null)[] {
  const placements: (Box | null)[] = requests.map(() => null);
  const placedBoxes: Box[] = [];

  // Highest priority first. Equal priorities keep the order they arrived in.
  const order = requests
    .map((_request, requestIndex) => requestIndex)
    .sort((firstIndex, secondIndex) => requests[secondIndex].priority - requests[firstIndex].priority || firstIndex - secondIndex);

  for (const requestIndex of order) {
    const freeBoxes = candidateBoxes(requests[requestIndex]).filter( // get a list of potential boxes for the specified label
      (box) =>
        isInside(box, surroundings.bounds) && // filter by placements that are within the bounds
        !placedBoxes.some((placedBox) => overlaps(box, placedBox)) && // filter by boxes that don't overlap any of the previously placed boxes
        !surroundings.obstacles.some((obstacle) => overlaps(box, obstacle)), // filter by boxes that don't overlap any obstacles (markers) from the label surroundings
    );

    // choose a box that doesn't cross any line, fallback to the first box in the array
      const chosenBox = freeBoxes.find((box) => !crossesAnyLine(box, surroundings.polylines)) ?? freeBoxes[0];

      // if we have chosen a box, set this labels placement to said box, and add to the list of already placed boxes
    if (chosenBox) {
      placements[requestIndex] = chosenBox;
      placedBoxes.push(chosenBox);
    }
  }

  return placements;
}

/** The eight positions tried, in order of preference: beside the marker first, then diagonal, then above and below. */
function candidateBoxes(request: LabelRequest): Box[] {
  const { anchorX, anchorY, widthPx, heightPx } = request;
  const eastLeft = anchorX + LABEL_GAP_PX;
  const westLeft = anchorX - LABEL_GAP_PX - widthPx;
  const centredLeft = anchorX - widthPx / 2;
  const middleTop = anchorY - heightPx / 2;
  const aboveTop = anchorY - LABEL_GAP_PX - heightPx;
  const belowTop = anchorY + LABEL_GAP_PX;

  const corners: [number, number][] = [
    [eastLeft, middleTop],
    [westLeft, middleTop],
    [eastLeft, aboveTop],
    [eastLeft, belowTop],
    [westLeft, aboveTop],
    [westLeft, belowTop],
    [centredLeft, aboveTop],
    [centredLeft, belowTop],
  ];
  return corners.map(([left, top]) => ({ left, top, right: left + widthPx, bottom: top + heightPx }));
}

// helper function to check that a box is within specified bounds
function isInside(box: Box, bounds: Box): boolean {
  return box.left >= bounds.left && box.top >= bounds.top && box.right <= bounds.right && box.bottom <= bounds.bottom;
}

// helper function to check whether two boxes overlap.
// boxes that share an edge do not overlap.
function overlaps(first: Box, second: Box): boolean {
  return first.left < second.right && second.left < first.right && first.top < second.bottom && second.top < first.bottom;
}

// helper function to check if a box crosses a lines
function crossesAnyLine(box: Box, polylines: number[][]): boolean {
  for (const polyline of polylines) {
    for (let index = 2; index < polyline.length; index += 2) {
      if (segmentCrossesBox(polyline[index - 2], polyline[index - 1], polyline[index], polyline[index + 1], box)) {
        return true;
      }
    }
  }
  return false;
}

/**
 * Whether a segment passes through a box. Each side of the box narrows the part of the
 * segment, as a fraction from 0 at its start to 1 at its end, that lies inside (Liang-Barsky).
 */
export function segmentCrossesBox(startX: number, startY: number, endX: number, endY: number, box: Box): boolean {
  const changeX = endX - startX;
  const changeY = endY - startY;
  let entryFraction = 0;
  let exitFraction = 1;

  // Each pair is how fast the segment moves toward the outside of one side, and how far
  // inside that side it starts.
  const sides: [number, number][] = [
    [-changeX, startX - box.left],
    [changeX, box.right - startX],
    [-changeY, startY - box.top],
    [changeY, box.bottom - startY],
  ];

  for (const [direction, distance] of sides) {
    if (direction === 0) {
      // Parallel to this side: outside it for the whole length, or not at all.
      if (distance < 0) {
        return false;
      }
      continue;
    }

    const fraction = distance / direction;
    if (direction < 0) {
      entryFraction = Math.max(entryFraction, fraction);
    } else {
      exitFraction = Math.min(exitFraction, fraction);
    }
    if (entryFraction > exitFraction) {
      return false;
    }
  }

  return true;
}
