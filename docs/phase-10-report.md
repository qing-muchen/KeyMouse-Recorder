# Phase 10 — Playback Engine Basic Report

## 1. Status

Completed。平台无关的基础 PlaybackEngine、validation gate、1.0× 绝对时间调度、尾部 idle duration、失败恢复、并发拒绝、重复播放、Fake timing tests 和真实安全 Playback 均已完成。未进入 Phase 11。

## 2. Git

- Branch: `main`
- Previous Commit: `58c16006d6994857fc6217e57862d4c41a0b90f7`
- New Commit: 本报告随 `feat(playback): add basic timeline playback engine` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Commit Message: `feat(playback): add basic timeline playback engine`
- Remote Tracking: 开始时为 `main...origin/main [ahead 9]`。没有 push、rebase、reset、amend 或历史重写。

## 3. Implemented

- Core `PlaybackEngine.PlayAsync(Macro)`。
- `PlaybackState`：Idle / Playing。
- immutable `PlaybackResult`。
- `PlaybackValidationException`，保留完整 ValidationResult。
- Core `IPlaybackScheduler` timing abstraction。
- Infrastructure `SystemPlaybackScheduler`。
- Playback 前一次性 MacroValidator validation gate。
- 固定 1.0×、按 Macro.Events 原顺序的 absolute elapsed scheduling。
- Same-timestamp、late-event 和 timestamp zero 行为。
- Recording.DurationUs 尾部 idle time preservation。
- 重复播放、顺序播放不同 Macro 和 concurrent Play rejection。
- Validation / Scheduler / Injector failure propagation 和 Idle 恢复。

没有实现 Speed、Pause/Resume、用户 Stop、Emergency Stop、stuck-input cleanup、Macro loop、环境适配、窗口定位、全局热键或 WPF Playback UI。

## 4. Files Changed

新增 Core：

- `src/MacroRecorder.Core/Playback/IPlaybackScheduler.cs`
- `src/MacroRecorder.Core/Playback/PlaybackEngine.cs`
- `src/MacroRecorder.Core/Playback/PlaybackResult.cs`
- `src/MacroRecorder.Core/Playback/PlaybackState.cs`
- `src/MacroRecorder.Core/Playback/PlaybackValidationException.cs`

新增 Infrastructure：

- `src/MacroRecorder.Infrastructure/Playback/SystemPlaybackScheduler.cs`

新增测试：

- `tests/MacroRecorder.Core.Tests/Playback/PlaybackEngineTests.cs`
- `tests/MacroRecorder.Core.Tests/Playback/SystemPlaybackSchedulerTests.cs`

修改：`README.md`。新增：`docs/phase-10-report.md`。删除：无。临时人工验证项目、输出和构建产物已删除。

## 5. Architecture

```text
Macro
  ↓
MacroValidator
  ↓
PlaybackEngine (Core)
  ├── IPlaybackScheduler
  │       ↓
  │  SystemPlaybackScheduler (Infrastructure)
  └── IInputInjector
          ↓
     WindowsInputInjector
```

PlaybackEngine 不依赖 WindowsInputInjector、Win32、WPF、文件系统、JSON 或 MacroRepository。Repository 仍由调用方负责 Load；Engine 的唯一播放输入是 Macro。

## 6. Playback State

Engine 初始为 Idle。Macro 通过 validation 后，在短 lock 内切换到 Playing；成功或任何异常都会在 finally 中恢复 Idle。

Completed / Faulted 不作为永久服务状态。成功由 PlaybackResult 表达，失败由异常表达，因此 Engine 可以立即再次 Play。Engine 不保存 active Macro 或 current index。

## 7. Validation Gate

每次 Play 在读取 Scheduler start timestamp 或调用 Injector 前执行一次 `MacroValidator.Validate(macro)`。任何 Error 抛 `PlaybackValidationException`，且 Scheduler Get/Wait 和 Injector 调用数均为 0。Exception Message 保持简短，调用方通过 ValidationResult 查看结构化 Issues。

Warning-only Macro 允许播放；Empty Macro 的 EMPTY_MACRO warning 不阻止播放。Empty duration=0 立即完成，positive duration 等待完整空录制时长。

