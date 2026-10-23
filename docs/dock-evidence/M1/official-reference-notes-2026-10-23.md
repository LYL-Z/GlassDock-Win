# Dock 官方资料初读记录

- 查询日期：2026-10-23
- 目标资料基线：原任务书所记 macOS 27 Golden Gate 用户指南；官方页面同时提供 macOS 26 Tahoe 等版本选择。
- 实机采样：**没有 Mac 实机、macOS build 档案、用户偏好档案、截图或录屏。所有 Mac 视觉/动作观察均为 NOT TESTED。** 本文归纳的是官方公开页面，不是本机观察。
- 代码/素材：本轮未复制 Apple 图标、截图或品牌文件进仓库；许可证文件未修改。

## 官方页面支持的行为事实

1. Apple 用户指南描述 Dock 可沿底部、左侧或右侧显示；默认可包含最多三个尚未在 Dock 中的近期应用，以及 Downloads 文件夹。页面也记载通过拖动添加/移除/重排条目、点击启动、拖文件到应用图标、通过 Command-click 在 Finder 中显示项目，以及 Option 组合操作。详见 [Use the Dock on Mac](https://support.apple.com/en-gb/guide/mac-help/mh35859/27/mac/27)。
2. Desktop & Dock 设置页面记载 Dock 大小、放大、位置、最小化动画、最小化到应用图标、自动隐藏/边缘唤出、打开应用时图标弹跳、正在打开应用指示点和建议/最近应用开关。页面没有给出可移植的尺寸、弹簧参数、像素半径或逐帧时序。详见 [Change Desktop & Dock settings on Mac](https://support.apple.com/en-ie/guide/mac-help/mchlp1119/27/mac/27)。
3. 文件夹官方页面记载 Downloads 默认项、文件夹/Stack 呈现、Fan/Grid/List 展开方式及多种排序选择。详见 [Use folders in the Dock on Mac](https://support.apple.com/en-gb/guide/mac-help/mchl231f08fb/27/mac/27)。
4. Apple HIG 的 Dock 菜单指导说明次级点击可列系统与应用提供的命令，菜单项可能随 app 是否打开而改变，并建议关键命令在其他位置也可用。详见 [Dock menus](https://developer.apple.com/design/human-interface-guidelines/dock-menus)。
5. Apple HIG 对 layered app icons 的说明指出，系统可对图标层动态施加高光、折射、半透明等视觉效果；视觉参数受平台和系统版本影响。它是图标设计指导，不是 macOS Dock 背板的渲染规格。详见 [App icons](https://developer.apple.com/design/human-interface-guidelines/app-icons)。Apple 的 [Materials HIG](https://developer.apple.com/design/human-interface-guidelines/materials) 将 Liquid Glass 描述为动态的控件/导航功能层，强调内容透出和层级；其 regular variant 会模糊并调整背景亮度，clear variant 更强调透过内容。但这是一般材料指导，不给 Dock 专用的 blur、边缘参数、尺寸或动画数值（A11）。
6. Apple 全屏帮助页说明：在 app 的 macOS 全屏模式中，可把指针移至 Dock 所在边缘显示/隐藏 Dock；最大化窗口与全屏不同，帮助页说最大化后 Dock 仍可见。此语义用于 Windows 分类参考，不表示本项目要覆盖 Windows DXGI 独占画面。详见 [Use apps in full screen on Mac](https://support.apple.com/en-gb/guide/mac-help/mchl9c21d2be/mac)。
7. Apple 通知设置页说明 app 可选择在 Dock 图标上显示通知数 badge；具体通知来源、分组和隐私偏好另由通知设置控制。详见 [Notifications settings on Mac](https://support.apple.com/en-ie/guide/mac-help/mh40583/27/mac/27)。
8. Apple 的 Dock 概览页描述固定/近期分隔、运行指示点、红色 badge、添加/移除和位置/大小入口。详见 [Get to know your desktop, menu bar, and Dock](https://support.apple.com/guide/mac-help/desktop-menu-bar-and-dock-mchlws12345m2/27/mac/27)。

9. Apple AppKit NSImage 名称文档列出 NSImageNameTrashEmpty 与 NSImageNameTrashFull 两种标准图像名；这证明有对应系统资源标识，但**不能单独证明 Dock 在具体系统版本中的切换时序、图像细节或默认素材**。详见 [NSImageName](https://developer.apple.com/documentation/AppKit/NSImage/Name-swift.typealias)。
10. Apple 文件删除帮助说明，拖到 Trash/Bin 的内容在清空前仍可恢复，清空时显示警告，并说明清空后永久删除；这支持“先可恢复、清空为破坏性操作”的语义，不证明 Windows 实现方式。详见 [Delete files and folders on Mac](https://support.apple.com/en-ie/guide/mac-help/mchlp1093/27/mac/27)。
11. Apple Platform Deployment 的 Dock 管理 payload 将 Genie 与 Scale 列为可设置的 minimize effect，并列出 size、magnification、position、auto-hide 等配置字段。该页证明管理配置可表达这些选项，不证明动画曲线、视觉输出、一般用户默认值或 Windows 实现能力。详见 [Dock device management payload settings](https://support.apple.com/guide/deployment/dock-payload-settings-depef1fdf19/1/web/1.0)。
12. Apple 用户指南明确说明 Light/Dark/Auto 外观应用于菜单栏、Dock、窗口和内置应用；它不定义独立的 Dock 调色板或具体材质效果。Windows 跟随系统主题是产品提案。详见 [Use a light or dark appearance on your Mac](https://support.apple.com/en-gb/guide/mac-help/mchl52e1c2d2/27/mac/27)。

## 原生功能分区、默认项与 Windows 提案边界

- Apple 用户指南称 Dock 可以包含最多三个未固定的近期 App 与 Downloads 文件夹，并用 Safari/Bin 作为常用 App/功能例子、Finder 作为打开 App 示例。该页面并未给出适用于所有系统版本、地区、安装软件和用户偏好的完整“默认图标及顺序”清单。
- Apple 来源描述固定 App、近期 App、文件/文件夹的分隔关系；本项目把它们抽象为“固定 App 区 / 可选近期区 / 文件与文件夹区”三个语义区。此区模型是产品规格归纳，分隔线的精确样式、轴向、位置和每台 Mac 的当前默认项仍待 REF 实机证据。
- Windows 首次运行元素均为产品建议：Explorer 的文件管理角色、用户自行固定的 App、建议默认关闭的近期区、Windows Known Folder 的 Downloads、Windows Recycle Bin、用户主动添加的文件夹，以及默认隐藏的 Handoff。任何默认设置和图标排序尚未由负责人批准。
- 对 Windows 本机 App 优先调用系统图标/身份能力，不随包附带 Apple 或未经许可的第三方品牌资源；Finder 只作为语义比较对象，不作 Windows 品牌或图像资源。

## 对产品规格的含义（工程提案，不冒充 Apple 事实）

- Dock 的对象至少需区分固定 App、建议/近期 App、文件/文件夹、Downloads、Trash、最小化窗口代理及可选临时跨设备项。正式布局需 Mac 实机确认分隔区端点、顺序、图标基线和不同屏幕的边界行为。
- “打开应用”“激活现有窗口”“显示其全部窗口”“将文件投给应用”是不同命令。Windows 映射需记录成功/拒绝/权限边界，不能将单纯启动进程等价于前台切换。
- Finder、Force Quit、Handoff、Genie 等专属语义必须明确写成 Windows 映射/近似或不实现，不宣称 Apple 原生组件。
- Apple 官方页面仅给语义与设置能力；几何、色差、模糊核、放大响应函数、弹跳曲线、菜单内容排序与时序都保留为 `unverified`，等待实际设备取样。
- 项目代码公开与使用 Apple 商标、系统图标或专有素材是分开的决策；本轮仅登记权利风险，不做复制或再分发判断。

## 版本和来源维护

原始 ZIP 的功能矩阵在文内记载查询日期 2026-10-22、目标基线 macOS 27 Golden Gate。本次重新打开 Apple 官方页面复核了其当前公开语义。网页当前列出 macOS 27 版本，但本项目没有 macOS 27 实机 build 记录。后续采样必须记录系统 build，且不能把官方帮助页面当作某一台机器默认 Dock 截图。

Microsoft 侧相关公开实现边界另见 [M0 最后技术阻断项](../M0/M0-remaining-blockers-and-stop-criteria.md)：WinUI 3 `AcrylicBrush` 只作窗内 Acrylic；官方桌面背景材料走 `Window.SystemBackdrop` / `SystemBackdropElement`；Composition backdrop effect 链属于公开能力，但项目 Dock HWND 是否取得外部窗口像素必须看本项目的实际合成捕获。

## CSV 引用编号

| 编号 | 官方来源 | 支持范围与边界 |
|---|---|---|
| A1 | [Use the Dock on Mac](https://support.apple.com/en-gb/guide/mac-help/mh35859/27/mac/27) | Dock 位置、近期最多三项、点击/拖放/快捷菜单/命令键操作、添加/移除/重排、Handoff、badge；不提供像素参数 |
| A2 | [Change Desktop & Dock settings on Mac](https://support.apple.com/en-ie/guide/mac-help/mchlp1119/27/mac/27) | 大小、放大、三边位置、最小化、自动隐藏/边缘唤出、启动动画、指示点、近期项开关，以及每显示器 Spaces 设置下 Dock 的可见范围 |
| A3 | [Use folders in the Dock on Mac](https://support.apple.com/en-gb/guide/mac-help/mchl231f08fb/27/mac/27) | Downloads、Folder/Stack、Fan/Grid/List 和排序 |
| A4 | [App icons HIG](https://developer.apple.com/design/human-interface-guidelines/app-icons) | 图标分层和系统视觉效果设计指导；不代表 Dock 背板规格 |
| A5 | [Dock menus HIG](https://developer.apple.com/design/human-interface-guidelines/dock-menus) | 次级点击菜单的系统/自定义命令语义与设计指导 |
| A6 | [Apple licensing and trademarks](https://www.apple.com/legal/intellectual-property/) | Apple 商标/知识产权的权利核查入口；不构成本项目素材授权 |
| A7 | [Use apps in full screen on Mac](https://support.apple.com/en-gb/guide/mac-help/mchl9c21d2be/mac) | macOS 全屏时通过屏幕边缘显示/隐藏 Dock；最大化与全屏行为不同 |
| A8 | [Notifications settings on Mac](https://support.apple.com/en-ie/guide/mac-help/mh40583/27/mac/27) | 可由 app 通知设置控制 Dock 图标 badge |
| A10 | [Get to know your desktop, menu bar, and Dock](https://support.apple.com/guide/mac-help/desktop-menu-bar-and-dock-mchlws12345m2/27/mac/27) | 固定/近期分隔、打开状态点、badge、添加/移除基本语义 |
| A11 | [Materials HIG](https://developer.apple.com/design/human-interface-guidelines/materials) | 一般 Liquid Glass 材料指导包括背景模糊/亮度调整与内容可见性；不是 Dock 背板规范，不提供 Dock 的具体着色、模糊半径或光学参数 |
| A12 | [Apple Design Resources License](https://developer.apple.com/support/downloads/terms/apple-design-resources/Apple-Design-Resources-License-20230621-English.pdf) | Apple 官方设计资源的许可只明确给 Apple 操作系统软件界面 mockup 使用，并限制嵌入/再分发；它不授权本 Windows 项目使用 Apple 资源 |
| A13 | [NSImageName](https://developer.apple.com/documentation/AppKit/NSImage/Name-swift.typealias) | AppKit 标准图像名称包含 Trash empty/full 资源标识；只支持资源名存在，不代表已观察 Dock 的状态切换或外观 |
| A14 | [Delete files and folders on Mac](https://support.apple.com/en-ie/guide/mac-help/mchlp1093/27/mac/27) | Trash/Bin 的可恢复与清空确认语义；清空后删除不可恢复 |
| A15 | [Dock device management payload settings](https://support.apple.com/guide/deployment/dock-payload-settings-depef1fdf19/1/web/1.0) | 管理配置可表达 Genie/Scale、位置、尺寸和放大等级等 Dock 设置；不证明动画画面、时序、默认设置或第三方实现 |
| A16 | [Use a light or dark appearance on your Mac](https://support.apple.com/en-gb/guide/mac-help/mchl52e1c2d2/27/mac/27) | Light、Dark、Auto 外观应用于菜单栏、Dock、窗口和内置应用；不定义独立 Dock 调色板或材质细节 |

## 逐条来源审查边界

矩阵 `EvidenceScopeReview` 对 DOCK-001–046 逐项写明：被引用页面实际支持的语义、页面没有支持的部分、Windows 映射是否只是产品提案。`OfficialSourceStatus` 只区分直接语义、部分支持、HIG 指导和来源缺口；它不是该功能的验收状态。Mac 硬件观察列仍对 46 项全部为 `NOT TESTED`。

重点更正：A4/A11 不能作为 Dock 背板的具体 blur/光学参数规格（A11只支持一般材料有背景模糊能力）；A1 的 Finder 示例不能证明 Finder 是通用默认固定项；A13 的 empty/full 图像资源名不能证明 Dock 图标切换状态；A2/A7 的 macOS 全屏说明不能证明 Windows DXGI 独占全屏行为；A15 的 MDM 字段不能用作动画视觉/时序证据；A16 证明 Dock 跟随 macOS Light/Dark/Auto，但不证明独立 Dock 主题或具体材质效果。
