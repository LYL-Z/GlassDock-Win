# M0 缺口补测与验收报告

- 日期：2026-10-22
- 项目：D:\MacOS\GlassDock-Win
- 状态：**M0 未验收通过；P0 缺口仍在；不进入 M1，不冻结生产技术路线。**
- 本轮边界：补足真实输入路由证据、一次 DWM 合成捕获及本机代码/单测结果；没有推送远端，没有提权，没有更改用户组。本轮最后收到用户明确要求：任何会覆盖桌面或注入鼠标的测试先征得同意。因此不再启动此类测试，也不切换显示模式。

## 1. 当前环境与证据边界

此前从本机查询到 Windows 11 x64，版本 10.0.26300；D 盘 .tools 内 .NET SDK 10.0.402；WinUI PoC 使用 Windows App SDK 2.5.1。显示接口报告主屏设备为 CDD、3840×2400、240Hz；GetDpiForWindow 返回 240 DPI（96 DPI 基准下 250%）。这些是操作系统报告值，不是独立核验的显示器型号或硬件规格。当前仅有一个活动屏幕记录。

同分辨率的 Windows 显示模式查询曾确认可切换 120Hz 并恢复到 240Hz；60Hz 的 CDS_TEST 返回 -2（该模式不受支持）。100/150/200% DPI、60Hz、240Hz 动画帧时间和多显示器组合没有通过实际 Dock 交互完成验证。不要从已有 250%/240Hz 结果外推。

Git 起始状态为 main、HEAD 1b22371e220a0cacbaaddc8a559407815167a2e8、无已配置远端。工作树中的本轮更改为两个 PoC 输入追踪代码、输入探针项目、输入测试脚本和 M0 报告、矩阵、README、技术比较、ADR。当前源码没有远端 CI 结果。D 盘本机工作树是本轮操作目标；没有在 C 盘创建项目文件、证据或构建输出。

## 2. 真实鼠标事件接收测试

### 方法与条件

创建了一个受控底层 Win32 窗口，记录收到的 WM_LBUTTONDOWN、WM_LBUTTONDBLCLK、WM_LBUTTONUP、按住左键时的 WM_MOUSEMOVE，以及点击次数和坐标。候选 Dock 进程另写入 WPF/WinUI 指针消息及占位按钮动作日志。脚本还记录 WindowFromPoint，但它只作窗口命中诊断，不作为输入穿透结论。

输入由 SendInput 合成；日志中以 extra-info 标记识别为 SendInput-synthetic。Windows 的消息路由、目标窗口过程和候选控件事件是真实执行的，但这不是物理鼠标手动验收。系统只有一个当前主显示器；报告坐标依赖当前 250% DPI 的屏幕映射。每个测试会临时显示全屏受控背景。用户指出此操作使其受惊后，测试已停止；脚本现在通过 PowerShell ShouldProcess 高影响确认提示，后续必须先获用户同意才能继续。

测试输出：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261022T104053Z\actual-input-results.csv。逐窗口原始事件日志在同一目录下的 Wpf-Bottom、Wpf-Left、Wpf-Right 与 WinUI-Bottom/Left/Right 子目录。原始日志包含本机 PID 与坐标，保持在被忽略的 D 盘 .local 下，不进入 Git。

### 结果

| 候选 | Dock 边 | 场景 | Dock 收到的指针事件 / 按钮动作 | 底层窗口点击 / 按住移动 | 实际接收方 | 结果 |
|---|---|---|---:|---:|---|---|
| WPF | Bottom / Left / Right | 内容区单击 | 每边 3 / 1 | 0 / 0 | Dock | PASS |
| WPF | Bottom / Left / Right | 内容区双击 | 每边 6 / 2 | 0 / 0 | Dock | PASS |
| WPF | Bottom / Left / Right | 从内容区拖向透明角 | 每边 5 / 0 | 0 / 0 | Dock | PASS |
| WPF | Bottom / Left / Right | 透明角单击 | 0 / 0 | 每边 1 / 1 | 底层窗口 | PASS |
| WPF | Bottom / Left / Right | 透明角双击 | 0 / 0 | 每边 2 / 0 | 底层窗口 | PASS |
| WPF | Bottom / Left / Right | 从透明角拖进 Dock | 0 / 0 | 每边 1 / 2 | 底层窗口 | PASS |
| WinUI 3 | Bottom / Left / Right | 透明角单击 | 每边 2 / 0 | 0 / 0 | Dock | FAIL |
| WinUI 3 | Bottom / Left / Right | 透明角双击 | 每边 4 / 0 | 0 / 0 | Dock | FAIL |

