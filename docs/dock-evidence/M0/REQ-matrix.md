# M0 需求与验收矩阵

更新日期：2026-10-22。状态仅使用 `PASS`、`FAIL`、`BLOCKED`、`NOT TESTED`。`PASS` 仅适用于证据栏明确写出的提交、场景和环境；截图捕获成功、API 可调用或脚本存在，不自动等于产品要求通过。

| ID | 要求 | 证据 / 实际覆盖 | 状态 |
|---|---|---|---|
| M0-01 | 比较 WinUI 3、WPF 与必要时的 Win32 + DirectComposition | [技术路线复评](../../tech-spike-comparison.md) 覆盖实际事件、视觉、全屏、DWM、API 候选和停止条件；最终框架决策仍独立阻塞 | PASS |
| M0-02 | 两候选可构建并能在本机启动 | 最新 Release build 11 项目成功；WPF/WinUI 均参与真实事件、视觉或共存运行 | PASS |
| M0-03 | 同一数据/几何基线覆盖 8 / 24 项 | Core 单测含 8、24 项样本及布局校验，当前 11/11 通过 | PASS |
| M0-04 | 44 / 58 / 72 DIP 图标尺寸 | 两候选在 250% DPI 下均完成这些尺寸的 WGC 捕获；尚未完成逐帧几何和视觉审查、其他 DPI 回归 | BLOCKED |
| M0-05 | Bottom / Left / Right 位置 | 两候选均有三边运行/捕获；三边输入路由的 WinUI region 候选 18/18 通过 | PASS |
| M0-06 | 透明圆角窗口的 DWM 合成效果 | 36/36 基线 WGC 捕获成功；WPF 有透射和阴影但无可确认背景模糊；WinUI 有浅色宿主边框；region 候选另有 2 帧 | BLOCKED |
| M0-07 | 透明区点击穿透且 Dock 内容仍可交互 | WPF 默认合成输入 18/18 PASS；WinUI 默认透明区 9/9 FAIL；WinUI `SetWindowRgn` 测试候选 18/18 PASS，但候选视觉和 DPI 长期适配未验收 | FAIL |
| M0-08 | Dock 中心命中、内容点击/拖动和置顶 | 最大化/无边框窗口及输入探针中两候选 Dock 中心命中并接收内容输入；范围仅为当前环境 | PASS |
| M0-09 | 100% / 150% / 200% DPI、动态 DPI 和多显示器 | 仅当前 250% DPI 单屏；其他缩放和跨屏迁移未执行 | NOT TESTED |
| M0-10 | 任务栏、最大化、无边框/独占全屏及恢复 | 最大化/无边框共存 4/4 PASS；DXGI 独占下 WPF/WinUI 表现不一致，未做游戏矩阵；测试后恢复校验通过 | BLOCKED |
| M0-11 | 浅/深背景、中心/圆角透射、模糊、边缘和阴影 | 36 张基线帧及 2 张 region 候选帧；没有路线显示出目标级背景模糊/液态玻璃 | FAIL |
| M0-12 | 60 / 120 / 240Hz 显示模式适配 | 当前设备 120/240Hz 模式切换和候选窗口生存有通过记录；120Hz 后独立看门狗恢复至 240Hz；当前分辨率不支持 60Hz | PASS |
| M0-13 | CPU / 内存资源测量 | 只有先前静态 idle 样本，不代表动画负载 | PASS |
| M0-14 | GPU 利用率及 GPU 内存测量 | 只有先前静态 idle 样本，不代表动画负载 | PASS |
| M0-15 | 区分应用更新、进程提交、DWM 和实际屏幕呈现时间 | 最新 DWM 被动探针 16/16 `S_OK`、全局约 240Hz；无 Dock 自身时间序列；PresentMon/ETW access denied；无面板 scanout 数据 | BLOCKED |
| M0-16 | 托盘 Show / Exit | WPF Show 与 Exit 实测 PASS；WinUI 图标和菜单弹出 PASS，但菜单项无法经 UIA/MSAA/GetMenu 读取或安全选中 | BLOCKED |
| M0-17 | 缺失、损坏、非法配置恢复与日志隔离 | WPF/WinUI 各三类输入共 6/6 PASS；日志与配置仅写入每次运行的 D 盘隔离目录 | PASS |
| M0-18 | 分层骨架和核心单测 | 当前 Release 构建 11 项目、0 警告/错误；Core 单测 11/11；退出码均为 0 | PASS |
| M0-19 | 指定基线提交的 GitHub Actions | `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 的 Windows CI run `39767098768` Build/Test 成功；[精确运行](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39767098768) | PASS |
| M0-20 | 当前工作树构建、测试和脚本解析 | D 盘 SDK 10.0.402；Release build 11 项目、0 warning/error；Core 11/11；PowerShell 20 脚本 0 ParserError | PASS |
| M0-21 | 指定 SHA 的 D 盘独立干净克隆验证 | 克隆 HEAD 精确为 `3a6ca88b6925f60ecea5971f8781b7482153ed0c`；独立恢复缓存、Release build 和 11/11 tests 均通过；仅证明该 SHA | PASS |
| M0-22 | 许可证决策与依赖盘点 | 仓库公开、未选项目级许可证；MIT/Apache-2.0/GPL-3.0/暂不授权比较已记录；LICENSE 占位措辞待用户决定且保持未修改 | BLOCKED |
| M0-23 | M0 报告、矩阵、技术比较与 README 状态同步 | 当前文件已按现有 WGC、输入、共存、全屏、托盘/配置、DWM、构建和许可证证据修订 | PASS |
| M0-24 | 所有 P0 通过后才能进入 M1 | M0-07 FAIL；视觉 FAIL/BLOCKED；M0-09 未测试；M0-10、M0-15、M0-16 阻塞 | BLOCKED |
| M0-25 | 交互测试确认、急停、看门狗和恢复 | 显式短语、高影响确认、独立看门狗、Ctrl+Shift+F12、超时及仅结束本轮进程均已实现；正常/失败路径恢复检查通过；急停热键和超时未被主动触发验证 | BLOCKED |
| M0-26 | 安全/探针提交的精确 SHA 远程 CI | `b4d78b8be3f47f08febbf261c720e6dc318e170a` 的 Windows CI run `39800785724` Build/Test 成功；[精确运行](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39800785724)；不覆盖当前改动 | PASS |
| M0-27 | WinUI 公开 HWND region 输入候选 | `SetWindowRgn` opt-in 变体三边 18/18 事件测试通过，Bottom / 58 DIP / 浅深 2 帧成功捕获；边缘/shadow/DPI 仍未充分验收 | PASS |
| M0-28 | 真正 DXGI 独占全屏与 Dock 共存 | DXGI 进入独占得到确认；WPF 保持独占但 WGC 帧不显示 Dock、命中 API 指向 Dock；WinUI 启动后退出独占。不是游戏矩阵且 WPF 输入未验证 | BLOCKED |
| M0-29 | 本轮 M0 缺口补测代码/脚本的远程 CI | 提交 `8be36ea111e35875dbc3ff8cab96474a36e6d413` 的 [Windows CI run 39896333410](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39896333410) Build/Test 均成功；Build 0 警告/0 错误，Core 11/11；仅 Actions runtime 弃用提示 | PASS |

## 验收结论

M0 未通过，M1 不开始，技术路线不冻结。输入证据由 `SendInput` 合成，并以 Dock 事件和独立下层窗口消息确认接收方；不是物理鼠标验收。视觉图像由 Windows.Graphics.Capture 捕获；截图状态 PASS 只代表捕获成功。DWM 数值只代表全局合成时序。当前 250% 单屏结果不外推至其他 DPI、多显示器或设备。

测试后最近一次视觉候选恢复检查 14/14 通过；区域输入候选恢复检查 12/12 通过；没有测试进程残留。显示边界、任务栏状态和原前台窗口已校验恢复。

完整结论和证据路径见 [M0 执行报告](M0-report.md)；API 根因、路线比较和停止条件见[技术路线复评](../../tech-spike-comparison.md)。
