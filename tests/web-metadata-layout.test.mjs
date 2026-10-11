import { test } from "node:test";
import assert from "node:assert/strict";
import * as ui from "../src/LanDeviceMonitor.App/Web/ui.js";

test("wide connection metadata keeps natural text widths and distributes four fields", () => {
  assert.equal(typeof ui.metadataLayout, "function", "the single-row metadata layout is missing");
  const layout = ui.metadataLayout([48, 51, 110, 136], 500);
  assert.equal(layout.scale, 1);
  assert.deepEqual(layout.widths, [48, 51, 110, 136]);
  assert.ok(Math.abs(layout.gap - 51.6666667) < 0.00001);
});

test("a portrait connection row fits all four full fields on the same line", () => {
  const layout = ui.metadataLayout([48, 51, 110, 136], 342);
  assert.equal(layout.widths.length, 4);
  assert.ok(Math.abs(layout.scale - 0.9268292683) < 0.00001);
  assert.ok(Math.abs(layout.gap - 7.4146341463) < 0.00001);
  assert.ok(Math.abs(layout.widths.reduce((sum, width) => sum + width, 0) + layout.gap * 3 - 342) < 0.00001);
});

test("short values use reduced spacing before their type is reduced", () => {
  const layout = ui.metadataLayout([48, 48, 98, 130], 352);
  assert.equal(layout.scale, 1);
  assert.deepEqual(layout.widths, [48, 48, 98, 130]);
  assert.ok(Math.abs(layout.gap - 9.3333333333) < 0.00001);
});

test("narrow and long-address rows never generate extra rows or overflow", () => {
  const layout = ui.metadataLayout([48, 60, 130, 150], 280);
  assert.equal(layout.widths.length, 4);
  assert.ok(layout.scale > 0 && layout.scale < 1);
  assert.ok(layout.widths.every(width => width > 0));
  assert.ok(layout.widths.reduce((sum, width) => sum + width, 0) + layout.gap * 3 <= 280.00001);
});
