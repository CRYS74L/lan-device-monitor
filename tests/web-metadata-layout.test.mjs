import { test } from "node:test";
import assert from "node:assert/strict";
import { metadataLayout } from "../src/LanDeviceMonitor.App/Web/ui.js";

test("wide metadata retains its original four-field order", () => {
  const layout = metadataLayout([48, 51, 110, 136], 500);
  assert.equal(layout.columns, 4);
  assert.deepEqual(layout.order, [0, 1, 2, 3]);
});

test("portrait metadata pairs status with IP and type with MAC", () => {
  const layout = metadataLayout([48, 51, 110, 136], 342);
  assert.equal(layout.columns, 2);
  assert.deepEqual(layout.order.slice(0, 2), [0, 2]);
  assert.deepEqual(layout.order.slice(2), [1, 3]);
});

test("metadata wraps when the original text and spacing no longer fit", () => {
  const widths = [48, 48, 98, 130];
  assert.equal(metadataLayout(widths, 396).columns, 4);
  assert.equal(metadataLayout(widths, 395).columns, 2);
});

test("very narrow long addresses retain the original single-column fallback", () => {
  const layout = metadataLayout([48, 60, 130, 150], 280);
  assert.equal(layout.columns, 1);
  assert.deepEqual(layout.order, [0, 1, 2, 3]);
});
