# Phase 1 — Input Event Domain Model Report

## 1. Status

Completed。仅领域模型、JSON 兼容性及对应测试/文档；未进入 Phase 2。

## 2. Git

- Branch: `main`，已有 `origin/main` 跟踪配置；本轮不推送。
- Previous Commit: `445dbf10a51930e89b6b210dd1df65371c02fcd6`。
- 开始时已核实 HEAD、祖先关系、最近日志和干净工作区，检查现有 Solution、Core 骨架、测试及分析配置。
- New Commit: 本报告随 `feat(core): define input event domain model` 提交，最终哈希在对话阶段报告提供，避免自引用。

## 3. Implemented

- 统一抽象 record `InputEvent`，保存相对微秒时间和强类型 `InputEventType`。
- `KeyboardInputEvent` 表示 KeyDown / KeyUp，保存 VirtualKey / ScanCode / Flags。
- `MouseInputEvent` 表示 Move、独立 ButtonDown / ButtonUp、VerticalWheel / HorizontalWheel。
- 扩展 Phase 0 的 `Macro` 骨架，保存完整 metadata 和不可变事件集合。
- 新增 RecordingMetadata、EnvironmentMetadata、PlaybackMetadata 和统一 MacroSchema 版本常量。
- 原生 System.Text.Json 多态属性固定 discriminator；仅在 tests 中配置序列化 options。

## 4. Files Changed

修改 2 个文件：

- `src/MacroRecorder.Core/Models/Macro.cs`
- `README.md`

新增 15 个文件：

- `src/MacroRecorder.Core/Models/Input/InputEvent.cs`
- `src/MacroRecorder.Core/Models/Input/KeyboardInputEvent.cs`
- `src/MacroRecorder.Core/Models/Input/MouseInputEvent.cs`
- `src/MacroRecorder.Core/Models/Input/InputEventType.cs`
- `src/MacroRecorder.Core/Models/Input/MouseButton.cs`
- `src/MacroRecorder.Core/Models/RecordingMetadata.cs`
- `src/MacroRecorder.Core/Models/EnvironmentMetadata.cs`
- `src/MacroRecorder.Core/Models/PlaybackMetadata.cs`
- `src/MacroRecorder.Core/Schema/MacroSchema.cs`
- `tests/MacroRecorder.Core.Tests/Models/DomainTestData.cs`
- `tests/MacroRecorder.Core.Tests/Models/InputEventTests.cs`
- `tests/MacroRecorder.Core.Tests/Models/MacroTests.cs`
- `tests/MacroRecorder.Core.Tests/Serialization/DomainJson.cs`
- `tests/MacroRecorder.Core.Tests/Serialization/DomainJsonTests.cs`
- `docs/phase-1-report.md`

删除：无。未修改 GUI、Infrastructure、项目依赖或 Phase 0 测试。

## 5. Domain Model

```text
InputEvent (abstract record)
├── KeyboardInputEvent (sealed record)
└── MouseInputEvent (sealed record)

Macro (sealed record)
├── RecordingMetadata
├── EnvironmentMetadata
├── PlaybackMetadata
└── ImmutableArray<InputEvent>
```

| 类型 | 关键字段 |
| --- | --- |
| InputEvent | `long TimestampUs`、`InputEventType EventType` |
| KeyboardInputEvent | `uint VirtualKey / ScanCode / Flags` |
| MouseInputEvent | `int X / Y / WheelDelta`、`MouseButton Button` |
| Macro | `int SchemaVersion`、`Guid Id`、Name / Description、`DateTimeOffset CreatedAt / UpdatedAt`、三类 metadata、Events |
| RecordingMetadata | `long DurationUs`、`int EventCount` |
| EnvironmentMetadata | `int ScreenWidth / ScreenHeight`、`double DpiScale` |
| PlaybackMetadata | `double DefaultSpeed`，默认 1.0 |

TimestampUs 非负、允许相同值；同时间戳以数组顺序区分。Mouse 坐标支持负数，WheelDelta 不限制为 ±120。
事件构造器拒绝负时间和与具体事件类型不匹配的 EventType；不进行全 Macro 合法性验证。
Macro 的必需字段使用 required init，SchemaVersion 有默认值但 JSON 必须显式提供。

## 6. JSON Design

- `kind` 取固定字符串 `keyboard` / `mouse`，不使用 CLR、程序集或 assembly-qualified type name。
- EventType / MouseButton 使用 `JsonStringEnumConverter(allowIntegerValues: false)`，保留 Enum 原名。
- 属性名使用 `JsonNamingPolicy.CamelCase`，输出可读缩进。
- `MacroSchema.CurrentVersion = 1`；JSON 缺失 schemaVersion 时拒绝反序列化。提供的其他版本值会被保留，支持性由未来 Validator 判断。
- 测试配置启用 `RespectRequiredConstructorParameters` 和 `RespectNullableAnnotations`，避免缺字段悄悄生成默认值。
- 独立事件使用基类 `InputEvent` 序列化；Macro.Events 自动使用同一基类多态契约。

从全量测试 TRX 的 `CompleteMixedMacroRoundTripPreservesEveryFieldAndOrder` 输出实际检查的鼠标事件：

```json
{
  "kind": "mouse",
  "x": -500,
  "y": 200,
  "button": "None",
  "wheelDelta": 0,
  "timestampUs": 100000,
  "eventType": "MouseMove"
}
```

