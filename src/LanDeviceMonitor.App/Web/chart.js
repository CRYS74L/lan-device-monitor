import { rate, clock, esc } from "./ui.js";

export function trafficScale(samples) {
  // Native CalculateScale ignores placeholders and samples with no directions.
  // The missing direction contributes only to axis bounds, never to a plotted point.
  const values = samples
    .filter((s) => !s.placeholder && (s.upload != null || s.download != null))
    .flatMap((s) => [s.upload ?? 0, s.download ?? 0]);
  const peak = values.length ? Math.max(...values) : 0;
  if (!peak) return { min: 0, max: 1024 };
  const minimum = Math.min(...values),
    span = peak - minimum;
  return span <= 0
    ? { min: 0, max: peak * 1.2 }
    : { min: Math.max(0, minimum - span * 0.1), max: peak + span * 0.1 };
}

function normalizeRange(range) {
  return [1, 5, 10].includes(range) ? range : 10;
}

function timestampAtOffset(time, source) {
  const offset =
    typeof source === "string" && source.match(/(Z|([+-])(\d{2}):(\d{2}))$/);
  if (!offset) return time;
  const minutes =
    offset[1] === "Z"
      ? 0
      : (Number(offset[3]) * 60 + Number(offset[4])) *
        (offset[2] === "-" ? -1 : 1);
  return (
    new Date(time + minutes * 60000).toISOString().slice(0, 19) + offset[1]
  );
}

// TrafficHistoryBuffer normalizes to whole seconds; GetVisibleSamples supplies
// range * 60 + 1 fixed slots ending at the latest received sample, not wall time.
export function trafficSlots(samples, range = 10, now = null) {
  const byTime = new Map();
  for (const sample of samples || []) {
    const timestamp = new Date(sample.at).getTime();
    if (!Number.isFinite(timestamp) || sample.placeholder) continue;
    const time = Math.floor(timestamp / 1000) * 1000;
    byTime.set(time, {
      ...sample,
      at: timestampAtOffset(time, sample.at),
      time,
      upload: Number.isFinite(sample.upload)
        ? Math.max(0, sample.upload)
        : null,
      download: Number.isFinite(sample.download)
        ? Math.max(0, sample.download)
        : null,
      placeholder: false,
    });
  }
  if (!byTime.size) return [];
  const end = now == null ? Math.max(...byTime.keys()) : Math.floor(now / 1000) * 1000,
    count = normalizeRange(range) * 60 + 1;
  const endSource = [...byTime.values()].at(-1).at;
  let start = end - (count - 1) * 1000;
  const before = [...byTime.keys()].filter(t=>t<start).sort((a,b)=>a-b).slice(-2);
  const padding = now != null && before.length ? Math.ceil((start-before[0])/1000) : 0;
  start -= padding*1000;
  const extra = now == null ? 0 : Math.max(0, Math.ceil((Math.max(...byTime.keys()) - end) / 1000));
  return Array.from({ length: count + extra + padding }, (_, i) => {
    const time = start + i * 1000;
    return (
      byTime.get(time) || {
        at: timestampAtOffset(time, endSource),
        time,
        upload: null,
        download: null,
        placeholder: true,
      }
    );
  });
}

export function trafficHoverIndex(samples, x, left, right) {
  if (!samples.length || x < left || x > right) return null;
  const index = Math.round(
    Math.max(0, Math.min(1, (x - left) / Math.max(1, right - left))) *
      (samples.length - 1),
  );
  return samples[index].placeholder ? null : index;
}

export function trafficTimeAxis(samples, range, left, right) {
  if (!samples.length) return [];
  range = normalizeRange(range);
  const distance = range === 1 ? 35 : range === 5 ? 38 : 40;
  const targetLabels = Math.min(range === 10 ? 8 : 6, samples.length);
  const nominalStep = Math.max(
    1,
    Math.floor(samples.length / Math.max(1, targetLabels - 1)),
  );
  const pixelStep = Math.max(
    1,
    Math.ceil((samples.length * distance) / Math.max(1, right - left)),
  );
  const step = Math.max(nominalStep, pixelStep);
  const point = (index) => ({
    index,
    time: samples[index].time,
    at: samples[index].at ?? samples[index].time,
    x: left + (index / Math.max(1, samples.length - 1)) * (right - left),
  });
  const points = [point(0)];
  for (let i = step; i < samples.length - step; i += step)
    points.push(point(i));
  if (samples.length > 1 && right - points.at(-1).x >= distance)
    points.push(point(samples.length - 1));
  return points;
}

