# M0 技术路线对比与复评

更新日期：2026-10-22。以下结果用于决定下一步验证，不代表最终生产框架已经选定。

## 当前判断

| 维度 | WPF + Win32 互操作 | WinUI 3 + Windows App SDK | Win32 + DirectComposition / Direct2D |
|---|---|---|---|
| 逐像素透明与点击路由 | 默认 PoC 在受控 SendInput 场景中 18/18 通过，Bottom/Left/Right；独占全屏中 HWND 命中与 WGC 可见性不一致 | 默认根 Grid 的透明区域 9/9 失败；可选 `SetWindowRgn` 候选内容与透明路由 18/18 通过 | 未实现；改用原生窗口不自动得到正确透明输入 |
| 修复验证状态 | `AllowsTransparency=True` layered window 与窗口命中 hook 尚未拆分机制；物理鼠标、非 250% DPI 和独占输入未验收 | 公开 `SetWindowRgn` 当前模式/DPI 的输入候选通过；视觉仍见外缘宿主/阴影轮廓，候选默认关闭 | 可自定义区域、命中、渲染与合成；需独立证明输入和可见结果 |
| DWM / Composition | WGC 36 帧矩阵成功，尚无背景模糊证据；WPF layered-window 能力需继续评估 | Windows App SDK / Composition 可以作为视觉构建基础，但不会自动解决 HWND 命中；WGC 36 帧矩阵也无目标级模糊 | 可直接建立 Composition 视觉树；性能/设备恢复/材质/输入均未测 |
| 玻璃、透射与阴影 | 代表性浅/深 WGC 样本可见背景透射和阴影，格线未模糊 | 灰色半透明 pill 可见，基线有浅色宿主边框；region 变体保留 pill，但外缘残留矩形轮廓 | 有组合材质的自由度；当前没有 PoC 证据 |
| 动画性能与帧时间 | 无 Dock 自身 update/Present 时间序列；DWM 全局约 240Hz 不代表 WPF 帧率 | 同样无进程级时间序列；DXGI 测试的模式变化不是帧率证据 | 未测；API 层级低不等于性能已达标 |
| DPI、多屏、动态刷新 | 仅当前系统报告 250% 单屏；120/240Hz 模式下窗口存活；未测 100/150/200%、跨屏、动态 DPI | 相同覆盖范围；region 候选只在当前 250% DPI 运行 | 可定义策略但实现和硬件验证不存在 |
| 任务栏、窗口、全屏共存 | 最大化/无边框 2/2 场景通过；DXGI 独占时 Dock 未出现在 WGC 帧而命中 API 仍指向 Dock，需解决 | 最大化/无边框通过；受控独占测试里启动 Dock 后 DXGI 状态变为非独占；不是游戏矩阵 | 未测；必须验证窗口 z-order、任务栏、exclusive/DWM 路径 |
| 托盘/恢复 | WPF 托盘 Show/Exit 实测通过；两候选隔离配置缺失/损坏/非法输入 6/6 恢复 | 菜单弹出已确认，但 UIA/MSAA/GetMenu 无法暴露菜单项，Show/Exit 阻塞 | 未实现 |
| Windows API 可维护性 | layered-window 是成熟公开机制，但 WPF 渲染、DPI、子 HWND 与窗口消息 hook 仍有风险 | HWND region 是公开机制，单候选证明可路由；抗锯齿、阴影、动态 region 更新和 DPI 迁移需验证；不引入私有 WinUI hack | 需要自己维护窗口输入、region、DPI、设备丢失、无障碍和资源生命周期，成本最高 |
| 后续扩展成本 | 当前输入领先，但全屏、玻璃与 frame-time 证据不足 | 默认输入失败有公开候选，不等于生产决策；视觉和托盘仍有问题 | 仅在现有路线出现有证据的底层限制时投入 PoC |

## WinUI 3 输入路径

