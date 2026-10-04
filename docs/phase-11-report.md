# Phase 11 — Playback Speed Report

## 1. Status

Completed。PlaybackEngine 现支持正有限运行时速度、事件与总时长缩放、可表示范围保护、兼容 1.0× overload、速度结果回报，并保持 Phase 10 的绝对时间调度、失败恢复和并发排斥。本阶段没有进入 Pause/Resume 或 Stop/Emergency Stop。

## 2. Git

- Branch: `main`
- Previous Commit: `5b5cd0ff97df01f2e2b73825366e655590809753`
- New Commit: 本报告随 `feat(playback): add runtime playback speed` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Remote Tracking: 开始时为 `main...origin/main [ahead 10]`。
- 没有执行 push、rebase、reset、amend、force push 或历史重写。

## 3. Implemented

- 新增 `PlaybackEngine.PlayAsync(Macro, double speed)`。
- 保留 `PlayAsync(Macro)`，固定委托到 1.0×，兼容 Phase 10 调用方。
- speed、Macro、playback gate、scaled timeline、Playback 的明确处理顺序。
- Event TimestampUs 与 Recording.DurationUs 的统一速度缩放。
- `PlaybackTimingException` 表达无法用 long 微秒表示的缩放结果。
- `PlaybackResult.PlaybackSpeed` 表达本次运行参数。
- 运行时速度不修改 Macro、PlaybackMetadata 或 JSON。

## 4. Files Changed

新增：

- `src/MacroRecorder.Core/Playback/PlaybackTimingException.cs`
- `docs/phase-11-report.md`

修改：

- `src/MacroRecorder.Core/Playback/PlaybackEngine.cs`
- `src/MacroRecorder.Core/Playback/PlaybackResult.cs`
- `tests/MacroRecorder.Core.Tests/Playback/PlaybackEngineTests.cs`
- `README.md`

删除：无。专用人工验证项目、stdout/stderr 和构建输出已清理，没有提交。

## 5. Architecture

```text
Macro + runtime speed
        ↓
speed validation
        ↓
MacroValidator
        ↓
PlaybackEngine state gate
        ↓
scaled absolute timeline
        ↓
IPlaybackScheduler + IInputInjector
```

`IPlaybackScheduler` 与 `SystemPlaybackScheduler` 没有修改，也不知道 speed。Core 仍不依赖 Win32、WPF、文件系统、JSON 或 Infrastructure。

## 6. Speed Model

运行速度是每次 Play 的参数，同一个 Macro 可以先按 2.0×、再按 0.5× 播放。合法 speed 必须 `double.IsFinite(speed) && speed > 0`，不施加 UI 范围限制；自动测试覆盖 0.01 和 100。

`PlaybackMetadata.DefaultSpeed` 保持 schema v1 中的持久化偏好。为了兼容 Phase 10，单参数 `PlayAsync(macro)` 明确使用 1.0×；本阶段不会读取或写回 Macro 中的 DefaultSpeed。

## 7. Timeline Scaling

每个事件目标使用：

```text
scaledTimestampUs = round(originalTimestampUs / speed)
```

取整采用 `MidpointRounding.AwayFromZero`，避免依赖默认 banker's rounding；0 始终为 0。Engine 先生成完整 scaled target array，再按 Macro.Events 当前数组顺序执行。没有 OrderBy / Sort，因此相同原始时间戳事件保持原顺序。

等待仍是：

```text
WaitUntilElapsedAsync(playbackStart, scaledTargetUs)
```

不存在 previous-delta 累加；迟到事件仍立即执行，Scheduler overshoot 不会逐事件积累。

## 8. Duration Scaling

全部事件后最终等待目标同样使用：

```text
scaledDurationUs = round(Recording.DurationUs / speed)
```

因此 10 秒 Macro 在 2× 下计划 5 秒，在 0.5× 下计划 20 秒。没有事件但 Duration=10 秒的 Macro 在 2× 下仍等待 5 秒，保留完整缩放后的空 timeline。

## 9. Overflow Safety

缩放通过 double 除法计算，不使用 `timestamp * reciprocal`。0 和 1.0× 走精确 long 路径；其他速度若结果非有限或达到 long 的正上界，则抛 `PlaybackTimingException`，不会 wrap 为负数。

Engine 在读取 Scheduler start timestamp 或调用 Injector 前缩放全部 event targets 和 duration。因此任何位置溢出都产生 0 wait、0 injection，并通过 finally 恢复 Idle。测试覆盖 `long.MaxValue / 2` 的安全结果和 `long.MaxValue / 0.01` 的拒绝。

## 10. Validation

调用顺序为：

1. null Macro 检查。
2. speed 必须正且有限。
3. MacroValidator semantic validation。
4. 尝试 Idle → Playing gate。
5. 计算完整 scaled timeline。
6. 开始 Scheduler / Injector 操作。

0、负数、NaN、正负 Infinity 均抛 `ArgumentOutOfRangeException(nameof(speed))`。测试使用同时无效的 Macro 证明 speed validation 先发生，且 Scheduler / Injector 调用数为 0。

## 11. Playback Result

成功结果包含：

- `InjectedEventCount`：成功注入的领域事件数。
- `ScheduledDurationUs`：缩放后的 Recording.DurationUs。
- `ActualElapsedUs`：真实单调运行耗时，不是计划值的别名。
- `PlaybackSpeed`：本次 runtime speed。

原三参数构造器仍存在并将 PlaybackSpeed 设为 1.0，已有调用方无需修改。四参数构造器也拒绝无效 speed。

