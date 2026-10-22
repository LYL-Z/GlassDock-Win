# M0 技术路线对比与复评

日期：2026-10-22。本文件记录本机可复核的 M0 证据和缺口，不构成最终框架决定。

## 当前评估

| 维度 | WPF + Win32 互操作 | WinUI 3 + Windows App SDK | Win32 + DirectComposition/Direct2D |
|---|---|---|---|
| 逐像素透明 / 输入穿透 | 在当前 250% 单屏条件下，受控下层窗口实际收到三边透明角单击、双击、从透明区开始的拖动；Dock 内容点击由 Dock 收到 | Bottom/Left/Right 透明角的单击和双击都由 Dock 根窗口收到，下层计数为 0；实测 FAIL | 还没有 PoC；不能声称自动解决透明像素命中 |
| DWM / DirectComposition | 单例 Windows.Graphics.Capture 捕获到 DWM 合成帧；没有独立 DirectComposition 视觉树对比 | 使用 Composition 体系不等于输入穿透或帧时间正确；没有捕获 WinUI 视觉对照 | 可直接控制 HWND 与合成视觉树，实施复杂度和生命周期责任更高 |
| 液态玻璃空间 | 当前截图看到背景透射和阴影；没有观察到背景模糊 | 理论上可使用 Windows Composition 效果；本 PoC 无实测结果 | 可选择 Direct2D/DComp 或其他渲染后端；材质、模糊和设备恢复都要自行实现 |
| 动画性能 / 帧时间 | 只有静态 CPU/内存/GPU 样本；PresentMon ETW 被系统拒绝，没有帧 CSV | 同样缺少真实呈现帧时间 | 未测。更底层不能替代帧跟踪 |
| 高 DPI / 多显示器 / 动态刷新 | 输入仅在 250% 单屏；无 100/150/200%、跨屏、物理刷新时序验证 | 相同缺口；透明输入失败仍未解决 | 可定义坐标/DPI 策略，但工程实现尚不存在 |
| 顶层、任务栏、全屏共存 | 既有窗口探测有置顶属性；任务栏、最大化、无边框或独占全屏恢复未测 | 既有窗口探测有置顶属性；系统共存场景未测 | 可控性可能更强，但未实测；不推导成通过 |
| Windows 11 API 可维护性 | 使用 WPF 和少量 HWND 消息钩子，现阶段代码较小；未来需维护 DPI、DWM、线程与窗口生命周期边界 | 使用 AppWindow、Win32 interop 与 Windows App SDK；需管理运行时/SDK 部署和交互边界 | API 边界最可控，但窗口消息、渲染、DPI、设备丢失、资源生命周期均要自行维护 |
| 后期架构扩展成本 | 适合继续做短期验证；对复杂合成效果和多屏动画是否合适仍未知 | 保留现有 UI/Composition 生态，但输入穿透失败是阻断项 | 初始成本和验证面最大；只有现有路线暴露实质限制才值得用最小 PoC 验证 |

## 本轮输入与视觉证据

- 真实事件接收记录位于 D 盘 .local/evidence/m0-gap/input/20261022T104053Z/actual-input-results.csv。输入由 SendInput 合成，但由实际 Win32 窗口过程和候选 Dock 的事件/按钮日志确认最终接收方；不属于物理鼠标测试。
- WPF 三边透明单击、双击、透明区起始拖动均由下层窗口收到；内容区点击/双击与从内容区发起拖动由 Dock 收到。
- WinUI 三边透明角单击、双击由 Dock 根窗口收到；下层窗口没有相应消息。WinUI 内容区与拖动场景因图标点未定位而未完成，不能推断。
- 唯一 DWM 视觉帧是 WPF、浅色、Bottom、58 DIP、250% DPI、240Hz 报告环境。背景通过 Dock 可见且图案仍清晰，观察到阴影，未观察到模糊；深色和 WinUI 未捕获。
- PresentMon ETW 返回 access denied，没有 frame CSV。CPU/GPU 值是静态 idle 资源样本，不是动画帧率。
- 输入测试脚本会显示全屏受控背景并注入合成鼠标事件，现已加入 PowerShell 高影响 ShouldProcess 确认；没有用户同意时不能再次执行。

