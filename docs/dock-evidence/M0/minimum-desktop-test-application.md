# M0 最小桌面测试申请（待授权，未执行）

**状态：准备稿。当前用户未授权本轮桌面测试；不要运行。** 以前的 `RUN M0 FINAL DESKTOP TEST` 已用于上一轮并消耗，不覆盖本申请。脚本识别的新短语为 `RUN M0 MINIMAL DESKTOP TEST`。

## 目的与场景

一次受控运行，最多三项观察：

1. **跨窗口背景模糊（1 场景）：** 一个黑白高对比条纹背景窗口放在 Bottom Dock 所在区域后方；一个项目 WinUI Dock HWND，Bottom、58 DIP；尝试微软建议的窗口级 `Window.SystemBackdrop = DesktopAcrylicBackdrop`，XAML 内容保持透明，临时关闭 `SetWindowRgn` 以分离宿主裁剪因素。探针记录 `IsSupported`、OS、远程会话、透明效果、高对比度及 Energy Saver 状态；这些字段只能说明已知环境条件，不能直接报告本 HWND 最终是否 fallback。通过 WGC 只捕获 Dock 及紧邻对照图案。要求中心图案可观察到扩散/模糊才算真实 blur；单纯发白/降低对比度为视觉 FAIL。若 fallback 条件不满足，只记 BLOCKED。
2. **非激活启动对照（一个合并场景，两个候选顺序执行）：** 记录当前前台 HWND/PID；分别用 WPF `ShowActivated=false` 和 WinUI `AppWindow.Show(false)` 显示候选，EnumWindows 按 PID 找可见 HWND；各观察 900 ms 并恢复原前台 HWND。Dock 可见且前台值不变才 PASS；Dock PID 成为 foreground 为 FAIL；无可见 HWND/外部事件干扰则 BLOCKED。该观察不重测输入，也不据此推断 DXGI 独占状态。
3. **DXGI 状态对照（每候选一次，共一组）：** 旧默认启动已有独占状态退出/前台受影响的失败，因此在同一授权范围内对 WPF、WinUI 非激活启动各做一次 3 秒受控 DXGI 独占状态对照。探针使用当前枚举到的显示模式，不调用模式切换 API 请求新分辨率/刷新率；不截图、不启动游戏、不注入输入。Dock 在独占画面中不可见可以是预期；Dock 进程成为前台或 `GetFullscreenState` 表明独占被退出才判 FAIL。退出后记录恢复状态；不测试游戏输入和退出游戏后的完整自动恢复。

## 桌面影响、时限和恢复

- 预计 3–5 分钟；整个主脚本 watchdog 5 分钟，DXGI 子脚本 watchdog 120 秒，低于此前约定的整组 10 分钟上限。
- 受控图案窗口只覆盖测试 Dock 所在的一块局部屏幕区域；测试 Dock 短暂置顶显示。两候选各进行最多 3 秒的测试自有 DXGI 独占窗口，桌面会短暂切换至该窗口并可能短暂改变前台/z-order。探针请求当前模式，不设置新分辨率、DPI 或刷新率；若检测到模式与原状态不同，只尝试恢复原模式并核对。没有鼠标/键盘输入注入、不改任务栏、不提权、不启动第三方应用或游戏。
- WGC 裁切图与主运行 JSON 元数据写入 D 盘项目 `.local/evidence/m0-minimal-final/`；DXGI 子探针 JSON 写入 `.local/evidence/m0-gap/fullscreen-exclusive/`。两个目录均为本地 `.local` 证据，不提交、不上传；不会持久化未裁切的全屏桌面帧。窗口级 Desktop Acrylic 覆盖整个 HWND，XAML 圆角边框不裁切系统背板；因此即使采到真实 blur，也必须独立检查矩形宿主边界，并允许此项视觉质量判 FAIL。
- Ctrl+Shift+F12 为急停；独立 watchdog 到时结束本次 manifest 登记的测试进程。Finally 先恢复进程环境变量，再清理本轮创建的窗口/进程；不结束原有程序；之后核对前台 HWND、显示边界、指针、任务栏、watchdog 退出和进程残留。
- 任一检查失败立即停止其余场景。恢复核验失败时不重试、不续测，报告残留状态供人工确认。

## 继续执行前提

必须由负责人本轮明确输入新的短语 `RUN M0 MINIMAL DESKTOP TEST`。该短语只授权本文列出的单场 WGC 捕获、WPF/WinUI 非激活对照和两项 3 秒受控 DXGI 状态对照；不授权输入注入、模式切换、其他视觉组合、真实游戏、物理多屏或额外重复运行。
