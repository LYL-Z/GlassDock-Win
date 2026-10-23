# M0 最终技术决策报告

- 日期：2026-10-23
- 仓库：`D:\MacOS\GlassDock-Win`
- 结果：**NO-GO（当前证据不足以冻结窗口/渲染路线或启动依赖该路线的生产实现）**
- 说明：这是按已批准范围得出的阶段决策，不表示原始 33 项 M0 全部通过；未通过、阻断和未测试项按原状态保留，完整 ID 去向见 [需求迁移矩阵](M0-to-M1-requirement-migration.md)。阶段调整记录见 [M0 范围变更](M0-scope-change-2026-10-23.md)。
- 后续允许开展不依赖路线的 M1 官方资料、产品行为规格、46 项映射和素材权利研究；这不等于 M0 GO、M1 整体开始验收或规格冻结。

**后续执行安排（2026-10-23）：** 负责人随后批准在 M0 仍为 NO-GO 的同时并行开展不依赖 Windows 最终选型的 M1 官方资料与产品规格草案。该调整不更改本报告的 M0 判定、不授权生产渲染器开发，也不代表 M1 已验收。具体边界见 [M0/M1 并行工作线变更](M0-M1-parallel-work-change-2026-10-23.md)。

## 决定性证据

用户输入 `RUN M0 FINAL DESKTOP TEST` 后，运行了一次有界的受控 WGC 实验：独立 WinUI Composition 对照和项目 WinUI Dock 各一张区域捕获，项目 Dock 固定 Bottom、58 DIP、当前显示器原生模式（3840×2400、窗口 DPI 240，即 250%）。没有输入注入、显示设置变化、提权、游戏启动、全屏测试或完整桌面帧落盘。

| 检查 | 实际结果 | 状态 |
|---|---|---|
| Composition API/Win2D Gaussian blur 建立 | 两个窗口均报告 `EffectBrushCreated`、HRESULT `0x00000000`，blur 为 28 DIP | PASS（仅 API 对象创建） |
| 同一 WinUI 窗口内的效果链 | 控制窗口的黑白 XAML 条纹在圆角面板内明显混合为灰色，面板外条纹仍清晰 | PASS（同窗内容 blur；不证明桌面/跨窗采样） |
| Dock 下方另一窗口/桌面的 backdrop 采样 | 项目 Dock WGC 图没有显示被条纹图案采样并模糊；面板呈近白实底。8 个静态图标/标签可见 | FAIL（当前 Dock 窗口集成未显示外部背景 blur） |
| 项目 Dock 圆角、阴影、region 外观 | 图中可见外轮廓/阴影；一张裁切图不足以判定像素级抗锯齿、阴影被裁边界或动态放大外溢 | NOT TESTED（完整边缘品质） |
| WPF 非激活启动 | WPF 进程启动并写出启动日志，但脚本未在 12 秒内通过 `Process.MainWindowHandle` 找到可见 HWND，随后停止。未生成 startup-result | BLOCKED（探针发现方式不可靠；不能判为 WPF 功能失败或通过） |
| WinUI 非激活启动 | WPF 检查中止后未执行 | NOT TESTED |
| 非激活启动下 DXGI 独占共存 | 因首个脚本未成功且恢复标志失败，按边界未启动第二脚本 | NOT TESTED；旧默认启动路径的焦点/独占问题仍保留 |

对照截图和元数据只保存在本地忽略目录，不进入 Git：

- 独立控制图：[independent-control.png](../../../.local/evidence/m0-final/20261023T111923Z-94bc33e6/composition-control/independent-control.png)
- 项目 Dock 图：[project-dock.png](../../../.local/evidence/m0-final/20261023T111923Z-94bc33e6/project-dock/project-dock.png)
- brush/window/capture 元数据：[本轮证据目录](../../../.local/evidence/m0-final/20261023T111923Z-94bc33e6/)
- 测试进程与桌面恢复记录：[post-state.json](../../../.local/evidence/interactive-tests/4b4d59d507ca4aa1be00685e3f38d75a/post-state.json)

