import { test } from "node:test";
import assert from "node:assert/strict";
import * as chart from "../src/LanDeviceMonitor.App/Web/chart.js";

test("hover motion shares one interruptible timeline without tweening displayed rates", () => {
  assert.equal(typeof chart.ChartHoverMotion, "function");
  const motion=new chart.ChartHoverMotion();
  const first={x:100,uploadY:30,downloadY:60,tooltipX:112,tooltipY:31};
  const second={x:300,uploadY:90,downloadY:20,tooltipX:180,tooltipY:31};
  assert.deepEqual(motion.update(1,first,true,true,1000,160),first);
  assert.deepEqual(motion.update(2,second,true,true,1010,160),first);
  const middle=motion.update(2,second,true,true,1090,160);
  assert.ok(middle.x>100&&middle.x<300);
  const progress=(middle.x-first.x)/(second.x-first.x);
  assert.ok(Math.abs((middle.downloadY-first.downloadY)/(second.downloadY-first.downloadY)-progress)<1e-10);
  assert.ok(Math.abs((middle.tooltipX-first.tooltipX)/(second.tooltipX-first.tooltipX)-progress)<1e-10);
  assert.deepEqual(motion.update(3,first,true,true,1090,160),middle);
  assert.deepEqual(motion.update(3,first,true,true,1250,160),first);
  motion.reset();
  assert.deepEqual(motion.update(4,second,true,true,1260,160),second);
  assert.deepEqual(motion.update(5,first,true,true,1270,0),first);
});

test("scrolling does not restart hover motion and missing directions do not fly in from zero", () => {
  assert.equal(typeof chart.ChartHoverMotion, "function");
  const motion=new chart.ChartHoverMotion();
  const first={x:100,uploadY:0,downloadY:60,tooltipX:112,tooltipY:31};
  const second={x:300,uploadY:90,downloadY:20,tooltipX:180,tooltipY:31};
  motion.update(1,first,false,true,0,160);
  assert.equal(motion.update(2,second,true,true,1,160).uploadY,90);
  const scrolled={...second,x:298,tooltipX:178};
  assert.deepEqual(motion.update(2,scrolled,true,true,161,160),scrolled);
});

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
