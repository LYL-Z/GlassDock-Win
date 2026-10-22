# M0 缺口补测与验收报告

- 日期：2026-10-22
- 项目：D:\MacOS\GlassDock-Win
- 状态：**M0 未验收通过；P0 缺口仍在；不进入 M1，不冻结生产技术路线。**
- 本轮边界：更新报告/矩阵/路线复评；完成精确提交 `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 的 D 盘独立克隆；静态审查 WinUI 命中路径；实现被动 DWM 探针及交互测试安全看门狗；本机 Release 构建和单测通过。没有运行桌面交互、输入注入、全屏、显示设置或提权操作。安全与探针提交 `b4d78b8be3f47f08febbf261c720e6dc318e170a` 已推送；GitHub Actions 精确 SHA run `39800785724` 已完成，Build 和 Test 均成功。此后仅进行文档校准，未再改代码。

## 1. 当前环境与证据边界

此前从本机查询到 Windows 11 x64，版本 10.0.26300；D 盘 .tools 内 .NET SDK 10.0.402；WinUI PoC 使用 Windows App SDK 2.5.1。显示接口报告主屏设备为 CDD、3840×2400、240Hz；GetDpiForWindow 返回 240 DPI（96 DPI 基准下 250%）。这些是操作系统报告值，不是独立核验的显示器型号或硬件规格。当前仅有一个活动屏幕记录。

同分辨率的 Windows 显示模式查询曾确认可切换 120Hz 并恢复到 240Hz；60Hz 的 CDS_TEST 返回 -2（该模式不受支持）。100/150/200% DPI、60Hz、240Hz 动画帧时间和多显示器组合没有通过实际 Dock 交互完成验证。不要从已有 250%/240Hz 结果外推。

本轮起始状态为 `main`、HEAD `3a6ca88b6925f60ecea5971f8781b7482153ed0c`、工作区干净，remote 为 `https://github.com/LYL-Z/GlassDock-Win.git`。本轮变更限于安全控制脚本/探针、解决方案纳入测试探针，以及 M0 文档。构建、缓存、克隆、日志和证据均在 `D:\MacOS\GlassDock-Win` 下；没有在 C 盘创建项目文件、证据或构建输出。

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
| DWM 全局合成节奏 | DwmGetCompositionTimingInfo / DWM_TIMING_INFO | 本机被动探针 8/8 次 S_OK，当前令牌未启用管理员角色；结构 292 字节，刷新/全局 compose-rate 约 239.998–240.004Hz。此数值仅为全局 DWM 时序，不能当作 Dock 逐进程呈现或面板扫描输出；首次调用外的累计帧计数按 Microsoft 文档解释。该子测试 PASS；整体 Dock 帧时序要求仍 BLOCKED |
| WGC 合成帧时间 | Direct3D11CaptureFrame.SystemRelativeTime | [Microsoft 文档](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime) 定义为合成器渲染帧时的 QPC 时间；现有视觉证据只有一帧，不能从单帧算帧间隔 |
| 显示器实际 scanout | 面板/硬件级测量 | 未采集，NOT TESTED |

曾尝试的 PresentMon 参数模板如下；PID、文件名与会话名按当次运行替换。它没有成功启动 ETW：

    D:\MacOS\GlassDock-Win\.tools\PresentMon\PresentMon-2.6.0-x64.exe --process_id <PoC_PID> --timed 10 --terminate_after_timed --output_file D:\MacOS\GlassDock-Win\.local\evidence\frames\<candidate>-<run>.csv --session_name <session-name> --no_console_stats

