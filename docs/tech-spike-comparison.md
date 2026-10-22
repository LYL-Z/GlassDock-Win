# M0 技术路线对照

日期：2026-10-22。此文记录 M0 原型观测与可证伪的下一步实验；不是最终框架决定。

## 候选路线

| 路线 | 窗口、绘制和输入路径 | 初步优点 | 风险和待证伪点 | 当前 M0 观测 |
|---|---|---|---|---|
| WinUI 3 + Windows App SDK Composition | WinUI `Window` / `AppWindow`、Composition 渲染、Win32 窗口互操作 | 原生 Windows UI 控件与 Composition API；有官方 Composition 原生互操作入口和性能指引 | 窗口透明像素是否同时成为输入穿透区须实测；App SDK Runtime、打包/部署与版本矩阵要核实；Composition 的存在本身不证明 60/120Hz 流畅 | 两种图标数均能启动并采集 CPU/内存；当前 DPI 下三边置顶；三个边缘的透明圆角查询均命中 Dock 进程，alpha-hole 查询失败。无可用帧时间数据 |
| WPF + Windows Composition / Win32 互操作 | WPF 无边框透明窗口、DWM 合成；用 HWND 消息对透明区域返回 `HTTRANSPARENT`，可在以后按需互操作 Composition | WPF 窗口和输入处理可以直接通过当前原型探测；现有 .NET/WPF 工具链容易在本机无打包启动 | 透明窗口、DPI 变化、焦点/置顶、桌面合成和动画成本仍需真实帧跟踪；WPF 与额外原生互操作的维护边界需要评估 | 当前 DPI 下三边置顶；三个边缘透明圆角的 `WindowFromPoint` 查询均不归 Dock 进程；同条件资源数据可采集。尚未实测帧时间、跨 DPI、全屏行为和真实底层点击 |
| 原生 Win32 + DirectComposition + Direct2D 或 Skia | HWND 管理输入和窗口生命周期；DirectComposition 管合成视觉树；Direct2D 或 Skia 绘制 | 窗口样式、命中测试和绘制生命周期可细分控制；可将每个 API 边界单独测量 | 不能预设它更快或自动解决 alpha hit testing；需要自行承担窗口/消息/焦点/DPI/设备丢失、渲染后端、资源释放、测试和发布；Skia 还引入本机二进制与许可证盘点 | 尚无可运行第三路 PoC，当前仅有官方 API 文档分析；不参与本轮实测排名 |

WinUI 官方原生 Composition 互操作文档说明了 WinUI 与原生 Composition 的集成入口；官方性能文档也把实际布局、渲染和启动成本作为需测问题，而非由框架名称推定。[Composition 原生互操作](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-native-interop) · [WinUI 性能建议](https://learn.microsoft.com/en-us/windows/apps/develop/performance/winui-perf)

DirectComposition 将视觉内容组织到视觉树并与 DWM 合成；它不替代 HWND 输入命中测试。原生路线必须将两者分开验证。[DirectComposition 架构与组件](https://learn.microsoft.com/en-us/windows/win32/directcomp/architecture-and-components) · [DirectComposition 基本概念](https://learn.microsoft.com/en-us/windows/win32/directcomp/basic-concepts)

## 同条件原型基线

- 两个可执行 PoC 使用相同的 `GlassDock.Core` 几何、8/24 占位项目和 44/58/72 DIP 尺寸选项；三个边缘均以 8 项、58 DIP 启动并测量窗口位置、置顶样式和中心/透明角落命中。
- 资源测量为静态 idle 样本。CPU 百分比按整机逻辑处理器数归一化；Working Set 和 Private Bytes 来自进程计数。结果见 [M0 报告](dock-evidence/M0/M0-report.md) 和 [汇总 CSV](dock-evidence/M0/runtime-summary.csv)。
- 240Hz 模式下采集 8 与 24 项、58/72 DIP；补充 24 项、44 DIP 采样。120Hz 只测 8/24 项、58 DIP 的 CPU/内存。GPU 计数器仅在 240Hz 静态场景采样。各项短 idle 数据不代表动画峰值或性能排序。
- 当前 3840×2400 模式由 Windows 显示设置接口报告为 240Hz，窗口 DPI 为 240（相对 96 DPI 的 250%）。同分辨率的 120Hz 模式可切换并已恢复；60Hz 测试模式被 Windows 报告为不支持。并未实测 100/150/200% 缩放。
- PresentMon 2.6.0 的本地二进制哈希已与官方发布资产核对，但 ETW 会话启动返回 `access denied`。没有帧时间数据，未尝试提权或修改用户组。

## 第三路线的可证伪实验

若继续探索 Win32 路线，先制作一次性 `Win32 + DirectComposition + Direct2D` PoC；如选择 Skia，则锁定具体 GPU/软件后端并把二进制、许可证和包体一并计入。统一沿用两个既有 PoC 的数据模型、图标尺寸、样本数和测试脚本。以下是建议的工程验收线，不是 Microsoft 的保证或任务书的官方标准：

1. **输入与透明边界**：在三个停靠边、每种 DPI 下分别对 100 个内容点和 100 个透明点执行 `WindowFromPoint`，内容点 100/100 命中 Dock，透明点 0/100 命中 Dock；再用受控底层窗口实际点击每个透明区至少 20 次，全部由底层窗口收到。
2. **DPI 与几何**：分别在 100%、150%、200% 和跨屏迁移后测屏幕物理矩形，边缘位置误差不超过 1 个物理像素；切换 8/24 项及 44/58/72 DIP 后没有越界、裁切或错误方向。
3. **窗口状态**：对任务栏自动隐藏/可见、独占全屏和无边框全屏分别检查遮挡、激活、恢复与边缘占位；任一状态丢失输入、覆盖任务栏或不能恢复即否证。
4. **帧与资源**：每配置至少运行 5 轮 × 30 秒，有可复核 ETW/PresentMon 帧记录；在 60 和 120Hz 下，p95 帧间隔不高于目标刷新周期，错过刷新周期的帧少于 1%，同时记录 CPU、专用/共享 GPU 内存、Working Set 和 Private Bytes。若采样权限或刷新模式不可用，结果为未验证，不能算通过。
5. **比较规则**：与 WPF/WinUI 同机器、同窗口覆盖面积、同刷新率、同动画和相同样本做交错顺序测试；只有当输入/窗口要求全部通过且至少一项关键瓶颈显著改善、没有另一项明显退化，才保留第三路线。否则淘汰该实验版本。

在现有 alpha-hole 失败和帧时序缺失前，不以“更底层”推定 Native 路线胜出，也不冻结生产框架。
