import { test } from "node:test";
import assert from "node:assert/strict";
import {
  trafficScale,
  trafficCurve,
} from "../src/LanDeviceMonitor.App/Web/chart.js";
import { rate, total, duration } from "../src/LanDeviceMonitor.App/Web/ui.js";
test("traffic curve preserves samples as controls and ends at the latest point", () => {
  assert.equal(
    trafficCurve([
      [0, 10],
      [10, 0],
      [20, 10],
    ]),
    "M0,10Q10,0 15,5Q20,10 20,10",
  );
  assert.equal(trafficCurve([[5, 8]]), "");
});
test("small real traffic stays visible, empty history has a safe scale", () => {
  assert.ok(trafficScale([{ upload: 0, download: 40 }]).max < 100);
  assert.deepEqual(trafficScale([]), { min: 0, max: 1024 });
  assert.deepEqual(trafficScale([{ upload: 0, download: 0 }]), {
    min: 0,
    max: 1024,
  });
});
test("unknown data is not presented as zero and byte units match the desktop", () => {
  assert.equal(rate(null), "—");
  assert.equal(rate(512), "0.50\u2009KB/s");
  assert.equal(rate(7), "0.01\u2009KB/s");
  assert.equal(total(1024 ** 3), "1.00\u2009GB");
  assert.equal(total(0), "0.00\u2009B");
  assert.equal(rate(234567), "229\u2009KB/s");
  assert.equal(total(67891234), "64.7\u2009MB");
});
test("session duration uses both endpoints and handles long sessions", () => {
  assert.equal(
    duration("2026-09-20T00:00:00Z", "2026-09-22T03:40:00Z"),
    "2\u2009天\u20093\u2009小时",
  );
  assert.equal(
    duration("2026-09-20T00:00:00Z", "2026-09-20T00:00:10Z"),
    "0\u2009分钟",
  );
});
