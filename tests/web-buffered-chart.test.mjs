import { test } from "node:test";
import assert from "node:assert/strict";
import { ChartTimeline, trafficViewportScale, trafficSeries, trafficCurveValue, EChartsLiveCurve, echartsSegments } from "../src/LanDeviceMonitor.App/Web/chart.js";

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

test("streaming keeps complete ECharts cubics and settled history stable without extending the buffer", () => {
  const live=new EChartsLiveCurve(),clock=new ChartTimeline();
  let source=[],previous=null,checked=0;
  for(let i=0;i<180;i++){
    if(i%5===0)source.push({time:i*1000,receivedTime:i*1000+123,download:100+(i*193)%997});
    clock.observe(i,5,i);assert.equal(clock.read(i),i-7);
    const frame=(tick,changed)=>trafficSeries(source,"download",t=>t/1000,v=>-v/1024,0,null,220,
      {x:7.483333333333333,y:184.0522875816993+(i%7)*23.1234567},changed,tick,live);
    const start=frame(i*1000,true),middle=frame(i*1000+110,false),curve=frame(i*1000+220,false);
    const expected=echartsSegments(curve.points,7.483333333333333,live.projectionY);
    assert.equal(curve.segments.length,Math.max(0,source.length-1),"A real interval was split into a visible seam");
    for(let j=0;j<expected.length;j++)for(let k=0;k<4;k++)for(let c=0;c<2;c++)
      assert.ok(Math.abs(curve.segments[j][k][c]-expected[j][k][c])<1e-9,"Settled geometry differs from the original ECharts curve");
    if(previous?.segments.length){
      const right=previous.points.at(-2)[0];
      for(let x=previous.points[0][0];x<=right;x+=.071){
        assert.ok(Math.abs(trafficCurveValue(previous.points,previous.segments,x)-trafficCurveValue(curve.points,curve.segments,x))<1e-8,
          `Settled history changed at wall=${i},x=${x}`);checked++;
      }
    }
    for(const sample of source)if(curve.segments.length)
      assert.ok(Math.abs(trafficCurveValue(curve.points,curve.segments,sample.time/1000)+sample.download/1024)<1e-8);
    for(const c of [start,middle,curve])for(const segment of c.segments)for(const control of segment)
      assert.ok(control[1]>=Math.min(segment[0][1],segment[3][1])-1e-9&&control[1]<=Math.max(segment[0][1],segment[3][1])+1e-9);
    previous=curve;
  }
  assert.ok(checked>100000);
});

test("new terminal controls transition for 220ms while Y scaling alone cannot reshape any part",()=>{
  const live=new EChartsLiveCurve();
  const samples=[{time:0,download:700},{time:5000,download:300},{time:10000,download:800}];
  const draw=(data,tick,changed,y)=>trafficSeries(data,"download",t=>t/1000,v=>-v/1024,0,null,220,{x:12,y},changed,tick,live);
  const original=draw(samples,0,true,180);
  const scaled=draw(samples,1000,true,800);
  assert.equal(original.path,scaled.path,"An automatic axis change altered ECharts' calculation metric");
  samples.push({time:15000,download:400});
  const start=draw(samples,2000,true,30),middle=draw(samples,2110,false,30),end=draw(samples,2220,false,30);
  assert.notEqual(start.path,middle.path);assert.notEqual(middle.path,end.path);
  assert.equal(start.segments.length,3);assert.equal(end.segments.length,3);
  assert.deepEqual(end.segments[0],original.segments[0]);
});
