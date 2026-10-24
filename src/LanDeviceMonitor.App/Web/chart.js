import { rate, clock, esc } from "./ui.js";
import { echartsSegments } from "./echarts-curve.js";
export { echartsSegments } from "./echarts-curve.js";

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
  const curves=["download","upload"].map(direction=>{
    const points=samples.filter(p=>!p.placeholder&&p[direction]!=null).map(p=>[p.time/1000,p[direction]]);
    return {points,segments:echartsSegments(points)};
  });
  return trafficCurveScale(curves,now/1000-normalizeRange(range)*60,now/1000);
}

export function trafficCurveScale(curves, left, right, factor = 1) {
  let minimum=Infinity,maximum=-Infinity;
  for(const curve of curves) {
    const bounds=trafficCurveBounds(curve.points,curve.segments,left,right);
    if(!bounds)continue;
    minimum=Math.min(minimum,bounds.min*factor,bounds.max*factor);
    maximum=Math.max(maximum,bounds.min*factor,bounds.max*factor);
  }
  if(!Number.isFinite(minimum)||maximum===0)return {min:0,max:1024};
  const span=maximum-minimum;
  return span<=0?{min:0,max:maximum*1.2}:{min:Math.max(0,minimum-span*.1),max:maximum+span*.1};
}

function curveLowerBound(points,x) {
  let left=0,right=points.length;
  while(left<right){const middle=(left+right)>>1;if(points[middle][0]<x)left=middle+1;else right=middle;}
  return left;
}

function cubic(a,b,c,d,t) { const u=1-t; return u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d; }

export function trafficCurveValue(points,segments,x) {
  if(!segments.length)return points[0][1];
  let lo=0,hi=segments.length;
  while(lo<hi){const middle=(lo+hi)>>1;if(segments[middle][3][0]<x)lo=middle+1;else hi=middle;}
  if(lo===segments.length)return segments.at(-1)[3][1];
  const [a,first,second,b]=segments[lo];
  if(x<=a[0]||a[1]===b[1])return a[1];
  if(x>=b[0])return b[1];
  // ECharts also bends x: invert the actual cubic before evaluating its y.
  let lower=0,upper=1;
  for(let i=0;i<36;i++){
    const middle=(lower+upper)/2;
    if(cubic(a[0],first[0],second[0],b[0],middle)<x)lower=middle;else upper=middle;
  }
  return cubic(a[1],first[1],second[1],b[1],(lower+upper)/2);
}

