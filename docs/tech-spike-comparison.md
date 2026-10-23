# M0 技术路线对比与复评

更新日期：2026-10-23。本文区分已观测结果、公开 API 能力和未验证推断；不代表生产框架已选定。

## 当前判断

| 维度 | WPF + Win32 互操作 | WinUI 3 + Windows App SDK | Win32 + DirectComposition / Direct2D |
|---|---|---|---|
| 逐像素透明与点击路由 | 默认 layered-window / hit-test 变体在当前 250% 单屏 `SendInput` 矩阵 18/18 通过；物理输入和独占输入未测 | 默认透明根区域 9/9 失败；公开 `SetWindowRgn` 候选当前输入矩阵 18/18 通过，但边缘/动画/DPI 代价未验 | 未实现；裸 Win32 不自动得到圆角透明输入，需要自建 hit-test 与 clip |
| 桌面背景模糊 | 新增默认关闭 `DWMWA_SYSTEMBACKDROP_TYPE=DWMSBT_TRANSIENTWINDOW` 候选；WPF HWND 为 `AllowsTransparency=True` layered window，whole-HWND DWM backdrop 是否生效、是否与 layered alpha 冲突，尚未显示验证 | 新增默认关闭 `SystemBackdropElement` + `DesktopAcrylicBackdrop` 候选，圆角边界可指定；是明确的公开桌面 Acrylic 路线，代码已构建但未运行/捕获 | 可自建 Composition 背景/效果链；视觉自由度高，但当前没有 PoC，也不应先于 Windows App SDK Composition 探索 |
| 材质还原空间 | WPF 可叠加自绘 tint、边缘高光和 WPF shadow；若 DWM whole-HWND 候选不可用，公开 Visual Layer + `HwndHost` 是可行互操作路径，但会引入 HWND 空气隔离、裁剪/透明度/输入路由风险 | 系统 Acrylic 可提供真实桌面模糊和透明 tint；`Microsoft.UI.Composition` 能继续叠加自定义视觉/动画。Acrylic 不是 macOS Liquid Glass，也不提供完整光学折射 | 可自定义更多光照、边缘、模糊、畸变与动画；复杂度、设备恢复、能耗和无障碍负担最高 |
| 动画与性能测量 | 当前 WPF `DoubleAnimation`；新增可选 `CompositionTarget.Rendering` 采样和动画完成计时，未收集实际运行 trace | 新增 Composition `Vector3KeyFrameAnimation` 和 finite `CompositionScopedBatch` 计时；可选 XAML rendering callback trace，未收集实际运行 trace | 可自行维护视觉树/动画；API 层级更低不代表帧率更稳，尚无测量 |
| 独占全屏启动 | DXGI 状态仍 true，Dock 不在 WGC 帧里；但采样前台 PID 是 Dock PID，当前默认路径违反不抢焦点。真实游戏输入和退出恢复未测 | Dock 启动后 DXGI 独占已变 false，前台 PID 是 Dock PID；`OnLaunched` 明确调用 `Window.Activate()`；该调用是明确候选原因，但本次未隔离所有因素 | 未测；全屏时仍须符合不抢焦点、不强退独占和自动恢复 |
| 非激活启动候选 | 已加入 `ShowActivated=false` 的显式开关，尚未实际验证 | 已加入公开 `AppWindow.Show(false)` 的显式开关，尚未实际验证 | 可使用 HWND show/no-activate 策略，但状态同步和 WinUI 生命周期需自建 |
| `SetWindowRgn` 视觉/扩展 | 不适用当前 WPF 输入路线 | 输入当前矩阵通过；绘制裁剪会切掉 region 外内容；公开 API 不保证高质量抗锯齿、外溢阴影/光晕或动画扩展。region 浅/深候选帧仍见宿主/阴影矩形边缘；不能作为最终视觉方案 | 可设计自定义 alpha mask/clip，但需完整实现和验证 |
| DPI / 多显示器 / 刷新 | 当前仅 250% 单屏、120/240Hz 窗口生存；其余设备依赖未测 | 相同；region 只在当前 250% 输入矩阵测过，布局变化/动态 DPI 未测 | 未测 |
| 托盘 / 恢复 | WPF Show/Exit 实际通过；隔离配置缺失/非法恢复通过 | Native `HMENU` 含 Show/Exit，router 回调映射单测 3/3；人工/键盘/辅助功能选择未测，当前 UIA/MSAA 读取失败不能归咎产品 | 未实现 |
| 现阶段结论 | 输入证据较强；全屏启动需无激活候选复测，whole-HWND DWM blur 与 layered window 的组合是风险项 | 公开系统材质路径较直接，输入仍依赖 region 与圆角视觉相容性；默认启动需非激活复测 | 暂不启动，除非前两条的公开方案经有界 PoC 证明存在实质限制 |

## 公开 API 的系统材质方案

### WinUI 3

