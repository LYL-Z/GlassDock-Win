# M0 需求与验收矩阵

更新日期：2026-10-23。此矩阵中的状态与证据是原始 M0 范围下的历史基线，不因阶段变更而改写。范围变更后的归属、风险和后续验收见 [M0 范围变更记录](M0-scope-change-2026-10-23.md) 和 [M0→M1 需求迁移矩阵](M0-to-M1-requirement-migration.md)。状态仅使用 `PASS`、`FAIL`、`BLOCKED`、`NOT TESTED`。`PASS` 仅适用于证据栏明确写出的提交、场景和环境；截图捕获成功、API 可调用或脚本存在，不自动等于产品要求通过。

| ID | 要求 | 证据 / 实际覆盖 | 状态 |
|---|---|---|---|
| M0-01 | 比较 WinUI 3、WPF 与必要时的 Win32 + DirectComposition | [技术路线复评](../../tech-spike-comparison.md) 覆盖实际事件、视觉、全屏、DWM、API 候选和停止条件；最终框架决策仍独立阻塞 | PASS |
| M0-02 | 两候选可构建并能在本机启动 | 最新 Release build 11 项目成功；WPF/WinUI 均参与真实事件、视觉或共存运行 | PASS |
| M0-03 | 同一数据/几何基线覆盖 8 / 24 项 | Core 单测含 8、24 项样本、布局校验、托盘命令路由与性能分位数逻辑，当前 18/18 通过 | PASS |
| M0-04 | 44 / 58 / 72 DIP 图标尺寸 | 两候选在 250% DPI 下均完成这些尺寸的 WGC 捕获；尚未完成逐帧几何和视觉审查、其他 DPI 回归 | BLOCKED |
| M0-05 | Bottom / Left / Right 位置 | 两候选均有三边运行/捕获；三边输入路由的 WinUI region 候选 18/18 通过 | PASS |
| M0-06 | 透明圆角窗口的 DWM 合成效果 | 先前 WPF/WinUI Acrylic 三候选真实 blur 0/18；最终对照中的条纹与 blur panel 同属一个 HWND，证明同窗 Composition 效果链可见 blur，但不证明桌面采样。项目 Dock 未显示下层条纹窗口的采样/模糊（brush 创建 HRESULT 0）。新证据 .local/evidence/m0-final/20261023T111923Z-94bc33e6/；当前 Dock 外部 backdrop integration FAIL，具体根因未定 | FAIL |
| M0-07 | 透明区点击穿透且 Dock 内容仍可交互 | WPF 默认合成输入 18/18 PASS；WinUI 默认透明区 9/9 FAIL；WinUI `SetWindowRgn` 测试候选 18/18 PASS，但候选视觉和 DPI 长期适配未验收 | FAIL |
| M0-08 | Dock 中心命中、内容点击/拖动和置顶 | 最大化/无边框窗口及输入探针中两候选 Dock 中心命中并接收内容输入；范围仅为当前环境 | PASS |
| M0-09 | 100% / 150% / 200% DPI、动态 DPI 和多显示器 | 仅当前 250% DPI 单屏；其他缩放和跨屏迁移未执行 | NOT TESTED |
| M0-10 | 任务栏、最大化、无边框/独占全屏及恢复 | 最大化/无边框共存 4/4 PASS。全屏默认运行中 WPF 前台 PID 等于 Dock PID；WinUI 前台 PID 等于 Dock PID且 DXGI 独占已退出；真实游戏输入和退出后的自动恢复未测。不可见本身不算失败 | FAIL |
| M0-11 | 浅/深背景、中心/圆角透射、模糊、边缘和阴影 | 本轮 18 个授权场景全部完成人工判读：背景图案透射但仍清晰、无可确认 blur；三候选均有矩形宿主边线；静态图标完整。像素级抗锯齿和动态外溢未测。原帧仅在 D 盘 .local | FAIL |
| M0-12 | 60 / 120 / 240Hz 显示模式适配 | 当前设备 120/240Hz 模式切换和候选窗口生存有通过记录；120Hz 后独立看门狗恢复至 240Hz；当前分辨率不支持 60Hz | PASS |
| M0-13 | CPU / 内存资源测量 | 既有 PoC 静态 idle 样本仅作限定性 PASS；新应用计时器现可分 Idle/Animation 每秒记录进程 CPU、working set、private bytes，但未运行新 trace | BLOCKED |
| M0-14 | GPU 利用率及 GPU 内存测量 | 本轮记录器明确输出 null/未测；没有有效 GPU utilization / GPU memory 数据 | NOT TESTED |
| M0-15 | 区分应用更新、进程提交、DWM 和实际屏幕呈现时间 | 已实现 Dock 逻辑更新、WPF/WinUI 框架渲染回调、动画时长及 P50/P95/P99 记录；尚未启动 PoC 生成实测 trace。框架回调不是提交/Present；DWM 仅全局，ETW `StartTrace` access denied，scanout 无数据 | BLOCKED |
| M0-16 | 托盘 Show / Exit | WPF 实际交互 PASS。WinUI 原生菜单及 dispatch 分支存在；命令 router 单测 3/3 PASS。真实选择、键盘导航和辅助功能仍未测试；自动化不能读菜单不是已证实产品缺陷 | BLOCKED |
| M0-17 | 缺失、损坏、非法配置恢复与日志隔离 | WPF/WinUI 各三类输入共 6/6 PASS；日志与配置仅写入每次运行的 D 盘隔离目录 | PASS |
| M0-18 | 分层骨架和核心单测 | 当前 Release 构建 11 项目、0 警告/错误；Core 单测 18/18；退出码均为 0 | PASS |
| M0-19 | 指定基线提交的 GitHub Actions | `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 的 Windows CI run `39767098768` Build/Test 成功；[精确运行](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39767098768) | PASS |
| M0-20 | 当前工作树构建、测试和脚本解析 | D 盘 SDK 10.0.402；2026-10-23 Release build 11 项目、0 warning/error；Core 18/18；PowerShell 20 脚本 0 ParserError；日志位于 `.local/evidence/m0-gap/architecture-20261023/` | PASS |
| M0-21 | 指定 SHA 的 D 盘独立干净克隆验证 | 克隆 HEAD 精确为 `3a6ca88b6925f60ecea5971f8781b7482153ed0c`；独立恢复缓存、Release build 和 11/11 tests 均通过；仅证明该 SHA | PASS |
| M0-22 | 许可证决策与依赖盘点 | 仓库公开、未选项目级许可证；MIT/Apache-2.0/GPL-3.0/暂不授权比较已记录；LICENSE 占位措辞待用户决定且保持未修改 | BLOCKED |
| M0-23 | M0 报告、矩阵、技术比较与 README 状态同步 | 本轮已按修订后的独占全屏判据、自动化限制、两条材质候选和新增性能测量边界更新 | PASS |
| M0-24 | 所有 P0 通过后才能进入 M1 | M0-07 默认 WinUI FAIL；M0-10 默认启动焦点/独占行为 FAIL；M0-11 视觉 FAIL；M0-09、M0-15 等仍未完成 | BLOCKED |
| M0-25 | 交互测试确认、急停、看门狗和恢复 | 最终一次 Composition 脚本的 watchdog、登记进程和桌面核心状态检查通过；全局 `RestorePassed=false`，因为 runner 未回滚为子进程设置的环境变量。脚本已静态修复，未重跑；急停热键/超时未主动验证 | BLOCKED |
| M0-26 | 安全/探针提交的精确 SHA 远程 CI | `b4d78b8be3f47f08febbf261c720e6dc318e170a` 的 Windows CI run `39800785724` Build/Test 成功；[精确运行](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39800785724)；不覆盖当前改动 | PASS |
| M0-27 | WinUI 公开 HWND region 输入候选 | `SetWindowRgn` opt-in 变体三边 18/18 事件测试通过，Bottom / 58 DIP / 浅深 2 帧成功捕获；边缘/shadow/DPI 仍未充分验收 | PASS |
| M0-28 | 真正 DXGI 独占全屏与 Dock 共存 | 允许 Dock 在 DXGI 独占期间隐藏。记录显示 WPF 前台进程为 Dock PID；WinUI 前台为 Dock 且 DXGI 独占退出。故默认启动路径对“不抢焦点/不影响独占”为 FAIL；WPF 实际输入拦截和退出后自动恢复仍 BLOCKED | FAIL |
| M0-29 | 本轮 M0 缺口补测代码/脚本的远程 CI | 提交 `8be36ea111e35875dbc3ff8cab96474a36e6d413` 的 [Windows CI run 39896333410](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39896333410) Build/Test 均成功；Build 0 警告/0 错误，Core 11/11；仅 Actions runtime 弃用提示 | PASS |
| M0-30 | 候选代码构建与非交互单测 | 当前候选本地 Release build 11 项目通过、Core 18/18；精确 SHA 8f6ca23ecb159dd20e71e0ef303a691cda9293ab 远程 Windows CI Build/Test 成功。运行时视觉由 M0-32 单独评定 | PASS |
| M0-31 | 本轮架构探针精确 SHA 远程 CI | `8f6ca23ecb159dd20e71e0ef303a691cda9293ab` 的 [Windows CI run 39924864083](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39924864083)；workflow、build-and-test job、Build、Test 均 completed/success | PASS |
| M0-32 | 三路线 18 场景玻璃视觉捕获与判读 | 用户以约定短语授权后，WGC 捕获 18/18；人工判读 0/18 visual pass、18/18 visual fail。所有场景背景图案仍清晰、矩形宿主边线可见；静态内容完整。证据 .local/evidence/m0-gap/visual/20261023T091558Z/visual-comparison-report.md 与 visual-assessment.csv | FAIL |
| M0-33 | 非激活启动及 Dock 自身实际性能 trace | WPF 非激活启动进程日志存在，但脚本未定位可见 HWND 后中止；结果 BLOCKED（探针限制），WinUI 未执行；无 Dock P50/P95/P99 实际运行数据 | BLOCKED |

最终运行补充（不新增需求 ID）：Composition control 的同窗 blur effect 为 PASS，不证明桌面采样；项目 Dock 外部 backdrop blur 为 FAIL；WPF non-activating 观察 BLOCKED；WinUI non-activating 与 DXGI non-activating 全屏 NOT TESTED。由于首次脚本未通过恢复验收，未运行 companion。证据和明确的 NO-GO 结论见 [M0 最终技术决策报告](M0-final-technical-decision.md)。最终代码/报告提交 `c8b11d170673fdf7b2ea7f7b3b470344f46e75b2` 的 [GitHub Actions run 39946556825](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39946556825) Build/Test 成功；CI 不运行桌面交互验证。

## 验收结论

M0 未通过，M1 不开始，技术路线不冻结。输入证据由 `SendInput` 合成，并以 Dock 事件和独立下层窗口消息确认接收方；不是物理鼠标验收。视觉图像由 Windows.Graphics.Capture 捕获；截图状态 PASS 只代表捕获成功。DWM 数值只代表全局合成时序。系统材质候选已完成本轮 WGC 捕获并视觉 FAIL；non-activating 启动仍只经过编译，不能写作功能通过。当前 250% 单屏结果不外推至其他 DPI、多显示器或设备。

## 建议的 A / B / C 后续分级（等待用户确认）

- **A：架构决定性阻断**：真实玻璃背景模糊在浅/深桌面下的合成效果；Dock 启动不得抢占全屏游戏焦点或使 DXGI 独占退出；Dock 隐藏时的实际游戏输入投递；退出全屏后无需重启的恢复；新应用侧动画/回调时间及资源数据能否在目标刷新模式下保持稳定。任一核心项未验证前不冻结框架、不进入 M1。
- **B：范围可控的后续验证**：WinUI tray Show/Exit 的真实选取、键盘和辅助功能；`SetWindowRgn` 在图标放大、边缘高光、阴影、菜单/拖拽及动态窗口尺寸时的视觉/交互回归；安全脚本急停热键的主动触发验证。状态保持 BLOCKED / NOT TESTED，按原始 M0 要求补齐。
- **C：硬件环境依赖**：真实 100/150/200% DPI、跨屏和多显示器、设备支持的 60/120/240Hz 物理输出组合，以及 HDR/VRR 等扩展设备矩阵。需要外接屏或另一台 Windows 实机；VM 只能补 API 行为，不能代替真实面板刷新/扫描输出。建议在最终 M0 验收前安排一次硬件补测窗口；无法提供设备时需用户明确延期或豁免，当前不自动调整门槛。

上述分级是建议，尚未替代原始 M0 验收矩阵或改变阶段门槛。

测试后最近一次视觉候选恢复检查 14/14 通过；区域输入候选恢复检查 12/12 通过；没有测试进程残留。显示边界、任务栏状态和原前台窗口已校验恢复。

完整结论和证据路径见 [M0 执行报告](M0-report.md)；API 根因、路线比较和停止条件见[技术路线复评](../../tech-spike-comparison.md)。