该完整 Macro 顶层 schemaVersion 为 1，含 7 个混合事件；DurationUs 为 500000，DpiScale 为 1.25，DefaultSpeed 为 1.5。
创建/更新时间由测试显式给定 UTC 值，未从系统读取时间。
JSON 只存在于内存与被忽略的测试结果输出，不保存临时 Macro JSON 文件。

## 7. Architecture Decisions

1. 鼠标采用统一 Down/Up 加 Button，五种按钮共享同一处理形状，避免重复 XButton 字段和重复事件类型。
2. 使用 System.Text.Json 原生多态属性，只定义稳定持久化标记，不编写自定义反射类型加载器或 Converter。
3. Events 采用 .NET 10 自带的 ImmutableArray，无新 NuGet 包；既能被原生 JSON 往返，也防止将 IReadOnlyList 转回底层 List 后修改。
4. 事件所有属性只读，metadata 为 init-only record；record 的 with 操作生成新对象。没有播放位置、取消令牌或按键状态。
5. Flags 使用 uint，完整保存来源数值；映射与过滤留给平台适配层，不把平台常量复制进 Core。
6. 只增加简单构造不变量，不提前实现 Validator。EventCount 与集合长度、duration 与 timeline、速度及字段组合仍需后续验证。

参考：[原生 JSON 多态](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism)、
[受支持的集合类型](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/supported-types)、
[JSON 必填属性及构造参数](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties)。

## 8. Tests

- 新增用例：82（包含 Theory 参数化展开）。
- Phase 0 用例：10，全部保留并通过。
- 总计：92；passed 92 / failed 0 / skipped 0。
- 结果：`.artifacts/test-results/phase-1.trx`，被 Git 忽略。

覆盖：键盘 Down/Up 所有字段；Move；五种鼠标按钮的 Down/Up；纵横滚轮与正负非标准步长；
全部 metadata；完整混合事件往返及具体运行时类型；相同时间戳顺序；可读 Enum/camelCase/discriminator；
负坐标与 int 边界；8 小时微秒值与 long.MaxValue；空 Events；不可变数组快照/接口/副本；
schemaVersion 必填与原值保留；必填字段、null 和构造参数；未知 discriminator 和数字 Enum 拒绝。

使用逐字段 equivalence helper，不依赖 Macro 的集合引用相等，不引入额外 assertion library。

## 9. Build

- SDK: `10.0.401`，沿用项目内安装。
- Configuration: Release。
- Warnings: 0。
- Errors: 0。
- nullable、analyzers、warnings-as-errors 配置保持不变；没有新增 warning suppression 或 null-forgiving 初始化。

实际执行：

```powershell
./scripts/dotnet.ps1 build MacroRecorder.slnx -c Release --no-restore --disable-build-servers '-m:1'
./scripts/dotnet.ps1 test MacroRecorder.slnx -c Release --no-build --no-restore '-m:1' --logger 'trx;LogFileName=phase-1.trx' --results-directory .artifacts/test-results
./scripts/dotnet.ps1 list MacroRecorder.slnx package --no-restore
```

## 10. Dependency Check

未新增任何 package。App / Core / Infrastructure 无 NuGet PackageReference。
测试仅保留 Microsoft.NET.Test.Sdk 17.14.1、xunit 2.9.3、xunit.runner.visualstudio 3.1.1。
Core 无 ProjectReference，不依赖 Infrastructure、WPF、Win32、文件系统或时钟服务。
本阶段新增的系统命名空间只有 System.Collections.Immutable 和 System.Text.Json.Serialization。

## 11. Manual Verification

已由开发过程直接查看测试实际输出及源代码，不代表用户已亲自确认：

- JSON 可读，camelCase 一致，无 CLR/程序集名称。
- keyboard / mouse discriminator 明确，Enum 是字符串。
- 负坐标 -500 正常保留，精细滚轮 15 / -30 正常保留。
- Macro.Events 没有可直接修改的集合 API；即使转换 IList，修改也抛出 NotSupportedException，已测试。
- 输入事件字段只读，metadata 为 init-only；Core 依赖保持干净。
- 本轮不需要真实 Hook、键鼠操作或 GUI 功能测试；WPF 项目随 Solution 一起通过编译。

## 12. Known Limitations

- 尚无 RecordingSession、Hook、JSON 文件 IO、MacroValidator 或 Playback；均为后续阶段范围，不是本阶段缺陷。
- 模型能够表达空 Events；能否播放由未来 Validator 决定。
- 调用方必须提供初始化的 ImmutableArray（可为空），不能使用 default 数组。
- required/nullable 不是完整输入验证：集合内 null 元素、字段范围、跨字段一致性、版本支持等仍需后续 Validator。
- 字符串 Enum 名称和 discriminator 是 schema 契约，重命名要考虑兼容性。
- JSON options 目前只位于测试；Phase 6 必须沿用相同配置，不能假设默认 JsonSerializerOptions 就有同等字段检查与可读格式。

## 13. Git Status

提交前检查完整 diff、whitespace、文件清单与 status，范围为 17 个 Phase 1 文件。
构建输出、SDK、缓存、TRX、运行日志、临时文件和个人绝对路径不进入提交。
提交后工作区状态及最终 Commit Hash 在对话报告中确认。

## 14. Next Phase

仅建议 **Phase 2 — High Resolution Clock + RecordingSession**。
本轮完成报告及独立提交后停止，不执行 Phase 2。