## 12. Failure Semantics

- Invalid speed：在 Macro validation、state gate、Scheduler 和 Injector 前拒绝。
- Invalid Macro：在 state gate、Scheduler 和 Injector 前抛 PlaybackValidationException。
- Timing overflow：进入 Playing 后、任何 wait/inject 前抛 PlaybackTimingException，finally 恢复 Idle。
- Scheduler / Injector failure：原异常传播，剩余事件不执行，finally 恢复 Idle。
- PlaybackResult 只表示成功，不引入第二套错误状态。

## 13. Concurrency

PlaybackEngine 继续使用单一短 lock 管理 Idle / Playing。有效的第二次 Play 在首个 Playback 运行时立即抛 InvalidOperationException，不排队，也不会影响首个 Playback 完成。

测试还验证同一个 Macro 依次以 2.0× 和 0.5× 执行；目标序列和事件序列分别独立，Engine 最终为 Idle，Macro 未改变。

## 14. Tests

- Phase 10 baseline: 510
- Phase 11 新增参数化测试用例: 27
- 总计: 537
- Passed: 537
- Failed: 0
- Skipped: 0

新增覆盖 0.25/0.5/1.0/2.0/4.0、0.01/100、0/负数/NaN/±Infinity、validation 顺序、事件与 duration 缩放、same timestamp、timestamp zero、fractional microsecond rounding、late event、empty Macro duration、long.MaxValue 安全缩放与 overflow、Macro 不变性、Result 兼容性、Injector failure、concurrent rejection 和不同速度重复播放。

## 15. Manual 1x Verification

专用可见 WinForms 窗口播放安全的 A Down / A Up Macro：ScheduledDuration 800,000 us，ActualElapsed 807,202 us，Stopwatch 观察 810 ms；窗口收到 1 次 A KeyDown 和 1 次 A KeyUp，PlaybackResult event count 为 2。

## 16. Manual 2x Verification

同一 Macro 使用 2.0×：ScheduledDuration 400,000 us，ActualElapsed 403,203 us，Stopwatch 观察 403 ms；窗口收到 1 次 A KeyDown 和 1 次 A KeyUp，PlaybackResult event count 为 2。

## 17. Manual 0.5x Verification

同一 Macro 使用 0.5×：ScheduledDuration 1,600,000 us，ActualElapsed 1,604,128 us，Stopwatch 观察 1,604 ms；窗口收到 1 次 A KeyDown 和 1 次 A KeyUp，PlaybackResult event count 为 2。

## 18. Timing Observation

```text
Speed   Original duration   Scheduled   Observed
1.0×    800 ms              800 ms      810 ms
2.0×    800 ms              400 ms      403 ms
0.5×    800 ms             1600 ms     1604 ms
```

观察结果符合 `duration / speed`。Windows 和 Task.Delay 调度精度由 OS 决定，本阶段不声称精确微秒回放。

## 19. Recording Isolation

真实 UnifiedRecorder 同时安装 keyboard/mouse low-level hooks 后，以 2.0× 播放 A Down / A Up：

- Playback InjectedEventCount: 2
- RecordingResult.EventCount: 0

证明 Phase 11 的速度缩放没有破坏 injected event filtering。本阶段没有加入 Recording / Playback coordinator。

## 20. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

完整 Solution 和测试项目均使用单 worker、关闭 shared compilation/build server 的受限环境稳定参数验证。

## 21. Dependency Check

新增 PackageReference：否。生产代码只使用 .NET BCL 及现有 Core abstraction。Core / Infrastructure / App 的 PackageReference 均保持为空；测试包版本未修改。

## 22. Schema Compatibility

**No Macro Schema Change.** 未修改 Macro、PlaybackMetadata、RecordingMetadata、InputEvent、MacroSchema.CurrentVersion 或 JSON contract。运行时 speed 不写入 Macro，真实播放前后的 Macro 和 Playback.DefaultSpeed 均保持原值。

## 23. Security Boundary

- 没有增加后台录制、隐藏运行、网络、遥测、远程控制、权限提升或安全机制绕过。
- 真实验证只在当前交互式桌面的专用窗口内注入 A 键；没有危险快捷键。
- 每个 KeyDown 都有对应 KeyUp，finally 额外发送安全 KeyUp。
- 不保存用户输入、Macro JSON、截图或输入日志；临时验证器及输出已清理。
- Engine 仍只接受调用方提供的已构造 Macro，不直接读取 JSON 或文件。

## 24. Known Limitations

以下属于后续 Phase，不是 Phase 11 缺陷：

- Pause / Resume 尚未实现。
- Stop / Emergency Stop 尚未实现。
- PressedKeys / PressedMouseButtons 追踪与 stuck-input cleanup 尚未实现。
- Loop / Repeat 尚未实现。
- Recording / Playback coordinator 尚未实现。
- UI 速度预设、范围限制、进度和播放入口尚未实现。
- Window targeting、前台 guard 和 environment adaptation 尚未实现。

## 25. Git Status

提交前检查完整 diff、diff --check、静态禁用项、依赖、Schema、生成物和 status。提交后最终状态与 Commit Hash 在对话报告确认。提交不包含 manual harness、stdout/stderr、临时 JSON、输入日志、bin、obj、TestResults、dump、SDK 或个人绝对路径。

## 26. Next Phase

只建议 **Phase 12 — Pause / Resume**。本阶段提交后停止，不实现 Phase 12、Phase 13 或后续 UI / coordinator 功能。
