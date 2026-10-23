# M0 evidence

**验收状态：M0 BLOCKED；M1 不开始；生产 UI 框架不冻结。**

- [M0 执行与缺口补测报告](M0-report.md)
- [逐项需求验收矩阵](REQ-matrix.md)
- [技术路线复评](../../tech-spike-comparison.md)
- [UI 栈 ADR](../../adr/0001-ui-rendering-stack.md)
- [既有窗口命中摘要](window-hit-tests.csv)
- [既有 CPU/内存资源摘要](runtime-summary.csv)
- [既有 GPU 计数器摘要](gpu-summary.csv)

2026-10-23 用户输入明确启动短语后，本轮只执行获批的三路线 × 浅/深受控底图 × Bottom/Left/Right × 58 DIP 视觉矩阵。18/18 帧由 Windows.Graphics.Capture 捕获并经人工审阅；捕获成功 18/18，视觉通过 0/18、失败 18/18。三种候选中的背景网格和斜线仍清晰，没有可确认的真实背景模糊；静态图标完整，三种方案均残留矩形宿主边缘。详情见本机 D 盘 .local/evidence/m0-gap/visual/20261023T091558Z/visual-comparison-report.md，逐帧表与原始截图不纳入版本控制或公开仓库。

该轮独立看门狗记录正常结束，桌面恢复检查通过，无本轮测试进程残留。没有注入输入，没有改变显示模式、DPI、分辨率或系统设置。WGC 图像是 DWM 合成帧，不是 scanout。

此前真实输入测试使用 SendInput 合成鼠标事件，由底层 Win32 窗口过程和候选 Dock 日志确认实际接收方；WPF 透明区在所测三边通过，WinUI 默认透明区三边失败，WinUI SetWindowRgn 候选三边通过。这不是物理鼠标手工验收。本轮视觉授权不包括任何新增输入测试。急停键与看门狗机制代码已实现，但热键和超时触发路径未主动验证。

本地 Release build 与 Core 单元测试通过不等于远端 CI、物理鼠标、跨 DPI/多屏、全屏恢复、应用帧时间或实际屏幕 scanout 已通过。
