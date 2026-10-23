# M0 最后技术阻断项与停止条件

更新时间：2026-10-23。M0 当前结论维持 **NO-GO**；本文件只收敛未决问题，不扩大验收矩阵。

## 当前决定性阻断

| 问题 | 已有证据 | 不能推出的结论 | 最小闭环/停止条件 |
|---|---|---|---|
| Dock HWND 跨窗口背景模糊 | WinUI 同窗条纹控制能看出 blur；项目 Dock HWND 视觉 FAIL。新增候选使用 `Window.SystemBackdrop = DesktopAcrylicBackdrop`，不与 Composition brush、`SystemBackdropElement`、阴影或 region 混用。 | 不能说 API 不支持；不能说创建 brush 成功等于外部像素采样；不能把同窗 blur 说成跨窗 blur；`DesktopAcrylicController.IsSupported()` 只报告系统支持能力，不能查询本窗口最终是否 fallback。 | 新授权后仅做一个受控背景和一个 Bottom/58 DIP Dock HWND 的跨窗捕获；确认背景与 Dock 是不同 HWND 且几何相交，再用 WGC 检查真实图样。可辨真实 blur 出现则停止 M0 视觉探索，完整材质交 M4；未出现且支持/回退上下文有效，则停止 WinUI 该集成支线，进入架构复审；环境不满足时记录 BLOCKED，不盲目换透明度。 |
| 非激活启动与前台归属 | WPF `ShowActivated=false` 和 WinUI `AppWindow.Show(activateWindow:false)` 已实现；上一轮 WPF 观察因 `Process.MainWindowHandle` 未发现窗口而 BLOCKED。当前脚本使用 EnumWindows 按 PID 找可见 HWND，并记录前后台 HWND/PID。 | 编译成功不证明窗口不抢焦点；WinUI 候选未观察；不能据此声称 DXGI 状态保留。 | 一次授权观察：先记录背景测试应用前台 HWND/PID，显示一个候选 Dock，等 900 ms，核对 foreground HWND/PID 未变且 Dock HWND 可见；失败则按记录准确判 FAIL/BLOCKED，不继续拓展。 |
| 最小 DXGI 共存观测 | 默认启动路径历史上出现 WinUI 令 DXGI 独占状态退出；非激活路径未测。 | 不能把之前默认路径缺陷外推到非激活候选；不要求 Dock 在独占画面上显示。 | 同一测试申请可加一个受控 DXGI 窗口状态前后对照；只记录 DXGI 状态是否被 Dock 启动改变，不测游戏、不注入输入、不改变显示模式。若没有可信独占 probe，则该项 BLOCKED。 |

## 静态代码与官方 API 核查

