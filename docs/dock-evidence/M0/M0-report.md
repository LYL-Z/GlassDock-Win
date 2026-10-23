# M0 缺口补测与验收报告

- 更新日期：2026-10-22（测试记录使用 UTC 文件夹名，部分时间显示为 2026-10-23 UTC）
- 仓库：`D:\MacOS\GlassDock-Win`
- 当前结论：**M0 未通过；不进入 M1；不冻结 WPF、WinUI 3 或 Win32 + DirectComposition。**
- 当前工作树起始 HEAD：`a1419b7a8be8e6f90a93bd5311cab868797034b6`。本报告记载其后的未提交 M0 代码和文档改动；没有把这些改动说成已通过远程 CI。
- 本次实际执行了已获批准的输入路由、WGC 视觉、窗口共存、显示刷新、托盘/配置恢复及 DXGI 独占全屏测试。未提权、未启动 M1、未改 LICENSE、未创建 Release。

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

基线矩阵位于 `D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261022T154558Z\`：36/36 次 WGC 捕获成功，组合为 WPF/WinUI 3 × 浅/深受控背景 × Bottom/Left/Right × 44/58/72 DIP。图像和元数据均在各场景目录中。这里的浅/深是测试背景颜色，不是切换 Windows 主题；所有样本来自当前 250% DPI、3840×2400 主屏。捕获成功只说明拿到了图像，不表示视觉要求通过；代表性图片经人工检查，尚未逐张完成视觉验收。

观察结果：

- WPF 代表样本中背景格线能透过 Dock，圆角和阴影可见；格线边缘依然清晰，没有可确认的背景模糊或液态玻璃效果。浅色样本：[WPF light / Bottom / 58 DIP](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/visual/20261022T154558Z/Wpf-Light-Bottom-58-DIP/composed-frame.png)，深色样本：[WPF dark / Bottom / 58 DIP](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/visual/20261022T154558Z/Wpf-Dark-Bottom-58-DIP/composed-frame.png)。
- WinUI 3 基线代表样本显示灰色半透明面板和显眼的浅色矩形宿主边框；背景细节可见，但没有可确认的背景模糊。代表帧：[WinUI light / Bottom / 58 DIP](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/visual/20261022T154558Z/WinUI-Light-Bottom-58-DIP/composed-frame.png)。
- 对启用圆角 region 的 WinUI 变体另捕获浅/深两帧，均为 Bottom / 58 DIP / 250% DPI，WGC 2/2 成功。Dock 内容仍完整，窗口区域外缘可见的矩形宿主/阴影轮廓仍需处理，未观察到目标级背景模糊。证据：[候选视觉矩阵](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/visual/20261023T033440Z/actual-visual-results.json)、[light](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/visual/20261023T033440Z/WinUI-Light-Bottom-58-DIP/composed-frame.png)、[dark](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/visual/20261023T033440Z/WinUI-Dark-Bottom-58-DIP/composed-frame.png)。

结论：透明透射、模糊、边缘、阴影是不同指标。当前证据支持部分透射和阴影观察，不支持背景模糊或液态玻璃目标已通过；完整视觉验收 **FAIL/BLOCKED**。

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

独占全屏使用当前模式建立 DXGI swap chain，并确认 `SetFullscreenState(true)` / `IsFullScreen`。设备模式测试前后均为 3840×2400@240Hz，未改变分辨率、DPI 或刷新率；恢复校验通过。实际观察：

- WPF 启动后 DXGI 仍报告独占；Dock HWND 报告可见且置顶，`WindowFromPoint` 在 Dock 中心解析到 Dock 进程，但保存的 WGC 主屏帧中看不到 Dock。测试没有在该场景注入输入，因此“Dock 不可见时是否仍拦截游戏输入”未验证，列为 **BLOCKED**。
- WinUI 3 Dock 启动后 DXGI 独占状态变为 false；WGC 组合帧里 Dock 可见，但外缘有浅色矩形宿主边框。该受控程序不是游戏，不能判定所有独占全屏应用行为，列为 **BLOCKED**。

证据：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\fullscreen-exclusive\20261023T032605Z\exclusive-fullscreen-results.json`、[WPF 帧](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/fullscreen-exclusive/20261023T032605Z/Wpf/after-dock-monitor.png)、[WinUI 帧](D:/MacOS/GlassDock-Win/.local/evidence/m0-gap/fullscreen-exclusive/20261023T032605Z/WinUI/after-dock-monitor.png)。捕获帧不是面板 scanout；状态矛盾不能判为全屏共存通过。

