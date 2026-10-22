# M0 evidence

**验收状态：M0 BLOCKED；M1 不开始；生产 UI 框架不冻结。**

- [M0 执行与缺口补测报告](M0-report.md)
- [逐项需求验收矩阵](REQ-matrix.md)
- [技术路线复评](../../tech-spike-comparison.md)
- [UI 栈 ADR](../../adr/0001-ui-rendering-stack.md)
- [既有窗口命中摘要](window-hit-tests.csv)
- [既有 CPU/内存资源摘要](runtime-summary.csv)
- [既有 GPU 计数器摘要](gpu-summary.csv)

本轮真实输入接收测试用 SendInput 合成鼠标事件，由底层 Win32 窗口过程和候选 Dock 日志确认实际接收方。WPF 透明区在所测三边通过；WinUI 透明区三边失败。输入脚本位于 scripts/verify-poc-input.ps1，运行前会要求高影响操作确认，因为它会显示全屏测试背景并向桌面注入鼠标事件。

一张 WPF 浅色 Bottom 的 WGC/DWM 合成帧证明该单一样例存在透明透射与阴影，未观察到背景模糊。原始输入日志、截图及元数据仍在被忽略的 D 盘 .local/evidence/；其中输入日志有本机 PID/坐标，不能当作公开报告附件。未通过的、未测试的和受阻项目见矩阵。

本地 Release build 与 Core 单元测试通过不等于远端 CI、物理鼠标、跨 DPI/多屏、全屏恢复、应用帧时间或实际屏幕 scanout 已通过。
