# Phase 7 — Macro Validation Report

## 1. Status

Completed。Core 已具备纯确定性的 Macro 语义验证，返回不可变结构化 Issues，一次收集所有能够安全判断的问题。Release Build、334 项全量测试和独立人工验证通过。未进入 Phase 8。

## 2. Git

- Branch: `main`
- Previous Commit: `e299ab23c877b8e6e3c81c29b9abfa27c544209e`
- New Commit: 本报告随 `feat(validation): add macro semantic validation` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Commit Message: `feat(validation): add macro semantic validation`
- Remote Tracking: 开始时为 `main...origin/main [ahead 6]`。没有 push、rebase、reset、amend 或历史重写。

## 3. Implemented

- `MacroValidator`：无状态静态入口 `Validate(Macro)`。
- `ValidationResult`：持有不可变 Issues，并由 Error issues 派生 IsValid。
- `ValidationIssue`：Severity、稳定 Code、Message、Path。
- `ValidationSeverity`：Warning / Error。
- `ValidationCodes`：集中定义 26 个稳定 machine code。
- Schema、Identity、Recording metadata、Environment、Playback、Date metadata、Event collection、Timeline、Keyboard、Mouse 规则。
- 一次验证收集所有能够安全判断的问题；Issue 顺序固定。

未实现自动修复、排序、Migration、Library、Playback、SendInput、Win32、文件服务或 WPF Validation UI。

## 4. Files Changed

新增生产文件：

- `src/MacroRecorder.Core/Validation/MacroValidator.cs`
- `src/MacroRecorder.Core/Validation/ValidationCodes.cs`
- `src/MacroRecorder.Core/Validation/ValidationIssue.cs`
- `src/MacroRecorder.Core/Validation/ValidationResult.cs`
- `src/MacroRecorder.Core/Validation/ValidationSeverity.cs`

新增测试：

- `tests/MacroRecorder.Core.Tests/Validation/MacroValidatorTests.cs`

修改：`README.md`。新增文档：`docs/phase-7-report.md`。删除：无。人工验证 harness 已删除。

## 5. Architecture

```text
Macro
  ↓
MacroValidator.Validate
  ↓
ValidationResult
  ├── IsValid (derived)
  └── ImmutableArray<ValidationIssue>
        ├── Severity
        ├── Code
        ├── Message
        └── Path
```

Validation 位于 Core，不引用 Infrastructure、文件系统、System.Text.Json、WPF 或 Win32。

## 6. Validation Result Design

`ValidationResult.Issues` 使用 `ImmutableArray<ValidationIssue>`，default array 被构造器拒绝。`IsValid` 不保存独立状态，而是每次由 Issues 中是否存在 `ValidationSeverity.Error` 派生；只有 Warning 时仍为 true。

`ValidationIssue` 是不可变 record。Code / Message / Path 必须非空白；Code 是稳定机器契约，Message 可以调整文案，Path 使用接近 JSON 的 camelCase 表达，例如 `recording.eventCount` 和 `events[3].timestampUs`。

## 7. Validation Rules

Schema：版本必须为正数且等于 `MacroSchema.CurrentVersion`。

Identity：ID 不能为 `Guid.Empty`；Name 不能为空或纯空白；Description 允许为空。

Recording：DurationUs / EventCount 不能为负；EventCount 必须等于 Events.Length；DurationUs 不能早于最后一个事件。

Timeline：逐现有顺序验证 timestamp，不排序、不修复；相等合法，下降报错。

Keyboard：只允许 KeyboardKeyDown / KeyboardKeyUp。现有构造器先行守住该不变量。

Mouse：只允许 move、button down/up、vertical/horizontal wheel，并验证 Button / WheelDelta 组合。

Environment：ScreenWidth / ScreenHeight 必须大于 0；DpiScale 必须为正有限数。

Playback：DefaultSpeed 必须为正有限数；Phase 7 不限制最大 4.0。

Warnings：空 Macro、UpdatedAt 早于 CreatedAt。Warning 本身不阻止使用。

## 8. Issue Codes

```text
SCHEMA_VERSION_INVALID
SCHEMA_VERSION_UNSUPPORTED
MACRO_ID_EMPTY
MACRO_NAME_EMPTY
RECORDING_DURATION_NEGATIVE
EVENT_COUNT_NEGATIVE
EVENT_COUNT_MISMATCH
DURATION_BEFORE_LAST_EVENT
EVENTS_UNINITIALIZED
EMPTY_MACRO
EVENT_NULL
TIMESTAMP_NEGATIVE
TIMESTAMP_DECREASING
EVENT_TYPE_UNSUPPORTED
KEYBOARD_EVENT_TYPE_INVALID
MOUSE_EVENT_TYPE_INVALID
MOUSE_BUTTON_REQUIRED
MOUSE_BUTTON_MUST_BE_NONE
MOUSE_MOVE_WHEEL_DELTA_INVALID
MOUSE_BUTTON_WHEEL_DELTA_INVALID
WHEEL_DELTA_ZERO
SCREEN_WIDTH_INVALID
SCREEN_HEIGHT_INVALID
DPI_SCALE_INVALID
PLAYBACK_SPEED_INVALID
UPDATED_AT_BEFORE_CREATED_AT
```

