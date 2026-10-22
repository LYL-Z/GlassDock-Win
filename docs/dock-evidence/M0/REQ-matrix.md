# M0 需求与验收矩阵

日期：2026-10-22。状态字段严格采用：PASS、FAIL、BLOCKED、NOT TESTED。证据状态按当前可复核范围判断；代码存在不自动等于实际场景通过。

| ID | 要求 | 证据 / 实际覆盖 | Status |
|---|---|---|---|
| M0-01 | 比较 WinUI 3、WPF 与必要时的 Win32 + DirectComposition 路线 | [技术路线复评](../../tech-spike-comparison.md)；列明实测差异、证据边界与可证伪实验；不冻结生产路线 | PASS |
| M0-02 | 两候选可构建并能在本机启动 | 当前 Release build 成功；本轮 WPF/WinUI 输入脚本启动了候选 PoC | PASS |
| M0-03 | 同一数据/几何基线覆盖 8 / 24 项 | Core 对 8/24 项有单元测试；已有 24 项资源样本 | PASS |
| M0-04 | 44 / 58 / 72 DIP 尺寸适配 | 启动参数、旧 idle 采样有三种尺寸；三尺寸真实几何/视觉适配未完成 | NOT TESTED |
| M0-05 | Bottom / Left / Right 布局 | 两候选均曾三边运行；本轮实际输入测试覆盖 WPF 三边和 WinUI 三边透明角 | PASS |
| M0-06 | 无边框透明圆角及 DWM 真实视觉 | 仅有 WPF / 浅色 / Bottom / 58 DIP / 250% / 240Hz 一张 WGC 合成帧；显示透射与阴影、未看到模糊；其他主题、边、尺寸未捕获 | BLOCKED |
| M0-07 | 透明区域不拦截真实输入 | WPF 三边的合成单击/双击及拖动起始到达底层窗口；WinUI 三边透明区的合成单击/双击被 Dock 接收，底层计数为零 | FAIL |
| M0-08 | 窗口置顶、几何与内容中心命中 | 旧窗口探测记录当前模式下三边窗口与置顶；WPF 有真实内容按钮事件；WinUI 内容区实点未覆盖 | BLOCKED |
| M0-09 | 100% / 150% / 200% DPI 与动态变化 | 仅在系统报告的 250% DPI 做输入测试；其他 DPI、多屏迁移未测 | NOT TESTED |
| M0-10 | Windows 任务栏、最大化、无边框/独占全屏及恢复 | 未实际完成共存、遮挡、前后台、隐藏再显示或恢复测试 | NOT TESTED |
| M0-11 | 浅/深背景视觉透射、模糊、边缘和阴影 | 单个浅色 WPF 样例可看到透射和阴影；深色、WinUI、多背景及视觉矩阵未测，且样例未观察到模糊 | BLOCKED |
| M0-12 | 60 / 120 / 240Hz 环境适配 | 系统当前报告 240Hz；曾切换到 120Hz 并恢复；当前分辨率 60Hz 模式不支持；未取得呈现帧时间数据 | BLOCKED |
| M0-13 | CPU / 内存测量 | 先前静态 idle 样本汇总见 runtime-summary.csv；样本不代表动画负载 | PASS |
| M0-14 | GPU 利用率及 GPU 内存 | 先前静态样本汇总见 gpu-summary.csv；不代表动画合成负载 | PASS |
| M0-15 | 区分更新、提交、DWM 与屏幕呈现帧时间 | PresentMon ETW access denied，无帧 CSV；DWM 全局计时 API 已作为候选调查，但本机未测；面板 scanout 未测 | BLOCKED |
| M0-16 | 托盘 Show / Exit 真实操作 | 源码有实现，未实际执行菜单交互、退出和恢复验收 | NOT TESTED |
| M0-17 | 缺失、损坏、非法配置恢复及日志隔离 | 有配置隔离与错误日志路径；没有注入缺失/损坏/非法配置并观察恢复 | NOT TESTED |
| M0-18 | 分层骨架和核心单测 | 当前 Release 构建通过；Core 单元测试 11/11 | PASS |
| M0-19 | GitHub Actions 远端 CI | workflow 文件存在；无 configured remote，未推送，未运行远端 Actions | NOT TESTED |
| M0-20 | 一条命令构建和单测 | 当前工作树 build.ps1 PASS；test.ps1 -NoRestore PASS，11/11 | PASS |
| M0-21 | 当前提交 / 改动的 D 盘干净克隆 | 旧提交 62c35de 的 clean-clone 不能覆盖当前 HEAD 及未提交改动；本轮未重做 | NOT TESTED |
| M0-22 | 项目许可证决策与依赖盘点 | 依赖清单存在；根 LICENSE 仍未授予许可占位；MIT / Apache-2.0 / GPL-3.0 / 暂不发行比较已记录，用户尚未选择 | BLOCKED |
| M0-23 | M0 报告、矩阵、技术比较和交接更新 | 本轮已更新文档及状态，保持证据路径和未测边界 | PASS |
| M0-24 | M0 完成后方可进入 M1 | M0-07 失败，多项关键 P0 未测/受阻，路线未冻结 | BLOCKED |

## 验收结论

M0 未通过，M1 不开始。WPF 透明区实测输入路由当前领先；WinUI 透明区失败；视觉只有一个 WPF 浅色合成样例；DPI、全屏/任务栏、托盘、配置恢复和有效帧时序仍不完整。未将旧提交 clean clone、workflow 文件、源码存在或单帧捕获替代为当前场景验收。

“实际鼠标事件”测试使用 SendInput 合成输入，并由底层窗口的窗口过程和候选应用事件日志确认路由；这不是物理鼠标手工测试。后续任何覆盖桌面、注入输入或切换显示模式的测试必须先获得用户同意；scripts/verify-poc-input.ps1 已加入 ShouldProcess 高影响确认门槛。

本地构建和 Core 测试结果只说明当前工作树本地验证，不代表远端 CI、干净克隆、游戏/独占全屏、真实面板呈现或发布验收。
