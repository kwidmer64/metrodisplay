import type { NetworkScene } from './contract';
import { toPixel, type Fit } from './fit';

const LINE_WIDTH_PX = 2;

// Draws every shape of every line as a polyline in its line's colour.
// context: the canvas to draw the network on
// scene: the collection of lines to draw
// fit: the dimensions of the extent
// returns void
export function drawNetwork(context: CanvasRenderingContext2D, scene: NetworkScene, fit: Fit): void {
    context.lineWidth = LINE_WIDTH_PX;
    context.lineJoin = 'round';
    context.lineCap = 'round';

    // loop through each line
    for (const line of scene.lines) {

        context.strokeStyle = line.color;

        for (const shape of line.shapes) {
            const points = shape.points;

            // check there are at least two coordinate pairs to draw
            if (points.length < 4) {
                continue;
            }

            // start the path, calculates it's end point in the fit in pixels, move to the calculated point
            context.beginPath();
            const [startX, startY] = toPixel(fit, points[0], points[1]);
            context.moveTo(startX, startY);

            // loop through each coordinate pair. the points array is [x0, y0, x1, y1,...], which is why we increment by 2
            for (let index = 2; index < points.length; index += 2) {
                // calculate the pixel point and draw the line
                const [pixelX, pixelY] = toPixel(fit, points[index], points[index + 1]);
                context.lineTo(pixelX, pixelY);
            }

            // renders the shape
            context.stroke();
        }
    }
}