工具诊断指出 ETW 采集需要管理员权限或 Performance Log Users 组成员资格；这两者均未使用。管理员执行会扩大本次采集权限，组成员变更会持久改变账户权限；采集还会记录进程呈现时序数据。未提供管理员授权，也未改组或提权。DWM API 探针已作为非提权观察运行成功，证据文件：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\dwm\dwm-composition-20261022T121946Z.jsonl`。一次 Pack=8 结构布局不匹配的探测记录 HRESULT `0x88980090`；修正为 Pack=4 后连续 8 个样本 `S_OK`。探针不创建窗口、不改设置、不注入输入。它只测全局 DWM，不能填补 Dock 自身更新/提交/scanout 的缺口。

没有证据证明达到 120Hz 或 240Hz 动画流畅度目标。DWM 报告约 240Hz 的全局 compose-rate 不是 Dock 性能证明。

## 7. WinUI 3 命中路径与技术路线复评

`LayoutRoot` 透明 Grid 覆盖整个 WinUI 窗口，并订阅根 Pointer 事件；窗口仍由矩形 HWND 承载。透明角运行日志显示根元素收到 PointerPressed，处理器没有设置 `Handled`。因此失败不是 Dock 按钮误吞事件，而是系统窗口命中与 XAML 根视觉树共同截获。当前可检验的公开 API 候选是 `SetWindowRgn`：将窗口区域限为胶囊外形可能让四角下传，但它会剪裁绘制，圆角抗锯齿、阴影和模糊边缘存在风险。该候选尚未实施或运行。`WS_EX_TRANSPARENT` 会让整个 layered window 的输入下传而失去内容交互；`HTTRANSPARENT` 的公开语义依赖同线程下层窗口，不足以保证当前独立下层进程。

WPF 使用 `AllowsTransparency=True` 并另有 `WM_NCHITTEST` 圆角逻辑。它在现有合成输入场景下真实通过，但日志没有隔离 alpha-hit-test 和 hook 两个机制，也未测物理鼠标、高 DPI、多屏或应用共存。因此 WPF 是输入行为的临时领先候选，不能据此冻结。请见[技术路线比较与停止条件](../../tech-spike-comparison.md)。

## 8. 本地、干净克隆和远程 CI 结果

### 本机工作树（D 盘）

| 验证 | 结果 |
|---|---|
| .NET SDK | 10.0.402；来自项目 `.tools\dotnet`；NuGet、CLI、临时、缓存/中间文件均指向 D 盘 |
| 依赖恢复 | PASS；`dotnet restore ... --locked-mode --disable-build-servers`，退出码 0 |
| Release 解决方案构建 | PASS；本轮 9 个项目，0 warnings、0 errors，退出码 0，用时 14.16 秒；未启动 PoC |
| Core 单元测试 | PASS；11/11，0 failed、0 skipped，退出码 0 |
| PowerShell 静态解析 | PASS；`common.ps1`、交互安全、输入、窗口、CPU/GPU/PresentMon、DWM 共 8 个脚本均无 ParserError |
| 安全互操作源编译 | PASS；只执行 `Add-Type` 编译 P/Invoke 声明，没有调用桌面 API |
| DWM 被动探针 | PASS；当前未启用管理员角色；2 秒约 8 次查询，S_OK 8/8；仅全局 DWM 指标 |
| 交互脚本 | 未运行；没有注入输入/覆盖桌面/改变焦点或显示模式 |

当前改动验证日志在 `D:\MacOS\GlassDock-Win\.local\evidence\final-validation\`：`dotnet-info.txt`、`restore-current.log`、`build-current.log`、`test-current.log`、`summary-current.txt`。所有构建输出和恢复缓存都位于 D 盘。该构建没有启动 Dock PoC，因此不能当作桌面交互或当前改动的视觉验证。

### 精确 SHA 干净克隆

从 GitHub 在 `D:\MacOS\GlassDock-Win\.local\clean-clones\3a6ca88b6925f60ecea5971f8781b7482153ed0c` 建立独立克隆，HEAD 与指定 SHA `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 一致，初始 Git 状态干净。克隆内独立安装 .NET SDK `10.0.402`；`DOTNET_CLI_HOME`、NuGet 包/HTTP 缓存、obj/bin、测试日志均落在克隆目录的 D 盘 `.local` / `.tools`。没有依赖主工作区非跟踪文件。