export function trafficSeries(samples, direction, x, y, bottom, motion = null) {
  // Native GetRenderableSampleSegments skips unknown slots and joins the real
  // samples on both sides, without adding zero-valued intermediate controls.
  const points = samples
    .filter((p) => !p.placeholder && p[direction] != null)
    .map((p) => [+x(p.time).toFixed(2), +y(p[direction]).toFixed(2)]);
  const slopes=trafficSlopes(points);
  if(motion){
   const real=samples.filter(p=>!p.placeholder && p[direction]!=null),tick=performance.now();
   slopes.forEach((target,i)=>{
    const key=direction+":"+real[i].time,old=motion.get(key);
    if(old){const p=Math.max(0,Math.min(1,(tick-old.started)/350)),value=old.from+(old.to-old.from)*(1-(1-p)**3);
     if(Math.abs(target-old.to)>.001)motion.set(key,{from:value,to:target,started:tick});
     slopes[i]=value;
    }else motion.set(key,{from:target,to:target,started:tick});
   });
   if(motion.size>3000)motion.clear();
  }
  const path = trafficCurve(points, slopes);
  const area = path
    ? path + `L${points.at(-1)[0]},${bottom}L${points[0][0]},${bottom}Z`
    : "";
  return { path, area, points };
}

export function trafficTooltipPosition(
  pointX,
  boxWidth,
  left,
  right,
  top,
  bottom,
  boxHeight,
) {
  let x = pointX + 12;
  if (x + boxWidth > right) x = pointX - boxWidth - 12;
  x = Math.max(left, Math.min(x, Math.max(left, right - boxWidth)));
  let y = top + 8;
  if (y + boxHeight > bottom) y = Math.max(top + 2, bottom - boxHeight - 2);
  return { x, y };
}

export function trafficSlopes(points) {
 const n=points.length, slopes=Array(n).fill(0);
 if(n<2)return slopes;
 const d=points.slice(1).map((p,i)=>(p[1]-points[i][1])/(p[0]-points[i][0]));
 slopes[0]=d[0]; slopes[n-1]=d[n-2];
 for(let i=1;i<n-1;i++){
  if(d[i-1]*d[i]<=0)continue;
  const before=points[i][0]-points[i-1][0], after=points[i+1][0]-points[i][0];
  const w1=2*after+before,w2=after+2*before;
  slopes[i]=(w1+w2)/(w1/d[i-1]+w2/d[i]);
 }
 return slopes;
}
export function trafficCurve(points, slopes = trafficSlopes(points)) {
 if(points.length<2)return "";
 let result=`M${points[0].join(",")}`;
 for(let i=1;i<points.length;i++){
  const a=points[i-1],b=points[i],third=(b[0]-a[0])/3;
  result+=`C${a[0]+third},${Math.max(Math.min(a[1],b[1]),Math.min(Math.max(a[1],b[1]),a[1]+slopes[i-1]*third))} ${b[0]-third},${Math.max(Math.min(a[1],b[1]),Math.min(Math.max(a[1],b[1]),b[1]-slopes[i]*third))} ${b.join(",")}`;
 }
 return result;
}

export class BufferedChartClock {
 constructor(){this.initialized=false;}
 read(now){return this.initialized ? this.from+(this.target-this.from)*Math.max(0,Math.min(1,(now-this.started)/Math.max(.001,this.duration))) : 0;}
 observe(target,previous,now){
  if(this.initialized && target===this.target)return;
  const forward=this.initialized && target>this.target;
  const current=forward?this.read(now):previous;
  this.duration=Math.max(.1,target-(forward?this.target:previous));
  this.from=Math.min(current,target);this.target=target;this.started=now;this.initialized=true;
 }
}

