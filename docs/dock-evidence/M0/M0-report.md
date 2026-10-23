# M0 缺口补测与验收报告

- 更新日期：2026-10-23（测试记录使用 UTC 文件夹名）
- 仓库：`D:\MacOS\GlassDock-Win`
- 当前结论：**M0 未通过；不进入 M1；不冻结 WPF、WinUI 3 或 Win32 + DirectComposition。**
- 桌面测试证据基线提交 `8be36ea111e35875dbc3ff8cab96474a36e6d413` 的 Windows CI run `39896333410` Build/Test 成功。本轮架构探针代码提交 `8f6ca23ecb159dd20e71e0ef303a691cda9293ab` 的 run `39924864083` Build/Test 也成功；两个提交均不代表 M0 整体验收通过。
- 本次实际执行了已获批准的输入路由、WGC 视觉、窗口共存、显示刷新、托盘/配置恢复及 DXGI 独占全屏测试。未提权、未启动 M1、未改 LICENSE、未创建 Release。
- 2026-10-23 本次续作先实施默认关闭的系统材质/非激活启动候选、应用侧性能记录器、托盘命令路由单测及文档/脚本修订；收到用户指定短语 RUN M0 DESKTOP TEST 后，仅执行本次获批的 18 场景 WGC 视觉矩阵。没有追加输入、全屏、显示模式或其他交互测试。

## 1. 环境与证据边界

当前设备报告 Windows 11 x64、主显示器 3840×2400、240 Hz、窗口 DPI 240（96 DPI 基准下 250%）；当前只发现一个活动显示器。DPI、显示器型号和硬件能力依据系统接口报告，不代表独立校准。所有项目改动、SDK、NuGet、临时文件、构建输出、日志、克隆和截图证据均保存在 D 盘仓库下；未将项目文件、证据或构建输出写入 C 盘。

`WindowFromPoint` 只作为命中诊断。输入结论依据候选事件日志和独立底层 Win32 窗口收到的消息。输入由 `SendInput` 合成，Windows 实际执行了消息路由，但不是物理鼠标手工验收。WGC 截图是 Windows.Graphics.Capture / Direct3D11 的桌面捕获帧，不是面板 scanout 测量。显示模式和 DXGI 状态都是本机受控探针的观测，不构成游戏兼容矩阵。

## 2. 输入穿透与公开 API 候选

基线证据：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261022T153139Z\actual-input-results.csv`。三边均测了内容区单击/双击、从 Dock 拖向透明角、透明角单击/双击、从透明角拖向 Dock。

| 候选/变体 | Bottom / Left / Right 实际结果 | 状态 |
|---|---|---|
| WPF 默认实现 | 18/18 场景符合预期；内容输入归 Dock，透明区输入归底层窗口 | PASS（仅当前 250% 单屏及合成输入） |
| WinUI 3 默认实现 | 内容单击/双击及从 Dock 开始拖动共 9/9 通过；透明角单击/双击及从透明角开始拖动共 9/9 失败，底层未收到应有输入 | FAIL |
| WinUI 3 `SetWindowRgn` 圆角候选 | 18/18 场景符合预期：透明区下传，内容点击和拖动仍由 Dock 接收 | PASS（测试变体，不是默认或已选架构） |

WinUI 候选实现调用公开 User32 `SetWindowRgn`，用公开 GDI `CreateRoundRectRgn` 建立圆角 HWND 区域；只在显式环境变量 `GLASSDOCK_M0_TEST_ROUNDED_REGION=1` 时启用。250% DPI 下，测试应用的窗口区域使用 8 DIP 内缩（20 像素）和 28 DIP 圆角（140 像素直径）。调用成功由 PoC 日志确认；三个位置都完成了底层消息验证。候选默认关闭。

对应输入证据：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261023T033334Z\actual-input-results.csv`；区域调用日志位于同一目录下 `WinUI-Bottom/Left/Right\state\logs\dock.log`。此前一次重试在前置检查处安全停止：探针实际字段是 `ExpectedAreaCovered`，脚本误查 `FullscreenCovered`；当次没有发送鼠标输入，恢复检查通过。当前脚本和 WGC 探针已统一使用真实字段。

这次候选只解决了 HWND 圆角外区域输入，不能证明抗锯齿、阴影、模糊和 DPI 动态变化都适合长期使用。需要结合下节候选截图判断，并继续保持默认关闭。

