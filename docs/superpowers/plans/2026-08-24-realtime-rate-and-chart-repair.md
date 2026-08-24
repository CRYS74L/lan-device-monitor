# 实时网速刷新与连续折线修复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让设备监控按照设置的刷新周期进行轮询和 UI 更新，使用路由器累计字节变化计算真实的区间平均速率，并恢复连续折线图表现。

**Architecture:** 保留现有单线程轮询和 UI 有界投递结构；轮询周期只在请求耗时超过设置值时受请求耗时限制，不额外再等待一个完整周期。DeviceRepository 在累计值变化时计算区间平均速率，并在后续设置周期内继续展示最近一次有效区间速率；MainWindow 每次成功轮询追加有效样本，TrafficChart 仅对启动前无数据的时间段留空，已有样本保持连续线段。在线/离线列表切换继续使用原 1040ms 视觉时长；动画期间冻结可见列表树，后台快照和计数照常更新，结束时一次性对齐最新列表，避免刷新事件打断动画。

**Tech Stack:** .NET 8、WPF、C#、PowerShell、项目自带测试程序。

**Spec:** 当前用户关于“回滚散点图、实际刷新频率符合设置、能用累计值换算但不能伪造、修改后启动软件验收并保持打开”的要求。

## Global Constraints

- 保留现有功能、布局、主题和图表颜色/样式；只恢复被 dev.141 改坏的连续折线数据表现。
- 不以随机数、线性插值或旧值冒充未知的每秒瞬时值；累计值差分只作为真实观测区间的平均速率。
- 只部署干净文件到 `C:\Path\To\LanDeviceMonitor`，不创建 E 盘其他备份或目录。
- 修改完成后启动部署版本并保持运行，供用户验收；不进行长时间压力复现。

---

### Task 1: 轮询周期回归

**Files:**
- Modify: `tests/LanDeviceMonitor.Tests/Program.cs`
- Modify: `src/LanDeviceMonitor.App/Services/MonitoringLoop.cs`

- [ ] **Step 1: Write the failing test**

将耗时超过设置周期的断言改为：下一轮不额外等待完整周期，`GetDelayAfterIteration(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1500)) == TimeSpan.Zero`；未超时仍等待剩余 750ms。

- [ ] **Step 2: Run the focused test and verify it fails**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore`

Expected: 轮询节奏检查失败，现有实现返回 1 秒而不是 0 秒。

- [ ] **Step 3: Write the minimal implementation**

在 `MonitoringLoop.GetDelayAfterIteration` 中将超周期分支改为 `TimeSpan.Zero`，保留请求未超周期时的剩余等待和单次循环不重叠。

- [ ] **Step 4: Run the focused test and verify it passes**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore`

Expected: 轮询节奏检查通过。

### Task 2: 累计差分的每周期有效显示与图表样本

**Files:**
- Modify: `tests/LanDeviceMonitor.Tests/Program.cs`
- Modify: `src/LanDeviceMonitor.App/Services/DeviceRepository.cs`
- Modify: `src/LanDeviceMonitor.App/Models/DeviceModels.cs`
- Modify: `src/LanDeviceMonitor.App/MainWindow.xaml.cs`
- Modify: `src/LanDeviceMonitor.App/Services/TrafficHistoryBuffer.cs`

- [ ] **Step 1: Write the failing regression test**

在实时速率回归中加入：第一次累计值建立基线；第二次累计值增加后得到区间平均速率；第三次累计值不变且原始瞬时值为 0 时，卡片和图表样本仍保留上一有效区间速率，并标记本周期有可绘制样本。该断言应失败于当前 dev.141 的“卡片归零、图表不追加”行为。

- [ ] **Step 2: Run the regression and verify it fails for the expected reason**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore -- --realtime-rate-regression`

Expected: 失败信息显示第三次轮询的卡片值为 0 或 `HasFresh...` 为 false。

- [ ] **Step 3: Write the minimal implementation**

让 `SpeedSample` 把“本轮存在有效原始速率/累计字段”与“数值是否变化”分开；累计差分得到的区间平均值在设置周期内作为最近有效速率继续展示，只有明确离线或经过现有空闲确认窗口才归零。每次成功轮询将该有效速率交给历史缓冲区，不把未知数据标记为真实零值。

- [ ] **Step 4: Run the regression and verify it passes**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore -- --realtime-rate-regression`

Expected: 实时速率回归通过，连续轮询样本数量和保留速率断言均通过。

### Task 3: 恢复连续折线视觉

**Files:**
- Modify: `src/LanDeviceMonitor.App/Controls/TrafficChart.cs`
- Modify: `tests/LanDeviceMonitor.Tests/Program.cs`

- [ ] **Step 1: Write the failing chart-data assertion**

让历史缓冲区在连续有效轮询样本下保持每秒槽位，并保留前段尚未采到数据的 placeholder；测试不得要求用未知数据伪造样本。

- [ ] **Step 2: Run the regression and verify it fails**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore -- --realtime-rate-regression`

Expected: 当前实现因只保留新原始点而无法满足连续有效样本断言。

- [ ] **Step 3: Write the minimal implementation**

恢复 dev.140 的连续系列绘制和面积填充；placeholder 只保留为启动前空白，不再将有效方向样本拆成散点段。保留 hover、缩放和主题资源。

- [ ] **Step 4: Run tests and inspect the diff**

Run: `dotnet build tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore` and `git diff --check`。

Expected: 构建成功、无 diff 检查错误，测试断言通过。

### Task 4: 在线/离线列表切换动画

**Files:**
- Modify: `tests/LanDeviceMonitor.Tests/Program.cs`
- Modify: `src/LanDeviceMonitor.App/MainWindow.xaml.cs`

- [x] **Step 1: Write the failing animation-duration regression**

约束列表切换动画必须保持原来的 1040ms；先用检查捕获不应擅自改短的实现。

- [x] **Step 2: Preserve duration while reducing overlap and making completion safe**

保持切换动画 1040ms；切换期间不再把监控刷新逐项写入可见 ListBox，切换前后清理透明度/位移动画时钟，避免快速反向切换时旧完成回调和新列表同时参与；详情面板动画延后到列表切换完成后再开始，避免两个大视觉树并发运动。

- [x] **Step 3: Run the focused regression**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore -- --filter-animation-regression`

Expected: 原 1040ms 动画时长回归通过。

### Task 5: 发布与实际验收

**Files:**
- Modify: `src/LanDeviceMonitor.App/LanDeviceMonitor.App.csproj`

- [ ] **Step 1: Bump the existing dev version once and build**

延续 `v0.1.0-dev.N` 序号，发布 self-contained `win-x64` 到项目 `dist` 目录。

- [ ] **Step 2: Verify published files against the exact deployment directory**

使用 SHA-256 对比 `dist/LanDeviceMonitor.App` 与 `C:\Path\To\LanDeviceMonitor`，要求缺失、额外、哈希不一致均为 0。

- [ ] **Step 3: Launch and leave the final app open**

启动 `C:\Path\To\LanDeviceMonitor\局域网设备助手.exe`，短时检查窗口、版本、设置默认值、标题时间每周期变化和图表连续线，不主动关闭最终进程。

- [ ] **Step 4: Commit, tag, and push**

创建唯一 annotated dev tag，核对工作区干净、标签指向 HEAD、远程分支和标签已推送。