## 帧时序测量边界

应用内部更新间隔需要应用自有计时；逐进程呈现提交间隔需 Present/ETW 证据；DWM 的 DwmGetCompositionTimingInfo/DWM_TIMING_INFO 提供全局合成时序与计数，但在 Windows 8.1 起 hwnd 参数必须为 NULL，因此不能按 Dock 单进程解释。[DwmGetCompositionTimingInfo](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmgetcompositiontiminginfo) · [DWM_TIMING_INFO](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_timing_info)。这是一项用户态 API 调查候选，尚未在本机执行验证。

Windows.Graphics.Capture 的 Direct3D11CaptureFrame.SystemRelativeTime 是合成器渲染该帧时的 QPC 时间；它可为捕获帧序列提供时间参考，不等价于应用 Present 调用时间，也不等价于面板实际 scanout。[Microsoft screen capture guidance](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture) · [SystemRelativeTime](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframe.systemrelativetime)。

PresentMon 诊断要求管理员权限或 Performance Log Users 组。没有提权或组成员变更；在获得用户授权前不运行管理员采集。

## 许可证选项比较

这不是路线选型或许可选择；根 LICENSE 当前仍是未授予许可的占位文件。

| 选项 | 主要影响 | 适合的目标 |
|---|---|---|
| MIT | 宽松许可；保留版权和许可声明，提供免责条款 | 易于复用、衍生或商业分发 |
| Apache-2.0 | 宽松许可；含明确专利授权/诉讼终止规则，并有声明、NOTICE 等分发义务 | 宽松使用且希望有明确专利条款 |
| GPL-3.0 | 强 copyleft；分发覆盖作品时需遵循 GPL 相应的源码与再分发义务 | 希望衍生分发继续共享相同许可自由 |
| 暂不公开发行 | 保留权利，不授予公众开源复制、修改、再分发授权 | 尚未决定是否发布 |

MIT 的许可文本和条件见 [OSI MIT license](https://opensource.org/license/mit)；Apache 2.0 的版权、专利授权及再分发条件见 [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0.html)；GPLv3 的目的和 copyleft 概述见 [GNU GPLv3 quick guide](https://www.gnu.org/licenses/quick-guide-gplv3.html)。Apache Software Foundation 说明 Apache-2.0 与 GPLv3 兼容，不代表 GPLv3 与所有旧版 GPL 兼容。[ASF compatibility note](https://apache.org/licenses/GPL-compatibility.html)。本项目最终选择由用户决定；未决定前不修改许可证、不公开分发。

## 决策建议与可证伪下一步

当前不冻结生产路线。WPF 仅作为透明输入行为的临时领先候选；WinUI 透明区真实事件路由是已复现失败项。WPF 还缺深色、多 DPI、多屏、系统共存、有效帧时序等 P0 证据；WinUI 内容点与拖动仍未测。

在继续桌面交互前，用户必须明确同意；本轮仅本地构建与 Core 单元测试通过。优先使用不改变桌面状态的代码检查、构建和 API 调研。获得许可后再按三条 Dock 边和候选补充输入/视觉测试。若 WinUI 的透明输入只能靠不可维护的私有接口规避，或 WPF 在跨 DPI/系统共存中出现不可接受限制，才为两条路线分别定量记录缺陷并决定是否制作原生最小 PoC。

建议的第三路实验必须保持范围小：同一输入探针、同一几何和可比较环境；分别测 HWND 透明输入命中和 DirectComposition 内容绘制；需要真实下层窗口消息而非只查 WindowFromPoint；不预设性能优势。当前证据不足以批准该 PoC，也不足以冻结 WPF。