## 3. DWM 合成视觉

旧基线矩阵位于 D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261022T154558Z\：36/36 次 WGC 捕获成功，覆盖 WPF/WinUI 3、浅/深受控背景、Bottom/Left/Right 及 44/58/72 DIP。它是基线捕获记录，不等同于 36 张逐张视觉验收。

用户于 2026-10-23 提供约定短语后，按限定范围执行新的三候选对照：WPF + DWM Acrylic、WinUI 3 + Desktop Acrylic、WinUI 3 + Desktop Acrylic + SetWindowRgn；每候选均为浅/深受控底图 × Bottom/Left/Right × 58 DIP，共 18 场景。WGC 捕获 18/18 成功，人工审阅完成 18/18，视觉 PASS 0/18、FAIL 18/18。每个图像的分辨率 3840×2400、DPI 240（250%），单显示器；18/18 元数据都通过受控背景标记校验。场景级原始捕获状态仍保存在 actual-visual-results.csv/json，视觉判定另外保存在 visual-assessment.csv/json，避免把捕获成功改写为视觉通过。

| 候选 | 捕获和视觉判定 | 真实模糊 | 边缘、宿主与阴影 | 内容 |
|---|---|---|---|---|
| WPF + DWM Acrylic | 捕获 6/6；视觉 FAIL 6/6 | 棋盘、网格和斜线仍清晰。WPF 日志记录 DWM system backdrop 属性返回 HRESULT 0，但这只证明调用成功 | 圆角面板可见，外侧有明显矩形宿主底色/边界；阴影随宿主边界呈方框 | 静态图标 6/6 完整；动画外溢未测 |
| WinUI 3 + Desktop Acrylic | 捕获 6/6；视觉 FAIL 6/6 | 候选启用日志存在，但底图图案仍清晰；没有确认材质模糊生效 | 圆角 pill 外仍有明显浅色矩形宿主和矩形阴影，深色底图上也显得偏白 | 静态图标 6/6 完整；动画外溢未测 |
| WinUI 3 + Desktop Acrylic + SetWindowRgn | 捕获 6/6；视觉 FAIL 6/6 | 区域裁剪不产生模糊，背景图案仍清晰 | pill 剪影最明确；六帧仍可见窗口外围矩形边线/光晕。圆角像素级抗锯齿未量化，不能据此判定 HRGN 已达到高质量玻璃边缘 | 静态图标 6/6 完整；缩放动画、拖拽和菜单未测 |

三条路线的图中都能透出一定背景图案，但只能证明透射/调色存在，不能将其计为真实模糊。原因尚未隔离：这些图像无法区分系统 acrylic 回退、当前窗口宿主组合限制或候选材质设置问题。捕获帧来自 WGC 的 DWM 合成主屏画面，不是显示面板 scanout。

本机报告和逐帧判定：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261023T091558Z\visual-comparison-report.md。该目录在 .local 中并被 Git 忽略；原始捕获没有提交或上传。独立看门狗成功注册 Ctrl+Shift+F12（Win32Error=0），看门狗运行约 46 秒后正常完成；未主动触发急停，因此急停后的端到端路径仍未验证。RestorePassed=true，本轮桌面恢复检查通过。

**结论：本次三条候选的核心材质视觉均 FAIL。** WPF DWM Acrylic 调用成功但没有可见 blur；WinUI Desktop Acrylic 候选也没有可辨真实 blur；SetWindowRgn 候选保住了更明确的圆角剪影，却没有解决 blur 和矩形边缘。不存在可以据此冻结的视觉路线。

## 4. DPI、尺寸和刷新率

| 覆盖项 | 实测结果 | 状态 |
|---|---|---|
| 250% DPI / 单屏 | 输入、视觉、共存和全屏探针运行于系统报告的 240 DPI 环境 | 有限覆盖 |
| 44 / 58 / 72 DIP | 两候选均完成 3 边、浅/深背景捕获；仅当前 250% DPI，未完成其他缩放比例的布局回归 | NOT TESTED（完整 DPI 适配） |
| 100% / 150% / 200% DPI | 未运行 | NOT TESTED |
| 多显示器、跨屏迁移 | 当前仅一个活动显示器；未验证 | NOT TESTED |
| 3840×2400 @ 240 Hz | 两候选窗口存活；刷新 API 和 DWM 全局率样本约 240 Hz | PASS（模式/窗口生存范围；不是帧率验收） |
| 3840×2400 @ 120 Hz | 两候选在 120/240Hz 模式测试中均存活；独立看门狗恢复到原始 240Hz，校验通过 | PASS（模式/窗口生存范围；不是帧率验收） |
| 60 Hz | 当前分辨率组合的 `CDS_TEST` 返回不支持；未伪造测试结果 | NOT TESTED（当前设备不支持） |

