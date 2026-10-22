# M0 技术路线对比与复评

日期：2026-10-22。当前记录支持继续验证，不构成最终框架决定。

## 当前判断

| 维度 | WPF + Win32 互操作 | WinUI 3 + Windows App SDK | Win32 + DirectComposition/Direct2D |
|---|---|---|---|
| 逐像素透明 / 点击穿透 | Windows 11、系统报告 250% DPI、单屏条件下，WPF 三边透明角的合成单击、双击、拖动起始由独立下层 Win32 窗口收到；Dock 内容事件由 WPF 收到。不是物理鼠标验收 | 三边透明角的合成单击、双击由 WinUI 根 Grid 收到，下层窗口计数为零；实测 FAIL。内容区和拖动尚未测试 | 未实现；不能推断它会自动提供逐像素透明输入 |
| 根因与公开修复空间 | PoC 使用 `AllowsTransparency=True` 的 WPF layered window，另有 `WM_NCHITTEST`/`HTTRANSPARENT` 圆角逻辑。实测成功，但还不能把成功唯一归因于其中一项，也未覆盖物理输入和其他 DPI | HWND 仍是窗口级命中对象，XAML 中 `Background="Transparent"` 的 `LayoutRoot` 覆盖整窗，并订阅根 `PointerPressed`；日志证实根元素接收了透明角事件。证据支持“窗口矩形命中 + 可命中的根视觉树”共同导致拦截；并非按钮把已处理事件吞掉。WinUI 没有已验证的逐像素 alpha 输入洞 API | 可自行定义 HWND 区域、命中与合成路径；必须用下层窗口真实消息验证，不会因改用原生代码而自动通过 |
| 受支持的 WinUI 候选 | layered-window 每像素 alpha 是 Win32 公开能力；WPF 的具体呈现/命中组合仍要在矩阵中复测 | `SetWindowRgn` 是公开 User32 API，可将 HWND 限成圆角区域，可能解决胶囊外的透明角；它剪裁绘制区域，可能影响抗锯齿边缘、阴影和玻璃边缘，尚未在本 PoC 实测。`WS_EX_TRANSPARENT` 对整个顶层 layered window 会一并放过 Dock 内容，不能单独作为解法。`HTTRANSPARENT` 文档限定转交到同线程的底层窗口，不能据此承诺跨进程/线程下层窗口可靠收到事件 | 更灵活但需自行承担窗口区、输入路由、DPI、合成视觉树和设备生命周期维护 |
| DWM / DirectComposition | WGC 已取得一个 DWM 合成帧；没有比较独立 Composition 视觉树 | Windows App SDK/Composition 可供视觉效果开发，但不会自动解决 HWND 的输入命中问题；没有 WinUI 捕获对照 | 能直接建立 Composition 视觉树；尚未测试性能、恢复、模糊与输入 |
| 玻璃、模糊与阴影 | 唯一对照帧（WPF / 浅色 / Bottom / 58 DIP）证实背景透射与阴影；方格保持清晰，没有观察到背景模糊 | 没有 WinUI 对照帧或模糊结果 | 能自行组合材质和模糊，但尚无 PoC 证据；不可将“可编程”写成“已达到液态玻璃” |
| 动画性能与呈现帧时间 | 没有逐进程 Present/ETW 帧样本。DWM 被动探针取得全局 240Hz 采样，不代表 WPF 帧率 | 同样无进程帧样本；DWM 全局探针不归属到 WinUI | 未测。低层 API 不是性能证据 |
| DPI、多显示器、动态刷新 | 只在系统报告 250% DPI / 单屏验证了有限输入场景；100/150/200%、跨屏和真实刷新节奏未测 | 相同环境缺口 | 可定义策略，但实现和实测不存在 |
| 任务栏、最大化、无边框/独占全屏、恢复 | 仅有旧的置顶/窗口探测；共存及状态恢复未测 | 相同；共存及恢复未测 | 未测；可控不等于已兼容 |
| Windows API 可维护性 | WPF layered-window 路径成熟且当前 PoC 小；仍需验证窗口消息/DPI/多屏，并检查 WPF 渲染、interop 与子 HWND 限制 | WinUI 的 AppWindow/HWND 互操作公开；当前问题在根树和窗口输入边界。以可选公开 region 实验验证，若损坏视觉或不能跨线程穿透，不采用私有 hack | 需自行负责窗口消息、region、绘制、DPI、设备丢失、无障碍与资源生命周期，初始成本最高 |
| 后续架构扩展成本 | 适合继续作为输入路径基线；视觉与性能是否满足目标仍未知 | 仅在公开 API 候选经真实事件和视觉验证后继续投入 | 只在 WPF/WinUI 有证据支持的实质限制时进入受控 PoC |

