import { test } from "node:test";
import assert from "node:assert/strict";
import { trafficScale } from "../src/LanDeviceMonitor.App/Web/chart.js";
import { rate, total, duration } from "../src/LanDeviceMonitor.App/Web/ui.js";
test("small real traffic stays visible, empty history has a safe scale", () => {
  assert.ok(trafficScale([{ upload: 0, download: 40 }]).max < 100);
  assert.deepEqual(trafficScale([]), { min: 0, max: 1024 });
  assert.deepEqual(trafficScale([{ upload: 0, download: 0 }]), {
    min: 0,
    max: 1024,
  });
});
test("unknown data is not presented as zero and byte units match the desktop", () => {
  assert.equal(rate(null), "— ");
  assert.equal(rate(512), "0.5 KB/s");
  assert.equal(rate(7), "0.01 KB/s");
  assert.equal(total(1024 ** 3), "1 GB");
});
test("session duration uses both endpoints and handles long sessions", () => {
  assert.equal(
    duration("2026-09-20T00:00:00Z", "2026-09-22T03:40:00Z"),
    "2 天 3 小时",
  );
  assert.equal(
    duration("2026-09-20T00:00:00Z", "2026-09-20T00:00:10Z"),
    "不足 1 分钟",
  );
});
