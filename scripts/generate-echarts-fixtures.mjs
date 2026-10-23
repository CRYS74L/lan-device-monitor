// Generate reference geometry directly from the pinned upstream package, not
// from the application's adapter or C# port.
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { resolve, join, dirname } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
const root=resolve(dirname(fileURLToPath(import.meta.url)),"..");
const modules=resolve(process.argv[2]||join(root,".tools/echarts-integration/deps/node_modules"));
if(JSON.parse(await readFile(join(modules,"echarts/package.json"))).version!=="6.0.0")throw Error("Expected ECharts 6.0.0");
const {ECPolyline}=await import(pathToFileURL(join(modules,"echarts/lib/chart/line/poly.js")));
const inputs=[
  ["valley",[[0,-30],[5,0],[10,-30]]],
  ["asymmetric-valley",[[0,-90],[5,-2],[15,-30]]],
  ["spike",[[0,0],[5,0],[10,-1000],[15,0],[20,0]]],
  ["zero",[[0,0],[5,0],[10,0]]],
  ["flat-then-rise",[[0,-1],[5,-1],[10,-1],[15,-9000]]],
  ["subpixel",[[0,0],[.01,-.00001],[.02,0],[.1,-50],[5,0]]],
];
let seed=42;
const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};
for(let n=0;n<20;n++){
  let x=0;
  inputs.push([`irregular-${n}`,Array.from({length:25},()=>{const p=[x,-Math.round(random()*random()*3000)];x+=1+random()*14;return p;})]);
}
const cases=[];
for(const [name,points]of inputs)for(const [scaleX,scaleY]of [[12.6,1.3],[2.52,.6],[1.26,80],[.1,.1]]){
  const segments=[];let previous;
  const context={moveTo(x,y){previous=[x/scaleX,y/scaleY];},bezierCurveTo(x1,y1,x2,y2,x,y){
    const end=[x/scaleX,y/scaleY];segments.push([previous,[x1/scaleX,y1/scaleY],[x2/scaleX,y2/scaleY],end]);previous=end;
  }};
  ECPolyline.prototype.buildPath.call({},context,{points:points.flatMap(([x,y])=>[x*scaleX,y*scaleY]),smooth:.5,smoothMonotone:"none",connectNulls:false});
  cases.push({name:`${name}-${scaleX}-${scaleY}`,points,scaleX,scaleY,segments});
}
await mkdir(join(root,"tests/fixtures"),{recursive:true});
await writeFile(join(root,"tests/fixtures/echarts-6.0.0-curves.json"),JSON.stringify({
  source:"Apache ECharts 6.0.0 ECPolyline.prototype.buildPath",smooth:.5,cases},null,0)+"\n");
console.log(`Generated ${cases.length} upstream reference cases, ${cases.reduce((n,c)=>n+c.segments.length,0)} segments.`);
