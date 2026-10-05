import { rate, clock, esc } from "./ui.js";

export function trafficScale(samples) {
  const values = samples.flatMap((s) => [s.upload || 0, s.download || 0]);
  const peak = values.length ? Math.max(...values) : 0;
  if (!peak) return { min: 0, max: 1024 };
  const minimum = Math.min(...values),
    span = peak - minimum;
  return span <= 0
    ? { min: 0, max: peak * 1.2 }
    : { min: Math.max(0, minimum - span * 0.1), max: peak + span * 0.1 };
}

// Match TrafficChart.AppendClashVergeQuadraticCurve: each sample controls
// a quadratic curve ending midway between it and the following sample.
export function trafficCurve(points) {
  if (points.length < 2) return "";
  let result = `M${points[0].join(",")}`;
  for (let i = 1; i < points.length; i++) {
    const current = points[i],
      next = points[Math.min(i + 1, points.length - 1)];
    result += `Q${current.join(",")} ${(current[0] + next[0]) / 2},${(current[1] + next[1]) / 2}`;
  }
  return result;
}

// Timestamp based plotting: gaps and unknown directions remain gaps, never fake zeros.
export class TrafficChart {
  constructor(element) {
    this.el = element;
    this.samples = [];
    this.cursor = null;
    this.range = 10;
    element.innerHTML = `<svg viewBox="0 0 640 232" preserveAspectRatio="none" aria-hidden="true"><defs>${["download", "upload"].map((direction) => `<linearGradient id="${direction}-fill" x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stop-opacity=".15"/><stop offset="100%" stop-opacity="0"/></linearGradient>`).join("")}</defs><g class="chart-grid"></g><path class="chart-area download" fill="url(#download-fill)"/><path class="chart-line download"/><path class="chart-area upload" fill="url(#upload-fill)"/><path class="chart-line upload"/><g class="chart-cursor" visibility="hidden"><path class="cursor-rule"/><circle class="download-point" r="4"/><circle class="upload-point" r="4"/></g></svg><div class="chart-empty">等待新的流量采样</div><output class="chart-tooltip" hidden></output>`;
    element.addEventListener("pointerdown", (e) => {
      this.select(e);
      if (e.pointerType !== "mouse") element.setPointerCapture(e.pointerId);
    });
    element.addEventListener("pointermove", (e) => {
      if (e.pointerType === "mouse" || e.buttons) this.select(e);
    });
    element.addEventListener("pointerleave", (e) => {
      if (e.pointerType === "mouse") this.clear();
    });
    element.addEventListener("pointercancel", () => this.clear());
    element.addEventListener("blur", () => this.clear());
    element.addEventListener("keydown", (e) => {
      if (e.key === "Escape") {
        this.clear();
        return;
      }
      if (!["ArrowLeft", "ArrowRight"].includes(e.key)) return;
      e.preventDefault();
      const current =
        this.cursor == null
          ? this.samples.length - 1
          : this.samples.findIndex((p) => p.time === this.cursor);
      const i = Math.max(
        0,
        Math.min(
          this.samples.length - 1,
          current + (e.key === "ArrowLeft" ? -1 : 1),
        ),
      );
      if (this.samples[i]) {
        this.cursor = this.samples[i].time;
        this.drawCursor();
      }
    });
    this.resizeObserver = new ResizeObserver(() => {
      if (this.cached && this.el.clientWidth > 0) this.update(...this.cached);
    });
    this.resizeObserver.observe(element);
  }
  update(samples, now, range = 10, interval = 1) {
    this.cached = [samples, now, range, interval];
    this.width = this.el.clientWidth || 600;
    this.height = this.el.clientHeight || 176;
    this.left = 60;
    this.right = Math.max(this.left + 1, this.width - 16);
    this.top = 23;
    this.bottom = Math.max(this.top + 1, this.height - 32);
    this.el
      .querySelector("svg")
      .setAttribute("viewBox", `0 0 ${this.width} ${this.height}`);
    this.range = range;
    this.end = new Date(now || Date.now()).getTime();
    this.start = this.end - range * 60000;
    this.samples = (samples || [])
      .map((s) => ({ ...s, time: new Date(s.at).getTime() }))
      .filter((s) => s.time >= this.start && s.time <= this.end);
    const scale = trafficScale(this.samples);
    this.max = scale.max;
    this.min = scale.min;
    this.x = (t) =>
      this.left +
      ((t - this.start) / (this.end - this.start)) * (this.right - this.left);
    this.y = (v) =>
      this.bottom -
      ((Math.max(this.min, v) - this.min) / Math.max(1, this.max - this.min)) *
        (this.bottom - this.top);
    let grid = "";
    for (let i = 0; i <= 4; i++) {
      const y = this.top + (i * (this.bottom - this.top)) / 4;
      grid += `<path d="M${this.left} ${y}H${this.right}"/>`;
    }
    for (let i = 0; i <= 6; i++) {
      const x = this.left + (i * (this.right - this.left)) / 6;
      grid += `<path d="M${x} ${this.top}V${this.bottom}"/>`;
    }
    for (let i = 0; i < 3; i++) {
      const y = this.top + (i * (this.bottom - this.top)) / 2;
      grid += `<text x="${this.left - 7}" y="${y + 4}" text-anchor="end">${esc(rate(this.max - ((this.max - this.min) * i) / 2).replace("/s", ""))}</text>`;
    }
    const labels = Math.max(
      2,
      Math.min(
        range === 10 ? 8 : 6,
        Math.floor(
          (this.right - this.left) / (range === 1 ? 35 : range === 5 ? 38 : 40),
        ) + 1,
      ),
    );
    for (let i = 0; i < labels; i++) {
      const ratio = i / (labels - 1),
        t = this.start + (this.end - this.start) * ratio;
      const label = range === 1 ? clock(t).slice(3) : clock(t, false);
      grid += `<text class="time-label" x="${this.left + (this.right - this.left) * ratio}" y="${this.bottom + 18}" text-anchor="${i === 0 ? "start" : i === labels - 1 ? "end" : "middle"}">${label}</text>`;
    }
    this.el.querySelector(".chart-grid").innerHTML = grid;
    for (const direction of ["download", "upload"]) {
      let path = "",
        area = "",
        segment = [],
        previous = 0;
      const finish = () => {
        if (segment.length < 2) {
          segment = [];
          return;
        }
        const curve = trafficCurve(segment);
        path += curve;
        area +=
          curve +
          `L${segment.at(-1)[0]},${this.bottom}L${segment[0][0]},${this.bottom}Z`;
        segment = [];
      };
      for (const p of this.samples) {
        if (
          p[direction] == null ||
          (previous && p.time - previous > Math.max(4000, interval * 2000))
        )
          finish();
        if (p[direction] != null)
          segment.push([
            +this.x(p.time).toFixed(2),
            +this.y(p[direction]).toFixed(2),
          ]);
        previous = p.time;
      }
      finish();
      this.el.querySelector(".chart-line." + direction).setAttribute("d", path);
      this.el.querySelector(".chart-area." + direction).setAttribute("d", area);
    }
    this.el.querySelector(".chart-empty").hidden = this.samples.length > 0;
    this.drawCursor();
  }
  select(e) {
    if (!this.samples.length) return;
    const rect = this.el.getBoundingClientRect();
    if (
      e.clientX < rect.left + this.left ||
      e.clientX > rect.left + this.right
    ) {
      this.clear();
      return;
    }
    const target =
      this.start +
      ((e.clientX - rect.left - this.left) / (this.right - this.left)) *
        (this.end - this.start);
    this.cursor = this.samples.reduce((a, b) =>
      Math.abs(a.time - target) < Math.abs(b.time - target) ? a : b,
    ).time;
    this.drawCursor();
  }
  clear() {
    this.cursor = null;
    this.drawCursor();
  }
  drawCursor() {
    const group = this.el.querySelector(".chart-cursor"),
      tip = this.el.querySelector("output");
    const point = this.samples.find((p) => p.time === this.cursor);
    if (!point) {
      group.setAttribute("visibility", "hidden");
      tip.hidden = true;
      return;
    }
    group.setAttribute("visibility", "visible");
    const x = this.x(point.time);
    group
      .querySelector("path")
      .setAttribute("d", `M${x},${this.top}V${this.bottom}`);
    for (const direction of ["download", "upload"]) {
      const circle = group.querySelector("." + direction + "-point");
      circle.setAttribute(
        "visibility",
        point[direction] == null ? "hidden" : "visible",
      );
      circle.setAttribute("cx", x);
      circle.setAttribute("cy", this.y(point[direction] || 0));
    }
    tip.hidden = false;
    const markup = `<b>${clock(point.at)}</b><span class="up">上传 ${rate(point.upload)}</span><span class="down">下载 ${rate(point.download)}</span>`;
    if (tip.innerHTML !== markup) tip.innerHTML = markup;
    tip.style.left = `${Math.min(58, Math.max(2, (x / this.width) * 100 - 12))}%`;
  }
}