模式切换证据：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\dpi-refresh\refresh-20261022T161000Z\refresh-results.json`。设备条件不足的 DPI/多屏/60Hz 组合可以通过外接屏或其他测试设备补齐；虚拟机只证明虚拟显示栈行为，不能替代物理刷新率验收。

## 5. 任务栏、窗口共存、全屏、托盘和配置恢复

普通最大化及无边框全屏模拟场景共 4/4 PASS（WPF/WinUI × 两种窗口状态）。受控下层程序与 Dock 均收到各自的合成点击，Dock 中心命中解析到 Dock 进程，任务栏保持可见；这不是 Alt+Tab 测试，也不覆盖所有 Shell 面板。证据：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\coexistence\20261022T163228Z\coexistence-results.json`。

独占全屏使用当前模式建立 DXGI swap chain，并确认 `SetFullscreenState(true)` / `IsFullScreen`。设备模式测试前后均为 3840×2400@240Hz，未改变分辨率、DPI 或刷新率；恢复校验通过。新的验收规则允许 Dock 在真正 DXGI 独占全屏期间不可见；可见性本身不作为失败。需要满足的是 Dock 不成为前台、不截获游戏输入、不造成独占状态退出，且退出全屏后自动恢复，无须关闭或重启 Dock。

- WPF：DXGI 独占状态在两次取样中均保持 true；Dock HWND 显示且置顶，`WindowFromPoint` 只作命中诊断；WGC 主屏图像中看不到 Dock，这与允许隐藏的规则相容。但 `ForegroundProcessIdAfterDock` 等于 Dock PID `71032`，直接违反“启动 Dock 不抢焦点”的要求，故当前默认启动行为 **FAIL**。独占场景的真实鼠标输入未测试，窗口是否截获游戏输入仍 **BLOCKED**。
- WinUI 3：两次取样中 DXGI 独占状态已是 false，前台 PID 等于 Dock PID `87388`。因此当前默认启动行为同时违反不抢焦点与不强制退出独占的要求，当前行为 **FAIL**。WinUI 的 `App.OnLaunched` 明确调用 `_window.Activate()`，公开文档说明该调用会尝试将窗口带到前台并取得输入焦点；但本次没有隔离激活与置顶/渲染宿主的因果作用，DXGI 独占退出的精确根因仍未确认。
- 两候选在退出真实全屏后能否自动恢复、是否需要重新启动，以及 Dock 隐藏时的游戏输入投递均未测，继续 **BLOCKED**。受控 DXGI 程序不是游戏兼容矩阵。

