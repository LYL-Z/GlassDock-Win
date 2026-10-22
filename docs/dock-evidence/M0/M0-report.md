# M0 执行与交接报告

- 日期：2026-10-22
- 工作目录：`D:\MacOS\GlassDock-Win`
- 目标：按任务书完成 M0 技术比较、两条 PoC、需求矩阵、本机测量和干净克隆验证。
- M0 结论：**工程工作和本地报告已完成；M0 验收门槛阻塞。** 不作生产框架选择，不进入 M1。

## 1. 环境与范围

- Windows 11 x64，版本 `10.0.26300`；本地 SDK `.tools\dotnet` 为 .NET SDK `10.0.402`，运行时 `10.0.13`；WinUI 使用 Windows App SDK `2.5.1`。
- Windows 显示设置接口返回活动设备 `CDD`、3840×2400、240Hz、32 位；`GetDpiForWindow` 返回 240 DPI，即 96 DPI 基准下 250%。这些是 Windows 当前报告值，不是独立验证的物理显示器规格。
- 当前分辨率支持的测试模式包括 240、120、100、75Hz；120Hz 模式测试后成功恢复 240Hz。相同分辨率下 60Hz 的 `CDS_TEST` 返回 -2（模式不支持）。不将 240Hz 上的测试结果外推至 60Hz。
- 当前任务所有项目文件、下载工具、NuGet、CLI 缓存、干净克隆和证据均在 D 盘。PresentMon 放在忽略目录 `.tools`；本机原始截图、资源 CSV、用户状态与运行日志放在忽略目录 `.local`，没有纳入 Git。
- 不检查、不清理、不写入 C 盘；D 盘上既有 `.local\legacy-appdata-from-C` 仅保持原状，不作为本轮验证输入。

## 2. 代码与执行记录

仓库包括 Core/App/Windows/Renderer/Desktop 层、WPF 与 WinUI 两个单独 PoC、MSTest、PowerShell build/test/measure/launch 脚本和 Windows CI workflow。两个 PoC 提供 8/24 个占位图标、44/58/72 DIP 尺寸、底/左/右布局、独立配置目录、日志入口、托盘 Show/Exit 实现。占位符不是 Apple 系统图标。

| 操作 | 命令 / 观察 | 结果 |
|---|---|---|
| Release 构建 | `.\scripts\build.ps1`（锁定包还原 + Release build） | 退出码 0；8 个项目成功；0 warning / 0 error |
| Core 测试 | `.\scripts\test.ps1 -NoRestore` | 退出码 0；11/11 通过 |
| 窗口探测 | `.\scripts\verify-poc-windows.ps1` | 退出码 0；两候选在 Bottom/Left/Right 各运行一次；透明角结果见下节 |
| 本地托管 CI | `.github/workflows/ci.yml` | workflow 配置存在；没有远端 repository，未运行 GitHub Actions |
| D 盘 clean clone | 本地 Git 源克隆后独立 restore/build/test | 结果和 commit 记录见第 6 节 |

远端仓库、发布、安装器和应用部署均不属于 M0 本地范围。

## 3. 窗口、边缘与输入结果

在当前 240 DPI 模式下，窗口探测脚本对两候选的每个边缘分别启动 8 项、58 DIP PoC，等待 2.5 秒后读取 top-level 窗口样式和 `WindowFromPoint`：

| 候选 | Bottom | Left | Right | 中心命中 | 透明角命中 Dock | 置顶 |
|---|---|---|---|---|---|---|
| WPF | 窗口存在 | 窗口存在 | 窗口存在 | 3/3 | 0/3 | 3/3 |
| WinUI 3 | 窗口存在 | 窗口存在 | 窗口存在 | 3/3 | 3/3 | 3/3 |

WPF 命中路径基于 `WM_NCHITTEST` / `HTTRANSPARENT` 的圆角几何；WinUI 原型窗口稳定置顶，但其透明角落仍由 Dock 进程持有，因此不满足穿透要求。整理后的数据在 `window-hit-tests.csv`，完整原始探测 CSV 在本机忽略目录 `.local\evidence\window-tests-20261022T093210Z.csv`。探测为 Windows 的窗口命中查询，不等同于将鼠标实际点击到底层应用并验证应用收到输入。

任务栏/自动隐藏/独占或无边框全屏覆盖恢复没有验收。轻/深背景的 GDI 像素探针无法读取可信 DWM 合成结果；不报告视觉透射已通过，也没有将原始截屏放进报告。

## 4. CPU、内存与 GPU

CPU/内存测量为每秒一次的静态 idle 样本。表中均值和峰值来自本地原始 CSV，CPU 是整机逻辑处理器归一化百分比。低 CPU 数字是短时低活动，不证明动画性能。24 项、44 DIP 追加测量为每路线 5 个样本；完整去敏汇总见 `runtime-summary.csv`。