export function trafficCurveBounds(points,segments,left,right) {
  if(!points.length||right<points[0][0]||left>points.at(-1)[0])return null;
  let min=Infinity,max=-Infinity;
  for(const segment of segments){
    if(segment[3][0]<left||segment[0][0]>right)continue;
    const a=trafficCurveValue(points,[segment],Math.max(left,segment[0][0]));
    const b=trafficCurveValue(points,[segment],Math.min(right,segment[3][0]));
    min=Math.min(min,a,b);max=Math.max(max,a,b);
  }
  for(const point of points)if(point[0]>=left&&point[0]<=right){min=Math.min(min,point[1]);max=Math.max(max,point[1]);}
  return Number.isFinite(min)?{min,max}:null;
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

export function trafficHoverSample(samples, target, start, end, intervalSeconds = 5) {
  if(target<start||target>end)return null;
  let selected=null,distance=Infinity;
  samples.forEach((point,index)=>{
    if(point.placeholder||(point.upload==null&&point.download==null)||point.time<start||point.time>end)return;
    const candidate=Math.abs(point.time-target);
    if(candidate<distance){selected=index;distance=candidate;}
  });
  // Use a sample's vertical column without borrowing across a missing interval.
  return distance<=Math.max(.001,selected==null?intervalSeconds:samples[selected].intervalSeconds??intervalSeconds)*500?selected:null;
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

export function prepareTrafficData(samples) {
  const byTime=new Map();
  for(const sample of samples || []) {
    const timestamp=new Date(sample.at).getTime();
    if(!Number.isFinite(timestamp)||sample.placeholder)continue;
    const time=timestamp;
    byTime.set(time,{...sample,time,receivedTime:sample.receivedAt?new Date(sample.receivedAt).getTime():time,placeholder:false,
      upload:Number.isFinite(sample.upload)?Math.max(0,sample.upload):null,
      download:Number.isFinite(sample.download)?Math.max(0,sample.download):null});
  }
  return [...byTime.values()].sort((a,b)=>a.time-b.time);
}
// Exact subdivision of an already displayed cubic. New ECharts controls may
// change its unseen suffix, while the visible prefix and its tangent stay fixed.
export function retainCurvePrefix(previous,target,visibleEnd,shift=0) {
  const old=previous.map(s=>s.map(p=>[p[0]+shift,p[1]]));
  const equal=(a,b)=>Math.abs(a[0]-b[0])<1e-9&&Math.abs(a[1]-b[1])<1e-12;
  if(!old.length||!equal(old[0][0],target[0])||!equal(old.at(-1)[3],target[3])||visibleEnd<=target[0][0])return [target];
  if(visibleEnd>=target[3][0])return old;
  if(old.length===1&&old[0].every((p,i)=>equal(p,target[i])))return old;
  const lerp=(a,b,t)=>a.map((v,k)=>v+(b[k]-v)*t);
  const split=(s,t)=>{
    const a=lerp(s[0],s[1],t),b=lerp(s[1],s[2],t),c=lerp(s[2],s[3],t);
    const d=lerp(a,b,t),e=lerp(b,c,t),p=lerp(d,e,t);
    return [[s[0],a,d,p],[p,e,c,s[3]]];
  };
  const fitEndTangent=(control,start,end)=>{
    const v=control.map((c,k)=>c-end[k]);let factor=1;
    for(let k=0;k<2;k++)if(v[k]!==0)
      factor=Math.min(factor,((v[k]<0?Math.min(start[k],end[k]):Math.max(start[k],end[k]))-end[k])/v[k]);
    return end.map((c,k)=>c+v[k]*Math.max(0,factor));
  };
  const result=[];
  for(const segment of old){
    if(segment[3][0]<=visibleEnd){result.push(segment);continue;}
    if(segment[0][0]>=visibleEnd){
      result.push([result.at(-1)[3],segment[1],fitEndTangent(target[2],result.at(-1)[3],target[3]),target[3]]);return result;
    }
    let lo=0,hi=1;
    for(let i=0;i<48;i++){const t=(lo+hi)/2;if(cubic(...segment.map(p=>p[0]),t)<visibleEnd)lo=t;else hi=t;}
    const [left,right]=split(segment,(lo+hi)/2);
    result.push(left,[right[0],right[1],fitEndTangent(target[2],right[0],target[3]),target[3]]);
    return result;
  }
  return old;
}

export class RetainedTrafficCurve {
  constructor(){this.parts=new Map();this.projectionX=null;}
  update(targets,keyAt,visibleEnd,projectionX){
    const reflow=this.projectionX!==null&&Math.abs(projectionX/this.projectionX-1)>=1e-6;
    const next=new Map();
    for(const target of targets){
      const key=`${keyAt(target[0][0])}:${keyAt(target[3][0])}`,old=this.parts.get(key);
      const shift=old?target[0][0]-old[0][0][0]:0;
      next.set(key,old&&!reflow?retainCurvePrefix(old,target,visibleEnd,shift):[target]);
    }
    this.parts=next;this.projectionX=projectionX;
    return [...next.values()].flat();
  }
}

export function trafficSeries(samples, direction, x, y, bottom, motion = null, motionDuration = 220,
  projection = {x:1,y:1}, samplesChanged = true, tick = performance.now(), retention = null, visibleEnd = -Infinity) {
  // Native GetRenderableSampleSegments skips unknown slots and joins the real
  // samples on both sides, without adding zero-valued intermediate controls.
  const points = samples
    .filter((p) => !p.placeholder && p[direction] != null)
    .map((p) => [x(p.time), y(p[direction])]);
  const real=samples.filter(p=>!p.placeholder && p[direction]!=null);
  const runs=[];let run=[];
  for(const sample of samples){
    if(sample.startsNewSegment&&run.length){runs.push(run);run=[];}
    if(!sample.placeholder&&sample[direction]!=null)run.push([x(sample.time),y(sample[direction])]);
  }
  if(run.length)runs.push(run);
  const pointAt=x=>{
    let i=Math.min(points.length-1,curveLowerBound(points,x));
    if(i>0&&Math.abs(points[i-1][0]-x)<Math.abs(points[i][0]-x))i--;
    return points[i];
  };
  // Use the measured endpoints verbatim, avoiding a round trip through the
  // display scale when matching retained intervals.
  let segments=runs.flatMap(points=>echartsSegments(points,projection.x,projection.y))
    .map(s=>[pointAt(s[0][0]),s[1],s[2],pointAt(s[3][0])]);
  if(motion||retention){
    const timeAt=x=>{
      let i=Math.min(points.length-1,curveLowerBound(points,x));
      if(i>0&&Math.abs(points[i-1][0]-x)<Math.abs(points[i][0]-x))i--;
      return real[i].receivedTime??real[i].time;
    };
    if(retention)segments=retention.update(segments,timeAt,visibleEnd,projection.x);
    if(motion){
    const activeKeys=new Set();
    segments=segments.map(target=>{
      const key=`${direction}:${timeAt(target[0][0])}:${timeAt(target[3][0])}`;
      activeKeys.add(key);
      const value=motion.get(key)??new ChartControlMotion(target,tick);
      value.retarget(target,tick,motionDuration,samplesChanged);
      motion.set(key,value);
      return value.read(tick,motionDuration);
    });
    for(const key of motion.keys())if(key.startsWith(direction+":")&&!activeKeys.has(key))motion.delete(key);
    }
  }
  const path = trafficCurve(points, segments);
  let area="",group=[];
  function closeArea(){if(group.length){area+=trafficCurve([group[0][0]],group)+`L${group.at(-1)[3][0]},${bottom}L${group[0][0][0]},${bottom}Z`;group=[];}}
  for(const segment of segments){if(group.length&&segment[0][0]!==group.at(-1)[3][0])closeArea();group.push(segment);}
  closeArea();
  return { path, area, points, segments };
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

export function trafficCurve(points, segments = echartsSegments(points)) {
 if(!segments.length)return "";
 let result="",end=null;
 for(const [a,first,second,b] of segments){
  if(!end||a[0]!==end[0]||a[1]!==end[1])result+=`M${a.join(",")}`;
  result+=`C${first.join(",")} ${second.join(",")} ${b.join(",")}`;
  end=b;
 }
 return result;
}

export class ChartTimeline {
  constructor(){this.initialized=false;}
  read(now){return this.initialized?this.anchorTime+Math.max(0,now-this.anchorTick):0;}
  observe(sourceTime,intervalSeconds,now){
    const delay=Math.max(1,Math.min(60,intervalSeconds))+2,target=sourceTime-delay;
    const reset=!this.initialized||delay!==this.delay;
    if(reset||sourceTime!==this.sourceTime){
      this.anchorTime=reset?target:Math.max(target,this.read(now));
      this.anchorTick=now;this.sourceTime=sourceTime;this.delay=delay;
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

export class ChartControlMotion {
  constructor(target,started){this.target=target;this.started=started;this.offsets=[[0,0],[0,0]];}
  retarget(target,now,duration,samplesChanged){
    if(samplesChanged){
      const current=this.read(now,duration),shift=target[0][0]-this.target[0][0];
      this.offsets=[1,2].map(i=>[current[i][0]+shift-target[i][0],current[i][1]-target[i][1]]);
      this.started=now;
    }
    this.target=target;
  }
  active(now,duration){return duration>0&&now-this.started<duration&&this.offsets.some(p=>p.some(v=>v!==0));}
  read(now,duration){
    const remaining=new ChartValueMotion(1,0,this.started).read(now,duration),a=this.target[0],b=this.target[3];
    const controls=[1,2].map((i,j)=>this.target[i].map((v,k)=>
      Math.max(Math.min(a[k],b[k]),Math.min(Math.max(a[k],b[k]),v+this.offsets[j][k]*remaining))));
    return [a,...controls,b];
  }
}

export class ChartHoverMotion {
  constructor(){this.reset();}
  update(key,target,hasUpload,hasDownload,now,duration=160){
    if(!this.initialized||duration<=0){
      this.offset={x:0,uploadY:0,downloadY:0,tooltipX:0,tooltipY:0};
      this.remaining=new ChartValueMotion(0,0,now);
    }else if(key!==this.key){
      const current=this.read(now,duration);
      this.offset={x:current.x-target.x,
        uploadY:this.hasUpload&&hasUpload?current.uploadY-target.uploadY:0,
        downloadY:this.hasDownload&&hasDownload?current.downloadY-target.downloadY:0,
        tooltipX:current.tooltipX-target.tooltipX,tooltipY:current.tooltipY-target.tooltipY};
      this.remaining=new ChartValueMotion(1,0,now);
    }
    this.key=key;this.target=target;this.initialized=true;
    this.hasUpload=hasUpload;this.hasDownload=hasDownload;
    return this.read(now,duration);
  }
  read(now,duration=160){
    const remaining=this.remaining.read(now,duration),t=this.target,o=this.offset;
    return {x:t.x+o.x*remaining,uploadY:t.uploadY+o.uploadY*remaining,downloadY:t.downloadY+o.downloadY*remaining,
      tooltipX:t.tooltipX+o.tooltipX*remaining,tooltipY:t.tooltipY+o.tooltipY*remaining};
  }
  reset(){this.initialized=false;}
}

export class TrafficChart {
  constructor(element, rangeControl = null) {
    this.el = element;
    this.samples = [];
    this.cursor = null;
    this.hoverMotion = new ChartHoverMotion();
    this.range = 1;
    this.rangeControl = rangeControl;
    this.rangeMotion = new ChartValueMotion(1);
    this.sourceReference = null;
    this.presentationClock = new ChartTimeline();
    this.retainedCurves={download:new RetainedTrafficCurve(),upload:new RetainedTrafficCurve()};
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
      this.seriesNodes[direction]={line:svg.querySelector(".chart-line."+direction),area:svg.querySelector(".chart-area."+direction),fill:svg.querySelector("#"+direction+"-fill")};
      const nodes=this.seriesNodes[direction];
      this.seriesLayer.append(nodes.area,nodes.line);
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
          : real.findIndex((p) => (p.receivedTime??p.time) === this.cursor);
      const i = Math.max(
        0,
        Math.min(real.length - 1, current + (e.key === "ArrowLeft" ? -1 : 1)),
      );
      if (real[i]) {
        this.hoverMotion.reset();
        this.cursor = real[i].receivedTime??real[i].time;
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
  update(samples, now, range = 1, interval = 5) {
    this.cached=[samples,now,range,interval];
    this.sampleInterval=interval;
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
      this.sampleLookup=new Map(this.samples.map(p=>[p.receivedTime??p.time,p]));
      this.sampleOrigin=this.samples[0]?.time??Date.now();
      this.seriesDirty=true;
    }
    const sourceNow=new Date(now).getTime();
    this.presentationClock.observe(sourceNow/1000,interval,tick/1000);
    const frameNow=this.presentationClock.read(tick/1000)*1000;
    this.end=frameNow;this.start=frameNow-presentationRange*60000;
    const projection={x:(this.right-this.left)/(presentationRange*60),
      y:(this.bottom-this.top)*1024/Math.max(1,(this.max??1024)-(this.min??0))};
    const projectionChanged=!this.projection||Math.abs(projection.x/this.projection.x-1)>=1e-6;
    if(this.seriesDirty||projectionChanged) {
      this.projection=projection;
      for(const direction of ["download","upload"]) {
        const curve=trafficSeries(this.samples,direction,t=>(t-this.sampleOrigin)/1000,v=>-v/1024,0,
          null,duration,projection,samplesChanged,tick,this.retainedCurves[direction],(frameNow-this.sampleOrigin)/1000);
        const {path,area}=curve;
        const nodes=this.seriesNodes[direction];
        nodes.curve=curve;
        if(nodes.line.getAttribute("d")!==path)nodes.line.setAttribute("d",path);
        if(nodes.area.getAttribute("d")!==area)nodes.area.setAttribute("d",area);
      }
      this.seriesDirty=false;
    }
    this.targetScale=trafficCurveScale(Object.values(this.seriesNodes).map(nodes=>nodes.curve),
      (this.start-this.sampleOrigin)/1000,(frameNow-this.sampleOrigin)/1000,-1024);
    this.maxMotion=!this.maxMotion||this.targetScale.max>this.maxMotion.read(tick,duration)
      ?new ChartValueMotion(this.targetScale.max):this.maxMotion.retarget(this.targetScale.max,tick,duration);
    this.minMotion=!this.minMotion||this.targetScale.min<this.minMotion.read(tick,duration)
      ?new ChartValueMotion(this.targetScale.min):this.minMotion.retarget(this.targetScale.min,tick,duration);
    this.max=this.maxMotion.read(tick,duration);this.min=this.minMotion.read(tick,duration);
    const sx=(this.right-this.left)/(presentationRange*60),sy=(this.bottom-this.top)*1024/Math.max(1,this.max-this.min);
    const tx=this.right-(frameNow-this.sampleOrigin)/1000*sx,ty=this.bottom+this.min/1024*sy;
    this.x=t=>this.left+(t-this.start)/(this.end-this.start)*(this.right-this.left);
    this.y=v=>this.bottom-Math.max(0,Math.min(1,(v-this.min)/Math.max(1,this.max-this.min)))*(this.bottom-this.top);
    this.seriesLayer.setAttribute("transform",`matrix(${sx} 0 0 ${sy} ${tx} ${ty})`);
    for(const direction of ["download","upload"]) {
      const nodes=this.seriesNodes[direction];
      nodes.fill.setAttribute("y1",-this.max/1024);
      nodes.fill.setAttribute("y2",-this.min/1024);
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
    this.projection=null;
    this.retainedCurves={download:new RetainedTrafficCurve(),upload:new RetainedTrafficCurve()};
    this.max=this.min=this.maxMotion=this.minMotion=null;
    this.rangeMotion=new ChartValueMotion(this.range);
    this.sourceReference=null;this.seriesDirty=true;
  }
  select(e) {
    if (!this.samples.length) return;
    const rect = this.el.getBoundingClientRect();
    // Pointer coordinates are rendered pixels; the plot uses layout pixels.
    const localX =
      ((e.clientX - rect.left) * this.width) / Math.max(1, rect.width);
    const localY = ((e.clientY - rect.top) * this.height) / Math.max(1, rect.height);
    if(localX<this.left||localX>this.right||localY<this.top||localY>this.bottom){this.clear();return;}
    const target=this.start+(localX-this.left)/(this.right-this.left)*(this.end-this.start);
    const index=trafficHoverSample(this.samples,target,this.start,this.end,this.sampleInterval);
    if (index == null || this.width < 100 || this.height < 80) {
      this.clear();
      return;
    }
    if(e.pointerType!=="mouse")this.hoverMotion.reset();
    this.cursor = this.samples[index].receivedTime??this.samples[index].time;
    this.drawCursor();
  }
  clear() {
    this.releasePointer(this.touch?.id);
    this.cursor = null;
    this.hoverMotion.reset();
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
      this.hoverMotion.reset();
      return;
    }
    group.setAttribute("visibility", "visible");
    const x = this.x(point.time);
    if(x<this.left||x>this.right){group.setAttribute("visibility","hidden");tip.hidden=true;this.hoverMotion.reset();return;}
    tip.hidden = false;
    const markup = `<b>${clock(point.at)}</b><span class="up">↑ ${rate(point.upload)}</span><span class="down">↓ ${rate(point.download)}</span>`;
    if (tip.innerHTML !== markup) tip.innerHTML = markup;
    tip.style.maxWidth = Math.max(1, this.right - this.left) + "px";
    tip.style.overflow = "hidden";
    const position = trafficTooltipPosition(x,tip.offsetWidth,this.left,this.right,this.top,this.bottom,tip.offsetHeight);
    const frame=this.hoverMotion.update(point.receivedTime??point.time,{x,uploadY:this.y(point.upload??0),downloadY:this.y(point.download??0),
      tooltipX:position.x,tooltipY:position.y},point.upload!=null,point.download!=null,performance.now(),
      matchMedia("(prefers-reduced-motion: reduce)").matches?0:160);
    group.setAttribute("transform",`translate(${frame.x} 0)`);
    group.querySelector("path").setAttribute("d",`M0,${this.top}V${this.bottom}`);
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
      circle.setAttribute("cx", "0");
      circle.setAttribute("cy", "0");
      circle.setAttribute("transform",`translate(0 ${frame[direction+"Y"]})`);
    }
    tip.style.left = "0px";tip.style.top = "0px";
    const tooltipX=Math.max(this.left,Math.min(frame.tooltipX,this.right-tip.offsetWidth));
    tip.style.transform=`translate3d(${tooltipX}px,${frame.tooltipY}px,0)`;
  }
}
