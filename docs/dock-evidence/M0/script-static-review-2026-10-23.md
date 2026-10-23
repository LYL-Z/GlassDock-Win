# M0 交互测试脚本静态复核

- 日期：2026-10-23
- 工作区：`D:\MacOS\GlassDock-Win`
- 状态：静态检查通过；真实桌面恢复、热键、看门狗异常路径和视觉/DXGI 行为仍未测试。

## 本轮发现的恢复缺陷

上一轮 harness 记录了运行进程自己的环境变量快照，随后为子进程继承而更改其 `GLASSDOCK_*` 值；旧 `finally` 未在停止状态核验前恢复该父进程环境。因此即使桌面和登记进程清理检查正常，`RestorePassed` 仍因环境变量不匹配而为 false。

修正过程中又发现，仅调用 `[Environment]::SetEnvironmentVariable(name, $null, 'Process')` 在当前 PowerShell/.NET 环境不能可靠还原“变量原本不存在”；缺失变量可能变成空字符串。当前恢复函数用 `Remove-Item Env:<name>` 表示缺失值；变量原值为空字符串时保留空值；之后分别核验变量存在性和值。

## 静态验证

运行：

```powershell
pwsh.exe -NoLogo -NoProfile -File scripts/test-m0-interactive-safety-static.ps1
pwsh.exe -NoLogo -NoProfile -File scripts/verify-m0-final-composition-and-activation.ps1 -PlanOnly
```

结果：

- PASS：PowerShell 7 parser 解析仓库中被检查的 PowerShell 脚本。
- PASS：父进程 `finally` 的环境恢复顺序和本次运行范围保护条件存在。
- PASS：10 个唯一变量在进程内往返后区分了 unset、empty string 和普通值；测试结束恢复了静态测试进程原值。
- PASS：PlanOnly 显示本轮范围为单次 WinUI Window-level Desktop Acrylic 背景捕获、WPF/WinUI 非激活启动观察、每候选一次 3 秒无捕获 DXGI 状态对照；最多 300 秒主看门狗、120 秒 DXGI 子看门狗；不注入输入、不请求模式切换。
- NOT TESTED：急停热键实际触发、watchdog 到时清理、真实桌面恢复、WGC 图像、窗口前台归属、DXGI exclusive 变化。

`PlanOnly` 仅生成计划文本，不启动受控背景窗口、Dock、DXGI surface 或 WGC。两份可复核终端记录保存在 `D:\MacOS\GlassDock-Win\.local\evidence\m0-final-close-20261023\interactive-safety-static.log` 和 `minimal-desktop-test-plan.txt`；正式运行若获授权，证据仅写入 [申请中指定的 D 盘 `.local` 路径](minimum-desktop-test-application.md)。