| 命令/步骤 | Exit code | 结果 |
|---|---:|---|
| `dotnet restore GlassDock-Win.sln --configfile NuGet.Config --locked-mode --disable-build-servers` | 0 | PASS；恢复日志 `restore.log` |
| `dotnet build GlassDock-Win.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true --disable-build-servers` | 0 | PASS；0 warnings、0 errors，约 13.17 秒 |
| `dotnet test GlassDock-Win.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true --logger console;verbosity=normal --disable-build-servers` | 0 | PASS；11/11 |

环境和日志：`D:\MacOS\GlassDock-Win\.local\clean-clones\3a6ca88b6925f60ecea5971f8781b7482153ed0c\.local\evidence\clean-clone\`（`dotnet-info.txt`、`environment.json`、`restore.log`、`build-final.log`、`test-final.log`、`summary.txt`）。这证明指定旧提交 clean clone，不代表本轮未提交改动的 clean clone。

### GitHub Actions（精确提交）

基线提交 `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 的 push workflow `Windows CI` 已完成，run `39767098768`，结论 `success`；唯一 job `build-and-test` 成功，Build 和 Test 步骤均 `success`。[基线运行记录](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39767098768)。本轮安全与探针提交 `b4d78b8be3f47f08febbf261c720e6dc318e170a` 的精确 SHA run `39800785724` 也已完成，结论 `success`；job `build-and-test` 的 Build、Test 步骤均 `success`。[本轮精确 SHA 运行记录](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39800785724)。工作流文件存在、基线通过、当前实现提交通过是不同的证据，现已分别记录。

## 9. 仓库公开与许可证状态