| 刷新模式 | 候选 / 项数 / 尺寸 | n | 平均 CPU（整机%） | Working Set 平均/峰值 MiB | Private Bytes 平均/峰值 MiB |
|---|---|---:|---:|---:|---:|
| 240Hz | WPF / 8 / 58 DIP | 5 | 0.000 | 127.06 / 129.04 | 98.63 / 100.64 |
| 240Hz | WinUI / 8 / 58 DIP | 5 | 0.012 | 137.84 / 137.97 | 99.71 / 99.75 |
| 240Hz | WPF / 24 / 58 DIP | 10 | 0.000 | 130.25 / 134.95 | 135.39 / 140.12 |
| 240Hz | WinUI / 24 / 58 DIP | 10 | 0.006 | 138.68 / 138.69 | 101.99 / 102.03 |
| 240Hz | WPF / 24 / 44 DIP | 5 | 0.000 | 148.26 / 150.64 | 140.45 / 142.87 |
| 240Hz | WinUI / 24 / 44 DIP | 5 | 0.112 | 141.46 / 141.91 | 104.85 / 105.07 |
| 240Hz | WPF / 8 / 72 DIP | 10 | 0.000 | 140.79 / 143.66 | 111.50 / 114.41 |
| 240Hz | WinUI / 8 / 72 DIP | 10 | 0.006 | 136.46 / 136.47 | 99.49 / 99.53 |
| 240Hz | WPF / 24 / 72 DIP | 10 | 0.000 | 149.93 / 153.70 | 150.15 / 153.98 |
| 240Hz | WinUI / 24 / 72 DIP | 10 | 0.070 | 141.43 / 141.59 | 104.86 / 105.00 |
| 120Hz | WPF / 8 / 58 DIP | 5 | 0.000 | 124.62 / 126.83 | 96.42 / 98.67 |
| 120Hz | WinUI / 8 / 58 DIP | 5 | 0.000 | 140.93 / 140.93 | 98.64 / 98.71 |
| 120Hz | WPF / 24 / 58 DIP | 5 | 0.576 | 137.33 / 139.31 | 138.15 / 139.97 |
| 120Hz | WinUI / 24 / 58 DIP | 5 | 0.000 | 139.99 / 140.03 | 102.15 / 102.21 |

120Hz WPF/24 的五个 CPU 样本出现孤立的 1 秒级读数，样本短且整机空闲状态有限，不能据此推断常态负载。Working Set 和 Private Bytes 是不同进程内存指标，不作相互替代。

240Hz 静态 GPU 计数器样本（专用 / 共享显存峰值）：WPF 8 项约 27.66 / 3.19 MiB，WPF 24 项约 61.17 / 4.44 MiB；WinUI 8 项约 31.58 / 2.80 MiB，WinUI 24 项约 36.21 / 4.00 MiB。每组显存计数器各 5 个时间点，WinUI 24 项 GPU 专用内存峰值跨 2 个适配器实例求最大值。进程 GPU 引擎样本均显示 0%；其采样场景是静止 Dock，不说明合成器没有渲染成本，也不构成帧时序证据。整理数据见 `gpu-summary.csv`。

## 5. 刷新与帧时序

已将官方 PresentMon v2.6.0 CLI 放在 D 盘 `.tools\PresentMon`，下载资产 SHA-256 与 GitHub 官方发布记录匹配。对 WPF 尝试启动 ETW capture，PresentMon 返回退出码 6，错误为 `failed to start trace session: access denied`，提示需管理员权限或 Performance Log Users 成员资格；没有采用提权或改组绕过。未产出有效帧 CSV。因此 120Hz 只通过切换模式和 CPU/内存采样，**没有**通过 frame pacing、卡顿率或 60/120Hz 动画门槛。

## 6. 构建、克隆与许可证

- 在 D 盘仓库运行锁定 restore、Release build 和 MSTest；随后使用同一 D 盘本地 Git 仓库克隆到忽略目录 `.local\clean-clone`。将本地 SDK复制到克隆的 `.tools\dotnet`，由脚本把 NuGet、HTTP、CLI 和临时目录重定向到克隆自己的 `.local`；干净克隆不能复用原工作树的 `bin/obj`。
- 在干净克隆中执行 `.\scripts\build.ps1` 与 `.\scripts\test.ps1 -NoRestore`，退出码和结果由最终验证补写；若缺少包则先由 locked restore 从 NuGet 获取至 D 盘缓存。验证后检查 `git ls-files` 不含 `.local`、`.tools`、`bin/obj` 或原始截屏。
- `.github/workflows/ci.yml` 的 Windows workflow 配置了相同 build/test 步骤，但没有配置远程、没有推送、没有声明托管 CI 通过。
- `docs/third-party-licenses.md` 是依赖元数据清单，不是许可意见。根 `LICENSE` 是明确未授予许可的占位文件；项目许可需权利人选择。未选之前不可发布或分发。

## 7. 选择与门槛

当前窗口行为中，WPF 的 alpha-hole 查询明显优于 WinUI；内存结果在样本数和空闲场景下呈现交叉，不能给出稳健性能排序。WinUI 有 alpha-hole 失败；帧时间权限受阻；100/150/200% DPI、任务栏/全屏、深浅背景、真实底层点击和托盘退出仍未完成；许可证也未确认。因此：

1. M0 技术路线选择保持未决，`docs/adr/0001-ui-rendering-stack.md` 不接受任一方案为生产框架。
2. M0 验收门槛为 **阻塞**；尚不能宣布里程碑通过。
3. M1 未开始。下一步仅在 M0 阻塞项获得证据、用户确认项目许可证并重新评审后进行。

## 8. 可复核工件

- [REQ 验收矩阵](REQ-matrix.md)
- [技术路线对照](../../tech-spike-comparison.md)
- [窗口命中汇总](window-hit-tests.csv)
- [CPU/内存汇总](runtime-summary.csv)
- [GPU 计数器汇总](gpu-summary.csv)
- [路线 ADR](../../adr/0001-ui-rendering-stack.md)
- 原始窗口、资源、GPU 与 PresentMon 诊断文件留在 D 盘 `.local\evidence`，不提交以避免把原始屏幕/进程上下文带入仓库。