默认运行中，覆盖全窗口的 `LayoutRoot` 使用透明背景并订阅 Pointer 事件；透明区域运行日志表明根 Grid 收到事件，事件处理器没有设置 `Handled`。实测因此证明 WinUI 根命中路径截获透明区域输入，但现有证据不能把窗口级 HWND 矩形和 XAML 根视觉树的贡献完全拆开。默认实现的透明单击、双击和透明区起始拖动在三边均失败；Dock 内容单击、双击和从内容区拖出正常。

本轮对 WinUI 增加了默认关闭的 `SetWindowRgn` 候选：按 8 DIP 内缩、28 DIP 圆角构造窗口 region。在 250% DPI 的 Bottom/Left/Right 位置，独立下层窗口收到透明区单击、双击和拖动消息，Dock 内容单击、双击和拖动也继续工作，18/18 PASS。调用记录确认 SetWindowRgn 成功。该结果表明公开 API 可以解决本 PoC 的当前输入边界；它还没有在其他 DPI、多屏和动态尺寸变化中验证，且视觉候选仍有外缘矩形宿主/阴影轮廓。

候选代码由 `GLASSDOCK_M0_TEST_ROUNDED_REGION=1` 显式启用，只是 M0 试验，不改变默认 WinUI PoC。视觉对照仅在 Bottom/58 DIP/250% DPI 取得浅/深 2 帧；图中 Dock 内容保留，但外缘轮廓仍需处理，且没有目标背景模糊。当前输入探索到此停止，不继续叠加其他系统级技巧。后续只有在实现发生变化且有明确问题时，才重新运行候选对照。

Microsoft 文档说明，layered window 每像素 alpha 为零的区域可将鼠标消息交给下方窗口；`WS_EX_TRANSPARENT` 可让整个 layered-window 形状的鼠标事件下传，会牺牲 Dock 内容点击。[Layered windows / hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)。`SetWindowRgn` 是公开 User32 API，可限制窗口绘制区域；region 方案可能剪裁阴影、抗锯齿或玻璃边缘。[SetWindowRgn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn)。`HTTRANSPARENT` 文档语义依赖同线程的底层窗口，不能据此承诺跨进程/线程事件传递。[WM_NCHITTEST](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest)。

WPF 使用 `AllowsTransparency=True` 和 `WM_NCHITTEST`/`HTTRANSPARENT` 圆角逻辑。现有 WPF 合成输入成功，但尚未拆分 layered alpha 和 hit-test hook 的作用。其独占全屏观察也留下未决项：DXGI 仍报告独占、Dock HWND 可见/置顶且中心 WindowFromPoint 指向 Dock，WGC 保存帧却没有 Dock；该场景未发送输入，因此不能断言 Dock 实际拦截了游戏输入，也不能判为全屏共存通过。

## WGC 视觉与 DWM 计时

基线视觉矩阵在 Windows.Graphics.Capture / Direct3D11CaptureFramePool 中取得 36/36 帧，覆盖 WPF/WinUI、浅/深受控背景、三边、44/58/72 DIP。WGC 是 DWM 合成画面捕获，不是物理 scanout。代表样本显示 WPF 可见背景透射与阴影，但格线仍清楚；WinUI 是灰色半透明面板，基线浅色外框明显；region 变体的 pill 与图标保留，但外缘轮廓仍见。没有样本显示目标级模糊/液态玻璃。浅/深是受控背景，不是切换系统主题；所有视觉样本只有当前 250% DPI。

被动探针 `scripts/measure-dwm-composition.ps1` 直接调用 `DwmGetCompositionTimingInfo(hwnd=NULL)`，本次约 4.27 秒收到 16/16 次 `S_OK`，Refresh/Compose 全局约 240.004 Hz。日志在 `D:\MacOS\GlassDock-Win\.local\evidence\final-validation\resume-20261023T032925Z\dwm-composition.jsonl`。该 API 的 Windows 8.1+ 用法要求传 NULL HWND，报告全局桌面合成时序，不归属 Dock 进程，也不是面板 scanout。[DwmGetCompositionTimingInfo](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcompositiontiminginfo) · [DWM_TIMING_INFO](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_timing_info)。