仓库公开，代码可在 GitHub 中被查看和 fork；这与项目授权不是同一件事。GitHub 对公开仓库的服务许可不等于 MIT/Apache/GPL 等广泛使用、修改和再分发授权。[GitHub 许可证说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。根 `LICENSE` 未修改；其现有“Do not redistribute, publish”绝对语句与 GitHub 允许在服务内 fork 的措辞不协调。建议由权利人选择许可证后再改为正式文本；若继续暂不授权，可考虑仅澄清为“未授予项目级许可，GitHub 服务条款允许的查看和 fork 除外”。这个替换建议没有写入 `LICENSE`，等待用户决定。

| 选项 | 对复用、商业化和衍生分发的主要影响 |
|---|---|
| MIT | 宽松许可，允许使用、修改、商业使用和再分发；需保留版权与许可声明，提供免责条款 |
| Apache-2.0 | 宽松许可；有明确贡献者专利授权及专利诉讼终止条款，且需保留声明并遵守 NOTICE 等要求 |
| GPL-3.0 | 使用和修改允许；再分发覆盖作品/衍生作品时需遵守相应 copyleft、源码提供和许可义务 |
| 暂不公开授权 | 保留著作权且不授予广泛复制、修改、商业使用和再分发权；GitHub 内查看/fork服务权限仍适用 |

没有自行选择许可证；MIT/Apache/GPL 差异仅供用户决策，不构成法律意见。

## 10. 测试安全保护与待授权计划

安全控制已经写入交互脚本：显式 `-ConfirmInteractiveTest` 和精确短语确认；PowerShell 高影响 `ShouldProcess`；单独隐藏看门狗进程；全局紧急键 `Ctrl+Shift+F12`；10–1800 秒 timeout；manifest 限定 D 盘证据目录；只在 PID、可执行文件路径和进程启动时间三者吻合时停止本轮进程；异常超时清理；异常恢复已登记的本地配置副本、释放本脚本注入且仍按下的鼠标左键，并尝试恢复原前台窗口。结束后检查任务栏可见性/边界、屏幕边界、环境变量、测试进程、看门狗和文件恢复。八个 PowerShell 脚本解析通过、程序集构建通过；**紧急键、watchdog timeout 和真实恢复流程没有交互运行验证**，因其测试会影响桌面，必须取得用户逐项许可。

建议授权顺序（每一项开始前单独确认范围）：

1. **点击路由，优先级 P0，约 4–6 分钟**：专用下层窗口实时记录消息；比较 WPF 基线与 WinUI 当前版本；若先完成公开 region 候选，再加一组候选对照。Bottom/Left/Right；内容区单击/双击，透明区单击/双击，透明区按住拖出/拖入。脚本会显示全屏顶置背景并改变前台焦点，使用 SendInput 合成鼠标（不注入键盘）；按 `Ctrl+Shift+F12` 立即停止；输入脚本默认 900 秒超时、参数上限 1800 秒。结束后检查恢复文件、任务栏/屏幕边界和前台窗口。
2. **视觉捕获，优先级 P0，约 2–3 分钟/批**：对照背景窗口、Dock 和 Windows.Graphics.Capture；浅/深主题、中心/圆角、背景 alpha/模糊/边缘/阴影，逐步覆盖 Bottom/Left/Right 和 44/58/72 DIP。会显示受控桌面背景并出现 Dock，捕获当前桌面图像写入 D 盘；不会注入输入或切换显示设置；快捷停止为 `Ctrl+Shift+F12`，看门狗默认 900 秒且最高配置为 1800 秒；结束时验证前台和窗口/显示边界。
3. **系统共存，优先级 P0，约 5–8 分钟**：经批准后依次运行任务栏共存、最大化、无边框窗口、Show/Exit、隐藏/恢复和配置异常修复。它会启动项目自己的可见窗口、切换焦点并打开托盘菜单；暂不包含独占全屏和系统设置变更；紧急键/看门狗相同，结束后核验桌面状态。
4. **DPI/分辨率/刷新模式，P0 环境补测，约 5 分钟/模式**：本机 DPI/模式切换会改变桌面和其他窗口的物理显示。当前没有经过验证的独立恢复通道，所以**本轮不申请、不执行**；待制定由操作系统显示模式外部回滚/第二显示设备双屏确认的恢复演练后，再提交单独申请。优先使用本机原生支持的 DPI/刷新组合或外接显示器；虚拟机仅作为逻辑适配补充。
5. **独占全屏，优先级 P1，约 2 分钟**：需启动能确认真正进入独占模式的测试程序，可能遮挡/抢占桌面；采用单独方案、Ctrl+Shift+F12、安全超时与退出恢复验证；不与一般窗口/边框全屏混为一项。

## 11. M0 总结与决策

本地 Release 构建、Core 单测、精确 SHA clean clone、原 SHA GitHub Actions，以及被动 DWM API 调用均有正面证据；其中 DWM 只证实全局 compose-rate 采样可用。WPF 在有限合成输入条件下通过；WinUI 透明角失败；唯一的浅色 WPF 合成帧只有透射/阴影，没有观察到模糊。DPI/多屏、任务栏/全屏共存、托盘/配置恢复、Dock 进程帧时间和路线视觉能力仍未完成验收。

**明确结论：M0 暂不通过；不进入 M1；不冻结 WPF、WinUI 或 DComp。** 设备依赖的未覆盖组合保留 NOT TESTED；可通过当前机器代码/交互验证的架构缺口保持 BLOCKED/FAIL。未经独立授权，不启动上述任何桌面交互测试。

## 12. 证据位置

- 需求矩阵：REQ-matrix.md
- 技术路线复评：../../tech-spike-comparison.md
- UI 栈 ADR：../../adr/0001-ui-rendering-stack.md
- 交互测试安全控制：../../../scripts/interactive-test-safety.ps1、`../../../spikes/GlassDock.M0.InputProbe/M0SafetyWatchdog.cs`
- 输入测试入口：../../../scripts/verify-poc-input.ps1；原始输入证据：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261022T104053Z\`
- DWM 探针：../../../scripts/measure-dwm-composition.ps1；输出：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\dwm\dwm-composition-20261022T121946Z.jsonl`
- WGC 截图与元数据：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\`
- 指定 SHA 干净克隆日志：`D:\MacOS\GlassDock-Win\.local\clean-clones\3a6ca88b6925f60ecea5971f8781b7482153ed0c\.local\evidence\clean-clone\`
- 先前资源汇总：runtime-summary.csv、gpu-summary.csv
- PresentMon 错误记录：`D:\MacOS\GlassDock-Win\.local\evidence\frames\PresentMon-Wpf-20261022T091522Z.stderr.txt`
