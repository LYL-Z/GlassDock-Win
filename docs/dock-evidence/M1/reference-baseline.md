# M1 参考基线档案（草案）

状态：**官方资料基线已建立；macOS 实机基线缺失，不能测量或冻结视觉/时序参数。** 本档案由原始 `01_macOS_Dock参考采样与需求冻结.md` 与 `00_总控执行协议.md` 转录并补充本轮状态；不能用 Windows PoC 或 Apple 文档页面替代 Mac 实机观察。

## 目标样本设定

| 字段 | 设定/状态 |
|---|---|
| 目标 macOS 版本 | 原始任务书指定 macOS 27；记录为待采样目标，不声称当前有实机 |
| 实际 macOS 版本/build | `NOT TESTED` |
| Mac 型号/芯片/内存 | `NOT TESTED` |
| 屏幕分辨率/缩放/刷新模式 | `NOT TESTED` |
| Dock 位置/尺寸/放大/自动隐藏 | `NOT TESTED` |
| 外观模式、壁纸与减少透明/动态效果偏好 | `NOT TESTED` |
| 安装应用、地区与固定/近期项 | `NOT TESTED` |
| 原始截图/录屏路径 | 无；未收到可用 Mac 样本 |
| 官方语义资料 | Apple 使用指南、Desktop & Dock 设置、Folders in Dock、HIG，见 [官方资料笔记](official-reference-notes-2026-10-23.md) |
| 样本状态主索引 | [REF-01–07 样本登记表](reference-sample-register.csv) 与 [manifest](reference-manifest.json) |

## Windows 工程 PoC 对照档案

- 仓库当前 M0 为 **NO-GO**；已观测到的 WPF/WinUI 构建和控件/输入 PoC 只作为本工程启动基线，不代表历史实现，更不代表最终 Dock 的性能或视觉。
- M0 背景模糊、焦点、输入、全屏、资源和帧时状态见 [M0 最终决策](../M0/M0-final-technical-decision.md) 与 [风险登记](architecture-risk-register.md)。
- 本轮代码/日志改动提交后，应在新构建报告中记录其最终 commit SHA；这里不把未提交工作树冒充提交基线。

## 采样与参数规则

Mac 实机一旦可用，每个 REF 记录设备/build、显示拓扑、比例、主题/偏好、Wallpaper ID、Dock 位置/大小、应用状态、动作、捕获方法、路径、SHA-256 和使用权利。原始捕获不因创建项目仓库而获得公开许可。未取得设备或样本时，视觉 token 保持 null + `UNVERIFIED`，见 [design-tokens.json](design-tokens.json)。

## Dock 几何与材质对照表

证据按三类记录，禁止相互替代：**Apple 官方资料**只支持其页面实际说明的功能语义；**Mac 实机截图/录屏**才可用于建立目标设备上的像素与时序样本；**Windows 实现值**只描述本项目当前输出。Apple 官方使用指南和 HIG 没有给出可直接套用的 Dock 像素尺寸、圆角半径、间距、模糊核或动画曲线，因此这些数值必须从有元数据的 Mac 样本测量，不能从 Windows 截图反推。来源边界见[官方资料初读记录](official-reference-notes-2026-10-23.md)。

| 对照项 | 可复现的测量定义 | Mac 目标值 | 当前 Windows 实验值/状态 |
|---|---|---|---|
| 背板宽高与图标比例 | 分别量背板可见外边界和图标可见边界；记录 `背板长轴/短轴`、`短轴/图标高度`、图标 DIP 与捕获像素比例 | `NOT TESTED`；按 REF-01/02 采集 | 最新 WGC 窗口帧：背板 299×82 DIP，48 DIP 图标；长短轴比 3.646，短轴/图标高 1.708。捕获为 250% DPI，图标对应 120×120 px。 |
| 圆角半径 | 在无阴影的实体轮廓处拟合圆弧，记录物理 px 和 DIP；分清几何圆角与抗锯齿边缘宽度 | `NOT TESTED` | WPF `CornerRadius=38 DIP`（代码参数；尚未对 Mac 参考拟合，也不是 macOS 测量值）。 |
| 上下左右内边距 | 从背板内边缘量至图标/分隔线可见边界，四向分别记录，不以窗口阴影空白代替 | `NOT TESTED` | 当前 Bottom：XAML 背板内边距 10 DIP 横向、8 DIP 纵向，另有 1 DIP 边框；窗口阴影 gutter 为 22 DIP。 |
| 图标间距 | 按图标实际 Alpha/可见轮廓的最近边界测相邻间隙，同时记录图标画布间距；两者不得混为一项 | `NOT TESTED` | 当前同分区图标可见矩形间距 16 DIP；受控四图标布局含两个语义分区。RGB 原图没有 Alpha，当前矩形不是光学轮廓测量。 |
| 分隔线 | 记录分隔线相对背板长轴的位置、厚度、长度、两侧留白和颜色/透明度 | `NOT TESTED` | 48 DIP 图标时，几何模型使用 9 DIP 分隔槽；当前横向布局为约 1×34.56 DIP 竖线。此为实现参数。 |
| 屏幕边缘距离 | 量背板可见边缘到物理屏幕边缘；另记录 Dock、菜单栏、任务栏/安全区域，不能只按窗口边界比较 | `NOT TESTED` | 当前 Bottom 定位参考 Windows work area；配置为 work area 底部留 8 DIP，背板再内缩 22 DIP gutter，即背板距 work-area 底部 30 DIP。不是 macOS 对照值。 |
| 图标视觉重心 | 在具备真实 Alpha 的源图上按 Alpha 加权求可见像素质心，记录相对图标布局框中心的偏移；对非 Alpha 素材需人工标注可见轮廓并注明估计 | `NOT TESTED` | 当前布局按正方形框居中，未做光学重心补偿；本地 RGB 图标不能直接给出可靠 Alpha 质心。 |
| 阴影和边缘高光 | 固定背景与曝光，记录阴影方向/扩散/衰减曲线和高光的厚度、亮度；避免把 JPEG/WGC 色差当材质 | `NOT TESTED` | WPF 参数为阴影 BlurRadius 18 DIP、Depth 4 DIP、Opacity 0.38；边框为 1 DIP。未与 Mac 实机样本比较。 |
| 浅色/深色材质 | 同一 Dock 位置、尺寸与高对比背景各采浅/深主题；先对齐背景，再分别测透射和边缘/中心对比度 | `NOT TESTED`；无 Mac 样本 | 本次仅有深色 WPF 背板窗口帧；浅色背景下的真实桌面合成未测。 |
| 背景透射与真实模糊 | 保存未遮挡背景参照和 Dock 合成样本；分别报告透射、局部边缘能量/清晰度变化和 blur，不以降低不透明度代替模糊 | `NOT TESTED` | 当前填充是 `#E9` 深色实色刷（约 91.4% 不透明），不是背景模糊。最新捕获只含 Dock HWND，不含它后方的桌面像素，因此不能测跨窗口透射或 blur。 |

当前 Windows 数值来自 2026-10-24 的一次授权窗口级 WGC Visual Smoke，场景为 Bottom、48 DIP、250% DPI；它是 Windows 工程基线，不是 macOS 参考值，也不构成材质或交互验收。Mac 参考数据仍为空；取得获准 Mac 样本后，按 REF-01/02 的元数据和同背景条件补测即可，不应为填满表格猜测数值。
