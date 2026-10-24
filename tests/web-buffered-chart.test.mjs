import { test } from "node:test";
import assert from "node:assert/strict";
import { ChartTimeline, trafficViewportScale, trafficSeries, trafficCurveValue, RetainedTrafficCurve } from "../src/LanDeviceMonitor.App/Web/chart.js";

const samples = [
  { time: 0, download: 100, upload: 100 },
  { time: 5000, download: 100, upload: 100 },
  { time: 10000, download: 1_000_000, upload: 100 },
  { time: 15000, download: 100, upload: 100 },
];

test("buffered playback moves at real-time speed through cached responses and resumes without history replay", () => {
  const clock = new ChartTimeline();
  clock.observe(1000, 5, 50);
  assert.equal(clock.read(50), 993);
  assert.equal(clock.read(52.5), 995.5);
  clock.observe(1000, 5, 53);
  assert.equal(clock.read(53), 996);
  clock.observe(1300, 5, 350);
  assert.equal(clock.read(350), 1293);
  assert.equal(clock.read(351), 1294);
});

test("a buffered peak changes the Y axis only as its visible rise enters the viewport", () => {
  assert.deepEqual(trafficViewportScale(samples, 1, 5000), { min: 0, max: 120 });
  const entering = trafficViewportScale(samples, 1, 6000);
  assert.ok(entering.max > 10_000 && entering.max < 200_000,
    `The partially visible rise should control the scale, got ${entering.max}`);
  const nearPeak = trafficViewportScale(samples, 1, 9000);
  assert.ok(nearPeak.max > entering.max && nearPeak.max < 1_000_000);
  assert.equal(trafficViewportScale(samples, 1, 10000).max, 1_099_990);
});

test("an old peak beyond the left clip cannot set the scale for the entire visible descending segment", () => {
  const points = [{ time: 0, download: 1_000_000, upload: null }, { time: 120000, download: 100, upload: null }];
  const scale = trafficViewportScale(points, 1, 120000);
  assert.ok(scale.max > 100 && scale.max < 600_000, `Hidden left peak affected the scale: ${scale.max}`);
});

test("a flat visible interval keeps its scale while a hidden peak waits in the buffer", () => {
  for(let now=17;now<5000;now+=17)
    assert.deepEqual(trafficViewportScale(samples,1,now),{min:0,max:120});
});

test("new batches and Y-axis changes preserve every already visible curve coordinate with the original seven-second buffer", () => {
  const retention=new RetainedTrafficCurve(), clock=new ChartTimeline();
  let previous=null,previousEnd=-Infinity,source=[];
  let checked=0;
  for(let i=0;i<80;i++){
    const now=i*1000;
    if(i%5===0)source.push({time:now,receivedTime:now+123,download:100+((i*193)%997),upload:0});
    clock.observe(i,5,i);
    const end=clock.read(i);
    assert.equal(end,i-7,"The repair increased playback latency");
    const curve=trafficSeries(source,"download",t=>t/1000,v=>v,0,null,220,
      {x:7.483333333333333,y:184.0522875816993+(i%7)*.1234567},true,i*1000,retention,end);
    if(previous?.segments.length){
      const right=Math.min(previousEnd,previous.points.at(-1)[0]);
      for(let x=previous.points[0][0];x<=right;x+=.071){
        assert.ok(Math.abs(trafficCurveValue(previous.points,previous.segments,x)-trafficCurveValue(curve.points,curve.segments,x))<1e-7,
          `Visible history reshaped at wall=${i}, x=${x}`);checked++;
      }
    }
    for(const sample of source)if(curve.segments.length)
      assert.ok(Math.abs(trafficCurveValue(curve.points,curve.segments,sample.time/1000)-sample.download)<1e-7);
    for(const segment of curve.segments)for(const control of segment)
      assert.ok(control[1]>=Math.min(segment[0][1],segment[3][1])-1e-7&&control[1]<=Math.max(segment[0][1],segment[3][1])+1e-7);
    for(let j=1;j<curve.segments.length;j++){
      const a=curve.segments[j-1],b=curve.segments[j];
      if(a[3][0]!==b[0][0])continue;
      let u=a[3].map((v,k)=>v-a[2][k]),v=b[1].map((v,k)=>v-b[0][k]);
      if(Math.hypot(...u)<1e-9)u=a[3].map((v,k)=>v-a[1][k]);
      if(Math.hypot(...v)<1e-9)v=b[2].map((v,k)=>v-b[0][k]);
      u=[u[0]*12,u[1]*.2];v=[v[0]*12,v[1]*.2];
      assert.ok(Math.abs(u[0]*v[1]-u[1]*v[0])/(Math.hypot(...u)*Math.hypot(...v)||1)<1e-8,
        `A subdivision or real sample introduced a corner at ${a[3][0]}`);
    }
    previous=curve;previousEnd=end;
  }
  assert.ok(checked>30000);
});

test("retained subdivisions join smoothly and survive a pruned history origin", () => {
  const retention=new RetainedTrafficCurve();
  let samples=[{time:0,download:700},{time:5000,download:300},{time:10000,download:800}];
  const first=trafficSeries(samples,"download",t=>t/1000,v=>v,0,null,220,{x:12,y:.15},true,0,retention,8);
  samples.push({time:15000,download:400});
  const next=trafficSeries(samples,"download",t=>t/1000,v=>v,0,null,220,{x:12,y:.5},true,0,retention,8);
  const split=next.segments.findIndex(s=>Math.abs(s[3][0]-8)<1e-7);
  assert.ok(split>=0);
  const a=next.segments[split],b=next.segments[split+1];
  const u=a[3].map((v,k)=>v-a[2][k]),v=b[1].map((v,k)=>v-b[0][k]);
  assert.ok(Math.abs(u[0]*v[1]-u[1]*v[0])<1e-7,"The frozen prefix introduced a corner");
  const pruned=trafficSeries(samples.slice(1),"download",t=>(t-5000)/1000,v=>v,0,null,220,{x:12,y:.8},true,0,retention,3);
  for(let x=5;x<8;x+=.03)assert.ok(Math.abs(trafficCurveValue(next.points,next.segments,x)-trafficCurveValue(pruned.points,pruned.segments,x-5))<1e-7);
  assert.notDeepEqual(first.segments,next.segments,"The unseen tail should still accept its future measurement");
});

test("fractional physical scales cannot invalidate measured endpoints through floating-point round trips", () => {
  const retention=new RetainedTrafficCurve();
  let source=[],previous=null,previousEnd=-Infinity,maximum=0;
  for(let i=0;i<180;i++){
    if(i%5===0)source.push({time:i*1000,download:100+(i*193)%997});
    const curve=trafficSeries(source,"download",t=>t/1000,v=>-v/1024,0,null,220,
      {x:7.483333333333333,y:184.0522875816993+(i%7)*.1234567},true,0,retention,i-7);
    if(previous?.segments.length)for(let x=0;x<=Math.min(previousEnd,previous.points.at(-1)[0]);x+=.071)
      maximum=Math.max(maximum,Math.abs(trafficCurveValue(previous.points,previous.segments,x)-trafficCurveValue(curve.points,curve.segments,x)));
    previous=curve;previousEnd=i-7;
  }
  assert.ok(maximum<1e-8,`Historical shape changed by ${maximum} KiB/s`);
});
