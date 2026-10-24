# Apache ECharts 曲线

- 版本：Apache ECharts 6.0.0，`smooth: 0.5`，`smoothMonotone: 'none'`。
- 上游源码：[src/chart/line/poly.ts](https://github.com/apache/echarts/blob/6.0.0/src/chart/line/poly.ts)。
- 网页使用原版 `ECPolyline.buildPath`，由 `scripts/echarts-curve-entry.js` 适配为保留的数据坐标路径。
- 桌面端 `Controls/EChartsCurve.cs` 是相同 `drawSegment` 分支的 C# 移植，输入为有限、时间递增的有效采样，保留原版的控制点约束与亚像素短段规则。空值过滤由两端的采样层处理。
- 曲线在显示坐标中计算，再还原为数据坐标；移动、坐标范围和悬停由应用管理。
- 实时追加使用完整的 ECharts 曲线，控制点变化按界面动画时长过渡；自动 Y 轴缩放保持曲线计算尺度不变，避免重新塑造历史。

## 重新生成

在项目根目录运行：

```powershell
npm install --prefix .tools/echarts-integration/deps --no-audit --no-fund --save-exact echarts@6.0.0 esbuild@0.25.12
node scripts/build-echarts-curves.mjs
node scripts/generate-echarts-fixtures.mjs
```

生成的网页模块随软件发布，不依赖 CDN。参考数据直接调用固定版本的上游包生成，网页和 C# 回归测试共用该数据核对每个控制点。

ECharts 使用 Apache-2.0，保留本目录的 `LICENSE`、`NOTICE`。打包代码涉及的 zrender 与 tslib 分别保留 `zrender-LICENSE` 和 `tslib-LICENSE`。这些第三方部分继续按各自许可证分发。
