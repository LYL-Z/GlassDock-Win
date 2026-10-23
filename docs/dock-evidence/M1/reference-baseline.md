# M1 参考基线档案（草案）

状态：**官方资料基线已建立；macOS 实机基线缺失，不能测量或冻结视觉/时序参数。** 本档案由原始 `01_macOS_Dock参考采样与需求冻结.md` 与 `00_总控执行协议.md` 转录并补充本轮状态；不能用 Windows PoC 或 Apple 文档页面替代 Mac 实机观察。

## 目标样本设定

| 字段 | 设定/状态 |
|---|---|
| 目标 macOS 版本 | 原始任务书指定 macOS 27；记录为待采样目标，不声称当前有实机 |
| 实际 macOS 版本/build | `NOT TESTED` |
| Mac 型号/芯片/内存 | `NOT TESTED` |
| 屏幕分辨率/缩放/刷新模式 | `NOT TESTED` |
| Dock 位置/尺寸/放大/自动隐藏 | `NOT TESTED` |
| 外观模式、壁纸与减少透明/动态效果偏好 | `NOT TESTED` |
| 安装应用、地区与固定/近期项 | `NOT TESTED` |
| 原始截图/录屏路径 | 无；未收到可用 Mac 样本 |
| 官方语义资料 | Apple 使用指南、Desktop & Dock 设置、Folders in Dock、HIG，见 [官方资料笔记](official-reference-notes-2026-10-23.md) |
| 样本状态主索引 | [REF-01–07 样本登记表](reference-sample-register.csv) 与 [manifest](reference-manifest.json) |

## Windows 工程 PoC 对照档案

- 仓库当前 M0 为 **NO-GO**；已观测到的 WPF/WinUI 构建和控件/输入 PoC 只作为本工程启动基线，不代表历史实现，更不代表最终 Dock 的性能或视觉。
- M0 背景模糊、焦点、输入、全屏、资源和帧时状态见 [M0 最终决策](../M0/M0-final-technical-decision.md) 与 [风险登记](architecture-risk-register.md)。
- 本轮代码/日志改动提交后，应在新构建报告中记录其最终 commit SHA；这里不把未提交工作树冒充提交基线。

## 采样与参数规则

Mac 实机一旦可用，每个 REF 记录设备/build、显示拓扑、比例、主题/偏好、Wallpaper ID、Dock 位置/大小、应用状态、动作、捕获方法、路径、SHA-256 和使用权利。原始捕获不因创建项目仓库而获得公开许可。未取得设备或样本时，视觉 token 保持 null + `UNVERIFIED`，见 [design-tokens.json](design-tokens.json)。