WPF 的 18 个记录场景全部达到脚本预期。透明双击在底层窗口日志中表现为 Windows 双击消息序列；“从透明角开始的拖动”由底层窗口得到按下和移动，点击开始后指针捕获不会在进入 Dock 时重新路由。

WinUI 只完成了三边透明角的单击与双击。此前运行时，自动找图标点的探测条件不适用于 WinUI 按钮处理路径，所以内容区单击、双击、两类拖动都没有记录；这些项目仍为 NOT TESTED，不能由透明角失败推断。测试脚本的图标定位逻辑已修正，但没有再次运行。当前脚本的确认提示和定位修复也没有做交互式运行验证。

可复现入口：scripts\verify-poc-input.ps1；受控底层窗口源码：spikes\GlassDock.M0.InputProbe\Program.cs。脚本会改动桌面可见状态并合成输入，带 ShouldProcess 高影响确认，不能在没有用户同意时启动。普通构建和单元测试不会启动该脚本。

### 结论边界

实际输入接收日志证明：在所记录的合成输入场景和当前环境中，WPF 透明圆角将事件交给底层窗口；WinUI 透明圆角仍由 Dock 收到。该证据比 WindowFromPoint 更直接，但不能替代物理鼠标操作、其他 DPI、多屏、全屏应用或不同 Windows 版本的验证。WinUI 的 alpha-hole 是当前 P0 失败项。

## 3. DWM 合成视觉验证

保存了一张 Windows.Graphics.Capture / Direct3D11CaptureFramePool 获取的主显示器合成帧：

- 图像：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\wpf-bottom-light-250dpi-240hz-r1.png
- 元数据：同名 .json
- SHA-256：66D5C3B07D7B13C59E9985B71FD90C11C03471322E9AA7FA03F08C448CCC9C85
- 条件：WPF、Bottom、浅色受控方格背景、8 个占位项、58 DIP、系统报告 250% DPI / 240Hz。捕获的是 DWM 合成图像，不是显示面板 scanout 或逐帧呈现时间。
- 观察：Dock 区域能看到后方方格，证实该样例存在真实透明透射；方格边缘仍清晰，未观察到背景模糊；可见阴影。此样例不支持“液态玻璃/模糊效果已通过”的说法。

捕获程序的当时版本在 WGC 元数据中记录 MonitorBounds 1536×960、FrameContentSize 3840×2400。前者是捕获进程当时受 DPI 虚拟化影响的逻辑坐标，后者是实际捕获帧像素尺寸；它们不一致，因此不能把旧元数据的 MonitorBounds 用作物理尺寸证据。捕获工具后来加上 PerMonitorV2 manifest，但没有重捕获来复核该修正。

没有保存 WinUI、深色主题、Left/Right、其他尺寸或背景内容对照帧。因此视觉矩阵整体为 BLOCKED；一张受控样例只能支持上述有限观察。背景标记和全屏测试背景约束用于避免将无关窗口误认成结果，但不应把这套全屏捕获流程在未经同意时再次启动。

## 4. DPI、尺寸、分辨率与刷新率覆盖

