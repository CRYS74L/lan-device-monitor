import { ECPolyline } from "echarts/lib/chart/line/poly.js";

// Let the original ECharts path builder calculate the display-space controls.
// Return data-space controls so the existing continuously moving viewport can
// translate the retained path without changing the measured samples.
export function echartsSegments(points, scaleX = 1, scaleY = 1) {
  if (points.length < 2) return [];
  const segments = [];
  let previous;
  const restore = (x, y) => [x / scaleX, y / scaleY];
  const context = {
    moveTo(x, y) { previous = restore(x, y); },
    bezierCurveTo(x1, y1, x2, y2, x, y) {
      const end = restore(x, y);
      segments.push([previous, restore(x1, y1), restore(x2, y2), end]);
      previous = end;
    },
  };
  ECPolyline.prototype.buildPath.call({}, context, {
    points: points.flatMap(([x, y]) => [x * scaleX, y * scaleY]),
    smooth: 0.5,
    smoothMonotone: "none",
    connectNulls: false,
  });
  return segments;
}
