# M0 技术路线对比与复评

更新日期：2026-10-23。本文区分已观测结果、公开 API 能力和未验证推断；不代表生产框架已选定。

> **范围变更说明：** 原始 33 项 M0 证据仍保留在 [基线矩阵](dock-evidence/M0/REQ-matrix.md)；完整视觉、动画、硬件矩阵与性能验收已按有记录的变更移交后续阶段，不代表 PASS 或豁免。逐 ID 去向见 [迁移矩阵](dock-evidence/M0/M0-to-M1-requirement-migration.md)。最终有限 Composition 尝试已运行但目标 Dock 路径未显示 blur；M0 技术决策仍为 **NO-GO，技术路线未冻结**。之后只获准并行开展 M1 规格研究，不包括生产实现或阶段验收。见 [最终决策](dock-evidence/M0/M0-final-technical-decision.md) 与 [M0/M1 并行变更记录](dock-evidence/M0/M0-M1-parallel-work-change-2026-10-23.md)。

## 当前判断

| 维度 | WPF + Win32 互操作 | WinUI 3 + Windows App SDK | Win32 + DirectComposition / Direct2D |
|---|---|---|---|
| 逐像素透明与点击路由 | 默认 layered-window / hit-test 变体在当前 250% 单屏 SendInput 矩阵 18/18 通过；物理输入和独占输入未测 | 默认透明根区域 9/9 失败；公开 SetWindowRgn 候选输入 18/18 通过，但矩形残边、AA、动画和 DPI 代价未验 | 未实现；裸 Win32 不自动得到圆角透明输入，需要自建 hit-test 与 clip |
| 桌面背景模糊 | WPF + DWM system backdrop 的 6 场景无确认 blur；当前 layered HWND 简单路径视觉 FAIL | Desktop Acrylic 6 场景无 blur。最终 Composition control 对照有真实 blur，但目标 Dock 区域没有可辨软化；当前 Dock XAML brush 集成视觉 FAIL | 未实现；其他系统级 backdrop/DirectComposition 链无实际 Dock 窗口证据 |
| 材质还原空间 | 当前 whole-HWND DWM 候选视觉 FAIL；WPF Visual Layer + HwndHost 尚未验证且有互操作代价 | 独立 WinUI Composition 控制窗口证实 blur API 可运行；目标 Dock HWND 的窗口 backdrop / 视觉层级配置尚未解决 | 自定义空间最大，但需自行承担圆角 mask、alpha、阴影、设备恢复、能耗及无障碍维护 |
| 动画与性能测量 | 可选 CompositionTarget.Rendering 采样和动画完成计时已实现；未收集运行 trace | 可选 XAML rendering callback trace 已实现；未收集运行 trace | 可自建视觉树/动画；API 层级更低不代表帧率更稳，尚无测量 |
| 独占全屏启动 | DXGI 状态仍 true、Dock 不在 WGC 帧里；但采样前台 PID 是 Dock PID，默认路径违反不抢焦点。游戏输入和退出恢复未测 | 启动后 DXGI 独占已变 false，前台 PID 是 Dock PID；OnLaunched 调用 Activate；因果未隔离 | 未测；仍须不抢焦点、不强退独占并能恢复 |
| SetWindowRgn 视觉/扩展 | 不适用当前 WPF 输入路线 | 区域输入矩阵 18/18 通过；本轮其材质变体 6/6 有 WGC 帧但 0/6 视觉通过。面板轮廓可见，仍有矩形宿主边线；像素级 AA、动画外溢、多 DPI 重建未测 | 可设计自定义 alpha mask/clip，但需完整实现和验证 |
| DPI / 多显示器 / 刷新 | 当前仅 250% 单屏、120/240Hz 窗口生存；其余设备依赖未测 | 相同；region 只在当前 250% 输入矩阵测过，布局变化/动态 DPI 未测 | 未测 |
| 托盘 / 恢复 | WPF Show/Exit 实际通过；隔离配置缺失/非法恢复通过 | Native HMENU 含 Show/Exit，router 回调单测 3/3；真实键盘/辅助功能选择未测，自动化读取失败不是产品缺陷证据 | 未实现 |
| 现阶段结论 | 输入证据较强；当前 DWM backdrop 简单组合视觉 FAIL；非激活测试 BLOCKED | 默认透明输入失败、region 输入候选通过；Acrylic 与当前 Dock Composition blur 集成视觉 FAIL；非激活/独占候选未完成 | 暂不启动；只有目标窗口级公开 Composition 路线经一项窄验证失败后再申请 |

