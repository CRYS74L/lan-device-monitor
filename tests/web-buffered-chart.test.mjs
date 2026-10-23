import { test } from "node:test";
import assert from "node:assert/strict";
import { ChartTimeline, trafficViewportScale } from "../src/LanDeviceMonitor.App/Web/chart.js";

const samples = [
  { time: 0, download: 100, upload: 100 },
  { time: 5000, download: 100, upload: 100 },
  { time: 10000, download: 1_000_000, upload: 100 },
  { time: 15000, download: 100, upload: 100 },
];

test("buffered playback moves at real-time speed through cached responses and resumes without history replay", () => {
  const clock = new ChartTimeline();
  clock.observe(1000, 5, 50);
  assert.equal(clock.read(50), 993);
  assert.equal(clock.read(52.5), 995.5);
  clock.observe(1000, 5, 53);
  assert.equal(clock.read(53), 996);
  clock.observe(1300, 5, 350);
  assert.equal(clock.read(350), 1293);
  assert.equal(clock.read(351), 1294);
});

test("a buffered peak changes the Y axis only as its visible rise enters the viewport", () => {
  assert.deepEqual(trafficViewportScale(samples, 1, 5000), { min: 0, max: 120 });
  const entering = trafficViewportScale(samples, 1, 6000);
  assert.ok(entering.max > 10_000 && entering.max < 200_000,
    `The partially visible rise should control the scale, got ${entering.max}`);
  const nearPeak = trafficViewportScale(samples, 1, 9000);
  assert.ok(nearPeak.max > entering.max && nearPeak.max < 1_000_000);
  assert.equal(trafficViewportScale(samples, 1, 10000).max, 1_099_990);
});

test("an old peak beyond the left clip cannot set the scale for the entire visible descending segment", () => {
  const points = [{ time: 0, download: 1_000_000, upload: null }, { time: 120000, download: 100, upload: null }];
  const scale = trafficViewportScale(points, 1, 120000);
  assert.ok(scale.max > 100 && scale.max < 600_000, `Hidden left peak affected the scale: ${scale.max}`);
});

test("a flat visible interval keeps its scale while a hidden peak waits in the buffer", () => {
  for(let now=17;now<5000;now+=17)
    assert.deepEqual(trafficViewportScale(samples,1,now),{min:0,max:120});
});