原始证据：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\fullscreen-exclusive\20261023T032605Z\exclusive-fullscreen-results.json`、[WPF 帧](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/fullscreen-exclusive/20261023T032605Z/Wpf/after-dock-monitor.png)、[WinUI 帧](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/fullscreen-exclusive/20261023T032605Z/WinUI/after-dock-monitor.png)。探针备注指出 WGC 捕获也可能改变独占状态；但 WinUI 的独占状态在取帧前后取样中都已退出。捕获帧不是面板 scanout。

托盘与配置：

- WPF 真实托盘图标右键菜单的 Show 和 Exit 均通过；测试确认先隐藏，再通过 Show 恢复，Exit 结束测试进程。
- WinUI 3 托盘图标注册及右键弹出菜单窗口均确认；原生 `HMENU` 建立了 `Show Dock` / `Exit GlassDock` 项，处理分支现由 Core `DockTrayCommandRouter` 承担，新增单测验证 Show、Exit、未知命令三种回调映射（3/3 PASS）。UIA/MSAA/`GetMenu` 无法定位是当前自动化工具对原生弹出菜单的读取限制，不能据此判定产品功能失败。真实菜单选择、键盘导航与辅助功能树仍 **NOT TESTED**，需后续交互授权。
- 缺失、损坏 JSON、非法 Edge 配置在 WPF/WinUI 各自隔离状态根下共 6/6 PASS：进程存活、默认 Bottom 被持久化，损坏 JSON 写出错误日志。未触碰用户真实配置。

证据目录：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\tray-config\`；配置矩阵 `20261023T030101Z`，WPF 托盘 `20261023T030531Z`，WinUI 菜单可访问性复核 `20261023T031135Z`。

少数旧运行的根级摘要文案与逐项记录不一致；原始文件保持不变，勘误按 `Tests`/`CaptureStatus`/样本字段和实际截图解读。详见 `D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\evidence-interpretation-errata.md`。

## 6. DWM 和帧时间

被动探针脚本 `scripts/measure-dwm-composition.ps1` 调用 `DwmGetCompositionTimingInfo(hwnd=NULL)`，不创建可见窗口、不改变显示模式、不注入输入，也未提权。当前复测约 4 秒、16/16 成功调用，HRESULT 全为 `S_OK`，全局 Refresh/Compose 约 240.004 Hz。日志：`D:\MacOS\GlassDock-Win\.local\evidence\final-validation\resume-20261023T032925Z\dwm-composition.jsonl`。

这只是 DWM 全局合成时间，不归属到 Dock 进程，不是应用逻辑更新间隔、逐进程呈现提交间隔，也不是面板 scanout。WGC 目前保存单帧，不能从单帧计算帧间隔。PresentMon/ETW 仍因 `StartTrace` access denied 无有效逐进程 CSV；没有使用管理员权限或修改账户组。因此 120Hz/240Hz 流畅度要求仍 **BLOCKED**，不得声称达到目标。

本轮新增 `DockPerformanceRecorder` / `M0PerformanceSession`，用 `GLASSDOCK_M0_PERF_TRACE=1` 显式启用。hover 动画入口也记为逻辑更新；记录器输出逻辑更新间隔、WPF/WinUI framework `CompositionTarget.Rendering` 回调间隔、动画完成/中断时长和 P50/P95/P99 分布。idle/animation 边界之间的渲染间隔被省略，避免把跨状态时间误标到任一阶段。CPU 时间在动画阶段边界采样成独立进程阶段窗口，working set/private bytes 同时按一秒间隔采样并标记采样瞬间的状态；一秒 CPU 区间可能跨阶段，因此不可按该标签解释为精确阶段 CPU。GPU 利用率和显存明确写为 null。trace 通过后台写入器写到显式指定的 D 盘 `GLASSDOCK_POC_STATE_ROOT`，没有此路径时拒绝启用。

本机 Release 构建与新增纯单测验证代码和统计公式；本轮没有启动窗口或创建性能 trace，因此目前没有任何 Dock 自身 P50/P95/P99、分阶段 CPU/内存或动画负载数值。`CompositionTarget.Rendering` 是框架渲染回调：WPF 文档描述它发生在 composition tree 渲染前；WinUI 文档说明该回调在 UI 线程，订阅会令线程每帧保持活跃。采集器会在 Dock 关闭时解绑，且默认关闭；计时回调是代理指标，不是 GPU submit、每进程 Present、DWM 最终合成或物理面板 scanout。订阅和阶段边界查询仍有测量开销，正式比较时须做 telemetry-off 对照。`scripts/measure-poc.ps1 -EnableM0Telemetry` 已连接此开关，但该脚本会显示置顶窗口并影响前台，未获批不运行。

## 7. 本地构建、测试、CI 与克隆

最新本地构建和测试使用仓库 D 盘 SDK `10.0.402`；`TEMP/TMP`、`DOTNET_CLI_HOME`、`NUGET_PACKAGES`、NuGet 缓存、bin/obj 均在 D 盘。

| 验证 | 结果 |
|---|---|
| 2026-10-23 当前工作树 Release solution build | PASS；11 个项目，0 警告、0 错误，退出码 0；日志 `D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\architecture-20261023\build.log`；SDK 版本 `10.0.402` |
| 2026-10-23 Core 单元测试 | PASS；18/18，0 失败、0 跳过，退出码 0；日志 `D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\architecture-20261023\test.log` |
| 2026-10-23 PowerShell 静态解析 | PASS；20 个脚本，0 ParserError；结果 `D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\architecture-20261023\powershell-parse.txt` |
| 2026-10-23 精确 SHA 远程 CI | 提交 `8f6ca23ecb159dd20e71e0ef303a691cda9293ab`；run `39924864083`；Windows CI `build-and-test`、Build 和 Test 步骤均 completed/success；API 状态快照 `D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\architecture-20261023\remote-ci.json`；[精确运行](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39924864083) |
| 差异/边界复核 | `git diff --check` PASS；LICENSE 与 HEAD 无差异；凭据模式扫描 16 个改动文件为 0 命中；检查时无 GlassDock PoC 进程；记录在同一目录的 `diff-check.txt`、`license-unchanged.txt`、`changed-files-review.txt`、`process-check.txt` |
| DWM 被动探针 | PASS；16/16 `S_OK`；仅全局合成指标 |
| 桌面测试恢复 | 最近一次视觉候选恢复检查 14/14 PASS；点击路由候选 12/12 PASS；无测试进程残留 |

使用命令：

```powershell
.\.tools\dotnet\dotnet.exe build GlassDock-Win.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true --disable-build-servers
.\.tools\dotnet\dotnet.exe test GlassDock-Win.sln --configuration Release --no-build --no-restore --logger 'console;verbosity=normal' --disable-build-servers
```

指定提交 `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 在 D 盘独立干净克隆中完成 locked restore、Release build 和 Core 11/11；该结果仅适用于该 SHA，不代表当前改动。路径：`D:\MacOS\GlassDock-Win\.local\clean-clones\3a6ca88b6925f60ecea5971f8781b7482153ed0c`。