本轮视觉证据仅针对本机 Windows 11、3840×2400、240 DPI / 250%、单显示器、58 DIP 图标与静态状态。浅/深表示受控底图颜色而非系统主题切换。18/18 捕获来自 WGC 的 DWM 合成主屏图像，不是 scanout；静态图标完整不代表 zoom、动画、菜单或拖拽已通过。报告和逐场景判定位于 D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261023T091558Z\，原始图像不提交 Git。

## 公开 API 的系统材质方案

### WinUI 3

WinUI 3 候选通过 SystemBackdropElement 设置 8 DIP 内边距、28 DIP 圆角区域，在图标 pill 后启用 Desktop Acrylic，前景保留低透明 tint、边缘高光和 ThemeShadow。本轮 6 个场景的运行日志确认 Desktop Acrylic 候选启用，但高对比网格、棋盘边界和斜线在面板内仍清晰可见，故该候选的真实 blur 视觉判定为 FAIL。当前捕获不能确定是系统回退、运行环境条件还是此窗口/材质组合限制；没有把未查明原因写成 API 不支持。[Materials overview](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials) · [SystemBackdropElement 与圆角区域](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)。

单纯 Desktop Acrylic 候选不通过，不代表 WinUI Composition blur API 不存在。本次 independent control 视觉 PASS 证明 effect 能在受控 WinUI composition window 内形成实际模糊；Dock HWND 的同类 brush 未显示模糊，说明目标窗口集成路径尚未通过。微软文档也区分常规 `CompositionBackdropBrush` 与系统 backdrop brush 的用法；API 公开存在不代表当前 XAML Border 已绑定到能够采样窗口外背景的配置。[Composition brushes](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-brushes) · [CompositionBackdropBrush API](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositionbackdropbrush?view=windows-app-sdk-2.0)。下一步不是扩大场景，而是在另获授权后仅验证目标 Dock HWND 的 window-level/system-backdrop 接入；若同一高对比背景仍无可见 blur，就停止该分支。API 成功也不等于 Liquid Glass 的折射、边缘色散或动态光学响应已实现。

### WPF

候选在 OnSourceInitialized 调用 DwmSetWindowAttribute 设置 DWMWA_SYSTEMBACKDROP_TYPE=38、DWMSBT_TRANSIENTWINDOW=3。六个场景日志的 HRESULT 均为 0；WGC 图像仍清晰透出测试网格，真实 blur 视觉判定为 FAIL，并能看到圆角 pill 外的矩形宿主底色/轮廓。由此停止当前 WPF layered HWND + whole-HWND DWM backdrop 的简单视觉分支；不能把 HRESULT 成功写成材质通过。[DWM system backdrop type](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)。

WPF 的 HwndHost / Visual Layer 互操作是另一条路线，不应混同于本次已失败的 DWM 候选。它还没有 PoC 证据。微软文档列出的 HwndHost 限制包括整体 opacity/transform 与父元素 clipping 差异，区域内输入也直接进入 Win32；这可能增加逐像素透明、圆角和 zoom 动画的集成成本。因此只在用户重新批准后考虑一次有界验证；若单次运行仍无真实 blur，或留下矩形宿主/不可接受裁切，就停止该分支。[WPF Visual Layer interop](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/using-the-visual-layer-with-wpf) · [WPF/Win32 interop 限制](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-win32-interoperation)。

旧 DwmEnableBlurBehindWindow 自 Windows 8 起不会产生预期 blur，不作为方案。[DwmEnableBlurBehindWindow](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow)。

### 最终一次 Composition 背景采样实验（已按限定授权运行）

本次使用 WinUI Composition `CompositionBackdropBrush` → Win2D `GaussianBlurEffect`（28 DIP）→ 圆角 XAML `Border`。控制窗口为独立全屏条纹 HWND；Dock 固定 Bottom、58 DIP，并启用当前 `SetWindowRgn` 圆角候选和 `ThemeShadow`。各保存一张 WGC 局部区域图，未裁切全屏源帧只在内存中用于裁剪，不落盘；没有增加主题/位置/尺寸矩阵。控制窗口看见真实条纹软化；项目 Dock 面板无可辨真实 blur，呈近白实底，8 个静态图标/标签可见。区域轮廓有边线/阴影，但一张局部图不足以量化 AA 或动态 region 裁剪。元数据在 `.local/evidence/m0-final/20261023T111923Z-94bc33e6/`。

