# Macro Recorder

Windows 10 / Windows 11 x64 本地键盘鼠标宏工具，使用 C#、.NET 10 和 WPF。

**当前已完成 Phase 0 — Project Foundation 和 Phase 1 — Input Event Domain Model。没有 Hook、录制、回放或输入注入。**
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
  MacroRecorder.Infrastructure/  本地目录及结构化文件日志
tests/
  MacroRecorder.Core.Tests/      领域模型、JSON 往返、基础设施及资源释放测试
scripts/
  dotnet.ps1                    项目内 SDK 和缓存入口
  Test-GuiSmoke.ps1             WPF 窗口、状态绑定和退出集成验证
docs/
  phase-0-report.md             阶段验收记录
  phase-1-report.md             领域模型与 JSON 设计、测试及审查记录
```

依赖方向：`App → Core + Infrastructure`，`Infrastructure → Core`。
目前测试数量不多，沿用一个测试项目，同时测试 Core 和 Infrastructure；将来按实际规模拆分。
所有项目启用 nullable、内置 .NET analyzers、推荐分析规则、确定性编译以及 warnings-as-errors。
上述命令使用单个 MSBuild worker 并关闭构建服务器，适用于本项目验证时的受限执行环境。
PowerShell 脚本转发带冒号的参数时需要保留示例中的引号。

`Macro` 是可重复使用的定义，包含版本、标识、名称、说明、创建/更新时间、录制摘要、环境信息、
播放默认偏好和 `ImmutableArray<InputEvent>`。必填字段采用 `required init`，事件属性只读；修改 record 副本不会改变原对象。
RecordingSession、PlaybackSession 和 MacroFile 以后分别实现，禁止把运行时状态或文件 IO 塞入 Macro。

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

下一阶段仅建议 **Phase 2 — High Resolution Clock + RecordingSession**；收到明确确认后再执行。