WinUI 3 候选使用 Windows App SDK 2.0+ 的 `SystemBackdropElement`，区域设为 `Margin=8 DIP`、`CornerRadius=28 DIP`，并把 Desktop Acrylic 放在图标 pill 背后。前景层保留低透明 tint、细边缘高光及独立的 `ThemeShadow`。这条路线直接向 DWM/系统 compositor 请求背景材质，验证时可以检查背景细线在 Dock 内是否真实变软，以及边界是否与圆角重合。

Windows App SDK 2.5.1 已通过本机 XAML/Release 编译；没有启动窗口或捕获，因此目前只证明 API 与 XAML 可编译。Acrylic 在透明效果关闭、低性能图形设备、远程桌面/虚拟机、电池节能或高对比度等系统条件下会回退到纯色。回退策略属于系统材质语义，必须在测试证据中记录环境；纯色回退不等于 Acrylic 正常通过。[Materials overview](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials) · [SystemBackdropElement 与圆角区域](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)。

这证明 WinUI 有一条维护性较好的真实桌面 blur 起点，不等于实现 Liquid Glass。Windows Composition 也公开提供 `CompositionBackdropBrush` 作为背景采样输入，并能把 Gaussian blur 等 effect brush 绑定到 SpriteVisual；Win2D effect graph 可继续组合 blur、颜色、对比度、mask 与动画参数。但 Composition Effect 只能使用受支持的内建 effect，不能直接指定任意自定义 shader。macOS 目标还包含折射/高光、边缘色散、动态光学响应等；这些效果仍需单独设计和验证，不能由 API 可用性推断已实现。[Composition brushes](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-brushes) · [Composition effects](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-effects)。

### WPF

新增候选在 `OnSourceInitialized` 调用公开的 `DwmSetWindowAttribute`：`DWMWA_SYSTEMBACKDROP_TYPE=38`，值 `DWMSBT_TRANSIENTWINDOW=3`。Windows 11 22621+ 上，DWM 将其解释为整块 HWND 后方的 Desktop Acrylic。该候选将 WPF pill tint 降低，但仅在 `GLASSDOCK_M0_VISUAL_CANDIDATE=wpf-dwm-acrylic` 时启用，并记录 HRESULT。DWM 调用返回成功只证明属性调用成功，不证明视觉模糊有效。[DWM system backdrop type](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)。

WPF 窗口当前依赖 `AllowsTransparency=True` 的 layered HWND 和 `WM_NCHITTEST` 路由。system backdrop 是 HWND 级整窗材料，而 Dock 需要 pill 区域材质；layered alpha 与 DWM backdrop 的组合没有运行时证据。若 HRESULT 成功但 WGC 中背景仍清晰或矩形外缘不合格，这条简单路线视为失败，不能改写为“背景模糊通过”。旧 `DwmEnableBlurBehindWindow` 自 Windows 8 起调用不会产生 blur，不作为方案。[DwmEnableBlurBehindWindow](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow)。

微软公开说明 WPF 可以通过 `HwndHost` 和 COM/WinRT interop 托管 Windows Composition Visual Layer；但 `HwndHost` 不支持整体 opacity/transform，也不遵循父元素 clipping，且其区域内输入直接转给 Win32。这会影响现有的逐像素透明、圆角裁剪与 Dock 图标动画，故是单独的有界备选 PoC，而不是当前默认修补。[WPF Visual Layer interop](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/using-the-visual-layer-with-wpf) · [WPF/Win32 interop 限制](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-win32-interoperation)。

视觉路线顺序：先测试已实现的 WinUI 区域 Acrylic 和 WPF DWM 整窗 backdrop；WinUI 是当前公开 API 更直接的区域玻璃验证对象。若 WinUI Acrylic 回退或不足，再做一次 WinUI 自定义 Composition blur/mask 有界 PoC；若 WPF DWM 对 layered HWND 无效，再只做一次 WPF Visual Layer/HwndHost 互操作 PoC。每条栈至多一条系统材质与一条互操作/自定义路径；若中心区域没有可见的真实背景 blur，或圆角边缘、阴影、zoom 裁切出现不可接受缺陷，即停止该分支并记录 FAIL。两次实现都失败才考虑 Win32 + DirectComposition。Acrylic 在节能、关闭透明效果、高对比度及低性能设备可能回退为纯色，这些系统降级需与常规运行结果分开记录。[Acrylic fallback](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic)。

## WinUI `SetWindowRgn` 候选的边界

已证实的只有：在 250% DPI、Bottom/Left/Right、当前 Dock 尺寸下，region 候选的实际底层消息与 Dock 内容事件 18/18 符合期望；浅/深候选 WGC 图 2/2 捕获成功，内容仍可见且边缘仍存在矩形宿主/阴影轮廓。它不能从输入通过推导视觉质量通过。