微软官方文档明确说明 `CompositionBackdropBrush` 在桌面应用可采样应用内容或应用窗口后方的像素，并可作为模糊效果输入；文档中的能力说明不等于当前环境已成功。[Composition brushes](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-brushes)。系统 Acrylic 同样是官方 backdrop 方案，但此前实测无 blur；应用内 `AcrylicBrush` 只模糊当前应用 XAML 内容，不是桌面 HostBackdrop。[Materials overview](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials) [In-app Acrylic](https://learn.microsoft.com/en-us/windows/apps/develop/ui/in-app-acrylic)。

判读已停止：effect brush 在两窗口创建成功；control 实际 blur PASS；Dock window blur FAIL。可归因范围是项目 Dock 的 system-backdrop/视觉层级集成，不是 API 创建失败。没有更多实验用来确定具体子因；按用户要求不继续调参数扩展样本。测试脚本的环境变量恢复和可见 HWND 枚举已静态修正，但没有重跑桌面测试。非激活启动因此 WPF=BLOCKED、WinUI=NOT TESTED，DXGI companion=NOT TESTED。

精确实现与单次授权门见 [ADR-0002](adr/0002-final-m0-rendering-and-window-host.md) 和 [M0 最终决策报告](dock-evidence/M0/M0-final-technical-decision.md)。

## WinUI SetWindowRgn 候选的边界

本次组合捕获为 SetWindowRgn + Desktop Acrylic 六场景：Light/Dark × Bottom/Left/Right。SetWindowRgn 日志均为 applied=True；WGC 6/6 成功，静态 Dock 内容 6/6 完整。视觉通过为 0/6：网格和斜线仍清晰，背景 blur 未生效。六张图均能看到圆角 Dock 剪影，但仍有不同程度的窗口外围矩形边线/光晕；本轮未定位该轮廓来自 region 边缘、DWM 阴影还是视觉宿主。

1. **玻璃边界和阴影**：当前截图没有显示 region 外内容被完全清除；仍有矩形轮廓和阴影残留。证据支持“该组合没有消除矩形宿主外观”，但不能仅凭截图断定轮廓的具体 compositor 来源。region 外的 blur、shadow 或 glow 是否被裁仍需单独像素级检查。
2. **圆角抗锯齿**：截图比例下 pill 圆角可见，没有在当前视图发现明显阶梯；没有放大做像素级 AA 测量，因此高质量连续抗锯齿仍 NOT TESTED，不能把外观轮廓可见写成 AA 通过。
3. **图标放大与内容裁剪**：所有场景是静态，没有输入注入或 hover 峰值帧。8 个静态图标没有被裁剪；靠边图标放大后是否超出 region 仍 NOT TESTED。
4. **尺寸/DPI 更新**：本轮仅 250% DPI、58 DIP 和当前 Dock 窗口尺寸。尚未验证跨 DPI、窗口尺寸改变或多显示器迁移后的 region 重建。
5. **长期扩展**：当前 evidence 只保留其透明输入价值和静态圆角剪影，不支持其作为最终玻璃窗口形状。菜单、拖拽、zoom、边缘光晕、阴影外溢和动画期间 region 更新都需要独立测试授权及证据。

因此 SetWindowRgn 当前不是最终视觉方案，也没有证据证明输入修复必须牺牲玻璃外观：输入矩阵确实 18/18 通过，材质矩阵却 0/6 视觉通过，且保留矩形边线。它可以继续作为 WinUI 输入候选与其他 Composition mask/blur 方案做一次有限技术验证；若需要动态扩大 region 或动画边界仍出现切边，就停止这个输入 workaround，不以不断重建 region 掩盖缺陷。

参考：[SetWindowRgn](https://learn.microsoft.com/en-us/windows/win32/api/user32/nf-winuser-setwindowrgn) · [Window regions](https://learn.microsoft.com/en-us/windows/win32/gdi/window-regions)。

## WinUI 默认输入与托盘

默认透明区域的 Pointer 日志证明事件到达覆盖全窗的 `LayoutRoot`，处理器没有标记 `Handled`；当前证据更符合 HWND 矩形区域与 XAML 根命中共同导致，不足以完全拆开各自贡献。公开 region 可以把几何上不需要的部分从 HWND 绘制/命中区域剔除，因而在当前 PoC 通过输入。其他不维护的系统级技巧（跨线程 `HTTRANSPARENT`、整窗 `WS_EX_TRANSPARENT`）无法满足 Dock 内容仍可交互的需求；本轮不引入。

Tray 是 Win32 原生 `CreatePopupMenu` / `AppendMenu` / `TrackPopupMenu(TPM_RETURNCMD)`。代码实际添加 Show、Exit 项，并将返回 command 分发到 `_showDock` 或 `_exit`；新增 Core 单测分别验证 Show、Exit 和未知命令行为。自动化工具无法读出 popup menu，不代表菜单项不存在；真实选中和键盘/辅助功能验证仍未完成。

## 独占全屏验收口径和实测

真正 DXGI 独占时 Dock 不可见允许是预期行为；Dock 不得成为前台、不应拦截游戏输入或导致独占状态退出，游戏退出后应自动恢复 Dock。原受控探针结果：

- WPF：DXGI 继续为 exclusive=true；Dock 未出现在 WGC 帧里（允许），但 Foreground PID 是 Dock PID。真实游戏输入没测，所以当前启动行为有焦点缺陷，隐藏 HWND 是否仍截获游戏输入待测。
- WinUI：Foreground PID 是 Dock PID；DXGI 已在 Dock 启动后的首轮取样时退出 exclusive，随后 WGC 帧可见 Dock。`App.OnLaunched` 调用 `_window.Activate()`，与前台变化吻合；仍未做禁用激活对照，不能把独占退出全部归因到它。

已实现默认关闭的非激活启动候选：WPF `Window.ShowActivated=false`；WinUI 通过公开 `AppWindow.Show(false)` 显示窗口，再用 `SWP_NOACTIVATE` 置顶。新脚本参数 `-NonActivatingDockStart` 可选择候选，但没有执行。下次获批时分别采集原前台 PID、Dock 前台 PID、DXGI 独占前后状态；仅当 Dock 非前台、DXGI 独占不因 Dock 启动而退出，才满足这两项。实际游戏输入和退出后恢复还需另一组获批情景。

API 依据：[WPF ShowActivated](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.showactivated) · [AppWindow.Show(Boolean)](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.show?view=windows-app-sdk-2.0) · [Window.Activate](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window)。

## Dock 自身性能记录器

新增纯逻辑 `DockPerformanceRecorder` 和 opt-in `M0PerformanceSession`：

- `GLASSDOCK_M0_PERF_TRACE=1` 后才订阅 `CompositionTarget.Rendering`；窗口关闭时解绑。没有启用时不增加逐帧回调负担。
- 记录 Dock 逻辑更新间隔、框架 rendering callback 间隔、动画完成/中断时长，并输出分布 P50/P95/P99 及保留的样本序列。
- 每秒记录进程 CPU（单核百分比与按逻辑处理器数归一化）、working set、private bytes，并按动画活动/空闲状态打标签；GPU 利用率/显存明确输出 null，表示未测。
- 输出 JSONL 受控在显式 D: `GLASSDOCK_POC_STATE_ROOT`，未设置 D: 路径时拒绝启动 trace。`scripts/measure-poc.ps1 -EnableM0Telemetry` 已能设置该开关；运行会显示 topmost Dock 并影响当前桌面，需要先获批。
- WPF 的回调在 WPF 把视觉树提交到 compositor 之前触发；WinUI 的 callback 在 UI thread 上且会使线程持续活跃。该订阅自身可能增加 CPU/电量开销，所以只用于短时诊断。两者都只是**框架渲染回调节奏**，不是 GPU command submit、per-process present、DWM 合成帧或面板 scanout。[WPF CompositionTarget.Rendering](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.compositiontarget.rendering?view=windowsdesktop-10.0) · [WinUI CompositionTarget.Rendering](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.compositiontarget.rendering?view=windows-app-sdk-2.0)。

当前通过纯单元测试验证了分位数、样本保存、Idle/Animation 分组、动画取消与 tray dispatch。没有运行 Dock 生成实测 JSONL，因此没有报告 P50/P95/P99 数值、图标负载差异、animation frame miss 或真实 CPU 消耗。DWM 被动约 240Hz 是全局合成指标，不能替代这些数据。

## 停止条件与有限验证计划

1. **系统材质 PoC**：WPF DWM backdrop 与 WinUI Desktop Acrylic（含 SetWindowRgn 变体）的授权 18 场景已完成，均未通过真实 blur 验收；不再重复这三种当前候选。若用户另行授权，只实施一次 WinUI Composition 背景采样 + blur + 圆角 mask；中心高对比线必须可见软化，且不得留下矩形宿主或明显切边。单次不满足即记录 FAIL 并停止该分支，不叠加 tint 或装饰层伪装。
2. **全屏候选**：先对比默认和显式 non-activating startup 的 foreground PID 与 DXGI state；仅有 no-focus/no-lost-exclusive 结果仍不等于通过游戏输入或自动恢复，后两者要单独获批。
3. **性能 trace**：获批后分别采 idle 与 pointer-triggered animation、8/24 图标、44/58/72 DIP；报告逻辑事件、callback 与动画完成时间分布、CPU/内存资源。120Hz 的 8.33ms、240Hz 的 4.17ms 可作 callback 间隔对比标尺，但不是最终呈现门槛；未取得进程 Present 或屏幕帧数据前，不得声称达到刷新率流畅度要求。
4. **DirectComposition 决策**：目前不启动原生 DComp PoC。Windows App SDK 自身基于 Composition 且对 WinUI/WPF/Win32 均提供 Visual Layer。只有 WinUI Acrylic + 有效透明输入相冲突，且 WPF DWM backdrop / 一次 HwndHost Visual Layer 公开路线均经有限验证未能满足时，再开一个只验证圆角/Backdrop/动画的原生小 PoC。[Visual Layer overview](https://learn.microsoft.com/en-us/windows/apps/develop/composition/visual-layer)。

## 建议的 A / B / C 后续分级（等待用户确认）

- **A：架构决定性阻断**：目标级真实背景模糊和圆角/阴影边界；Dock 不抢独占全屏焦点或使 DXGI 状态退出；Dock 隐藏时无实际游戏输入拦截；全屏后自动恢复；应用侧动画和资源测量足以比较两个栈。A 未解决不冻结、不进 M1。
- **B：范围可控的未完成验证**：WinUI tray Show/Exit、键盘和辅助功能；region 在图标 zoom / 菜单 / 拖拽 / 动态尺寸时的裁剪；急停热键主动触发与多批次负载脚本。继续记录 BLOCKED / NOT TESTED。
- **C：需其他硬件**：100/150/200% 实际 DPI、跨屏迁移、多显示器、物理 60/120/240Hz 模式及 HDR/VRR/GPU 变体。借用外接屏或另一台 Windows 实机补测；VM 不能代替真实刷新与 scanout。建议在最终 M0 验收前安排硬件补测；若无法获得，提交给用户决定延期/豁免，不自动放宽。

这是验收分级建议，不改变当前 M0 阶段门槛。许可证保持待定，未改 `LICENSE`。Apple 官方图标、标识和其他第三方素材仍需单独核对授权，代码许可证不能替代素材授权。

## 当前决策

就核心问题逐项回答：

- **本轮哪种方案视觉最好：** Dock 目标窗口没有候选达到玻璃视觉验收。独立 Composition control 的真实 blur 最清楚，但它不是 Dock 窗口效果；WinUI + SetWindowRgn 的旧截图只证明静态圆角剪影，不证明材质。
- **哪种最值得继续：** WinUI Composition 保留为候选，理由是独立控制窗口的真实 blur 已通过；但目标 Dock HWND 的当前集成失败，因此现在不建议进入 M1。WPF 保留其较强输入证据；当前 DWM backdrop + layered HWND 视觉 FAIL，非激活观测 BLOCKED。
- **是否已证明某框架有不可接受的底层限制：** 尚未证明框架整体不可能；已明确否定的是当前 Dock XAML backdrop brush 集成路径。具体是 system-backdrop 配置、视觉层级或宿主兼容仍待定。
- **现在是否启动原生 Win32 + DirectComposition：** 否。当前唯一必要的下一项证据是目标 Dock HWND 的 window-level/system-backdrop 单窗口实验；只有它失败后，才比较 WPF Visual Layer 与最小原生后端。

实际透明输入证据仍由 WPF 默认路径 18/18 与 WinUI region 候选 18/18 支持；WinUI 默认透明区 9/9 失败。材料证据则是本轮三候选各 6 场景，视觉 0/18 通过。性能方面仍无应用 trace、进程 Present 或 scanout 数据，DWM 全局 240Hz 数值不替代 Dock 动画测量。独占全屏启动的焦点/状态问题仍未解决。

因此 M0 技术结论仍为 **NO-GO：不冻结 WPF，不放弃 WinUI，不启动原生 DirectComposition PoC。** 负责人之后批准 M1 独立规格研究与技术收尾并行，但不批准依赖窗口/渲染路线的生产实现。上一轮运行因恢复标志失败而停止；任何后续实际桌面捕获、焦点/全屏、输入、显示设置或性能动画测试，都必须另行取得明确授权。原始 33 项仍按各自 ID 跟踪，不改写成 PASS。

## 主要历史证据

- 输入路由：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261022T153139Z\actual-input-results.csv 与 D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261023T033334Z\actual-input-results.csv
- 基线 WGC：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261022T154558Z\
- 18 场景材质视觉及逐项判读：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261023T091558Z\visual-comparison-report.md
- 独占 DXGI：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\fullscreen-exclusive\20261023T032605Z\exclusive-fullscreen-results.json
- 最新已确认全局 DWM 计时：D:\MacOS\GlassDock-Win\.local\evidence\final-validation\resume-20261023T032925Z\dwm-composition.jsonl