Path 示例：`schemaVersion`、`id`、`recording.durationUs`、`environment.dpiScale`、`events[1].timestampUs`、`events[4].button`。

## 9. Timeline Validation

- TimestampUs 必须 >= 0。
- 从 index 0 到末尾进行相邻 non-decreasing 比较。
- 相同 timestamp 合法，集合顺序解决 tie。
- 每一个相邻下降都报告 `TIMESTAMP_DECREASING`。
- 不调用 Sort / OrderBy，不建立重排后的 Events。
- Events 非空时，DurationUs 必须 >= 最后一个事件的 timestamp。
- 比较只使用关系运算；`long.MaxValue` 不加减，不会 overflow。

`InputEvent` 构造器已经拒绝负 timestamp，因此正常 public API / JSON 路径不能产生负值；Validator 仍保留 defensive `TIMESTAMP_NEGATIVE` 分支，没有使用 reflection/unsafe 伪造测试状态。

## 10. Mouse Validation

- Move：Button=None、WheelDelta=0。
- ButtonDown / ButtonUp：Button 必须为 Left / Right / Middle / XButton1 / XButton2，WheelDelta=0。
- Vertical / Horizontal Wheel：Button=None，WheelDelta 不能为 0。
- +120、-120、+60、-60、+15、-15 等任意非零 signed delta 合法。
- X/Y 不做范围检查；负坐标合法。
- 不比较当前显示器大小，不读取系统环境。

## 11. Floating Point Validation

DpiScale 与 DefaultSpeed 都先通过 `double.IsFinite`，然后检查 > 0。因此 0、负数、NaN、PositiveInfinity、NegativeInfinity 均被拒绝。Playback speed 在 Phase 7 不应用 Phase 11 的 0.25–4.0 执行范围。

## 12. Purity / Boundary

Validator：

- 不修改 Macro、metadata 或 Events。
- 不写文件、不序列化、不记录日志。
- 不访问网络、Win32、Hook、Recorder 或 Playback。
- 不读取系统时间、屏幕或当前机器环境。
- 不排序、不修复、不删除事件。
- 相同 Macro 重复验证，Issues 内容和顺序一致。

## 13. Persistence Boundary

`MacroFileStore` 未修改，仍只执行 Load / Deserialize。集成测试保存并读取 `schemaVersion = 2`：Phase 6 Load 成功并保留版本，随后 Phase 7 返回 `SCHEMA_VERSION_UNSUPPORTED`。

```text
MacroFileStore.LoadAsync
  ↓ Macro
MacroValidator.Validate
  ↓ ValidationResult
```

## 14. Tests

- Phase 6 baseline: 255
- Phase 7 新增: 79
- 总计: 334
- Passed: 334
- Failed: 0
- Skipped: 0

覆盖 valid / empty / warning / error+warning / multi-error、稳定顺序、重复结果、source immutability、Schema、identity、metadata、timeline、equal timestamp、long.MaxValue、constructor invariants、default/null/unknown event、键盘、鼠标按钮、move、wheel、负坐标、DPI、速度、日期 warning、ValidationResult 和 Persistence→Validation 边界。

## 15. Manual Verification

临时 Core-only console harness 使用生成的非敏感事件：

```text
Valid macro: IsValid=True
Decreasing macro: IsValid=False
Issue: TIMESTAMP_DECREASING | events[1].timestampUs
Repeated result equal=True
Macro unchanged=True
```

Harness 及其 bin/obj 已删除。

## 16. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

最终验证针对完整 solution。

## 17. Dependency Check

新增 PackageReference：否。Validation 只使用 Core model、`System.Collections.Immutable` 和其他 BCL API；未引入 FluentValidation、Newtonsoft.Json 或其他框架。

## 18. Schema Compatibility

**No Macro Schema Change.** 未修改 MacroSchema.CurrentVersion、Macro properties、InputEvent discriminator、InputEventType、MouseButton 或 JSON fields。MacroFileStore / MacroJsonSerializer 未修改。

## 19. Security / Privacy

- 没有新增 input capture、persistence、network、telemetry 或隐藏行为。
- Validator 不记录 Macro、键盘内容、鼠标轨迹或用户数据。
- 人工验证只使用代码生成的固定非敏感事件。
- 没有自动修复或静默丢弃数据。

## 20. Known Limitations

以下属于后续 Phase，而非 Phase 7 缺陷：

- Macro Library 尚未实现。
- Playback / SendInput 尚未实现。
- Global Hotkeys 尚未实现。
- WPF 正式工作流尚未实现。
- Schema Migration 尚未实现。
- Playback 入口尚未存在，因此还没有最终 execution gate / ValidatedMacro wrapper。

## 21. Git Status

提交前检查完整 diff、静态边界、依赖、Schema、生成物和 status；提交后最终状态与 Commit Hash 在对话报告确认。提交不包含 JSON fixture、temp、bin、obj、TestResults、日志、用户 Macro、SDK、dump、截图或个人绝对路径。

## 22. Next Phase

只建议 **Phase 8 — Macro Repository / Library**。本阶段提交后停止，不实现 Library、Playback、SendInput 或 WPF UI。
