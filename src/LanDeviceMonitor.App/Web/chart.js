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

export function trafficViewportScale(samples, range, now) {
  const first=samples.findIndex(p=>p.time>=now-normalizeRange(range)*60000);
  return trafficScale(first<0?[]:samples.slice(Math.max(0,first-2)).filter(p=>p.time<=now));
}

export function trafficLiveTail(sample, direction, start, end, live = true) {
  if (!live || sample?.[direction] == null || sample.time < start || sample.time > end) return null;
  return { startTime: sample.time, endTime: end, value: sample[direction] };
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
export function trafficSlots(samples, range = 10, now = null, presentationRange = normalizeRange(range)) {
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
    count = Math.ceil(presentationRange * 60) + 1;
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

export function trafficTimeAxisWindow(start, end, range, left, right, offsetSource = null) {
  range = normalizeRange(range);
  const spacing = range === 1 ? 35 : range === 5 ? 38 : 40;
  const count = Math.min(range === 10 ? 8 : 6, Math.max(1, Math.floor((right-left)/spacing)+1));
  return Array.from({length:count},(_,i)=>{
    const progress = count===1 ? 0 : i/(count-1), time=start+(end-start)*progress;
    return {time, at:timestampAtOffset(time,offsetSource), x:left+(right-left)*progress};
  });
}

export function trafficTimeAxis(samples, range, left, right) {
  if (!samples.length) return [];
  const ticks=trafficTimeAxisWindow(samples[0].time,samples.at(-1).time,range,left,right,samples.at(-1).at);
  return ticks.map((point,i)=>({...point,index:Math.round((samples.length-1)*(ticks.length===1?0:i/(ticks.length-1)))}));
}

function prepareTrafficData(samples) {
  const byTime=new Map();
  for(const sample of samples || []) {
    const timestamp=new Date(sample.at).getTime();
    if(!Number.isFinite(timestamp)||sample.placeholder)continue;
    const time=Math.floor(timestamp/1000)*1000;
    byTime.set(time,{...sample,time,at:timestampAtOffset(time,sample.at),placeholder:false,
      upload:Number.isFinite(sample.upload)?Math.max(0,sample.upload):null,
      download:Number.isFinite(sample.download)?Math.max(0,sample.download):null});
  }
  return [...byTime.values()].sort((a,b)=>a.time-b.time);
}
export function trafficSeries(samples, direction, x, y, bottom, motion = null, motionDuration = 220, slopeScale = null) {
  // Native GetRenderableSampleSegments skips unknown slots and joins the real
  // samples on both sides, without adding zero-valued intermediate controls.
  const points = samples
    .filter((p) => !p.placeholder && p[direction] != null)
    .map((p) => [x(p.time), y(p[direction])]);
  const real=samples.filter(p=>!p.placeholder && p[direction]!=null);
  const slopes=trafficSlopes(motion && slopeScale != null
    ? real.map(p=>[p.time/1000,p[direction]]) : points);
  if(motion){
   const tick=performance.now();
   slopes.forEach((target,i)=>{
    const key=direction+":"+real[i].time,old=motion.get(key);
    const value=old ? old.retarget(target,tick,motionDuration) : new ChartValueMotion(target,target,tick);
    motion.set(key,value);
    slopes[i]=value.read(tick,motionDuration)*(slopeScale??1);
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

export class ChartTimeline {
  constructor(){this.initialized=false;}
  read(now){return this.initialized?this.anchorTime+Math.max(0,now-this.anchorTick):0;}
  observe(sourceTime,latestSampleTime,now){
    const current=this.initialized?this.read(now):sourceTime;
    if(!this.initialized||sourceTime!==this.sourceTime||latestSampleTime>current){
      this.anchorTime=Math.max(sourceTime,latestSampleTime,current);
      this.anchorTick=now;this.sourceTime=sourceTime;
    }
    this.initialized=true;
  }
  reset(){this.initialized=false;}
}

export class ChartValueMotion {
  constructor(from, to = from, started = 0) { this.from=from; this.to=to; this.started=started; }
  read(now, duration) {
    const p=duration<=0?1:Math.max(0,Math.min(1,(now-this.started)/duration));
    return this.from+(this.to-this.from)*(1-(1-p)**3);
  }
  retarget(target, now, duration) {
    return target===this.to?this:new ChartValueMotion(this.read(now,duration),target,now);
  }
}

export class TrafficChart {
  constructor(element, rangeControl = null) {
    this.el = element;
    this.samples = [];
    this.cursor = null;
    this.range = 1;
    this.rangeControl = rangeControl;
    this.rangeMotion = new ChartValueMotion(1);
    this.sourceReference = null;
    this.presentationClock = new ChartTimeline();
    this.slopeMotion = new Map();
    element.innerHTML = `<svg viewBox="0 0 640 232" preserveAspectRatio="none" aria-hidden="true"><defs>${["download", "upload"].map((direction) => `<linearGradient id="${direction}-fill" x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stop-opacity=".15"/><stop offset="100%" stop-opacity="0"/></linearGradient>`).join("")}</defs><g class="chart-grid"></g><path class="chart-area download" fill="url(#download-fill)"/><path class="chart-line download"/><path class="chart-area upload" fill="url(#upload-fill)"/><path class="chart-line upload"/><g class="chart-cursor" visibility="hidden"><path class="cursor-rule"/><circle class="download-point" r="4"/><circle class="upload-point" r="4"/></g></svg><div class="chart-empty">等待新的流量采样</div><output class="chart-tooltip" hidden></output>`;
    const svg=element.querySelector("svg"), ns="http://www.w3.org/2000/svg";
    const clip=document.createElementNS(ns,"clipPath");clip.id="traffic-plot-clip";
    this.clipRect=document.createElementNS(ns,"rect");clip.append(this.clipRect);svg.querySelector("defs").append(clip);
    const plot=document.createElementNS(ns,"g");plot.setAttribute("clip-path","url(#traffic-plot-clip)");
    this.seriesLayer=document.createElementNS(ns,"g");
    this.seriesLayer.setAttribute("class","chart-series");
    plot.append(this.seriesLayer);
    this.seriesNodes={};
    for(const direction of ["download","upload"]) {
      const tailLine=document.createElementNS(ns,"path"),tailArea=document.createElementNS(ns,"path");
      tailLine.setAttribute("class","chart-line chart-tail-line "+direction);
      tailArea.setAttribute("class","chart-area chart-tail-area "+direction);
      tailArea.setAttribute("fill",`url(#${direction}-fill)`);
      this.seriesNodes[direction]={line:svg.querySelector(".chart-line."+direction),area:svg.querySelector(".chart-area."+direction),fill:svg.querySelector("#"+direction+"-fill"),tailLine,tailArea};
      const nodes=this.seriesNodes[direction];
      this.seriesLayer.append(nodes.area,tailArea,nodes.line,tailLine);
      this.seriesNodes[direction].fill.setAttribute("gradientUnits","userSpaceOnUse");
    }
    this.gridElement=svg.querySelector(".chart-grid");
    this.gridPath=document.createElementNS(ns,"path");this.gridElement.append(this.gridPath);
    this.rateLabels=Array.from({length:3},()=>{const t=document.createElementNS(ns,"text");this.gridElement.append(t);return t;});
    this.timeLabels=[];
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
      const real = this.samples.filter((p) => !p.placeholder && p.time >= this.start && p.time <= this.end);
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
    const frame=()=>{
      if(this.cached&&!document.hidden&&this.el.isConnected&&this.el.clientWidth>0)this.update(...this.cached);
      this.animationFrame=requestAnimationFrame(frame);
    };
    this.animationFrame=requestAnimationFrame(frame);
  }
  update(samples, now, range = 1, interval = 1, live = true) {
    this.cached=[samples,now,range,interval,live];
    this.width=this.el.clientWidth||600;this.height=this.el.clientHeight||176;
    this.left=60;this.right=Math.max(61,this.width-24);this.top=23;this.bottom=Math.max(24,this.height-32);
    range=normalizeRange(range);
    if(range!==this.range)this.clear();
    this.range=range;
    const tick=performance.now(), indicator=this.rangeControl?.querySelector(".segment-indicator");
    const transition=indicator?getComputedStyle(indicator).transitionDuration.split(",")[0]:"0.22s";
    const duration=matchMedia("(prefers-reduced-motion: reduce)").matches?0:parseFloat(transition)*(transition.trim().endsWith("ms")?1:1000);
    this.rangeMotion=this.rangeMotion.retarget(range,tick,duration);
    const presentationRange=this.rangeMotion.read(tick,duration);
    const samplesChanged=this.sourceReference!==samples;
    if(samplesChanged) {
      this.sourceReference=samples;
      this.samples=prepareTrafficData(samples);
      this.sampleLookup=new Map(this.samples.map(p=>[p.time,p]));
      this.latestSamples=Object.fromEntries(["download","upload"].map(direction=>[direction,this.samples.findLast(p=>p[direction]!=null)]));
      for(const key of this.slopeMotion.keys()) {
        const [direction,time]=key.split(":");
        if(this.sampleLookup.get(Number(time))?.[direction]==null)this.slopeMotion.delete(key);
      }
      this.sampleOrigin=this.samples[0]?.time??Date.now();
      this.seriesDirty=true;this.scaleKey=null;
    }
    const sourceNow=new Date(now).getTime();
    this.presentationClock.observe(sourceNow/1000,(this.samples.at(-1)?.time??sourceNow)/1000,tick/1000);
    const frameNow=this.presentationClock.read(tick/1000)*1000;
    this.end=frameNow;this.start=frameNow-presentationRange*60000;
    const scaleKey=range+":"+Math.floor(frameNow/1000);
    if(this.scaleKey!==scaleKey) {
      this.targetScale=trafficViewportScale(this.samples,range,frameNow);
      this.scaleKey=scaleKey;
    }
    this.maxMotion=!this.maxMotion||(samplesChanged&&this.targetScale.max>this.maxMotion.read(tick,duration))
      ?new ChartValueMotion(this.targetScale.max):this.maxMotion.retarget(this.targetScale.max,tick,duration);
    this.minMotion=!this.minMotion||(samplesChanged&&this.targetScale.min<this.minMotion.read(tick,duration))
      ?new ChartValueMotion(this.targetScale.min):this.minMotion.retarget(this.targetScale.min,tick,duration);
    this.max=this.maxMotion.read(tick,duration);this.min=this.minMotion.read(tick,duration);
    const sx=(this.right-this.left)/(presentationRange*60),sy=(this.bottom-this.top)*1024/Math.max(1,this.max-this.min);
    const tx=this.right-(frameNow-this.sampleOrigin)/1000*sx,ty=this.bottom+this.min/1024*sy;
    this.x=t=>this.left+(t-this.start)/(this.end-this.start)*(this.right-this.left);
    this.y=v=>this.bottom-Math.max(0,Math.min(1,(v-this.min)/Math.max(1,this.max-this.min)))*(this.bottom-this.top);
    this.seriesLayer.setAttribute("transform",`matrix(${sx} 0 0 ${sy} ${tx} ${ty})`);
    if(this.seriesDirty||this.seriesAnimating) {
      for(const direction of ["download","upload"]) {
        const {path,area}=trafficSeries(this.samples,direction,t=>(t-this.sampleOrigin)/1000,v=>-v/1024,0,this.slopeMotion,duration);
        const nodes=this.seriesNodes[direction];
        if(nodes.line.getAttribute("d")!==path)nodes.line.setAttribute("d",path);
        if(nodes.area.getAttribute("d")!==area)nodes.area.setAttribute("d",area);
      }
      this.seriesAnimating=duration>0&&[...this.slopeMotion.values()].some(m=>m.from!==m.to&&tick-m.started<duration);
      this.seriesDirty=false;
    }
    for(const direction of ["download","upload"]) {
      const nodes=this.seriesNodes[direction];
      nodes.fill.setAttribute("y1",-this.max/1024);
      nodes.fill.setAttribute("y2",-this.min/1024);
      // Keep the live edge continuous without adding samples or hover timestamps.
      const tail=trafficLiveTail(this.latestSamples[direction],direction,this.start,frameNow,live);
      const x=tail?(tail.startTime-this.sampleOrigin)/1000:0,end=tail?(tail.endTime-this.sampleOrigin)/1000:0;
      const line=tail?`M${x},${-tail.value/1024}H${end}`:"";
      nodes.tailLine.setAttribute("d",line);
      nodes.tailArea.setAttribute("d",tail?line+`V0H${x}Z`:"");
    }
    this.drawAxes(range,frameNow);
    this.el.querySelector(".chart-empty").hidden=this.samples.some(p=>p.upload!=null||p.download!=null);
    this.drawCursor();
  }
  drawAxes(range,now) {
    const size=this.width+":"+this.height;
    if(this.gridSize!==size) {
      this.el.querySelector("svg").setAttribute("viewBox",`0 0 ${this.width} ${this.height}`);
      const padding=1.75;
      for(const [key,value] of Object.entries({x:this.left-padding,y:this.top-padding,width:this.right-this.left+padding*2,height:this.bottom-this.top+padding*2}))this.clipRect.setAttribute(key,value);
      let grid="";
      for(let i=0;i<=4;i++)grid+=`M${this.left} ${this.top+i*(this.bottom-this.top)/4}H${this.right}`;
      for(let i=0;i<=6;i++)grid+=`M${this.left+i*(this.right-this.left)/6} ${this.top}V${this.bottom}`;
      this.gridPath.setAttribute("d",grid);this.gridSize=size;
    }
    const set=(node,name,value)=>{if(node.getAttribute(name)!==String(value))node.setAttribute(name,value);};
    const text=(node,value)=>{if(node.textContent!==value)node.textContent=value;};
    for(let i=0;i<3;i++) {
      const node=this.rateLabels[i];set(node,"x",this.left-7);set(node,"y",this.top+i*(this.bottom-this.top)/2+4);set(node,"text-anchor","end");
      text(node,rate(this.max-(this.max-this.min)*i/2).replace("/s",""));
    }
    const ticks=trafficTimeAxisWindow(this.start,now,range,this.left,this.right,this.samples.at(-1)?.at);
    while(this.timeLabels.length>ticks.length)this.timeLabels.pop().remove();
    while(this.timeLabels.length<ticks.length) {
      const node=document.createElementNS("http://www.w3.org/2000/svg","text");node.setAttribute("class","time-label");this.gridElement.append(node);this.timeLabels.push(node);
    }
    ticks.forEach((point,i)=>{
      const node=this.timeLabels[i];set(node,"x",point.x);set(node,"y",this.bottom+18);set(node,"text-anchor",i===0?"start":i===ticks.length-1?"end":"middle");
      text(node,range===1?clock(point.at).slice(3):clock(point.at,false));
    });
  }
  reset() {
    this.clear();
    this.presentationClock.reset();
    this.slopeMotion.clear();
    this.max=this.min=this.maxMotion=this.minMotion=null;
    this.rangeMotion=new ChartValueMotion(this.range);
    this.sourceReference=null;this.seriesDirty=true;this.scaleKey=null;
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
    const point = this.sampleLookup?.get(this.cursor);
    if (!point || point.placeholder) {
      group.setAttribute("visibility", "hidden");
      tip.hidden = true;
      return;
    }
    group.setAttribute("visibility", "visible");
    const x = this.x(point.time);
    if(x<this.left||x>this.right){group.setAttribute("visibility","hidden");tip.hidden=true;return;}
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