| 条件 | 现有证据 | 状态 |
|---|---|---|
| DPI 250% | Windows 报告 240 DPI；输入路由测试在该环境完成；视觉捕获一例的帧为 3840×2400 | 有限覆盖 |
| DPI 100% / 150% / 200% | 未执行真实 Dock 操作验证 | NOT TESTED |
| 图标 44 / 58 / 72 DIP | 源码启动参数及旧资源采样覆盖这些尺寸；只有 58 DIP 有本轮 WPF 视觉证据，未完成三尺寸几何/视觉回归 | NOT TESTED（完整适配验收） |
| Bottom / Left / Right | 真实输入测试三边；视觉仅 Bottom；未覆盖跨屏移动 | 输入：WPF PASS、WinUI 透明区 FAIL；视觉：NOT TESTED |
| 多显示器 | 当前只有一个活动显示器记录，没有第二屏 | NOT TESTED |
| 240Hz | 当前系统报告 240Hz；静态资源数据及一张视觉帧；无有效帧时间数据 | 刷新率读取：有；帧时间：BLOCKED |
| 120Hz | 曾切换系统模式并恢复；旧 CPU/内存样本，不是呈现帧时间测试 | 有限覆盖 |
| 60Hz | 当前分辨率模式测试不支持，未取得有效 Dock 帧时间 | NOT TESTED |

无法支持的组合不通过修改报告来“凑齐”。后续需在设备原生支持的 DPI/显示模式下进行；若要覆盖更多组合，可使用支持这些模式的外接显示器或 Windows 虚拟机，但虚拟机结果只能代表其虚拟 GPU/显示栈，不能替代主机设备的物理刷新验收。

## 5. Windows 任务栏、全屏与恢复能力

本轮没有启动普通最大化窗口、无边框全屏窗口、独占全屏程序或托盘交互测试。也没有验证任务栏共存、Show/Exit 菜单、隐藏/重新显示，或缺失/损坏/非法配置恢复。源代码包含托盘菜单和配置隔离路径并不构成实测。P0 状态：NOT TESTED。

真正独占全屏场景需要能够确认该应用实际进入独占模式，而不能将普通最大化或无边框全屏误称为独占全屏；当前设备和项目没有这类确认记录。由于前述意外，涉及全屏覆盖或系统交互状态的补测必须先获得用户明确同意。本轮不进入 M1，也没有实现未来阶段的自动隐藏/游戏检测功能。

## 6. 资源与帧时间

CPU、内存和 GPU 表是此前在当前机器采集的静态 idle 样本，不是本轮动画性能测试。具体采样数、归一化方法和局限沿用 runtime-summary.csv 与 gpu-summary.csv。样本短、低负载，不证明动画峰值或帧时间稳定。

| 指标层级 | 方法 | 当前证据 / 结论 |
|---|---|---|
| 应用内部更新间隔 | 应用自计时或逻辑更新日志 | 未实现 / 未采集，NOT TESTED |
| 逐进程呈现提交和 GPU 帧时序 | PresentMon / ETW | PresentMon v2.6.0 退出码 6；StartTrace 返回 access denied；没有帧 CSV，BLOCKED |
| DWM 全局合成节奏 | DwmGetCompositionTimingInfo / DWM_TIMING_INFO | [Microsoft API 文档](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcompositiontiminginfo) 给出受支持的桌面 API；Windows 8.1 起 hwnd 必须为 NULL，所得为全局合成器信息，不能当作某个 Dock 的逐进程呈现或面板扫描输出。[计时结构](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_timing_info) 中 cFramesDisplayed / cFramesDropped 需第二次调用后才有效。文档没有列出调用需要提权；据此它是潜在的非提权方案（推断），但本机尚未运行验证，NOT TESTED |
| WGC 合成帧时间 | Direct3D11CaptureFrame.SystemRelativeTime | [Microsoft 文档](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime) 定义为合成器渲染帧时的 QPC 时间；现有视觉证据只有一帧，不能从单帧算帧间隔 |
| 显示器实际 scanout | 面板/硬件级测量 | 未采集，NOT TESTED |