控制图中的条纹和面板属于同一 HWND/WinUI visual tree，因此只证明 Composition blur effect 能处理同窗内容，不证明它能采样 Dock 下方另一个窗口的像素。项目 Dock 捕获未显示外部背景 blur，因此跨窗/system-backdrop 能力仍未验证，当前实现候选失败。现有证据不能区分 system-backdrop 配置、视觉层级、窗口宿主限制或代码集成的具体贡献；也不能据此声称 Windows Composition API 不存在，或外推为 WinUI 整体不可能。

## 测试中止与桌面恢复

脚本在 WPF 启动观察中止，`Completed=false`、`StopReason=runner-failed`、`RestorePassed=false`。恢复检查中，测试登记的进程清理、原前台窗口、指针、任务栏、显示边界及 watchdog 退出均记录为通过；失败项是测试脚本为子进程设置的进程环境变量没有在脚本退出前回滚。实际脚本运行结束后，项目路径下未发现 WPF/WinUI/WGC 测试进程；独立 desktop state checks 没有报告任务栏、显示边界、前台或指针变化。

因为 `RestorePassed` 为 false，本轮授权范围要求停止；没有运行伴随的 DXGI 独占观察，也没有重跑或扩展其他桌面场景。事后仅修复了测试脚本的环境回滚和可见 HWND 枚举实现；这些修正只做静态检查，不构成本次运行通过证据，也未再次启动桌面窗口。

## 技术决定

- **主路线：暂不选定。** WinUI 3 Composition 的同窗 blur effect 通过；Dock 跨窗 backdrop 未验证成功。WPF 当前有较强的有限输入穿透证据，但 DWM system backdrop 简单路径此前视觉失败，且本轮非激活启动未取得有效结果。
- **备选：保留 WPF 与 WinUI 3，不冻结任一者。** 渲染和 HWND 管理仍应与 `GlassDock.Core` 状态/几何分开，避免把框架类型引入核心模型。该边界是低成本架构建议，不是已实现的完整抽象层。
- **DirectComposition：本轮不启动。** 现在还没有证据证明公开的窗口级 Composition/SystemBackdrop 集成已尝试到位；直接换到原生 API 会扩大范围。若以后重新批准，只需先做一项目标 Dock HWND 的窗口级 backdrop 验证，限定 Bottom/58 DIP/同一条纹背景；真实 blur 在 Dock 窗口不出现即停止该路线并重新评估原生后端。
- **技术结论：NO-GO。** 不冻结路线、不启动依赖最终路线的生产实现。当前阻断是 Dock 窗口级 blur 能力与非激活 HWND 观测尚未取得可靠证据，不是缺少更多视觉矩阵。负责人后来授权独立 M1 规格线并行；这不改变本 NO-GO。继续桌面观察须取得新的、范围对应的启动短语。

## 工程验证边界

- 本地 `dotnet restore --locked-mode`、Release solution build（11 项目、0 警告/错误）及 Core 单测（18/18）在本轮桌面试验前通过；日志在 `.local/evidence/m0-final-prep/`。脚本修复后 21 个 PowerShell 脚本解析无错误，不运行桌面程序。
- 最终代码/报告提交 `c8b11d170673fdf7b2ea7f7b3b470344f46e75b2` 已推送；精确 GitHub Actions [Windows CI run 39946556825](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39946556825) 中 Build 与 Test 均成功。该 CI 不执行桌面交互/视觉场景。CI 附注只有 Actions Node.js 20 弃用提醒。
- LICENSE 未改，未创建 Release，未改写 Git 历史。
- 当前设备以外的 DPI/多显示器/刷新率组合、真实游戏输入/恢复、Dock 自身帧时序及长期运行仍为原需求状态；见 [需求追踪矩阵](REQ-matrix.md) 与 [M0→M1 迁移矩阵](M0-to-M1-requirement-migration.md)。