- 项目 `CompositionBackdropBlurBrush` 通过 `CompositionTarget.GetCompositorForCurrentThread().CreateBackdropBrush()` 建立 backdrop source，再把 `GaussianBlurEffect` 的输出写到 `XamlCompositionBrushBase`；项目 Dock 的 `MainWindow.xaml` 为透明 Grid，目标 `GlassPanel.Border.Background` 挂载该 brush，`BackdropElement` 默认 Collapsed，当前 Composition candidate 没有设置 `Window.SystemBackdrop`。因此应把当前视觉 FAIL 表述为“本实现/窗口当前没显示外部 blur”，而非“Composition 不支持”。
- 微软官方 Composition brush 文档明确说明 `CompositionBackdropBrush` 可采样 app 内容或桌面应用窗口后方像素；官方同页代码示例用 `CreateBackdropBrush()` 连接 Gaussian blur，并在一个 Composition visual 树中把 EffectBrush 放到图像 visual 上方。项目独立控制窗口同样属于同 HWND/XAML 树，它验证 effect chain，但不能替代项目 HWND 跨窗采样观察。[Composition brushes](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-brushes)，[WinUI XAML visual-layer interoperability](https://learn.microsoft.com/en-us/windows/apps/develop/composition/using-the-visual-layer-with-xaml)
- 微软明确说明 WinUI 3 的 `AcrylicBrush` 是窗内模糊，UWP 的 `BackgroundSource=HostBackdrop` 不可用于 WinUI 3；窗级桌面透视应使用 `Window.SystemBackdrop = DesktopAcrylicBackdrop`，需要元素边界时使用 `SystemBackdropElement`。之前 18 场景候选验证了 element-level 路线；当前 Dock 的 Composition test 不是 Window.SystemBackdrop 路线。下一次拟采用官方 window-level path，以排除“应用元素背板与 HWND 背板层级”这一差别。[In-app acrylic](https://learn.microsoft.com/en-us/windows/apps/develop/ui/in-app-acrylic)，[System backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops)
- `CreateHostBackdropBrush` 是 `Windows.UI.Composition.Compositor` 上公开文档记录的 API，含义为在窗口绘制前采样 visual 背景，并受系统设置/电源策略控制。当前项目使用的是 `Microsoft.UI.Composition.Compositor`，不能未经核对 WinAppSDK 对应 API 和运行支持就假设两者可互换。[CreateHostBackdropBrush](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositor.createhostbackdropbrush)
- 系统 Acrylic/Backdrop 可能因远程会话/VM、图形硬件、Windows 版本、透明效果开关、电池节能或高对比度而 fallback。Microsoft 也要求自定义透明效果自行响应 `AdvancedEffectsEnabled`；本次新增了透明效果偏好、Win 版本和远程会话日志字段，但未作桌面运行验证。当前 Composition probe 的 `EffectBrushCreated/HRESULT=0` 只说明对象创建成功，不能证明 material 未被 fallback 或实际采到目标窗口。[Materials overview](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials)，[Composition tailoring](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-tailoring)

本次静态新增的观测字段还包括窗口/视觉树类型、`Window.SystemBackdrop` 与 `SystemBackdropElement` 实际类型及可见性、`SetWindowRgn` 是否请求/成功、brush 创建目标、候选来源和“视觉采样待图像判断”的显式状态。System backdrop 日志增加 `IsSupported`、OS、远程会话、透明效果开关、高对比度和 Energy Saver 状态；这些字段只能帮助识别已知回退上下文，公开 API 没有暴露“本 HWND 实际触发了哪条回退”的结果，仍必须以 WGC 图像验证。非激活结果 JSON 另记录 HWND 寻找算法、观察延迟、前台是否保持、窗口句柄及前后台 HWND/PID。上述日志只在下一次获授权运行时提供数据；目前没有补写任何运行结果。

新的最小申请使用两个不同 HWND（受控条纹背景与 Dock，属于同一个测试进程）验证跨 HWND 采样。Window-level Acrylic 覆盖整个 Window 内容宿主；透明 XAML 圆角 Border 不会自动把系统背板裁为圆角，且此隔离候选有意不调用 `SetWindowRgn`。如捕获显示 blur 但有矩形宿主边界，应分别记录“外部采样能力存在”和“圆角窗口几何未通过”。

## 脚本恢复缺陷的静态根因

上一轮实际失败的机制是：运行器保存了 Process-scope 环境快照，`Set-M0ProbeEnvironment` 修改了 runner 自身的 `GLASSDOCK_*` 值供子进程继承，但旧 `finally` 没有先恢复 runner 进程的值，所以后续 `RestorePassed=false`。静态复核又发现原先补上的恢复候选仍不严谨：它用 `[Environment]::SetEnvironmentVariable($name, $null, 'Process')` 处理原本不存在的值；当前 PowerShell/.NET 环境中这会留下空字符串值，而旧比较先把 null 转成字符串，因而不能可靠区分“变量缺失”和“变量存在但为空”。这不是注册表/用户级/系统级环境变量变化，也不等于桌面状态检查失败。

当前脚本在 `finally` 中先调用 `Restore-M0InteractiveTestEnvironment`；快照为 null 时使用 `Remove-Item Env:<name>` 删除变量，非 null（包括空字符串）才使用 Process-scope setter。随后 `Stop-M0InteractiveTestSession` 用 null-aware 比较核验存在性和数值。新增非交互静态回归脚本将用哨兵值分别验证缺失、空字符串和普通值。本轮不触发视觉/输入/DXGI runner，也不验证急停热键或 watchdog 超时路径。

## 有界原则

- 只允许前述单一 Dock backdrop 捕获、单一 non-activate 观察及必要的一次 DXGI 状态对照。
- 一个受控背景窗口覆盖 Dock 所在区域，捕获仅裁切到 Dock 及必要的背景参照区域；不得保留全屏桌面图。
- 任何异常、急停、看门狗失败或桌面恢复核验失败，立即停止剩余场景；不得重跑或扩大范围。
- 整组申请上限 10 分钟，每个脚本 watchdog 不超过 5 分钟；只结束本轮登记进程，恢复原前台和环境变量，做完资源残留检查。
- 每种场景只运行一次。真实视觉判定按图像，不按 API HRESULT。

后续申请见 [M0 最小桌面测试申请](minimum-desktop-test-application.md)。
