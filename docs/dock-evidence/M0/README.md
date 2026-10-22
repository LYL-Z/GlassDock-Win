# M0 evidence

**验收状态：M0 BLOCKED；M1 不开始；生产 UI 框架不冻结。**

- [M0 执行与缺口补测报告](M0-report.md)
- [逐项需求验收矩阵](REQ-matrix.md)
- [技术路线复评](../../tech-spike-comparison.md)
- [UI 栈 ADR](../../adr/0001-ui-rendering-stack.md)
- [既有窗口命中摘要](window-hit-tests.csv)
- [既有 CPU/内存资源摘要](runtime-summary.csv)
- [既有 GPU 计数器摘要](gpu-summary.csv)

既有真实输入接收测试用 SendInput 合成鼠标事件，由底层 Win32 窗口过程和候选 Dock 日志确认实际接收方；WPF 透明区在所测三边通过，WinUI 透明区三边失败。这不是物理鼠标验收。本轮仅静态检查与代码构建，没有启动桌面交互。交互入口位于 `scripts/verify-poc-input.ps1`，现在要求显式短语、高影响确认，并由独立看门狗监听 Ctrl+Shift+F12 和超时清理；这些恢复控制本身尚未通过端到端交互验证。

一张 WPF 浅色 Bottom 的 WGC/DWM 合成帧证明该单一样例存在透明透射与阴影，未观察到背景模糊。新增 DwmGetCompositionTimingInfo 被动探针在当前未启用管理员角色的令牌下 8/8 次返回 S_OK，读到约 240Hz 的全局 compose-rate；这不是 Dock 帧时间。精确 SHA `3a6ca88b6925f60ecea5971f8781b7482153ed0c` 的 D 盘独立干净克隆 Restore/Build/Test 通过，原 SHA GitHub Actions Build/Test 也通过；本轮实现 SHA `b4d78b8be3f47f08febbf261c720e6dc318e170a` 的远程 Build/Test 已通过 [Actions run](https://github.com/LYL-Z/GlassDock-Win/actions/runs/39800785724)。原始输入日志、截图及元数据仍在被忽略的 D 盘 `.local/evidence/`；其中输入日志含本机 PID/坐标，不作为公开报告附件。未通过、未测试和受阻项目见矩阵。

本地 Release build 与 Core 单元测试通过不等于远端 CI、物理鼠标、跨 DPI/多屏、全屏恢复、应用帧时间或实际屏幕 scanout 已通过。