## WinUI 3 输入问题分析

静态代码检查发现：`MainWindow.xaml` 的透明 `LayoutRoot` 覆盖整个窗口；`MainWindow.xaml.cs` 为该根 Grid 注册 `PointerPressed`、`PointerReleased` 和拖动 `PointerMoved`。处理器只记录输入，没有将事件标记为 handled。既有运行日志表明透明角事件到达了这个根元素，所以问题不是图标按钮误吞事件。由于 WinUI 主窗口由 HWND 承载，系统先把矩形窗口当作命中目标；XAML 根树又把透明 Brush 所在区域作为可命中的元素。实际测试能证明失败路径，但仅凭这些代码还不能把根因拆分为单一层。

Microsoft 的 layered-window 文档说明，逐像素 alpha 为零的区域可将鼠标消息传给下方窗口；同一文档也说明 `WS_EX_TRANSPARENT` 会忽略整个 layered-window 的形状并让鼠标事件全部下传。对 Dock 来说，整窗透明穿透会牺牲图标和 Dock 内容交互。[Layered windows / hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)。

`SetWindowRgn` 是公开、长期存在的 Win32 API，定义系统可绘制的窗口区域。[SetWindowRgn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn)。圆角 region 可作为一个有限的公开 API 候选，把胶囊外的四角从 HWND 区域排除；它不是逐像素 alpha 洞，也可能剪掉阴影或抗锯齿/模糊边缘。尚未改动 WinUI PoC 来默认使用该 API，也尚未对 HWND region 的下层真实事件交付做用户批准后的实测。因此当前结论是“有一个值得做的、公开 API 的窄候选”，而不是“已有可靠修复”。

`WM_NCHITTEST` 的 `HTTRANSPARENT` 说明是把消息继续交给同线程底层窗口；当前下层测试窗口和 Dock 是不同进程，因此该机制不能独立保证跨线程/跨进程转发。[WM_NCHITTEST](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest)。WPF 的实际通过可能来自 layered-window 的 alpha 命中行为和/或 PoC 的 hit-test hook；现有日志没隔离这两种机制。后续验证要维持 Dock 内容点击正常、透明区域单/双击和按住移动到达独立下层窗口，并同时检查剪裁、阴影和玻璃外观。

## 被动 DWM 计时探针

脚本：`scripts/measure-dwm-composition.ps1`。本机以未开启管理员角色的当前令牌运行了约 2 秒采样；8/8 次 `DwmGetCompositionTimingInfo(hwnd=NULL)` 返回 `S_OK`，结构大小 292 字节，HRESULT 错误字段为 0；刷新和全局 compose-rate 约为 239.998–240.004 Hz，单次 API 调用约 96–142 微秒。第一轮 Pack 布局错误，16 次得到 HRESULT `0x88980090`；改为 Pack=4 后重复采样通过。证据在 `D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\dwm\dwm-composition-20261022T121946Z.jsonl`。

该 API 在 Windows 8.1 及之后要求 HWND 参数为 NULL；结果是桌面合成时序，不能归属于 Dock 进程，也不是最终面板 scanout。[DwmGetCompositionTimingInfo](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcompositiontiminginfo) · [DWM_TIMING_INFO](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_timing_info)。本次没有创建可见窗口、改变显示设置或注入输入，也没有请求提权。应用逻辑更新间隔仍未采集；逐进程 Present/ETW 仍因 `StartTrace` access denied 而 BLOCKED；WGC `SystemRelativeTime` 需要连续采集帧才可作合成帧间隔代理，也尚未采集序列。任何一个都不能单独被称为面板真实呈现帧时间。

## 证据状态与停止条件

