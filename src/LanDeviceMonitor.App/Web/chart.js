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

// Timestamp based plotting: gaps and unknown directions remain gaps, never fake zeros.
export class TrafficChart {
  constructor(element) {
    this.el = element;
    this.samples = [];
    this.cursor = null;
    this.range = 10;
    element.innerHTML = `<svg viewBox="0 0 640 232" preserveAspectRatio="none" aria-hidden="true"><defs><linearGradient id="download-fill" x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stop-color="#1681f7" stop-opacity=".13"/><stop offset="100%" stop-color="#1681f7" stop-opacity="0"/></linearGradient></defs><g class="chart-grid"></g><path class="chart-area" fill="url(#download-fill)"/><path class="chart-line download"/><path class="chart-line upload"/><g class="chart-cursor" visibility="hidden"><path class="cursor-rule"/><circle class="download-point" r="4"/><circle class="upload-point" r="4"/></g></svg><div class="chart-empty">等待新的流量采样</div><output class="chart-tooltip" hidden></output>`;
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
    this.height = this.el.clientHeight || 232;
    this.left = 46;
    this.right = this.width - 8;
    this.top = 26;
    this.bottom = this.height - 30;
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
    for (let i = 0; i < 4; i++) {
      const y = this.top + (i * (this.bottom - this.top)) / 3;
      grid += `<path d="M${this.left} ${y}H${this.right}"/><text x="${this.left - 6}" y="${y + 4}" text-anchor="end">${esc(rate(this.max - ((this.max - this.min) * i) / 3).replace("/s", ""))}</text>`;
    }
    for (let i = 0; i < 3; i++) {
      const t = this.start + ((this.end - this.start) * i) / 2;
      grid += `<text x="${this.left + (i * (this.right - this.left)) / 2}" y="${this.height - 5}" text-anchor="${i === 0 ? "start" : i === 2 ? "end" : "middle"}">${clock(t).slice(0, 5)}</text>`;
    }
    this.el.querySelector(".chart-grid").innerHTML = grid;
    for (const direction of ["download", "upload"]) {
      let path = "",
        area = "",
        segment = [],
        previous = 0;
      const finish = () => {
        if (!segment.length) return;
        path += "M" + segment.map((p) => p.join(",")).join("L");
        area +=
          `M${segment[0][0]},${this.bottom}L` +
          segment.map((p) => p.join(",")).join("L") +
          `L${segment.at(-1)[0]},${this.bottom}Z`;
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
      if (direction === "download")
        this.el.querySelector(".chart-area").setAttribute("d", area);
    }
    this.el.querySelector(".chart-empty").hidden = this.samples.length > 0;
    this.drawCursor();
  }
  select(e) {
    if (!this.samples.length) return;
    const rect = this.el.getBoundingClientRect();
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
    const markup = `<b>${clock(point.at)}</b><span class="down">↓ ${rate(point.download)}</span><span class="up">↑ ${rate(point.upload)}</span>`;
    if (tip.innerHTML !== markup) tip.innerHTML = markup;
    tip.style.left = `${Math.min(58, Math.max(2, (x / this.width) * 100 - 12))}%`;
  }
}