## 8. Timeline Scheduling

Scheduler API 使用：

```text
WaitUntilElapsedAsync(playbackStartTimestampUs, event.TimestampUs)
```

每个 TimestampUs 都表示从同一个 playback start 起算的绝对 elapsed target。Engine 不使用 previous event timestamp，也不把第一事件重新归零。Timestamp 0 尽快注入；future timestamp 等待；same timestamp 按原数组顺序连续注入；late event 立即执行。

Phase 10 始终使用原 TimestampUs，`PlaybackMetadata.DefaultSpeed` 不参与计算。

## 9. Drift Avoidance

若 Event 1 的目标为 100 ms，但 Scheduler 延迟到 105 ms 才返回，Event 2 仍请求 start-relative 200 ms，而不是再等待 100 ms 到 205 ms。生产 Scheduler 每次醒来重新读取单调时钟并计算相对同一 start 的 elapsed，延迟不会逐事件累积。

Fake Scheduler 测试模拟第一个 100 µs target 实际在 150 µs 执行；后续注入仍发生在 200 µs 和 300 µs。

## 10. Long / Overflow Safety

Engine 不计算 `startTimestamp + event.TimestampUs`，因此不会因 long addition wrap 为负值。Scheduler 使用 Int128 计算 `current - start` 和 remaining；PlaybackResult 的 actual elapsed 同样使用 Int128，并钳制到 0…long.MaxValue。

自动测试瞬时完成 8 小时 Macro，并覆盖 start=-1、TimestampUs/DurationUs=long.MaxValue，确认请求目标没有 wrap。

## 11. Final Duration / Idle Tail

全部 Events 注入后，Engine 额外请求：

```text
WaitUntilElapsedAsync(start, macro.Recording.DurationUs)
```

因此 last event @1s、Duration=5s 的 Playback 仍在约 5s 总时长后完成。Tail wait 不增加 InjectedEventCount。若 Injector 已失败，不会执行 tail；若 tail Scheduler 失败，播放失败并恢复 Idle。

## 12. Playback Result

成功结果包含：

- `InjectedEventCount`：成功注入的领域事件数，不是 Native INPUT 数。
- `ScheduledDurationUs`：原始 Recording.DurationUs。
- `ActualElapsedUs`：完成时单调 clock 与 playback start 的安全差值，可因 OS scheduling delay 大于计划时长。

Result 为 get-only immutable record，不保存 Macro 或 mutable event collection。

## 13. Failure Semantics

- Validation failure：PlaybackValidationException；0 wait、0 inject、State 保持 Idle。
- Injector failure：立即终止，不注入剩余 Events、不等待 tail，原异常传播。
- Scheduler failure：立即终止，原异常传播；包含 event wait 和 final tail wait。
- 所有已开始的失败路径通过 finally 恢复 Idle；下一次 Play 可正常运行。

PlaybackResult 只代表成功，不同时使用 Success=false 和异常两套 failure channel。

## 14. Concurrency

单一 state lock 将 validation gate 和 Idle→Playing 转换串行化。Playing 时第二个 PlayAsync 立即抛 InvalidOperationException，不会与首个 Macro 交叉；首个 Playback 保持运行并可正常完成。

完成或失败后 Engine 回到 Idle。同一 Macro 自动测试连续播放三次，事件序列每次相同；也验证 Macro A 后可播放 Macro B。

## 15. Tests

- Phase 9 baseline: 472
- Phase 10 新增: 38
- 总计: 510
- Passed: 510
- Failed: 0
- Skipped: 0

覆盖 empty/keyboard/mouse/mixed timeline、原顺序、timestamp zero、future、same timestamp、late events、absolute targets、drift、8-hour 与 long.Max timing、DefaultSpeed ignored、idle tail、Result fields、validation gate、warning-only、Injector/Scheduler failure、tail failure、actual elapsed、clock regression、Playing state、concurrent rejection、失败恢复、同 Macro 三次播放、不同 Macro 顺序播放和 Macro 不变性。