托盘与配置：

- WPF 真实托盘图标右键菜单的 Show 和 Exit 均通过；测试确认先隐藏，再通过 Show 恢复，Exit 结束测试进程。
- WinUI 3 托盘图标注册及右键弹出菜单窗口均确认；但菜单项无法通过 UI Automation、MSAA 或标准 `GetMenu` 读取，因此未猜坐标，Show/Exit **BLOCKED**。
- 缺失、损坏 JSON、非法 Edge 配置在 WPF/WinUI 各自隔离状态根下共 6/6 PASS：进程存活、默认 Bottom 被持久化，损坏 JSON 写出错误日志。未触碰用户真实配置。

证据目录：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\tray-config\`；配置矩阵 `20261023T030101Z`，WPF 托盘 `20261023T030531Z`，WinUI 菜单可访问性复核 `20261023T031135Z`。

少数旧运行的根级摘要文案与逐项记录不一致；原始文件保持不变，勘误按 `Tests`/`CaptureStatus`/样本字段和实际截图解读。详见 `D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\evidence-interpretation-errata.md`。

## 6. DWM 和帧时间

被动探针脚本 `scripts/measure-dwm-composition.ps1` 调用 `DwmGetCompositionTimingInfo(hwnd=NULL)`，不创建可见窗口、不改变显示模式、不注入输入，也未提权。当前复测约 4 秒、16/16 成功调用，HRESULT 全为 `S_OK`，全局 Refresh/Compose 约 240.004 Hz。日志：`D:\MacOS\GlassDock-Win\.local\evidence\final-validation\resume-20261023T032925Z\dwm-composition.jsonl`。

这只是 DWM 全局合成时间，不归属到 Dock 进程，不是应用逻辑更新间隔、逐进程呈现提交间隔，也不是面板 scanout。WGC 目前保存单帧，不能从单帧计算帧间隔。PresentMon/ETW 仍因 `StartTrace` access denied 无有效逐进程 CSV；没有使用管理员权限或修改账户组。因此 120Hz/240Hz 流畅度要求仍 **BLOCKED**，不得声称达到目标。

## 7. 本地构建、测试、CI 与克隆

最新本地构建和测试使用仓库 D 盘 SDK `10.0.402`；`TEMP/TMP`、`DOTNET_CLI_HOME`、`NUGET_PACKAGES`、NuGet 缓存、bin/obj 均在 D 盘。

| 验证 | 结果 |
|---|---|
| 当前工作树 Release solution build | PASS；11 个项目，0 警告、0 错误，退出码 0；日志 `D:\MacOS\GlassDock-Win\.local\evidence\final-validation\resume-20261023T032851Z\build-after-readiness-fix.log` |
| Core 单元测试 | PASS；11/11，0 失败、0 跳过，退出码 0；同目录 `test-after-readiness-fix.log` |
| PowerShell 静态解析 | PASS；20 个脚本，0 ParserError；结果 `resume-20261023T032925Z\powershell-parse-summary.txt` |
| DWM 被动探针 | PASS；16/16 `S_OK`；仅全局合成指标 |
| 桌面测试恢复 | 最近一次视觉候选恢复检查 14/14 PASS；点击路由候选 12/12 PASS；无测试进程残留 |

使用命令：

```powershell
.\.tools\dotnet\dotnet.exe build GlassDock-Win.sln --configuration Release --no-restore -p:ContinuousIntegrationBuild=true --disable-build-servers
.\.tools\dotnet\dotnet.exe test GlassDock-Win.sln --configuration Release --no-build --no-restore --logger 'console;verbosity=normal' --disable-build-servers
```

指定提交 `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 在 D 盘独立干净克隆中完成 locked restore、Release build 和 Core 11/11；该结果仅适用于该 SHA，不代表当前改动。路径：`D:\MacOS\GlassDock-Win\.local\clean-clones\3a6ca88b6925f60ecea5971f8781b7482153ed0c`。