当前没有应用逻辑更新间隔日志，没有 PresentMon/ETW 成功采集，也没有 WGC 连续帧时间序列或面板 scanout 测量。StartTrace `access denied` 后没有提权或更改用户组。因此不能声称达到 120Hz/240Hz 流畅度。

## 证据和停止条件

- **输入**：WPF 默认当前矩阵 PASS；WinUI 默认 FAIL，唯一公开 `SetWindowRgn` 候选当前矩阵通过。WinUI 输入技巧探索停止在该公开候选，不再尝试私有 hack。候选仍需 DPI/多屏/视觉适配。
- **视觉**：当前两候选都没有背景模糊证据，region 也没有解决 WinUI 外缘轮廓。下一次视觉工作应针对公开 DWM/Composition 材质实现提出具体方案、预期截图特征和停止点；同一路线经两次有依据的实现尝试仍无模糊或造成不可接受边缘时，停止把该栈视作能完成目标材质的唯一方案，再讨论 DComp PoC。
- **性能**：先采不覆盖桌面的应用逻辑更新时间；寻找受支持、无需提权的进程 Present 方案；若只能拿到应用内部代理指标，不能称为最终呈现时间。若确需管理员采集，需给出精确命令、权限和风险并等待用户授权。
- **共存**：最大化/无边框的受控窗口 4/4 PASS；独占全屏仍 BLOCKED。需要以已运行的真实全屏程序验证 Dock 在游戏开始前已运行和运行中状态的交互，且判断可见性、点击及是否造成焦点/独占模式转换。

## 架构级阻断与设备覆盖

当前可在已有设备上推进的架构问题：液态玻璃级背景模糊、WinUI 候选外缘质量、WPF/WinUI 全屏行为歧义、WinUI 托盘菜单可访问性，以及 Dock 进程自身呈现指标。不能把它们因为硬件缺项标成通过。

设备依赖包括 100/150/200% 实际缩放、多显示器跨屏、当前分辨率不支持的 60Hz。外接屏/备用测试机可以补实际模式；虚拟机适合 OS/API 行为，但不替代物理屏刷新证据。不能获得的硬件组合需明确请求验收延期，不降低阈值也不无限等待。

## 许可证与公开仓库

GitHub 仓库公开。站内查看/fork 权限与项目级许可证不是一回事；没有许可证时不能推导出广泛站外复制、修改、商业使用或再分发授权。[GitHub: Licensing a repository](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。根 `LICENSE` 保持不变；现有“Do not redistribute, publish”占位语句可在用户决定后改成清楚区分 GitHub 站内查看/fork 与项目级授权的措辞。

| 许可选项 | 复用/商业化/再分发影响 |
|---|---|
| MIT | 宽松许可，可使用、修改、商业使用和再分发；保留版权与许可证声明 |
| Apache-2.0 | 宽松许可，明确专利授权与专利诉讼终止，附带 NOTICE/声明义务 |
| GPL-3.0 | 允许使用和修改；分发覆盖作品及衍生作品时需履行 copyleft 和对应源码义务 |
| 暂不授权 | 保留著作权、不授予广泛复用权；GitHub 站内服务许可仍适用 |

## 当前决策

WPF 默认输入有正面证据但全屏可见/命中存在歧义；WinUI 默认透明输入失败，其公开 region 候选修复当前测试矩阵，但视觉边缘和系统适配未过；两边均无目标模糊和有效 Dock 进程呈现时间。**不冻结 WPF、不放弃 WinUI、不进入 M1。** 当两路线出现有证据的实质底层限制后，再评估严格范围的 Win32 + DirectComposition PoC。

参考：[DWM timing API](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcompositiontiminginfo) · [Layered-window hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) · [SetWindowRgn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn) · [HTTRANSPARENT](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest) · [GitHub licensing](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository).
