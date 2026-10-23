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
5. Apple HIG 对 layered app icons 的说明指出，系统可对图标层动态施加高光、折射、半透明等视觉效果；视觉参数受平台和系统版本影响。它是图标设计指导，不是 macOS Dock 背板的渲染规格。详见 [App icons](https://developer.apple.com/design/human-interface-guidelines/app-icons)。Apple 的 [Materials HIG](https://developer.apple.com/design/human-interface-guidelines/materials) 将 Liquid Glass 描述为动态的控件/导航功能层，强调内容透出、流动感和视觉层级；它没有给出 Dock 的具体 blur、尺寸或动画数值（A11）。
6. Apple 全屏帮助页说明：在 app 的 macOS 全屏模式中，可把指针移至 Dock 所在边缘显示/隐藏 Dock；最大化窗口与全屏不同，帮助页说最大化后 Dock 仍可见。此语义用于 Windows 分类参考，不表示本项目要覆盖 Windows DXGI 独占画面。详见 [Use apps in full screen on Mac](https://support.apple.com/en-gb/guide/mac-help/mchl9c21d2be/mac)。
7. Apple 通知设置页说明 app 可选择在 Dock 图标上显示通知数 badge；具体通知来源、分组和隐私偏好另由通知设置控制。详见 [Notifications settings on Mac](https://support.apple.com/en-ie/guide/mac-help/mh40583/27/mac/27)。
8. Apple 的 Dock 概览页描述固定/近期分隔、运行指示点、红色 badge、添加/移除和位置/大小入口。详见 [Get to know your desktop, menu bar, and Dock](https://support.apple.com/guide/mac-help/desktop-menu-bar-and-dock-mchlws12345m2/27/mac/27)。

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
| A11 | [Materials HIG](https://developer.apple.com/design/human-interface-guidelines/materials) | Liquid Glass 是动态材料，强调流动感、边缘光学效果及内容可见性；是跨平台设计指导，不提供 Dock 的具体着色、模糊半径或光学参数 |
| A12 | [Apple Design Resources License](https://developer.apple.com/support/downloads/terms/apple-design-resources/Apple-Design-Resources-License-20230621-English.pdf) | Apple 官方设计资源的许可只明确给 Apple 操作系统软件界面 mockup 使用，并限制嵌入/再分发；它不授权本 Windows 项目使用 Apple 资源 |
