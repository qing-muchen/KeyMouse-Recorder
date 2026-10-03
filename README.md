# Macro Recorder

Windows 10 / Windows 11 x64 本地键盘鼠标宏工具，使用 C#、.NET 10 和 WPF。

**当前已完成 Phase 0–5，包括领域模型、RecordingSession、显式启动的全局键盘/鼠标录制边界、基础鼠标移动采样和统一录制生命周期。尚无持久化、回放或输入注入。**
每个 Phase 独立实现、测试、检查和提交，报告后等待用户确认；不得自动进入下一阶段。

## 开发环境

- Windows x64；.NET SDK `10.0.401`，允许同一 feature band 的后续稳定补丁（见 `global.json`）。
- SDK 下载：[Microsoft .NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。
- 如果只有运行时，可将官方 Windows x64 SDK ZIP 解压到 `.tools/dotnet/`。建议核对官方发布元数据的 SHA-512。
- `scripts/dotnet.ps1` 优先使用项目内 SDK，否则使用 PATH 中已有 SDK；不自动下载安装。
- 脚本将 CLI 状态、NuGet 缓存和临时文件限制在项目内，并关闭 CLI 遥测，仅临时设置当前进程环境，结束后恢复。
- 应用没有第三方运行时包。测试依赖 Microsoft.NET.Test.Sdk、xUnit 和其 VS 测试适配器；首次还原需要访问 NuGet。

在仓库根目录运行：

```powershell
./scripts/dotnet.ps1 restore MacroRecorder.slnx
./scripts/dotnet.ps1 build MacroRecorder.slnx -c Release --no-restore --disable-build-servers '-m:1'
./scripts/dotnet.ps1 test MacroRecorder.slnx -c Release --no-build --no-restore '-m:1'
./scripts/dotnet.ps1 run --project src/MacroRecorder.App -c Release --no-build --no-restore
```

## 项目结构及依赖

```text
MacroRecorder.slnx
src/
  MacroRecorder.App/             WPF 启动组合、基础窗口、只读 ViewModel
  MacroRecorder.Core/            与 UI/Win32/IO 无关的领域模型和日志边界
  MacroRecorder.Infrastructure/  本地目录、结构化文件日志、Windows 键盘/鼠标 Hook
tests/
  MacroRecorder.Core.Tests/      领域模型、JSON 往返、基础设施及资源释放测试
scripts/
  dotnet.ps1                    项目内 SDK 和缓存入口
  Test-GuiSmoke.ps1             WPF 窗口、状态绑定和退出集成验证
docs/
  phase-0-report.md             阶段验收记录
  phase-1-report.md             领域模型与 JSON 设计、测试及审查记录
  phase-2-report.md             单调时钟、录制会话及并发边界记录
  phase-3-report.md             全局键盘 Hook、录制集成及资源生命周期记录
  phase-4-report.md             全局鼠标 Hook、移动采样及资源生命周期记录
  phase-5-report.md             统一录制生命周期、回滚、并发及真实键鼠验收记录
```

依赖方向：`App → Core + Infrastructure`，`Infrastructure → Core`。
目前测试数量不多，沿用一个测试项目，同时测试 Core 和 Infrastructure；将来按实际规模拆分。
所有项目启用 nullable、内置 .NET analyzers、推荐分析规则、确定性编译以及 warnings-as-errors。
上述命令使用单个 MSBuild worker 并关闭构建服务器，适用于本项目验证时的受限执行环境。
PowerShell 脚本转发带冒号的参数时需要保留示例中的引号。

`Macro` 是可重复使用的定义，包含版本、标识、名称、说明、创建/更新时间、录制摘要、环境信息、
播放默认偏好和 `ImmutableArray<InputEvent>`。必填字段采用 `required init`，事件属性只读；修改 record 副本不会改变原对象。
RecordingSession 是独立运行时对象；PlaybackSession 和 MacroFile 以后分别实现，禁止把运行时状态或文件 IO 塞入 Macro。

## Phase 1 领域模型与内存 JSON

- `InputEvent` → `KeyboardInputEvent` / `MouseInputEvent`，事件时间是非负 `long TimestampUs` 相对微秒，不是 DateTime。
- 键盘保留 `uint VirtualKey / ScanCode / Flags`，不解释/过滤 Flags。
- 鼠标使用统一 `MouseButtonDown / MouseButtonUp` 加 `MouseButton`，支持 Left / Right / Middle / XButton1 / XButton2。
- `int X / Y` 支持负屏幕坐标；`int WheelDelta` 保留原始正负值，包含非 120 倍数。
- 构造器仅守住非负时间和键鼠事件类型匹配。排序、metadata 一致性、鼠标字段组合、范围和播放许可由未来 Validator 处理。
- `MacroSchema.CurrentVersion = 1`；新 Macro 默认使用当前版本，`[JsonRequired]` 防止 JSON 缺失版本时悄悄使用默认值。
- `RecordingMetadata` 保存 DurationUs / EventCount；`EnvironmentMetadata` 保存 ScreenWidth / ScreenHeight / DpiScale；
  `PlaybackMetadata.DefaultSpeed` 默认 1.0，属于保存偏好而非运行状态。
- 元数据时间由调用方提供，推荐 UTC；模型不读取系统时间。Events 可为空，使用 `ImmutableArray<InputEvent>.Empty`，不能使用未初始化的 default 数组。

原生 `JsonPolymorphic` / `JsonDerivedType` 使用稳定 `kind: "keyboard" / "mouse"` 标记，不保存 CLR/程序集类型名。
独立事件需通过 `JsonSerializer.Serialize<InputEvent>` 序列化，以保留基类多态契约；Macro.Events 已使用该基类。
Enum 名称和 discriminator 属于 schema v1 契约，未来重命名需要考虑版本兼容。

序列化 options 目前仅放在测试的 `Serialization/DomainJson.cs`：camelCase、可读缩进、字符串 Enum（拒绝整数 Enum），
并启用必填构造参数及 nullable 检查。后续 Phase 6 文件序列化服务应沿用该配置。
没有新增 MacroJsonSerializer、文件 IO、验证服务或第三方包。

## 本地目录及日志

首次启动在**可执行程序所在目录**建立：

```text
data/
  macros/               未来存放用户 Macro JSON
  config/               Phase 19 实现 settings.json
  logs/                 每个应用实例一个 session-*.jsonl
```

当前采用便携目录，便于开发验收且不向项目外写入；运行位置必须可写，不需要管理员权限。
目录创建可重复执行，不覆盖已有内容。`settings.json` 目前仅预留路径，不创建配置内容。
开发构建的数据放在忽略的 `bin/` 下，清理构建输出会删除它们；正式数据位置在后续发布前需要确认。

日志字段：UTC timestamp、level、eventId，发生异常时附加 exceptionType 和 errorCode。
UTC 仅用于诊断时间；后续输入时间线必须使用 Stopwatch 相对微秒时间。
日志不接收任意输入文本、不记录异常消息或堆栈中的用户内容，不包含键盘数据、密码、窗口标题或宏内容。
写入错误向调用方传播，GUI 提供错误提示；退出释放日志句柄。当前没有日志轮转和保留期策略。
程序启动失败时，日志可能尚未创建，此时会显示错误类别提示。未处理的 UI 异常记录后退出；可重新启动。

应用不联网、不上传、不遥测。仅开发时的 SDK 下载和 NuGet restore 使用网络。
Idle 界面没有输入监听能力，主窗口 code-behind 仅调用 `InitializeComponent()`。

## Phase 0 验证

自动化测试覆盖目录幂等创建、已有文件保护、路径拒绝、IO 错误传播、结构化日志、
异常内容排除、并发日志完整性、日志实例隔离和 Dispose 句柄释放。
GUI 验收：启动窗口、确认 `Idle · 未录制`、核对本地目录和启动日志、关闭窗口并确认退出日志及进程结束。
可在构建后运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-GuiSmoke.ps1`。
该脚本仅启动并关闭自己的应用进程，不模拟键鼠输入；UI Automation 检查不能替代人工外观审查。
具体执行结果见 `docs/phase-0-report.md`。

## Phase 1 验证

全量测试 92 项通过（原有 10 项 + 新增 82 个参数化测试用例），0 failed / 0 skipped；Release Build 为 0 warnings / 0 errors。
覆盖全事件类型、五种鼠标按钮、正负精细滚轮值、负坐标、长时间戳、完整 metadata、混合事件及同时间戳顺序、空数组、
事件集合不可变性、实际 JSON 格式和多态恢复、必填字段及构造参数、schemaVersion 不可省略。
样例 JSON 从真实 round-trip 测试的输出查看；测试仅使用内存字符串，没有生成 Macro JSON 文件。
详细报告见 `docs/phase-1-report.md`。

## Phase 2 单调时钟与录制会话

- Core 的 `IMonotonicClock` 只暴露任意基准下的单调微秒值，不依赖 wall clock。
- Infrastructure 的 `StopwatchMonotonicClock` 使用 BCL Stopwatch 计数器，并在边界内转换为整数微秒。
- `RecordingSession` 构造即开始录制，通过类型化 Add API 统一生成 TimestampUs；调用方不传录制时间。
- Timeline 允许相同时间戳；时钟回退时钳制到上一事件时间，保证 non-decreasing。
- Stop 使用实际停止时刻计算 Duration，包含最后事件后的空闲时间，并返回不可变 `RecordingResult`。
- Cancel 丢弃内部事件；Stopped / Cancelled 后的 Add、Stop、Cancel 均抛出 InvalidOperationException。
- 一个短 lock 保护状态、时钟读取、顺序和 List append；Stop/Add 的边界也在同一临界区内。

全量测试 114 项通过（Phase 2 新增 22 项），0 failed / 0 skipped；Release Build 为 0 warnings / 0 errors。
Phase 1 JSON schema 未修改。当前仍没有 Keyboard/Mouse Hook、MouseMove Sampling、JSON 文件 IO、Validator 或 Playback。
详细报告见 `docs/phase-2-report.md`。

## Phase 3 键盘录制

- Windows Infrastructure 使用 `WH_KEYBOARD_LL`，集中封装 Set / Unhook / CallNext、消息循环和原生结构。
- Hook 在专用后台线程安装并运行消息循环；Start 等待安装结果，Stop 投递退出消息并有界等待线程结束。
- `KeyboardHookService` 输出轻量 `KeyboardCaptureEvent`；平台无关的 `KeyboardRecorder` 映射后交给现有 RecordingSession。
- `WM_KEYDOWN` / `WM_SYSKEYDOWN` 映射 KeyboardKeyDown，`WM_KEYUP` / `WM_SYSKEYUP` 映射 KeyboardKeyUp。
- VirtualKey、ScanCode 和原始 Flags 保留；Win32 message time 不进入 Domain，TimestampUs 仍仅由 RecordingSession 生成。
- `LLKHF_INJECTED` 和 `LLKHF_LOWER_IL_INJECTED` 默认不录制；Extended 和 AltDown 不会被误过滤。
- Recorder 支持 Stop 后绑定新的 RecordingSession 重启；Dispose 释放 Hook，Dispose 后禁止 Start。
- Callback 始终继续 Hook 链，托管异常被限制在 native boundary 内并通过 LastCallbackException 观察。

全量测试 146 项通过（Phase 3 新增 32 项），0 failed / 0 skipped；Release Build 为 0 warnings / 0 errors。
真实键盘人工验证覆盖 A、Shift+A、Ctrl+C、Alt+A、长按 B、停止边界和新 Session 隔离，全部通过；验证临时文件已清理。
当前没有正式 WPF 录制入口，只有 Core / Infrastructure 能力；不会在应用启动或 Idle 时自动安装 Hook。
详细报告见 `docs/phase-3-report.md`。

## Phase 4 鼠标录制

- Windows Infrastructure 使用 `WH_MOUSE_LL` 捕获 Move、Left/Right/Middle/XButton Down/Up、垂直和水平滚轮。
- 键盘与鼠标 Hook 各有独立线程，但共用经过 Phase 3 测试的内部生命周期和集中 P/Invoke 边界。
- X/Y 使用 `int` 并保留负坐标；XButton 从 mouseData 高位 WORD 解析，WheelDelta 按 signed short 解析。
- `LLMHF_INJECTED` 和 `LLMHF_LOWER_IL_INJECTED` 默认不录制；Win32 time 不进入 Domain。
- `MouseMoveSampler` 默认以 8,000 us 间隔比较 last accepted move；第一次接受，0 表示全部接受，时钟回退会接受并重置基线。
- Move 之外的按钮与滚轮完全绕过采样；拖拽保留 ButtonDown、采样后的 Move 和 ButtonUp。
- 每次 Start 重置 sampler 并绑定新的 RecordingSession；Stop 不结束 Session，Dispose 释放 Hook。

全量测试 206 项通过（Phase 4 新增 60 项），0 failed / 0 skipped；Release Build 为 0 warnings / 0 errors。
真实鼠标验收在 30 秒内收到 23,581 个 raw Move，采样后记录 2,858 个；点击、垂直滚轮、两个侧键、拖拽、Stop 和 Session 重启通过。
当前硬件未观察到水平滚轮事件；正负水平 delta 映射由单元测试覆盖。临时验证器和坐标轨迹均未保留。
当前没有 Unified Recorder 或正式 WPF 录制入口，应用启动和 Idle 不会自动安装 Hook。
详细报告见 `docs/phase-4-report.md`。

## Phase 5 统一录制生命周期

- `UnifiedRecorder` 独占每次 `RecordingSession`，按 Keyboard → Mouse 顺序启动，并在两者都成功后进入 `Recording`。
- `StopRecording` 先停止两个输入 Recorder，再停止 Session 并返回不可变 `RecordingResult`；键鼠事件直接进入同一 Session，不做 Merge 或 Sort。
- `CancelRecording` 停止输入并取消 Session，不产生正式结果；Stop / Cancel 后都可创建全新的 Session 再次录制。
- 部分启动失败会反向回滚已尝试组件并取消 Session；Stop、Cancel、Dispose 对每项资源执行 best-effort cleanup，多项失败使用 `AggregateException` 汇总。
- `Stopped / Starting / Recording / Stopping / Cancelling / Disposed` 单一状态机替代多组 bool；生命周期操作串行化，输入 callback 不经过协调器锁。
- 组件边界由最小 `IKeyboardRecorder` / `IMouseRecorder` 接口表达，便于确定性注入启动、停止和释放失败。

全量测试 228 项通过（Phase 5 新增 22 项），0 failed / 0 skipped；Release Build 为 0 warnings / 0 errors。
真实统一录制验收第一段收到 689 个事件（键盘 30、鼠标 659），指定操作和混合时间线通过；第二次录制、Cancel 后恢复、Hook cleanup 全部通过。
当前 `RecordingResult` 仍只存在内存中，App 尚未接入正式录制 UI；没有实现 JSON 文件 IO、Validator 或 Playback。
详细报告见 `docs/phase-5-report.md`。

下一阶段仅建议 **Phase 6 — Macro JSON Persistence**；收到明确确认后再执行。
