import { test } from "node:test";
import assert from "node:assert/strict";
import { ChartTimeline, ChartSamplingClock, trafficViewportScale, trafficSeries, trafficCurveValue, EChartsLiveCurve, echartsSegments, TRAFFIC_DATA_UPDATE_MS, ChartValueMotion } from "../src/LanDeviceMonitor.App/Web/chart.js";

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

test("new terminal controls use the longer refresh duration while Y scaling alone cannot reshape any part",()=>{
  const live=new EChartsLiveCurve();
  const samples=[{time:0,download:700},{time:5000,download:300},{time:10000,download:800}];
  const draw=(data,tick,changed,y)=>trafficSeries(data,"download",t=>t/1000,v=>-v/1024,0,null,TRAFFIC_DATA_UPDATE_MS,{x:12,y},changed,tick,live);
  const original=draw(samples,0,true,180);
  const scaled=draw(samples,1000,true,800);
  assert.equal(original.path,scaled.path,"An automatic axis change altered ECharts' calculation metric");
  samples.push({time:15000,download:400});
  const start=draw(samples,2000,true,30),middle=draw(samples,2700,false,30),end=draw(samples,2000+TRAFFIC_DATA_UPDATE_MS,false,30);
  assert.notEqual(start.path,middle.path);assert.notEqual(middle.path,end.path);
  assert.equal(start.segments.length,3);assert.equal(end.segments.length,3);
  assert.deepEqual(end.segments[0],original.segments[0]);
});

test("viewport shares the sampling phase so a four-second coordinate offset cannot create a right-edge gap",()=>{
  for(const phase of [-18,-4,0,4,18]){
    const source=new ChartTimeline(),sampling=new ChartSamplingClock();
    const points=Array.from({length:15},(_,i)=>({time:(900+i*5+phase)*1000,receivedTime:(900+i*5)*1000}));
    sampling.observe(points);
    for(let tick=0;tick<5;tick+=.016){
      const wall=970+tick;
      source.observe(wall,5,tick);
      const end=source.read(tick)+sampling.read(tick);
      assert.ok(points.at(-1).time/1000>end,"The viewport passed the last measured endpoint");
      assert.ok(Math.abs(wall-(end-phase)-7)<1e-8,"The phase correction added playback delay");
    }
  }
});

test("slow cadence drift and polling jitter stay covered without pausing, retiming history or extending latency",()=>{
  for(const period of [2.5,5,10]){
    const source=new ChartTimeline(),sampling=new ChartSamplingClock();
    const points=[];let next=-20,previousEnd=null,minCoverage=Infinity;
    for(let i=0;i<36000;i++){
      const wall=i/10;
      while(next*(period+.02)+(next%2===0?.25:-.25)<=wall){
        const received=next*(period+.02)+(next%2===0?.25:-.25);
        points.push({time:(next*period-4)*1000,receivedTime:received*1000});next++;
        sampling.observe(points);
      }
      source.observe(wall,period,wall);
      const end=source.read(wall)+sampling.read(wall),latest=points.at(-1);
      minCoverage=Math.min(minCoverage,latest.time/1000-end);
      if(previousEnd!==null)assert.ok(end-previousEnd>=.089999&&end-previousEnd<=.110001,"Playback paused or jumped during phase correction");
      const actualPhase=(latest.time-latest.receivedTime)/1000;
      assert.ok(Math.abs(wall-(end-actualPhase)-(period+2))<1.1,"Playback latency drifted beyond sampling jitter");
      previousEnd=end;
    }
    assert.ok(minCoverage>0,`A ${period}s source developed a right-edge gap: ${minCoverage}`);
    const before=sampling.read(3600);sampling.observe([]);assert.ok(Number.isFinite(sampling.read(3600.1)));
    sampling.reset();sampling.observe(points);assert.ok(Number.isFinite(sampling.read(3900)));
    assert.ok(Number.isFinite(before));
  }
});

test("curve and axis refresh share a longer interruptible duration while the selector retains 220ms",()=>{
  const motion=new ChartValueMotion(120000,12000,1000);
  assert.ok(motion.read(1270,TRAFFIC_DATA_UPDATE_MS)>12000);
  assert.equal(motion.read(1000+TRAFFIC_DATA_UPDATE_MS,TRAFFIC_DATA_UPDATE_MS),12000);
  const reversed=motion.retarget(50000,1300,TRAFFIC_DATA_UPDATE_MS);
  assert.equal(reversed.from,motion.read(1300,TRAFFIC_DATA_UPDATE_MS));
  assert.equal(reversed.read(1300+TRAFFIC_DATA_UPDATE_MS,TRAFFIC_DATA_UPDATE_MS),50000);
  const selector=new ChartValueMotion(1,5,1000);
  assert.equal(selector.read(1220,220),5);
  assert.equal(motion.read(1000,0),12000);
});

test("unchanged snapshots do not restart the long refresh transition",()=>{
  const live=new EChartsLiveCurve(),samples=[{time:0,download:700},{time:5000,download:300},{time:10000,download:800}];
  const draw=(tick)=>trafficSeries(samples,"download",t=>t/1000,v=>-v/1024,0,null,TRAFFIC_DATA_UPDATE_MS,{x:12,y:180},true,tick,live);
  draw(0);samples.push({time:15000,download:400});const start=draw(2000),middle=draw(2700);
  assert.notEqual(start.path,middle.path);
  let end;for(let tick=2800;tick<=3800;tick+=100)end=draw(tick);
  assert.ok(!live.active(3800,TRAFFIC_DATA_UPDATE_MS),"An identical sample kept restarting the animation");
  const expected=echartsSegments(end.points,12,180);
  for(let i=0;i<expected.length;i++)for(let j=0;j<4;j++)for(let k=0;k<2;k++)
    assert.ok(Math.abs(end.segments[i][j][k]-expected[i][j][k])<1e-9);
});