export class TrafficChart {
  constructor(element) {
    this.el = element;
    this.samples = [];
    this.cursor = null;
    this.range = 10;
    this.presentationClock = new BufferedChartClock();
    this.slopeMotion = new Map();
    element.innerHTML = `<svg viewBox="0 0 640 232" preserveAspectRatio="none" aria-hidden="true"><defs>${["download", "upload"].map((direction) => `<linearGradient id="${direction}-fill" x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stop-opacity=".15"/><stop offset="100%" stop-opacity="0"/></linearGradient>`).join("")}</defs><g class="chart-grid"></g><path class="chart-area download" fill="url(#download-fill)"/><path class="chart-line download"/><path class="chart-area upload" fill="url(#upload-fill)"/><path class="chart-line upload"/><g class="chart-cursor" visibility="hidden"><path class="cursor-rule"/><circle class="download-point" r="4"/><circle class="upload-point" r="4"/></g></svg><div class="chart-empty">等待新的流量采样</div><output class="chart-tooltip" hidden></output>`;
    const svg=element.querySelector("svg"), ns="http://www.w3.org/2000/svg";
    const clip=document.createElementNS(ns,"clipPath");clip.id="traffic-plot-clip";
    this.clipRect=document.createElementNS(ns,"rect");clip.append(this.clipRect);svg.querySelector("defs").append(clip);
    const plot=document.createElementNS(ns,"g");plot.setAttribute("clip-path","url(#traffic-plot-clip)");
    for(const path of svg.querySelectorAll(".chart-line,.chart-area"))plot.append(path);
    svg.insertBefore(plot,svg.querySelector(".chart-cursor"));
    element.addEventListener("pointerdown", (e) => {
      if (e.pointerType === "mouse" && e.button !== 0) return;
      this.select(e);
      if (e.pointerType !== "mouse" && this.cursor != null) {
        this.touch = {
          id: e.pointerId,
          x: e.clientX,
          y: e.clientY,
          moved: false,
        };
        element.setPointerCapture(e.pointerId);
      }
    });
    element.addEventListener("pointermove", (e) => {
      if (
        this.touch?.id === e.pointerId &&
        Math.hypot(e.clientX - this.touch.x, e.clientY - this.touch.y) > 8
      )
        this.touch.moved = true;
      if (e.pointerType === "mouse" || e.buttons) this.select(e);
    });
    element.addEventListener("pointerup", (e) => {
      const moved = this.touch?.id === e.pointerId && this.touch.moved;
      this.releasePointer(e.pointerId);
      if (moved) this.clear();
      // A stationary touch retains its inspected sample until scroll/navigation.
    });
    element.addEventListener("lostpointercapture", (e) => {
      if (this.touch?.id === e.pointerId) this.clear();
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
      const real = this.samples.filter((p) => !p.placeholder);
      const current =
        this.cursor == null
          ? real.length - 1
          : real.findIndex((p) => p.time === this.cursor);
      const i = Math.max(
        0,
        Math.min(real.length - 1, current + (e.key === "ArrowLeft" ? -1 : 1)),
      );
      if (real[i]) {
        this.cursor = real[i].time;
        this.drawCursor();
      }
    });
    this.resizeObserver = new ResizeObserver(() => {
      if (this.cached && this.el.clientWidth > 0) this.update(...this.cached);
    });
    this.resizeObserver.observe(element);
    this.motionTimer = setInterval(() => {
      if (this.cached && !document.hidden && this.el.isConnected && this.el.clientWidth > 0)
        this.update(...this.cached);
    }, 33);
  }
  update(samples, now, range = 10, interval = 1) {
    this.cached = [samples, now, range, interval];
    this.width = this.el.clientWidth || 600;
    this.height = this.el.clientHeight || 176;
    this.left = 60;
    this.right = Math.max(this.left + 1, this.width - 24);
    this.top = 23;
    this.bottom = Math.max(this.top + 1, this.height - 32);
    this.el
      .querySelector("svg")
      .setAttribute("viewBox", `0 0 ${this.width} ${this.height}`);
    range = normalizeRange(range);
    if (range !== this.range) this.clear();
    this.range = range;
    const valid=(samples||[]).filter(s=>!s.placeholder);
    let frameNow=Date.now();
    if(valid.length){
      const upload=valid.findLast(s=>s.upload!=null),download=valid.findLast(s=>s.download!=null);
      const target=Math.min(new Date((upload||valid.at(-1)).at).getTime(),new Date((download||valid.at(-1)).at).getTime());
      const previous=new Date((valid.findLast(s=>new Date(s.at).getTime()<target)||valid.at(-1)).at).getTime();
      const tick=performance.now()/1000;
      this.presentationClock.observe(target/1000,previous/1000,tick);
      frameNow=this.presentationClock.read(tick)*1000;
    }
    for(const [key,value] of Object.entries({x:this.left,y:this.top,width:this.right-this.left,height:this.bottom-this.top}))
      this.clipRect.setAttribute(key,value);
    this.samples = trafficSlots(samples, range, frameNow);
    this.end = frameNow;
    this.start = frameNow - range * 60000;
    const scale = trafficScale(this.samples);
    const tick=performance.now(),blend=1-Math.exp(-(tick-(this.scaleTick??tick-1000))/120);
    this.scaleTick=tick;
    this.max=this.max==null?scale.max:this.max+(scale.max-this.max)*blend;
    this.min=this.min==null?scale.min:this.min+(scale.min-this.min)*blend;
    this.x = (t) =>
      this.left +
      ((t - this.start) / Math.max(1, this.end - this.start)) *
        (this.right - this.left);
    this.y = (v) =>
      this.bottom -
      Math.max(
        0,
        Math.min(
          1,
          (Math.max(this.min, v) - this.min) / Math.max(1, this.max - this.min),
        ),
      ) *
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
    const labels = trafficTimeAxis(this.samples.filter(s=>s.time>=this.start).slice(0, range * 60 + 1), range, this.left, this.right);
    for (let i = 0; i < labels.length; i++) {
      const point = labels[i];
      const label =
        range === 1 ? clock(point.at).slice(3) : clock(point.at, false);
      grid += `<text class="time-label" x="${point.x}" y="${this.bottom + 18}" text-anchor="${i === 0 ? "start" : i === labels.length - 1 ? "end" : "middle"}">${label}</text>`;
    }
    this.el.querySelector(".chart-grid").innerHTML = grid;
    for (const direction of ["download", "upload"]) {
      const { path, area } = trafficSeries(
        this.samples,
        direction,
        this.x,
        this.y,
        this.bottom,
        this.slopeMotion,
      );
      this.el.querySelector(".chart-line." + direction).setAttribute("d", path);
      this.el.querySelector(".chart-area." + direction).setAttribute("d", area);
    }
    this.el.querySelector(".chart-empty").hidden = this.samples.some(
      (p) => p.upload != null || p.download != null,
    );
    this.drawCursor();
  }
  select(e) {
    if (!this.samples.length) return;
    const rect = this.el.getBoundingClientRect();
    // Pointer coordinates are rendered pixels; the plot uses layout pixels.
    const localX =
      ((e.clientX - rect.left) * this.width) / Math.max(1, rect.width);
    const target=this.start+(localX-this.left)/(this.right-this.left)*(this.end-this.start);
    const rounded=Math.round(target/1000)*1000;
    const found=this.samples.findIndex(s=>s.time===rounded && !s.placeholder);
    const index=found<0?null:found;
    if (index == null || this.width < 100 || this.height < 80) {
      this.clear();
      return;
    }
    this.cursor = this.samples[index].time;
    this.drawCursor();
  }
  clear() {
    this.releasePointer(this.touch?.id);
    this.cursor = null;
    this.drawCursor();
  }
  releasePointer(id) {
    if (id == null) return;
    if (this.touch?.id === id) this.touch = null;
    if (this.el.hasPointerCapture(id)) this.el.releasePointerCapture(id);
  }
  drawCursor() {
    const group = this.el.querySelector(".chart-cursor"),
      tip = this.el.querySelector("output");
    group
      .querySelectorAll("circle")
      .forEach((circle) => circle.setAttribute("visibility", "hidden"));
    const point = this.samples.find((p) => p.time === this.cursor);
    if (!point || point.placeholder) {
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
      circle.setAttribute("r", "3.5");
      circle.setAttribute("stroke", "var(--tool-tip)");
      circle.setAttribute("stroke-width", "3");
      circle.setAttribute("paint-order", "stroke fill");
      circle.setAttribute(
        "visibility",
        point[direction] == null ? "hidden" : "visible",
      );
      circle.setAttribute("cx", x);
      circle.setAttribute("cy", this.y(point[direction] || 0));
    }
    tip.hidden = false;
    const markup = `<b>${clock(point.at)}</b><span class="up">↑ ${rate(point.upload)}</span><span class="down">↓ ${rate(point.download)}</span>`;
    if (tip.innerHTML !== markup) tip.innerHTML = markup;
    tip.style.maxWidth = Math.max(1, this.right - this.left) + "px";
    tip.style.overflow = "hidden";
    const position = trafficTooltipPosition(
      x,
      tip.offsetWidth,
      this.left,
      this.right,
      this.top,
      this.bottom,
      tip.offsetHeight,
    );
    tip.style.left = position.x + "px";
    tip.style.top = position.y + "px";
  }
}
