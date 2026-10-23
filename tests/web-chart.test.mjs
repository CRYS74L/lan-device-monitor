import { test } from "node:test";
import assert from "node:assert/strict";
import {
  trafficScale,
  trafficCurve,
} from "../src/LanDeviceMonitor.App/Web/chart.js";
import { rate, total, duration } from "../src/LanDeviceMonitor.App/Web/ui.js";
import * as chartFunctions from "../src/LanDeviceMonitor.App/Web/chart.js";

test("live presentation holds the last known rate to the right edge without creating samples",()=>{
  assert.equal(typeof chartFunctions.trafficLiveTail,"function");
  const point={time:1000,download:1024,upload:null};
  assert.deepEqual(chartFunctions.trafficLiveTail(point,"download",0,5000),{startTime:1000,endTime:5000,value:1024});
  assert.equal(point.time,1000);
  assert.equal(chartFunctions.trafficLiveTail(point,"download",0,5000,false),null);
  assert.equal(chartFunctions.trafficLiveTail(point,"upload",0,5000),null);
  assert.equal(chartFunctions.trafficLiveTail(point,"download",2000,5000),null);
});

test("the first frame and a resumed frame show current time instead of replaying old samples",()=>{
  const timeline=new chartFunctions.ChartTimeline();
  timeline.observe(1000,995,50);
  assert.ok(timeline.read(50)>=1000);
  timeline.observe(1300,1300,350);
  assert.ok(timeline.read(350)>=1300);
  const first=timeline.read(350);
  assert.equal(timeline.read(351)-first,1);
});

test("cached server timestamps cannot restart or accelerate the per-frame clock",()=>{
  const timeline=new chartFunctions.ChartTimeline();
  for(let frame=0;frame<240;frame++) {
    const tick=50+frame/120;
    timeline.observe(1000,995,tick);
    assert.ok(Math.abs(timeline.read(tick)-(1000+frame/120))<1e-9);
  }
});

test("axis bounds ignore expired data and peaks outside the visible time window",()=>{
  assert.deepEqual(chartFunctions.trafficViewportScale([{time:-28800000,upload:1,download:900000}],1,0),{min:0,max:1024});
  assert.deepEqual(chartFunctions.trafficViewportScale([{time:0,upload:100,download:100},{time:2000,upload:900000,download:900000}],1,1000),{min:0,max:120});
});

test("time coordinates keep their complete set while the window advances through fractional seconds", () => {
  const data=Array.from({length:661},(_,i)=>({at:new Date(i*1000).toISOString(),upload:i,download:i*2}));
  for(const range of [1,5,10]) {
    const counts=new Set();
    for(let fraction=0;fraction<1000;fraction+=17) {
      const end=659000+fraction,start=end-range*60000;
      const slots=chartFunctions.trafficSlots(data,range,end).filter(s=>s.time>=start&&s.time<=end);
      const ticks=chartFunctions.trafficTimeAxis(slots,range,60,820);
      counts.add(ticks.length);
      assert.equal(ticks[0].x,60);
      assert.equal(ticks.at(-1).x,820);
    }
    assert.deepEqual([...counts],[range===10?8:6]);
  }
});

test("chart changes finish at the selector duration and rapid reversals keep their current frame", () => {
  for (const duration of [220, 480]) {
    const motion = new chartFunctions.ChartValueMotion(1, 10, 1000);
    assert.equal(motion.read(1000, duration), 1);
    assert.ok(motion.read(1000 + duration / 2, duration) > 1);
    assert.ok(motion.read(1000 + duration / 2, duration) < 10);
    assert.equal(motion.read(1000 + duration, duration), 10);
    const reversal = motion.retarget(5, 1000 + duration / 3, duration);
    assert.equal(reversal.from, motion.read(1000 + duration / 3, duration));
    assert.equal(reversal.read(1000 + duration / 3 + duration, duration), 5);
    assert.equal(motion.read(1000, 0), 10);
  }
});

