# Phase 2 — High Resolution Clock + RecordingSession Report

## 1. Status

Completed。实现、Release Build、全量测试、依赖审查和范围检查均通过；未进入 Phase 3。

## 2. Git

- Branch: `main`
- Previous Commit: `e58fff0d33df8bdffef92bb66751d90a058d0862`
- New Commit: 本报告随 `feat(core): add recording session and monotonic clock` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Remote Tracking Status: 开始时为 `main...origin/main [ahead 1]`。本阶段不 push、rebase、reset 或改写历史。

## 3. Implemented

- `IMonotonicClock`：Core 层可替换的单调微秒时钟。
- `StopwatchMonotonicClock`：Infrastructure 层的 BCL Stopwatch 生产实现。
- `RecordingSession`：构造即进入 Recording，提供类型化键盘/鼠标 Add API。
- `RecordingSessionState`：Recording / Stopped / Cancelled。
- `RecordingResult`：DurationUs、不可变 Events 和派生 EventCount。
- Stop：计算真实会话持续时间、冻结事件、转入 Stopped。
- Cancel：丢弃内部事件引用、转入 Cancelled。
- Thread safety：单一短锁保护生命周期、时间戳、事件顺序和 Stop/Add 边界。

没有新增 Hook、Recorder orchestration、采样、文件服务、Validator、Playback 或 GUI 功能。

## 4. Files Changed

新增 11 个文件：

- `src/MacroRecorder.Core/Time/IMonotonicClock.cs`
- `src/MacroRecorder.Core/Recording/RecordingSession.cs`
- `src/MacroRecorder.Core/Recording/RecordingSessionState.cs`
- `src/MacroRecorder.Core/Recording/RecordingResult.cs`
- `src/MacroRecorder.Infrastructure/Time/StopwatchMonotonicClock.cs`
- `tests/MacroRecorder.Core.Tests/TestDoubles/FakeMonotonicClock.cs`
- `tests/MacroRecorder.Core.Tests/TestDoubles/BlockingMonotonicClock.cs`
- `tests/MacroRecorder.Core.Tests/Time/StopwatchMonotonicClockTests.cs`
- `tests/MacroRecorder.Core.Tests/Recording/RecordingSessionTests.cs`
- `tests/MacroRecorder.Core.Tests/Recording/RecordingSessionConcurrencyTests.cs`
- `docs/phase-2-report.md`

修改 1 个文件：`README.md`。

删除：无。Phase 1 的 Models、Schema 和 JSON 测试没有修改。

## 5. Clock Design

接口：

```csharp
public interface IMonotonicClock
{
    long GetTimestampMicroseconds();
}
```

接口直接返回任意基准下的单调微秒值。RecordingSession 只记录起点并做差，不知道 Stopwatch ticks、Frequency 或 wall clock。
这使 Fake 完全确定，也防止未来 Hook 自行制造 TimestampUs。

生产实现构造时保存 `Stopwatch.GetTimestamp()` 原点；每次读取调用
`Stopwatch.GetElapsedTime(origin, Stopwatch.GetTimestamp())`，由 BCL 按实际 Frequency 换算成 TimeSpan，随后以
`TimeSpan.TicksPerMicrosecond` 做整数截断。每次读取没有 Stopwatch 分配、浮点累计状态或 wall-clock 依赖。
小于一微秒的变化可以得到相同值，符合 Timeline 允许相同 timestamp 的设计。

当前开发机环境：

- `Stopwatch.IsHighResolution = true`
- `Stopwatch.Frequency = 10,000,000 Hz`

这些仅为环境验证信息，不写入 Macro。Macro 只保存跨机器可解释的 TimestampUs。

## 6. RecordingSession Design

```text
                  Stop()
Recording ───────────────────→ Stopped
    │
    └──────── Cancel() ──────→ Cancelled
```

构造函数立即读取时钟并进入 Recording，避免“已创建但忘记 Start”的非法中间状态。
Stopped / Cancelled 均为终态；此后的 Add、Stop 或 Cancel 抛出 InvalidOperationException，不静默忽略程序错误。
Cancel 清空并释放内部 List 引用，不返回结果。

Add API 接收键盘或鼠标原始字段，Session 在临界区内读取时钟、生成具体 Phase 1 InputEvent 并 `List.Add`。
调用方的正常路径无法传入 TimestampUs。内部录制期间使用 List，Stop 时只做一次 `ToImmutableArray()`。

## 7. Timestamp Semantics

```text
TimestampUs = currentMonotonicMicroseconds - sessionStartMicroseconds
```

- TimestampUs 是相对微秒，不是 DateTime 或 DateTimeOffset。
- 事件按照成功进入 Session 临界区的 Add 顺序保存，不在 Stop 时排序。
- 允许相邻事件具有相同 TimestampUs。
- 如果时钟早于 Session 起点，相对值钳制为 0。
- 如果时钟相对上一事件回退，值钳制到 lastTimestampUs，保证 non-decreasing。
- 差值使用 Int128 中间量；极端超出 long 的时钟跨度饱和到 long.MaxValue，避免减法溢出。

生产 Stopwatch 理论上不会回退；钳制策略保护测试替身和异常平台条件下的持久化 Timeline。

## 8. Stop Semantics

