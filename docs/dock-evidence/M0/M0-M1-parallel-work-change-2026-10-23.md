# M0 / M1 阶段依赖调整记录

- 记录日期：2026-10-23
- 变更类型：负责人明确调整阶段依赖；不更改原始任务书或历史验收结果。
- M0 结论：**NO-GO，保持不变。**
- M1 状态：规格研究线**已启动且进行中**；M1 未验收、规格未冻结、生产实现未启动。

## 变更前后

| 工作 | 原串行门槛 | 调整后归属 | 变更理由 | 风险/证据状态 |
|---|---|---|---|---|
| Apple 官方 Dock 功能资料、HIG 与来源索引 | 等待 M0 GO | 可立即开展 M1 规格研究 | 不依赖 Windows HWND 或渲染后端 | 官方资料只证明其公开语义，不证明 Mac 实机行为或精确视觉参数 |
| DOCK-001–046 需求转规格、功能/区域拆解 | 等待 M0 GO | 可立即开展 M1 规格研究 | 产品语义和 Windows 映射可以先形成可审阅草案 | 原 ID、描述、类别及阶段保留；46 项实机观察均未因此通过 |
| 图标/固定区/近期区/文件区、布局、手势、动画、自动隐藏规格 | 等待 M0 GO | 可立即开展 M1 规格研究 | 需求分析独立于框架 | 尺寸、时序、位置、光学效果必须等 Mac 样本校准，标为 provisional / NOT TESTED |
| 素材来源与权利审查 | 等待 M0 GO | 可立即开展 M1 规格研究 | 授权审查与技术选型无依赖 | 许可证仍待权利人决定；本变更不改 LICENSE、不授予 Apple/第三方素材权利 |
| M1 规格冻结与阶段验收 | 等待 M0 GO 及 Mac 样本 | 仍受 M0 决策和样本门槛约束 | 冻结需要可实施架构与可追溯参考证据 | M1 整体不能宣布通过 |
| 生产窗口宿主、渲染器、输入/动画/系统整合开发 | 等待 M0 GO 或获批 CONDITIONAL GO | 仍被 M0 NO-GO 阻断 | 错误宿主选择可能造成重大返工 | 禁止以规格并行工作暗中启动生产渲染器 |

## 双工作线

### A：Windows 技术架构收尾（M0，NO-GO）

仅收敛到：

1. 对现有跨 HWND backdrop 失败完成静态根因排查及关键日志补足。
2. 一次桌面授权内的 Dock 跨窗口背景模糊观察。
3. 一次非激活启动/前台窗口归属观察；若获批，可加一次 DXGI 状态对照。
4. 据此形成 GO / CONDITIONAL GO / NO-GO。失败且没有公开可维护替代路径时，停止同类重复实验，进行一次架构复审并推荐 Win32 + DirectComposition 是否值得做最小 PoC。

没有新的明确授权，不创建可见窗口、不捕获桌面、不切换焦点/全屏、不注入输入。

### B：Dock 产品规格开发（M1 独立研究，草案）

已允许并正在形成的交付：

- [官方来源阅读笔记与事实边界](../M1/official-reference-notes-2026-10-23.md)；
- [原 46 项 ID 的逐项初步规格矩阵](../M1/spec-matrix.csv) 与本轮原始 ZIP 逐行一致性检查；
- [产品区域/状态行为草案](../M1/behavior-specification-draft.md) 和 [首次运行默认项目目录](../M1/default-item-catalog.md)；
- [Mac 实机参考基线](../M1/reference-baseline.md)、[采样计划](../M1/reference-sample-register.csv)、[manifest](../M1/reference-manifest.json)；所有 REF 样本仍缺实机证据；
- [Apple/第三方素材授权风险台账](../M1/rights-and-reference-ledger.md)；
- [未测视觉 token](../M1/design-tokens.json)、[可行性/阶段依赖初稿](../M1/feasibility-and-requirements.md) 与 [架构风险登记](../M1/architecture-risk-register.md)。
- 与 M0 技术门禁的逐项依赖映射。

不能作为已启动的生产实现：M2 状态核心、M3 HWND 宿主、M4 玻璃/图标 renderer、M5 动画及后续 Windows Shell 整合。

## 原始要求与历史结果如何保留

- ZIP 内的 `00_总控执行协议.md`、`01_macOS_Dock参考采样与需求冻结.md`、`12_功能对照矩阵_资料来源.md` 均未修改。
- M0 原始 33 项状态及 DOCK-001–046 原阶段不在本变更中重写；详见 [M0→后续阶段需求迁移矩阵](M0-to-M1-requirement-migration.md) 和新建的 [M1 初步规格矩阵](../M1/spec-matrix.csv)。
- 此项只是允许与 M0 并行做独立产品研究，不等于 M0 完成、M1 整体通过、风险豁免或技术路线冻结。