test("range zoom retains whole-second samples and never interpolates measured rates", () => {
  const samples = Array.from({ length: 601 }, (_, i) => ({ at: new Date(i * 1000).toISOString(), upload: i, download: i * 2 }));
  for (const range of [1, 1.3, 3.5, 5, 7.7, 10]) {
    const slots = chartFunctions.trafficSlots(samples, 10, 600000, range);
    assert.ok(slots.some(p => p.time === 600000));
    assert.ok(slots.every(p => p.time % 1000 === 0));
    assert.ok(slots.filter(p => !p.placeholder).every(p => p.upload === p.time / 1000 && p.download === p.upload * 2));
  }
});

test("zoom projects sample tangents without restarting their animation", () => {
  const samples = [{ time: 0, upload: 0 }, { time: 5000, upload: 30 }, { time: 10000, upload: 0 }];
  const motion = new Map();
  chartFunctions.trafficSeries(samples, "upload", t => t / 100, v => 100 - v, 100, motion, 220, -.1);
  const initial = motion.get("upload:5000");
  chartFunctions.trafficSeries(samples, "upload", t => t / 50, v => 100 - v, 100, motion, 220, -.05);
  assert.equal(motion.get("upload:5000"), initial);
  assert.equal(initial.to, 0);
});

test("retained series keeps byte-level rates before the viewport scales them", () => {
  const series=chartFunctions.trafficSeries([{time:0,upload:1},{time:1000,upload:2}],"upload",t=>t/1000,v=>-v/1024,0);
  assert.deepEqual(series.points,[[0,-1/1024],[1,-2/1024]]);
});

test("fixed slots end at the latest received sample, keep missing directions unknown", () => {
  assert.equal(typeof chartFunctions.trafficSlots, "function");
  const slots = chartFunctions.trafficSlots(
    [
      { at: "2026-10-05T00:00:00.120Z", upload: 7, download: null },
      { at: "2026-10-05T00:00:00.990Z", upload: 0, download: null },
      { at: "2026-10-05T00:00:02.450Z", upload: null, download: 40 },
    ],
    1,
  );
  assert.equal(slots.length, 61);
  assert.equal(slots[0].time, Date.parse("2026-10-04T23:59:02Z"));
  assert.equal(slots[60].time, Date.parse("2026-10-05T00:00:02Z"));
  assert.equal(slots[58].upload, 0);
  assert.equal(slots[58].download, null);
  assert.equal(slots[59].placeholder, true);
  assert.equal(slots[59].upload, null);
  assert.equal(slots[60].upload, null);
  assert.equal(slots[60].download, 40);
  assert.deepEqual(chartFunctions.trafficSlots([], 10), []);
});

test("hover chooses a fixed time slot and does not borrow a distant real sample", () => {
  assert.equal(typeof chartFunctions.trafficHoverIndex, "function");
  const slots = Array.from({ length: 61 }, (_, i) => ({
    placeholder: i !== 60,
  }));
  assert.equal(chartFunctions.trafficHoverIndex(slots, 300, 60, 660), null);
  assert.equal(chartFunctions.trafficHoverIndex(slots, 659, 60, 660), 60);
  assert.equal(chartFunctions.trafficHoverIndex(slots, 59, 60, 660), null);
  assert.equal(chartFunctions.trafficHoverIndex(slots, 661, 60, 660), null);
});

test("normalized slots and time-axis labels keep the server timestamp offset", () => {
  const slots = chartFunctions.trafficSlots(
    [{ at: "2026-10-05T00:20:02.900+08:00", upload: 12, download: null }],
    1,
  );
  assert.equal(slots.at(-1).at, "2026-10-05T00:20:02+08:00");
  assert.equal(slots[0].at, "2026-10-05T00:19:02+08:00");
  const labels = chartFunctions.trafficTimeAxis(slots, 1, 60, 560);
  assert.equal(labels[0].at, "2026-10-05T00:19:02+08:00");
  assert.equal(labels.at(-1).at, "2026-10-05T00:20:02+08:00");
});