- **输入路径**：已有 WPF PASS（限定于记录的合成输入和当前单屏/250% 条件），WinUI 透明角 FAIL。获得针对该项的桌面交互许可后，最多先做一轮 WPF 基线复核和 WinUI 圆角 HWND region 候选对照；若候选无法让独立下层窗口收到输入，或必须使用私有/不受支持技巧，停止 WinUI 输入修复探索并保留 FAIL，不继续堆叠系统级 hack。
- **视觉路径**：经单独授权后，使用 Windows.Graphics.Capture 对两主题、中心/圆角、3 边、44/58/72 DIP 和高对比背景采集合成图。透明透射、背景模糊、边缘和阴影分开记录。若 WPF/WinUI 的公开 DWM/Composition 能力在两次有依据的实现尝试后仍没有背景模糊或造成不可接受的剪裁/边缘，停止将其当作完成目标的路线；再评估受控 DComp PoC。
- **性能路径**：先做不覆盖桌面的应用逻辑更新间隔日志；逐进程 Present 时序通过受支持的无管理员方案获得后，分别在设备支持的 120/240Hz 测 60 秒动画样本并重复 3 次。把逻辑更新、进程 Present、DWM 全局 compose-rate 和面板 scanout 分列。原任务书没有数值化 P95/丢帧门槛，故不把本段提议的采样次数写成验收阈值；应在测量证据到位后再由验收矩阵给出结果，不反向降低标准。
- **停止研究条件**：WinUI 经一个公开 API region 候选仍失败即停止 WinUI 修补；WPF 若可见玻璃效果在两次公开 API 实现尝试后无法支持目标模糊，或后续真实输入/窗口共存出现不可接受故障，才启动范围严格的 Win32 + DirectComposition 最小 PoC。不得因 “低层更快” 预先选择 DComp。

## 架构级阻断与设备覆盖

架构级阻断项是当前可在本机通过获批测试/代码验证解决的：WinUI 透明区下层事件路由；玻璃模糊与边缘质量；窗口/任务栏/全屏共存恢复；Dock 自身帧时序测量方法。它们不能因为硬件缺项而自动延期或标为通过。

设备依赖项包括当前机器没有的 100/150/200% 实际缩放、多显示器跨屏、原生支持的 120Hz/240Hz 模式，以及当前显示组合不支持的 60Hz。虚拟机可补充逻辑尺寸和 OS API 行为，但其虚拟 GPU/显示栈不代表本机物理刷新；外接屏可补足实际屏幕模式。当前只有一个活动显示器记录，60Hz 同分辨率模式此前测试不支持；100/150/200% DPI 与第二屏组合不得写成通过。设备无法提供的覆盖须在最后 M0 核验时明确列作环境限制，并由验收方决定延期，不得无限期等待，也不得自行放宽。

## 许可证与公开仓库

GitHub 仓库当前公开。GitHub 官方说明，公开仓库用户可在服务中查看和 fork；无项目许可证时仍受默认版权规则限制，不能据此获得广泛的复制、修改、衍生或再分发授权。[GitHub：Licensing a repository](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。根 `LICENSE` 未修改，但其当前“Do not redistribute, publish”绝对措辞与 GitHub 对 fork 的服务许可不一致；建议权利人决定许可证时把占位文案改成类似“未授予项目级许可；GitHub 服务条款允许的查看和 fork 除外”，或直接替换为所选正式许可证。该文案方案仅供决定，等待用户选择，当前没有写入 `LICENSE`。

MIT 允许宽泛使用、修改、商业使用和再分发，但要求保留版权/许可声明；Apache-2.0 也宽松并增加明确专利授权、专利诉讼终止与 NOTICE/声明义务；GPL-3.0 允许使用、修改和分发，但分发覆盖作品及衍生作品时要求遵循相应 copyleft 和源码提供义务；暂不公开授权则保留权利，GitHub 的查看/fork 服务权利仍存在但没有更广泛项目授权。最终许可证由用户决定，本轮不改 `LICENSE`。

## 当前决策

WPF 目前只有输入行为的临时领先证据；WinUI 有可验证的公开 region 候选但没有实测修复；DWM API 被动采样可用但不测 Dock；任何路线都没有完整玻璃视觉和有效的进程呈现帧证据。因此**不冻结 WPF、不放弃 WinUI、不批准 M1**。只有输入、视觉、性能/稳定性和系统共存证据达到要求，才冻结路线。原生 Win32 + DirectComposition 仍是有门槛的备选 PoC。

参考：[DWM timing API](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcompositiontiminginfo) · [Layered-window hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) · [SetWindowRgn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn) · [HTTRANSPARENT](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest) · [GitHub licensing](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-the-repository/licensing-a-repository).
