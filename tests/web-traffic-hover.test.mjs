import { test } from "node:test";
import assert from "node:assert/strict";
import * as chart from "../src/LanDeviceMonitor.App/Web/chart.js";

test("hover columns select the nearest actual sample across a normal sampling interval", () => {
  assert.equal(typeof chart.trafficHoverSample, "function");
  const points = [{ time: 1000, download: 100, upload: null }, { time: 6000, download: 300, upload: 0 }];
  assert.equal(chart.trafficHoverSample(points, 2100, 0, 10000, 5), 0);
  assert.equal(chart.trafficHoverSample(points, 4900, 0, 10000, 5), 1);
  assert.equal(points[1].time, 6000);
  assert.equal(points[1].upload, 0);
});

test("hover never selects a buffered future point or bridges a sampling interruption", () => {
  assert.equal(typeof chart.trafficHoverSample, "function");
  const points = [{ time: 1000, download: 100 }, { time: 6000, download: 300 }, { time: 30000, download: 900 }];
  assert.equal(chart.trafficHoverSample(points, 5800, 0, 5500, 5), null);
  assert.equal(chart.trafficHoverSample(points, 5400, 0, 5500, 5), null);
  assert.equal(chart.trafficHoverSample(points, 18000, 0, 35000, 5), null);
  assert.equal(chart.trafficHoverSample(points, -1, 0, 35000, 5), null);
  assert.equal(chart.trafficHoverSample([{ time: 1000, placeholder: true }, { time: 2000, download: null, upload: null }], 1800, 0, 5000, 5), null);
});