test("time axis distributes coordinates evenly and respects the minimum pixel spacing", () => {
  assert.equal(typeof chartFunctions.trafficTimeAxis, "function");
  const minute = Array.from({ length: 61 }, (_, i) => ({ time: i * 1000 }));
  assert.deepEqual(
    chartFunctions.trafficTimeAxis(minute, 1, 60, 560).map((p) => p.index),
    [0, 12, 24, 36, 48, 60],
  );
  const ten = Array.from({ length: 601 }, (_, i) => ({ time: i * 1000 }));
  assert.deepEqual(
    chartFunctions.trafficTimeAxis(ten, 10, 60, 560).map((p) => p.index),
    [0, 86, 171, 257, 343, 429, 514, 600],
  );
  assert.deepEqual(
    chartFunctions.trafficTimeAxis(ten, 10, 60, 90).map((p) => p.index),
    [0],
  );
});

test("series connects real samples across placeholders without generating zero points", () => {
  assert.equal(typeof chartFunctions.trafficSeries, "function");
  const samples = [
    { time: 0, upload: 10, download: null },
    { time: 1000, upload: null, download: null, placeholder: true },
    { time: 9000, upload: 20, download: 30 },
  ];
  assert.deepEqual(
    chartFunctions.trafficSeries(
      samples,
      "upload",
      (t) => t / 100,
      (v) => 40 - v,
      50,
    ),
    {
      path: "M0,30C30,26.666666666666668 60,23.333333333333332 90,20",
      area: "M0,30C30,26.666666666666668 60,23.333333333333332 90,20L90,50L0,50Z",
      points: [
        [0, 30],
        [90, 20],
      ],
    },
  );
  assert.deepEqual(
    chartFunctions.trafficSeries(
      samples,
      "download",
      (t) => t / 100,
      (v) => 40 - v,
      50,
    ),
    {
      path: "",
      area: "",
      points: [[90, 10]],
    },
  );
  assert.deepEqual(
    trafficScale([
      { upload: null, download: null, placeholder: true },
      { upload: 100, download: 200 },
    ]),
    { min: 90, max: 210 },
  );
  assert.deepEqual(trafficScale([{ upload: null, download: null }]), {
    min: 0,
    max: 1024,
  });
});

test("tooltip tries native right 12px then left 12px and stays within narrow bounds", () => {
  assert.equal(typeof chartFunctions.trafficTooltipPosition, "function");
  assert.deepEqual(
    chartFunctions.trafficTooltipPosition(200, 100, 60, 600, 23, 200, 80),
    { x: 212, y: 31 },
  );
  assert.deepEqual(
    chartFunctions.trafficTooltipPosition(550, 100, 60, 600, 23, 200, 80),
    { x: 438, y: 31 },
  );
  assert.deepEqual(
    chartFunctions.trafficTooltipPosition(80, 100, 60, 140, 23, 100, 80),
    { x: 60, y: 25 },
  );
});
test("traffic curve passes through every sample and preserves interior segments", () => {
  assert.equal(
    trafficCurve([
      [0, 10],
      [10, 0],
      [20, 10],
    ]),
    "M0,10C3.3333333333333335,6.666666666666666 6.666666666666666,0 10,0C13.333333333333334,0 16.666666666666668,6.666666666666666 20,10",
  );
  assert.equal(trafficCurve([[5, 8]]), "");
});
test("small real traffic stays visible, empty history has a safe scale", () => {
  assert.ok(trafficScale([{ upload: 0, download: 40 }]).max < 100);
  assert.deepEqual(trafficScale([]), { min: 0, max: 1024 });
  assert.deepEqual(trafficScale([{ upload: 0, download: 0 }]), {
    min: 0,
    max: 1024,
  });
});
test("unknown data is not presented as zero and byte units match the desktop", () => {
  assert.equal(rate(null), "—");
  assert.equal(rate(512), "0.50\u2009KB/s");
  assert.equal(rate(7), "0.01\u2009KB/s");
  assert.equal(total(1024 ** 3), "1.00\u2009GB");
  assert.equal(total(0), "0.00\u2009B");
  assert.equal(rate(234567), "229\u2009KB/s");
  assert.equal(total(67891234), "64.7\u2009MB");
});
test("session duration uses both endpoints and handles long sessions", () => {
  assert.equal(
    duration("2026-09-20T00:00:00Z", "2026-09-22T03:40:00Z"),
    "2\u2009天\u20093\u2009小时",
  );
  assert.equal(
    duration("2026-09-20T00:00:00Z", "2026-09-20T00:00:10Z"),
    "0\u2009分钟",
  );
});