远程 CI 已核实的精确提交：基线 `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 的 [Windows CI run 39767098768](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39767098768) 成功；实现提交 `b4d78b8be3f47f08febbf261c720e6dc318e170a` 的 [run 39800785724](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39800785724) 成功；本轮 M0 缺口补测提交 `8be36ea111e35875dbc3ff8cab96474a36e6d413` 的 [run 39896333410](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39896333410) 成功。该最新 run 的 Build 为 0 警告/0 错误，Test 为 11/11；GitHub 另有 Actions Node.js 20 弃用及 setup-dotnet 依赖 `punycode` 弃用提示，不是项目构建警告，步骤均成功。每项远程结果仅适用于所列 SHA。

本次续作已推送为精确提交 `8f6ca23ecb159dd20e71e0ef303a691cda9293ab`；对应远程 run `39924864083` 的 `build-and-test`、Build 和 Test 均成功。远程 CI 只证明该提交的构建/测试工作流通过，不证明候选材质已运行或桌面场景验收通过。

## 8. WinUI 与 WPF 技术结论

WinUI 默认透明区失败日志表明事件到达覆盖全窗的透明 `LayoutRoot`；处理器没有标记 `Handled`，证据更符合“矩形 HWND 命中加可命中的 XAML 根元素”截获，而非按钮吞掉已处理事件。`SetWindowRgn` 公开候选已让三边透明区下传，同时保留内容点击和拖动；其实际视觉候选仍有矩形边缘，shadow/AA/放大裁剪未过。`WS_EX_TRANSPARENT` 会让整个 layered window 的输入下传，不能单独满足 Dock 内容交互；`HTTRANSPARENT` 对跨线程下层转发的保证不足。

WPF 默认透明路由证据较强，但其独占全屏窗口虽保持 DXGI exclusive，探针采到的前台 PID 是 Dock；不可见本身符合修订后的全屏规则，抢焦点则不符合。WinUI 的 `App.OnLaunched` 显式 `Activate()` 与 Dock 成为前台相符，独占状态同时退出；原因尚未单因素隔离。WinUI tray popup 无法被自动化工具读取不是产品功能失败，命令映射的纯单测 3/3 通过，实际菜单选择仍未验证。

本轮已运行并审阅三种材质候选各 6 个 WGC 场景；三者均没有可确认的真实 blur，矩形宿主边线仍可见。WinUI 的公开 Composition API 能力仍值得通过一次独立、有限的背景采样/blur/mask PoC 验证，但不能把 API 可达性写成视觉通过。当前结果不证明任一框架整体不可能；WPF DWM backdrop + layered HWND 的本次简单组合已视觉 FAIL，WinUI Desktop Acrylic 配置也视觉 FAIL。WPF Visual Layer/HwndHost 尚未验证且有互操作代价。**现有证据仍不足以确定最终框架。** 原生 Win32 + DirectComposition 暂不启动。

## 9. 仓库公开与许可证

仓库公开和代码获得项目级授权是两件事。GitHub 服务允许对公开仓库进行站内查看和 fork；这不等于对站外复制、修改、商业使用和再分发授予 MIT/Apache/GPL 等许可。根 `LICENSE` 仍保持原样，未选择正式许可证。其“Do not redistribute, publish”占位语句与公开仓库站内 fork 许可边界容易混淆；可选澄清措辞是“未授予项目级许可；GitHub 服务条款允许的站内查看和 fork 除外”，需权利人决定后再改。

| 选项 | 主要影响 |
|---|---|
| MIT | 允许广泛使用、修改、商业使用和再分发；保留版权与许可证声明 |
| Apache-2.0 | 宽松复用并含明确专利授权及专利诉讼终止条款；需保留声明并遵循 NOTICE 要求 |
| GPL-3.0 | 允许使用、修改和分发；分发覆盖作品/衍生作品时有 copyleft 与对应源码提供义务 |
| 暂不公开授权 | 保留著作权，不授予广泛的复制、修改、商业使用和再分发权；不影响 GitHub 服务内查看/fork 权限 |

以上为项目决策比较，不是法律意见。没有更改 `LICENSE`。

## 10. M0 决策

已证实：WPF 默认点击路由 18/18 通过；WinUI 默认透明区 FAIL，但其公开 region 输入候选在当前限定矩阵中 18/18 通过；普通最大化/无边框共存 4/4 通过；配置恢复 6/6 通过；120/240Hz 显示模式和窗口生存测试通过；DWM 全局计时可被动读取。WinUI tray Show/Exit 路由单测 3/3 通过；应用侧性能分位数统计单测通过。

本次视觉候选矩阵捕获 18/18、完成审阅 18/18，视觉 PASS 0、FAIL 18。三种候选的真实背景模糊都没有显示出来，矩形宿主边缘仍存在；静态图标内容完整。WPF 的 DWM 属性调用成功不等于视觉 blur 成功。WinUI Desktop Acrylic 已启用但未在当前配置显示可辨 blur；失败原因尚未单独隔离。区域候选的静态圆角剪影当前最清楚，但不满足玻璃材质验收。

仍阻止验收的事项：三条视觉候选的真实背景模糊与干净边缘均未通过；WPF 在独占全屏时保持 DXGI exclusive 但前台 PID 为 Dock；WinUI 启动后成为前台并使 DXGI exclusive 退出；独占下实际游戏输入与退出后的自动恢复未测；WinUI tray 实际键盘/鼠标选择与辅助功能路径未测；100/150/200% DPI 和多屏未测；应用计时 trace、独立进程 Present 与面板 scanout 未取得；正式许可证继续待权利人决定。当前已完成的视觉捕获不证明应用动画性能。

**结论：M0 不通过，M1 不开始，技术路线不冻结。** 本次视觉 FAIL 不足以判定 WPF 或 WinUI 框架本身不可能；它否定的是当前 WPF layered HWND + DWM backdrop 和当前 WinUI Desktop Acrylic 组合的视觉结果。暂不启动原生 DirectComposition PoC；在重新获批的下一轮中，可将单次 WinUI Composition 背景采样/blur/mask 与 WPF Visual Layer 作为有界候选，按预先定义的停止条件验证。不得在未获得新桌面测试授权时启动新捕获或交互。

## 11. 主要证据路径

- 输入穿透基线：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261022T153139Z\actual-input-results.csv`
- WinUI region 输入候选：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261023T033334Z\actual-input-results.csv`
- 36 张基线 WGC 捕获：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261022T154558Z\；本轮 18 场景玻璃候选：D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261023T091558Z\
- WinUI region 候选视觉：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261023T033440Z\`
- 最大化/无边框共存：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\coexistence\20261022T163228Z\coexistence-results.json`
- 独占 DXGI：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\fullscreen-exclusive\20261023T032605Z\exclusive-fullscreen-results.json`
- 托盘/配置：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\tray-config\`
- 120/240Hz 模式：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\dpi-refresh\refresh-20261022T161000Z\refresh-results.json`
- 本次架构续作本地构建/18 项单测/20 个脚本解析与边界复核：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\architecture-20261023\`