曾尝试的 PresentMon 参数模板如下；PID、文件名与会话名按当次运行替换。它没有成功启动 ETW：

    D:\MacOS\GlassDock-Win\.tools\PresentMon\PresentMon-2.6.0-x64.exe --process_id <PoC_PID> --timed 10 --terminate_after_timed --output_file D:\MacOS\GlassDock-Win\.local\evidence\frames\<candidate>-<run>.csv --session_name <session-name> --no_console_stats

工具诊断指出 ETW 采集需要管理员权限或 Performance Log Users 组成员资格；这两者均未使用。管理员执行会扩大本次采集权限，组成员变更会持久改变账户权限；采集还会记录进程呈现时序数据。未提供管理员授权，也未改组或提权。DwmGetCompositionTimingInfo 是下一项可调查的无需 ETW 会话权限的候选，但只提供全局 DWM 时间信息，当前仍未经本机实测。

没有证据证明达到 120Hz 或 240Hz 流畅度目标。

## 7. WPF 与 WinUI 3 技术复评

| 维度 | WPF + Win32 互操作 | WinUI 3 + Windows App SDK | 当前判断 |
|---|---|---|---|
| 逐像素透明与点击穿透 | 当前 PoC 使用 WPF 无边框透明窗及 HWND 命中测试；受控窗口真实收到三边透明角单击、双击和拖动起始事件 | 三边透明角的合成输入被 Dock 根窗口接收，底层窗口未收到 | WPF 暂时领先；WinUI 有已复现 P0 失败 |
| DWM / DirectComposition | WPF 窗口由 Windows 合成；没有直接评测额外 Composition 视觉树 | Composition API 可与 WinUI 集成；框架使用 Composition 不自动证明 alpha 输入穿透或 Present 时序 | 两边的 DWM 合成性能仍缺系统化对照 |
| 液态玻璃 | 当前捕获只显示 alpha 透射与阴影，没有观察到背景模糊 | WinUI/Composition 有视觉效果扩展空间，但本 PoC 未捕获、未测效果 | 两边都未验证完整玻璃效果 |
| 动画性能和帧时间 | 无 PresentMon 帧样本；现有资源为静态 idle | 同样无有效帧样本 | 无法排序 |
| 高 DPI、多显示器、动态刷新 | 250% 单屏下输入测试；跨 DPI/屏幕、动态变化未测 | 同样未完成，且透明输入已失败 | 两边均未通过适配门槛 |
| 顶层窗口、任务栏、全屏共存 | 旧窗口探测显示置顶；真实任务栏/全屏共存和恢复未测 | 旧窗口探测显示置顶；真实任务栏/全屏共存和恢复未测 | 不能从 topmost 属性推出共存能力 |
| Windows 11 API 与维护 | WPF 加少量明确 Win32 消息钩子；需要约束平台互操作与 DPI 生命周期 | WinUI AppWindow/Windows App SDK 互操作边界；需跟踪 SDK/runtime 部署兼容性 | 可维护性需在关键行为证明后再评估 |
| 后续扩展成本 | 适合当前快速 PoC；是否能承载复杂 Composition 渲染、设备丢失和多屏场景未知 | 提供现成 UI/Composition 框架，但核心输入限制需先解决 | 现阶段不做生产选型 |

结论：WPF 是当前真实点击穿透证据下的临时领先候选，不是最终决定。WinUI 的实际透明区失败必须先解决或确认其不可维护地绕过。还需补足视觉、DPI/多屏、刷新帧时序和系统共存证据。只有两条现有路线都暴露出明确且实质性的底层限制时，再制作范围受控的 Win32 + DirectComposition PoC；目前没有证据支持它必然更快或自动解决输入命中。

## 8. 构建、单测、CI、克隆与许可证

本轮在源仓库当前工作树运行：