test("appending a spike preserves interior segments and curves stay bounded", () => {
 const points = [[0, 0], [9, 30], [21, 0]];
 assert.equal(trafficCurve([...points, [27, 90]]).split("C")[1], trafficCurve(points).split("C")[1]);
 const commands = trafficCurve(points).split("C").slice(1);
 commands.forEach((command, i) => {
  const v = command.trim().split(/[ ,]+/).map(Number), a = points[i], b = points[i+1];
  assert.deepEqual(v.slice(4), b);
  for (let k=0;k<=100;k++) {
   const t=k/100,u=1-t,y=u*u*u*a[1]+3*u*u*t*v[1]+3*u*t*t*v[3]+t*t*t*b[1];
   assert.ok(y>=Math.min(a[1],b[1])-1e-9 && y<=Math.max(a[1],b[1])+1e-9);
  }
 });
});

test("equal-time 0,30,0 is symmetric and joins share a tangent", () => {
 const points=[[0,0],[5,30],[10,0]], slopes=chartFunctions.trafficSlopes(points);
 function value(i,t){const a=points[i],b=points[i+1],h=(b[0]-a[0])/3,u=1-t;
 return u*u*u*a[1]+3*u*u*t*(a[1]+slopes[i]*h)+3*u*t*t*(b[1]-slopes[i+1]*h)+t*t*t*b[1];}
 for(let k=0;k<=100;k++)assert.ok(Math.abs(value(0,k/100)-value(1,1-k/100))<1e-9);
 assert.equal(slopes[1],0);
 const ramp=chartFunctions.trafficSlopes([[0,0],[5,15],[10,30]]);
 assert.deepEqual(ramp,[3,3,3]);
});
test("wall time advances the viewport without generating traffic", () => {
 const samples=[{at:"2026-10-05T00:00:00Z",upload:0,download:30}];
 const slots=chartFunctions.trafficSlots(samples,1,Date.parse("2026-10-05T00:00:04Z"));
 assert.equal(slots.filter(s=>!s.placeholder).length,1);
 assert.equal(slots.at(-1).download,null);
 assert.equal(slots.at(-5).download,30);
});

test("the viewport follows source time at normal speed while keeping original samples", () => {
 const clock=new chartFunctions.ChartTimeline();
 clock.observe(10,5,100);
 assert.equal(clock.read(102.5),12.5);
 assert.equal(clock.read(106),16);
 clock.observe(15,10,105);
 assert.equal(clock.read(105),15);
 assert.equal(clock.read(107.5),17.5);
 assert.equal(clock.read(999),909);
 const slots=chartFunctions.trafficSlots([
  {at:"2026-10-05T00:00:05Z",upload:0,download:0},
  {at:"2026-10-05T00:00:10Z",upload:30,download:30}
 ],1,Date.parse("2026-10-05T00:00:07Z"));
 assert.equal(slots.at(-1).download,30);
 assert.ok(slots.at(-1).time>Date.parse("2026-10-05T00:00:07Z"));
});

test("left boundary retains predecessor points for clipping instead of dropping a segment", () => {
 const start=Date.parse("2026-10-05T00:00:00Z");
 const points=Array.from({length:16},(_,i)=>({at:new Date(start+i*5000).toISOString(),upload:i,download:i}));
 const slots=chartFunctions.trafficSlots(points,1,start+72000);
 const actual=slots.filter(s=>!s.placeholder);
 assert.ok(actual[0].time<start+12000);
 assert.ok(actual.filter(s=>s.time<start+12000).length>=2);
});
