import type { NetworkScene, StationMarker } from './contract';
import { toPixel, type Fit } from './fit';
import { placeLabels, type Box, type LabelRequest } from './labels';

const MARKER_COLOR = '#c5ced8';
// The page background, so a ring reads as hollow over a line.
const RING_CENTRE_COLOR = '#07090c';
const NAME_COLOR = '#9aa7b4';
const NAME_FONT = '11px ui-monospace, monospace';
const NAME_HEIGHT_PX = 13;

const DOT_RADIUS_PX = 1.5;
const RING_RADIUS_PX = 3.5;
const RING_WIDTH_PX = 1.5;

// Rank 1 is an ordinary station. A terminus (2) and an interchange (3) get a ring and a name.
const NAMED_FROM_RANK = 2;

interface PlacedMarker {
    station: StationMarker;
    pixelX: number;
    pixelY: number;
}

/**
 * Draws a marker at every station, and a name beside each terminus and interchange that has
 * room for one. Call after the lines, so markers sit on top of them.
 * bounds: the area names must stay inside, normally the whole canvas.
 */
export function drawStations(context: CanvasRenderingContext2D, scene: NetworkScene, fit: Fit, bounds: Box): void {
    // loop through each station, get the position, and return them as array of PlacedMarker's
    const markers: PlacedMarker[] = scene.stations.map((station) => {
        const [pixelX, pixelY] = toPixel(fit, station.x, station.y);
        return { station, pixelX, pixelY };
    });

    // The font has to be set before names are measured.
    context.font = NAME_FONT;

    // get only the markers that should have labels
    const namedMarkers = markers.filter((marker) => marker.station.rank >= NAMED_FROM_RANK && marker.station.name !== '');

    // create a label request for each marker that needs a label associated with it
    const requests: LabelRequest[] = namedMarkers.map((marker) => ({
        anchorX: marker.pixelX,
        anchorY: marker.pixelY,
        widthPx: context.measureText(marker.station.name).width,
        heightPx: NAME_HEIGHT_PX,
        priority: marker.station.rank,
    }));

    // get the boxes for the labels
    const nameBoxes = placeLabels(requests, {
        bounds,
        obstacles: markers.map(markerBox),
        polylines: linesInPixels(scene, fit),
    });

    // draw every station marker
    for (const marker of markers) {
        drawMarker(context, marker);
    }

    context.fillStyle = NAME_COLOR;
    context.textAlign = 'left';
    context.textBaseline = 'middle';

    // add text to each name box
    nameBoxes.forEach((nameBox, nameIndex) => {
        if (nameBox) {
            context.fillText(namedMarkers[nameIndex].station.name, nameBox.left, (nameBox.top + nameBox.bottom) / 2);
        }
    });
}

function drawMarker(context: CanvasRenderingContext2D, marker: PlacedMarker): void {
    context.beginPath();
    if (marker.station.rank < NAMED_FROM_RANK) {
        context.arc(marker.pixelX, marker.pixelY, DOT_RADIUS_PX, 0, 2 * Math.PI);
        context.fillStyle = MARKER_COLOR;
        context.fill();
        return;
    }

    context.arc(marker.pixelX, marker.pixelY, RING_RADIUS_PX, 0, 2 * Math.PI);
    context.fillStyle = RING_CENTRE_COLOR;
    context.fill();
    context.lineWidth = RING_WIDTH_PX;
    context.strokeStyle = MARKER_COLOR;
    context.stroke();
}

/** The square a marker covers, sized for the larger marker so no name crowds either kind. */
function markerBox(marker: PlacedMarker): Box {
    const reachPx = RING_RADIUS_PX + RING_WIDTH_PX / 2;
    return {
        left: marker.pixelX - reachPx,
        top: marker.pixelY - reachPx,
        right: marker.pixelX + reachPx,
        bottom: marker.pixelY + reachPx,
    };
}

function linesInPixels(scene: NetworkScene, fit: Fit): number[][] {
    const polylines: number[][] = [];
    for (const line of scene.lines) {
        for (const shape of line.shapes) {
        const polyline: number[] = [];
        for (let index = 0; index + 1 < shape.points.length; index += 2) {
            const [pixelX, pixelY] = toPixel(fit, shape.points[index], shape.points[index + 1]);
            polyline.push(pixelX, pixelY);
        }
        polylines.push(polyline);
        }
    }
    return polylines;
}