远程 CI 已核实的精确提交：基线 `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 的 [Windows CI run 39767098768](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39767098768) 成功；实现提交 `b4d78b8be3f47f08febbf261c720e6dc318e170a` 的 [run 39800785724](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39800785724) 成功。Build 和 Test 均成功的声明只针对各自 SHA；当前未提交改动没有远程 CI 结果。

## 8. WinUI 与 WPF 技术结论

WinUI 默认透明区失败日志表明事件到达覆盖全窗的透明 `LayoutRoot`；处理器没有标记 `Handled`，证据更符合“矩形 HWND 命中加可命中的 XAML 根元素”截获，而非按钮吞掉已处理事件。`SetWindowRgn` 公开候选已让三边透明区下传，同时保留内容点击和拖动；其代价是实际窗口形状和阴影受 region 裁剪，测试只覆盖当前尺寸/DPI，不能直接作为最终设计。`WS_EX_TRANSPARENT` 会让整个 layered window 的输入下传，不能单独满足 Dock 内容交互；`HTTRANSPARENT` 对跨线程下层转发的保证不足。

WPF 默认实现的输入证据最好，但全屏测试中出现“DXGI 仍独占、Dock 命中却未在 WGC 帧里出现”的未决状态。两候选都有真实背景透射样本，但没有达到目标模糊；WGC/DWM 测量也没有提供 Dock 自身帧时间。WinUI 默认输入失败现在有一个公开 API 实验通过，但需解决视觉边缘、动态 DPI 和长期维护验证。**当前证据不支持冻结任何框架。** 若公开 WPF/WinUI 路线在满足输入和玻璃视觉之间出现不可调和限制，再以严格范围验证原生 Win32 + DirectComposition；目前没有必要仅为“更底层/更新”而启动该 PoC。

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

当前已证实：WPF 默认点击路由 18/18 通过；WinUI 默认透明区 FAIL，但其公开 region 候选在当前限定矩阵中 18/18 通过；两套程序均取得真实 WGC 帧；配置恢复 6/6 通过；普通最大化/无边框共存 4/4 通过；120/240Hz 模式及窗口生存测试通过；DWM 全局计时可被动采集；指定旧提交的远程 CI 和干净克隆均通过。

仍阻止验收的事项：缺少达到目标的背景模糊/液态玻璃视觉；WPF 独占全屏时 HWND 命中与合成帧可见性不一致；WinUI 启动后退出 DXGI 独占；WinUI 托盘 Show/Exit 无法选择；100/150/200% DPI 和多屏未测；Dock 自身更新/呈现帧时间未测；许可证未决；当前工作树的新改动尚无远程 CI。

**结论：M0 不通过，M1 不开始，技术路线不冻结。** 设备缺项要在有合适外接屏/测试机时补证，不能写为 PASS；架构级问题要继续依照已批准范围验证或明确停止条件。

## 11. 主要证据路径

- 输入穿透基线：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261022T153139Z\actual-input-results.csv`
- WinUI region 输入候选：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\input\20261023T033334Z\actual-input-results.csv`
- 36 张基线 WGC 捕获：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261022T154558Z\`
- WinUI region 候选视觉：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\visual\20261023T033440Z\`
- 最大化/无边框共存：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\coexistence\20261022T163228Z\coexistence-results.json`
- 独占 DXGI：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\fullscreen-exclusive\20261023T032605Z\exclusive-fullscreen-results.json`
- 托盘/配置：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\tray-config\`
- 120/240Hz 模式：`D:\MacOS\GlassDock-Win\.local\evidence\m0-gap\dpi-refresh\refresh-20261022T161000Z\refresh-results.json`
- 当前本地构建/测试/脚本解析：`D:\MacOS\GlassDock-Win\.local\evidence\final-validation\resume-20261023T032851Z\` 与 `resume-20261023T032925Z\`