Timing 单元测试使用 Fake Scheduler，自动 Playback 测试使用 Fake Injector，不向真实桌面发送输入。Production Scheduler 只有一个无窄上限的 20 ms monotonic sanity test。

## 16. Manual Keyboard Playback

项目专用临时 WinForms 窗口执行：A Down/Up，随后 Shift Down、A Down/Up、Shift Up。PlaybackResult 注入 6 个领域事件；窗口观察到 3 次 KeyDown、3 次 KeyUp。所有 Down 均配对 Up，finally 额外执行安全 KeyUp cleanup。

## 17. Manual Mouse Playback

Mouse-only Macro 按 Timeline 执行：移动到测试按钮、LeftDown、LeftUp、移动到安全文本框、Vertical Wheel +120。PlaybackResult 为 5 个领域事件；窗口收到 1 次 Click 和 wheel delta 120。

Horizontal Wheel / XButton 未进行真实 Playback；Phase 9 automatic mapping tests 继续通过。

## 18. Manual Mixed Playback

混合 Macro 执行 MouseMove、Click、A Down/Up、MouseMove、Wheel。窗口观察顺序：

```text
Click → KeyDown:A → Wheel
```

PlaybackResult 为 7 个领域事件，顺序符合 Macro.Events。

## 19. Timing Observation

```text
Scheduled ms: 0, 500, 1000
Observed ms:  1, 502, 1002
```

600 ms idle-tail Macro 的最后 KeyUp 约在 64 ms 被窗口观察到，PlayAsync 约在 610 ms 返回，确认尾部时间没有丢失。

Domain timestamp resolution 为 microseconds；Windows/Task.Delay 的实际 scheduling precision 由 OS 决定。本阶段不声称微秒级准确回放。

## 20. Recording Isolation Smoke Test

真实 UnifiedRecorder 运行期间播放 A Down/Up + MouseMove 的 3-event Macro：

- Playback InjectedEventCount: 3
- RecordingResult.EventCount: 0

确认整段 Playback 的 SendInput injected events 未重新进入录制。本阶段没有增加 Recording/Playback coordinator。

## 21. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

最终验证针对完整 solution。

## 22. Dependency Check

新增 PackageReference：否。生产实现只使用 .NET BCL、现有 IMonotonicClock 和 Phase 9 IInputInjector；没有 Rx、Quartz、timing 或 automation package。

## 23. Schema Compatibility

**No Macro Schema Change.** 未修改 Macro、InputEvent、RecordingMetadata、PlaybackMetadata、SchemaVersion 或 JSON contract。

## 24. Security / Platform Boundary

- 真实验证只使用普通交互式桌面的项目专用临时窗口。
- 不提升权限、不绕过 UIPI、不接触 Secure Desktop。
- 未使用危险快捷键、系统按钮或破坏性输入。
- 测试 Macro 的所有 Down 均有对应 Up；临时 harness 额外执行安全 release。
- 不保存输入日志、Macro JSON、截图或真实用户输入内容。

## 25. Known Limitations

以下属于后续 Phase，而非 Phase 10 缺陷：

- Playback Speed 尚未实现；DefaultSpeed 暂不控制实际速度。
- Pause / Resume 尚未实现。
- Stop / Emergency Stop 尚未实现。
- Stuck-key / stuck-button cleanup 尚未实现。
- Recording / Playback isolation coordinator 尚未实现。
- Macro loop / repeat count 尚未实现。
- Environment coordinate adaptation 尚未实现。
- Window targeting / foreground guard 尚未实现。
- WPF Playback UI / progress 尚未实现。

## 26. Git Status

提交前检查完整 diff、静态边界、依赖、Schema、生成物和 status；提交后最终状态与 Commit Hash 在对话报告确认。提交不包含 manual harness、临时文本、Macro JSON、截图、input logs、bin、obj、TestResults、dump、SDK 或个人绝对路径。

## 27. Next Phase

只建议 **Phase 11 — Playback Speed**。本阶段提交后停止，不实现 Pause/Resume、Stop/Emergency Stop、stuck-input cleanup、环境适配或 WPF Playback UI。