1. **玻璃边界和阴影**：`SetWindowRgn` 限制系统允许绘制的窗口区域。region 外的透明边缘、blur、shadow 或 glow 可能被硬切；DWM 也可能在 region 外继续产生宿主边缘。现有候选图实际留下矩形轮廓，尚未定位具体是哪一层产生。新的 Acrylic 与 region 尚未组合捕获。
2. **圆角抗锯齿**：公开 API 描述 region 的绘制裁剪，不承诺对 HRGN 圆角做可供液态玻璃使用的连续抗锯齿。`CreateRoundRectRgn` 是几何裁剪候选；若高 DPI 放大后边缘出现台阶，不能依赖外侧装饰层掩盖。
3. **图标放大与菜单**：当前动画把图标放大到 1.08 倍，已有截图没有在 hover 动画峰值采样。靠窗口边缘的图标若变换后超出 region 会被裁；此项未验证。单独的系统菜单/Popup 是独立顶层 surface，不应简单推断它也被主 HWND region 裁，但实际菜单交互仍待测。
4. **尺寸/DPI 更新**：候选 region 在 `ApplyEdge` 调整窗口大小时重建；源码没有完整的 `WM_DPICHANGED`/多显示器迁移后自动重建验证。因此跨屏 DPI 变化后 region 可能与新窗口边界脱节，属于未验证风险。
5. **长期扩展**：每种尺寸、方向与 DPI 都要维护同一套几何；dock zoom、拖拽预览、边缘 glow、动画外溢等要求扩出 region。它适合保留为输入解决候选，不具备最终视觉方案资格，直到高质量圆角与阴影/动画外溢通过实际合成截图。

因此它目前不是“可行”或“不可行”的最终判定：输入矩阵通过是真实证据；圆角抗锯齿、动画外溢、模糊边缘和 shadow 仍需同一组合窗口的合成截图。若想保留硬区域的输入收益，可用静态图标先验证 region；若动画时需要变化 region 或把 shadow 放到 region 外，需测其持续重建和切边，不能从 18/18 输入测试推断接受。

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

1. **系统材质 PoC**：获批后仅测当前环境的浅/深受控背景、Bottom/Left/Right、58 DIP；WGC 需检查中心和圆角区域都能看出真实模糊、边缘高光连续、无矩形宿主影、阴影不硬切。若一条路线经两次有依据的公开 API 实现仍没有模糊或出现不可接受的窗口边缘，就停止该路线的材质修补并记录 FAIL，不叠加装饰层伪装。
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

- **接近目标玻璃的公开 API 能力：WinUI 3 较强。** 它直接提供 pill 区域 Desktop Acrylic、圆角 backdrop element、Composition 背景采样与内建效果组合；WPF 当前直接候选只能请求整 HWND DWM 材质，并与逐像素 layered window 有未解组合风险。此判断是基于公开 API 表面和当前架构的优先级，不是实际视觉验收结果。
- **正确输入路由的实测证据：WPF 较强。** 默认路径当前 18/18；WinUI 默认 9/9 透明区域失败，SetWindowRgn 候选 18/18，但其圆角抗锯齿、阴影、zoom 裁切仍未验证。
- **长期稳定动画性能：目前无法比较。** 框架回调、动画时长和资源采集代码已实现，但没有运行时 trace，更没有逐进程 Present / 最终屏幕输出证据。Composition API 可支持动画不等于 Dock 达到 120Hz/240Hz。
- **是否能 1:1 复刻 Liquid Glass：目前两者都没有证据证明可以。** Desktop Acrylic 是系统模糊/色调材料；Composition 的 effect graph 能给出更多受支持的内建效果，但不接受任意自定义 shader。若目标必须包括真实动态折射/光学畸变，WinUI、WPF 上层材质 API 都不能直接保证；低层 D3D/DirectComposition 可能扩大自定义空间，但成本和可维护性高，且当前未实现、未测。

因此 WinUI 是视觉 PoC 的第一验证对象，WPF 继续保留作为输入路由对照和一次 DWM/Visual Layer 有界尝试；二者都不能现在选为最终架构。新材质的运行时图像、非激活启动、输入与恢复、实测动画负载共同决定是否有一个技术栈同时满足三项核心条件。

因此 **不冻结 WPF，不放弃 WinUI，不进入 M1，也不启动原生 DirectComposition PoC**。下一轮先用已获批范围分别测试 Desktop Acrylic 和 DWM backdrop，然后测试 non-activating startup；新的全屏/输入交互仍须单独申请。最终框架决定应等候视觉、系统交互和实际性能证据共同闭合。

## 主要历史证据

- 输入路由：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261022T153139Z\actual-input-results.csv` 与 `20261023T033334Z\actual-input-results.csv`
- 基线 WGC：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261022T154558Z\`
- region 视觉候选：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261023T033440Z\`
- 独占 DXGI：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\fullscreen-exclusive\20261023T032605Z\exclusive-fullscreen-results.json`
- 最新已确认全局 DWM 计时：`D:\MacOS\GlassDock-Win\.local\evidence\final-validation\resume-20261023T032925Z\dwm-composition.jsonl`