Stop 在同一临界区内读取当前相对时间，并应用与事件相同的 non-decreasing 钳制：

```text
DurationUs = max(stopRelativeUs, lastEventTimestampUs)
```

因此 1 秒发生最后事件、5 秒停止时，DurationUs 为 5 秒。它表示完整录制会话长度，而不是最后事件时间。
Stop 成功冻结 ImmutableArray、释放内部 List 引用，再将状态设为 Stopped；不会留下可继续追加的集合。

## 9. Thread Safety

RecordingSession 使用一个私有 lock，保护：

- state 和内部 List 生命周期
- clock read、相对时间计算与 lastTimestampUs
- InputEvent 创建和 List append
- Stop/Cancel 与并发 Add 的边界

临界区内没有 IO、日志、JSON、Task.Run 或全集合排序。两个并发 Add 的顺序定义为成功进入临界区的顺序。
确定性边界测试让 Stop 在锁内阻塞于可控 Clock；等待的 Add 在 Stop 完成后被拒绝，结果无部分事件。

## 10. RecordingResult

RecordingResult 包含：

- `long DurationUs`
- `ImmutableArray<InputEvent> Events`
- 从 Events.Length 派生的 `int EventCount`

构造器拒绝负 Duration 和 default ImmutableArray。它不包含名称、Guid、wall-clock metadata、环境或播放偏好。
Phase 2 不直接创建完整 Macro，因为这些信息应由未来 application service / Unified Recorder 显式提供。

## 11. Tests

- 新增：22 个测试用例（含 Theory 展开）
- 原有：92 个，全部继续通过
- 总计：114 passed / 0 failed / 0 skipped
- TRX：`.artifacts/test-results/phase-2.trx`，已被 Git 忽略

覆盖内容：构造即 Recording、空会话、键盘/鼠标相对时间及原始字段、负坐标、五类鼠标场景、Add 顺序、相同时间戳、
时钟回退、停止后的 idle tail、不可变结果、Stop/Cancel 全部非法转换、8 小时精确微秒、巨大时钟基准、极端差值饱和、
256 个并发 Add 无丢失/损坏，以及同步控制的 Stop/Add 边界。

Stopwatch 测试连续读取 10,000 次，只断言非负和不回退；没有 Thread.Sleep、Task.Delay 或精确调度时间断言。

初次全量测试有 1 个测试失败：事件内容和顺序完全一致，但 `Assert.Equal` 选择了 ImmutableArray 容器相等重载。
测试改为逐位置 `Assert.Same` 后通过；生产实现无需改动。

## 12. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

最终命令：

```powershell
./scripts/dotnet.ps1 build MacroRecorder.slnx -c Release --no-restore --disable-build-servers '-m:1'
./scripts/dotnet.ps1 test MacroRecorder.slnx -c Release --no-build --no-restore '-m:1' --logger 'trx;LogFileName=phase-2.trx' --results-directory .artifacts/test-results
./scripts/dotnet.ps1 list MacroRecorder.slnx package --no-restore
```

## 13. Dependency Check

没有新增 package 或 ProjectReference。App / Core / Infrastructure 仍无第三方运行时包。
测试继续使用已有 Microsoft.NET.Test.Sdk 17.14.1、xunit 2.9.3 和 xunit.runner.visualstudio 3.1.1。
Core 只定义时钟接口；Infrastructure 通过原有 Core 引用提供 Stopwatch adapter。

## 14. Compatibility Check

**No Schema Change.** Phase 1 的 Macro、InputEvent、metadata、discriminator、Enum 和 MacroSchema 均未修改。
全部 DomainJsonTests 继续通过。RecordingSessionState 与 RecordingResult 位于运行时 Recording 目录，不进入 Macro JSON。

## 15. Manual Verification

代码和依赖扫描确认：

- Core 无 WPF、Win32、Infrastructure 或 File IO 依赖。
- RecordingSession 不读取 DateTime / DateTimeOffset，不使用 Thread.Sleep、Task.Delay、Hook 或输入注入。
- 生产 clock 明确使用 System.Diagnostics.Stopwatch。
- Stop 后只有 ImmutableArray；通过 IList 视图修改会抛出 NotSupportedException，测试已覆盖。
- 构建产物、SDK、TRX、日志、data 和临时文件均位于 Git 忽略范围。

本阶段没有 GUI、真实键鼠或 Hook 人工操作，因为没有对应功能变化。

## 16. Known Limitations

以下尚未实现，属于后续阶段而非缺陷：

- Keyboard Hook
- Mouse Hook
- Unified Recorder orchestration
- MouseMove Sampling
- JSON file IO
- MacroValidator
- Playback / SendInput

时钟输出为整微秒，同一微秒内的事件靠 Add 顺序区分。Cancel 不返回被丢弃事件的诊断快照。

## 17. Git Status

提交前检查完整 diff、whitespace、依赖、范围扫描和 status；提交后最终状态与 Commit Hash 在对话报告确认。
预计仅包含本节列出的 12 个 Phase 2 文件，不包含缓存、测试输出或个人绝对路径。

## 18. Next Phase

仅建议 **Phase 3 — Keyboard Recorder**。本阶段提交后停止，不实现 Keyboard Hook 或进入 Phase 3。
