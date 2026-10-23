# ADR-0002：GlassDock 主窗口宿主与渲染路线

- 状态：**Not accepted — M0 outcome NO-GO**
- 日期：2026-10-23
- 范围：只记录 M0 技术决策；不确定 Liquid Glass 最终实现，不选择/改变许可证。
- 证据：见 [M0 最终技术决策报告](../dock-evidence/M0/M0-final-technical-decision.md) 和仓库本地 `.local/evidence/m0-final/20261023T111923Z-94bc33e6/`。

## 背景和本轮结果

原始 M0 输入证据：WPF 默认输入路由在当前限定测试中 18/18 通过；WinUI 3 默认透明区 9/9 失败；公开 `SetWindowRgn` 候选 18/18 通过。WPF DWM system backdrop 和 WinUI Desktop Acrylic 系列此前的视觉矩阵均未显示可确认的真实模糊。

本轮用户授权的单次 Composition 比较得到：WinUI Composition `CompositionBackdropBrush` + Win2D Gaussian blur 的控制窗口中，条纹与模糊面板同属一个 HWND/visual tree，条纹显示真实软化；这只验证同窗内容 blur。相同 effect brush 用在目标 WinUI Dock XAML Border 时，捕获图未显示对 Dock 下方条纹窗口的跨窗采样，面板呈近白实底。两个窗口均报告 effect brush 创建成功、HRESULT `0x00000000`。因此同窗 blur 效果链通过，但**当前 Dock 的外部 backdrop 集成没有通过**。本轮不足以将具体根因归结为 system-backdrop 配置、视觉层级、窗口类型或单一代码错误。

非激活启动观察在 WPF 运行阶段中止：进程有启动日志且清理时仍存活，但脚本未能在 12 秒内通过 `Process.MainWindowHandle` 定位可见 HWND。WinUI 观察及 DXGI 非激活独占观察未运行。因此不能判断非激活方案已工作，也不能把此观测缺口归为 Dock 功能 FAIL。

## 决定

1. 不接受当前 `XamlCompositionBrushBase` Dock 背景集成为主渲染实现。
2. 不冻结 WPF 或 WinUI 3。WPF 保留为输入路径对照；WinUI 3 + Windows App SDK Composition 保留为候选，但尚未证明目标 Dock HWND 的真实桌面背景 blur。
3. 不启动原生 Win32 + DirectComposition PoC。当前尚未验证目标窗口级系统 backdrop 接口；原生后端的实现和维护成本更高。
4. 当前阶段结果为 **NO-GO：不进入 M1**。阻断原因是目标 Dock HWND 的 blur 证据失败，以及非激活启动观察和非激活 DXGI 状态没有有效结果。

## 后续唯一必要的视觉判别实验

只有用户重新授权桌面测试后，才做一个固定 Bottom/58 DIP 的窗口级 backdrop 实验：在目标 Dock HWND 上使用受支持的 system backdrop / host backdrop 组合和圆角 mask，对同一测试条纹窗口捕获 Dock 的局部区域。判定只回答 Dock 窗口实际有无 blur；不再新增位置、主题、尺寸或负载矩阵。若 effect 创建成功但 Dock 局部仍无 blur，即停止该 WinUI 集成路线，转为架构复审，不通过叠加透明层伪装。

非激活观测 harness 已作静态修正：进程级测试环境变量在恢复阶段还原；可见窗口通过枚举该候选 PID 的可见顶层 HWND 定位；异常写入本地 runner-error 证据。修正没有在桌面运行，需下一次授权时才有运行结果。它不能替代前述视觉实验。

## 架构边界与风险

- `GlassDock.Core` 继续持有状态、几何和命令，不依赖 WPF/WinUI XAML 类型。
- 窗口宿主管理（HWND、显示器、前台和层级）与材质/渲染实现分开；只有在实际出现第二个后端时才增加接口，不提前重写项目架构。
- `SetWindowRgn` 目前仅是 WinUI 输入候选。输入 18/18 通过不证明 region 外缘抗锯齿、阴影、放大溢出、多 DPI 动态重建或菜单拖拽合格。
- Composition control 成功不代表 Dock 成功；DWM 全局刷新率不代表 Dock 呈现性能；当前没有进程级 Present 或 scanout 证据。
- 完整 blur、圆角光学边缘、材质动态、动画和负载验收仍必须保留在后续需求矩阵，不能预先记 PASS。

## 重新打开条件

收到新的明确授权后，先确认修订后的桌面安全脚本通过静态检查和无窗口计划检查；然后只运行上述一个窗口级 blur 实验及必要的非激活窗口观察。出现恢复失败立即停止。只有目标 Dock HWND 真实 blur 可见且非激活窗口观察可归因时，才重新评估 CONDITIONAL GO；否则保持 NO-GO，并比较 WPF Visual Layer 与有限原生后端路线。
