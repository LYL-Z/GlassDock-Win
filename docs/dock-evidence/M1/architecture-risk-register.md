# GlassDock-Win 架构风险登记表

更新：2026-10-23。M0 **NO-GO**；M1 V0.1 为临时产品规格基线。负责人 D-01～D-09 已确认；独立 M2 Core 纯逻辑工作已授权并行推进，窗口/渲染路线仍未冻结。

| 风险 | 证据/性质 | 影响 | 最小处理与停止条件 | 当前状态 |
|---|---|---|---|---|
| Dock HWND 的外部背景 blur 未证明 | 实测失败；WinUI Composition 同窗控制 blur 成功，Dock HWND capture 未显示外部图样 blur。Microsoft 官方文档说明 CompositionBackdropBrush 支持 background pixels；WinUI 桌面推荐窗口级 `SystemBackdrop`，二者目标层级不同 | 决定窗口/渲染后端，可造成根本性返工 | 下一次获批单场景用 Window-level Desktop Acrylic；真实图案模糊即停止 M0 视觉探索并把自研材质移交 M4；仍不模糊且无回退时停止该支线，转架构复审。不得再调透明度无限试 | BLOCKED/FAIL：当前项目集成 FAIL，根因未确定 |
| 系统条件回退掩盖材质 | 官方资料列出 Windows 版本、图形能力、透明效果设置、电池节能/高对比度、RDP/虚拟机等条件；历史捕获没有完整回退遥测 | 误将环境 fallback 当 API 不支持，或将实色 fallback 当 blur | 新候选日志记录 OS、`IsSupported`、透明效果、高对比度、Energy Saver、远程会话及窗口/视觉层级；公开 API 不提供具体回退原因，`null/unknown` 保持 BLOCKED，最终仍依赖 WGC 图像 | 当前字段有静态实现并编译；运行值和图像仍 NOT TESTED |
| 自定义 Composition 效果未纳入系统降级策略 | Microsoft 文档要求自定义效果响应 Advanced Effects 设置；当前 Composition blur probe 只设置 Magenta fallback 处理 API 例外，没有自动监听用户关闭透明效果 | 隐私/无障碍/功耗策略可能不一致 | 若未来将其选为 renderer，先实现系统偏好监听和纯色 fallback，并验证；若项目只采用受系统管理的 SystemBackdrop，则保留系统自动 fallback | 当前 probe 风险，不是生产行为 |
| WPF/DWM backdrop 和非激活行为无决定性证据 | WPF 透明区输入有正面证据，但简单 DWM backdrop视觉失败；ShowActivated=false 运行未被可靠观察 | WPF 作为主路线可能在视觉/焦点方面返工 | 仅做一次有效 non-activate 观察；视觉完整材质移交 M4，不要求 M0还原 | BLOCKED/FAIL 历史项保留 |
| WinUI 非激活启动候选未实际测试 | `AppWindow.Show(false)` + `SWP_NOACTIVATE` 代码候选存在；日志/运行观察没有完成 | Dock 出现时抢焦点或改变 DXGI 状态 | 单场景前后台 HWND/PID 对照；可同轮一个 DXGI state check；异常即停止 | NOT TESTED |
| WinUI默认透明命中与 SetWindowRgn 质量权衡 | 默认区域透明点击失败；region 候选输入测试有 18/18 正面记录。Region 不等于逐像素透明；角落、边缘、投影和动画裁剪仍没有像素级实测 | 可能为输入正确牺牲 AA 边缘、阴影或放大外溢 | 先保留为可替换输入策略，不冻结；将圆角及超出窗口内容的动画验收到 M4/M5 | 输入候选 PASS，视觉/扩展性 NOT TESTED |
| Mac 实机规格缺失 | 本轮只读 Apple 官方网页；无 Mac OS build、偏好或捕获 | 精确尺寸、光学效果、动画函数、默认应用顺序可能失真 | M1 可做语义规格；冻结视觉/动作参数前要有具备元数据的实机样本，或项目负责人正式接受延期风险 | BLOCKED（依赖硬件/授权素材） |
| Windows 最近应用活动来源未实现 | D-02 已决定默认开启、最多三项、仅必要本地状态、不上传；目前没有可信 Windows activity provider 接入 Core | 若误把默认开启当作数据已可用，可能伪造最近项或读取过量私人历史 | 在真实 provider 通过隐私审查前保持 Recent 空列表；只接受明确观察事件；不扫描或上传历史 | NOT TESTED / provider 未实现 |
| 底部默认与 Windows 任务栏空间冲突 | D-03 已决定默认 Bottom 且不得修改任务栏；当前没有窗口工作区/AppBar 生产实现 | 可能出现重叠或可用空间不足，影响第一印象与可达性 | 由 Dock 配置提供用户选择的兼容方式；可说明系统自动隐藏选项，不替用户修改；在 M3/M9 桌面授权验收 | BLOCKED：窗口共存仍依 M0/M3 |
| 第三方/Apple 图标和品牌素材授权未定 | 现有 LICENSE 为占位，Apple素材与项目代码许可证分属不同权利 | 发行/商标侵权风险 | 只建来源台账、使用原创/合法 Windows 图标；公开发行前由权利人决定项目许可证并审查各素材授权 | 独立发布门槛，非 M1 技术阻断 |
| Dock自身帧时与实际 scanout 证据不全 | 旧数据是 DWM全局时间/逻辑或回调代理；未形成 Dock per-process presented frame data | 无法承诺120/240Hz动画流畅性 | 延至 M11；分离逻辑更新、框架提交、DWM和物理输出，按测量能力标注 | BLOCKED / NOT TESTED |
| 原 46 项完整行为范围的实现代价 | 46 项保留原始 ID、需求和阶段，已逐项增加来源范围、OwnerDecisionRefs、优先级、依赖和验收；D-09 原则接受 P0/P1/P2 作排期参考并保留调整权 | 把排期误读为豁免，或把产品决策误读为 Apple 事实，会丢失要求或误导实现 | 所有原始功能继续保留；优先级不豁免验收；M1 仍 PROVISIONAL，Mac 参数和未决首启项目不冻结 | IN PROGRESS；M2 Core 第一批已另获授权 |

风险状态不能替代 `PASS/FAIL/BLOCKED/NOT TESTED` 验收。规格草案状态见矩阵；原始结果仍在 M0 报告和迁移矩阵。