| 操作 | 命令 | 结果 |
|---|---|---|
| Release 构建 | .\scripts\build.ps1 | PASS；8 个项目；0 警告、0 错误；本轮代码编译成功 |
| Core 单元测试 | .\scripts\test.ps1 -NoRestore | PASS；11/11 |
| 输入探针独立构建 | .\.tools\dotnet\dotnet.exe build spikes\GlassDock.M0.InputProbe\GlassDock.M0.InputProbe.csproj --configuration Release --no-restore | PASS；0 警告、0 错误；没有启动探针窗口 |
| 实际输入测试 | scripts\verify-poc-input.ps1 的 20261022T104053Z 运行 | WPF 三边 PASS；WinUI 透明区三边 FAIL；WinUI 内容区与拖动 NOT TESTED |
| 当前改动 clean clone | 未运行 | NOT TESTED |
| GitHub Actions 远端 CI | 当前未配置 remote；未推送 | NOT TESTED |

旧报告记录过从提交 62c35de85256698f06b1f08359a43ac0f3acfb81 做的 D 盘 clean clone。它不是当前 HEAD，也不包含现在的输入追踪改动，不能当作当前改动的 clean-clone 证据。远端 workflow 文件存在也不等于 GitHub Actions 运行通过。

项目许可证尚未选择，根 LICENSE 仍是未授予许可的占位内容。比较仅供权利人决策，不构成法律意见：

| 方案 | 大致分发效果 | 适用考虑 |
|---|---|---|
| MIT | 宽松许可；允许使用、修改、再分发及销售，要求保留版权和许可声明，并含免责条款 | 希望最少使用限制、广泛复用 |
| Apache-2.0 | 宽松许可；含明确专利授权及专利诉讼终止条款，保留声明并处理 NOTICE 等要求 | 希望宽松采用，同时更明确地处理贡献者专利授权 |
| GPL-3.0 | 强 copyleft；分发覆盖作品时有源码、许可、修改和相应条款义务 | 希望衍生分发继续共享相同自由；需审慎评估分发与组合方式 |
| 暂不公开发行 / 保留所有权利 | 不作开源许可授权；第三方通常不能据此取得复制、修改或分发授权 | 尚未决定公开策略时的临时状态；当前 LICENSE 已表达未授予许可 |

如目标是广泛复用，可比较 MIT 与 Apache-2.0；如希望分发后的衍生作品继续受 GPL 条款约束，则评估 GPL-3.0；如还没决定是否公开，则先保持未公开发行。项目当前许可证决定留给用户，不做修改或发布。

许可原文参考：[MIT（OSI）](https://opensource.org/license/mit)、[Apache-2.0（ASF）](https://www.apache.org/licenses/LICENSE-2.0.html)、[GPLv3（GNU）](https://www.gnu.org/licenses/gpl-3.0.en.html) 与 [GNU GPLv3 说明](https://www.gnu.org/licenses/quick-guide-gplv3.html)。

## 9. M0 总结

M0 的局部基础工程与本地构建/单测通过，但不是验收通过。WPF 的受控真实输入路由在当前屏幕模式下通过；WinUI 透明角实际事件路由失败；DWM 真实合成仅有一张 WPF/浅色/Bottom 截图且未看到背景模糊；帧时间、跨 DPI、多屏、任务栏/全屏/托盘/配置恢复仍有 P0 缺口；项目许可证未决定。结果详见 [逐项需求矩阵](REQ-matrix.md)。

**明确结论：M0 暂不验收；技术路线不冻结；M1 不开始。** 后续全屏桌面覆盖、合成输入或显示模式变化测试，须先征得用户同意。当前只完成了 D 盘本地构建和 Core 单元测试，没有运行交互回归。

## 10. 证据位置

- 需求矩阵：REQ-matrix.md
- 技术路线复评：../../tech-spike-comparison.md
- UI 栈 ADR：../../adr/0001-ui-rendering-stack.md
- 输入脚本：../../../scripts/verify-poc-input.ps1
- 输入接收程序：../../../spikes/GlassDock.M0.InputProbe/
- 合成截图与元数据：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\
- 输入事件 CSV 与原始日志：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261022T104053Z\
- 先前资源汇总：runtime-summary.csv、gpu-summary.csv
- PresentMon 错误记录：D:\MacOS\GlassDock-Win\.local\evidence\frames\PresentMon-Wpf-20261022T091522Z.stderr.txt
