# Phase 12 — Pause / Resume Report

## 1. Status

Completed。Playback 现支持外部持有 PlaybackSession，并在不修改 Macro 的前提下暂停、冻结 active timeline、恢复剩余时间。Phase 10 绝对调度与 Phase 11 runtime speed 保持兼容。未进入 Stop / Emergency Stop。

## 2. Git

- Branch: `main`
- Previous Commit: `cb2126e0f339724c742ee9d62ceed40c5634753a`
- New Commit: 本报告随 `feat(playback): add pause and resume control` 提交；最终哈希在对话报告中给出，避免提交自引用。
- Remote Tracking: 开始时为 `main...origin/main [ahead 11]`。
- 没有执行 push、rebase、reset、amend、force push 或历史重写。

## 3. Implemented

- 新增 public PlaybackSession runtime handle。
- 新增 PlaybackEngine.StartPlayback(Macro[, speed])。
- 保留 PlayAsync(Macro[, speed]) 向后兼容入口。
- PlaybackState 增加 Paused。
- 新增 PauseAsync / ResumeAsync。
- 新增 active timeline freeze 和 asynchronous resume gate。
- 新增 CurrentEventIndex、Completion、PlaybackSpeed、TotalPausedDurationUs Session 视图。
- PlaybackResult 增加 TotalPausedDurationUs，并保留旧构造器。

没有实现 Stop、Emergency Stop、CancellationToken、输入状态 cleanup、Loop、Repeat、Queue、UI 或环境适配。

## 4. Files Changed

新增：

- `src/MacroRecorder.Core/Playback/PlaybackSession.cs`
- `tests/MacroRecorder.Core.Tests/Playback/PlaybackSessionTests.cs`
- `docs/phase-12-report.md`

修改：

- `src/MacroRecorder.Core/Playback/PlaybackEngine.cs`
- `src/MacroRecorder.Core/Playback/PlaybackState.cs`
- `src/MacroRecorder.Core/Playback/PlaybackResult.cs`
- `README.md`

删除：无。人工验证项目、stdout/stderr 和构建输出均已清理。

## 5. Architecture

```text
PlaybackEngine
  ├── validates Macro + runtime speed
  ├── enforces one active session
  └── creates PlaybackSession
             ├── scaled targets
             ├── event cursor
             ├── pause state / resume gate
             ├── paused duration
             ├── IPlaybackScheduler
             └── IInputInjector
```

Engine 只保留 active Session 引用用于单播放排斥与 State 转发。事件游标、时间数据、暂停门和 Completion 全部属于 Session。

## 6. Playback Session Design

`StartPlayback` 立即返回已处于 Playing 的 Session，不等待 Macro 结束。调用方通过：

```text
session.PauseAsync()
session.ResumeAsync()
await session.Completion
```

控制本次运行。现有 `PlayAsync` 内部创建 Session 并返回其 Completion，Phase 10/11 调用方无需修改。

Session 持有 Macro 只读引用和预计算的 scaled targets；不修改 Events、timestamp、metadata 或 Playback.DefaultSpeed。

## 7. Playback State

Session 和 Engine 对外状态为：

```text
Idle → Playing → Paused → Playing → Idle
```

Session 完成或失败后进入 Idle，Engine 清除 active Session 引用。没有引入 Stopping、Cancelling 或 Completed 状态。

## 8. Pause Semantics

- Playing → Paused。
- Paused 再次 Pause：成功 no-op，不重置 pause start。
- Idle Pause：抛 InvalidOperationException。
- Pause 返回后，新的 Injector 调用不能越过 Session state lock。
- 正在执行的一个同步 Inject 会先完成，Pause 随后取得 lock 并返回。

暂停门使用 `TaskCompletionSource` 和 `RunContinuationsAsynchronously`，不阻塞线程，不使用 Thread.Suspend 或 Thread.Sleep。

## 9. Resume Semantics

- Paused → Playing，并累加本段暂停的 monotonic duration。
- Playing 再次 Resume：成功 no-op。
- Idle Resume：抛 InvalidOperationException。
- Resume 打开异步 gate，使等待循环重新计算包含 pause offset 的 absolute target。

如果 Resume 读取 clock 时异常，finally 仍会切换到 Playing 并打开 gate，因此不会永久卡在 Paused；异常继续传播。

## 10. Timeline Freeze Model

Session 使用：

```text
activeElapsed = currentMonotonicTime
              - playbackStart
              - completedPauseDuration
              - currentPauseDuration
```

Scheduler target 使用：

```text
physicalElapsedTarget = activeTimelineTarget + completedPauseDuration
```

若旧 Scheduler wait 在暂停期间到期，Session 不注入事件，而是等待 resume gate。恢复后重新请求加入 pause offset 的 absolute target，所以只等待 active timeline 的剩余部分。

每次 Injector 调用前再次在同一个 state lock 内验证 Playing 和 active elapsed，消除 wait 返回与 Pause 之间的注入竞态。

## 11. Speed Compatibility

事件与 Recording.DurationUs 仍先按 `original / speed` 缩放，pause offset 再应用到缩放后的 active target。

自动测试验证原始 1,000 us timeline 在 2× 下变为 500 us；active 运行 200 us 后暂停 1,000 us，恢复后目标为 wall elapsed 1,500 us，仍只需 300 us active time。

所有 Phase 11 speed tests 保持通过。

## 12. Playback Result Changes

PlaybackResult 现在包含：

- InjectedEventCount
- ScheduledDurationUs
- ActualElapsedUs
- PlaybackSpeed
- TotalPausedDurationUs

ScheduledDurationUs 是缩放后的 active playback timeline；ActualElapsedUs 是包含暂停时间的真实 monotonic wall elapsed；TotalPausedDurationUs 是已累计暂停时间。

旧三参数和四参数构造器继续可用，并把 TotalPausedDurationUs 设为 0。

## 13. Failure Handling

- Validation / speed / initial timing failure：Session 不会成为 active。
- Scheduler / Injector / pause-adjusted timing failure：Session 进入 Idle，Engine 清除 active reference，原异常传播。
- Pause clock failure：状态保持 Playing。
- Resume clock failure：异常传播，但状态恢复 Playing、gate 打开。
- PlaybackResult 只表示成功。

本阶段没有用 Stop、Cancel 或 CancellationToken 处理失败。

## 14. Concurrency

Session 使用一个短 state lock 线性化 Pause、Resume、event injection 和 completion transition；任何 lock 都不会跨越 await。

自动测试并发释放 32 个 Pause/Resume 控制调用，确认没有死锁或非法组合；最后显式 Resume 后 Playback 正常完成。Playing 或 Paused 时第二个 Playback 仍立即拒绝，不等待。

## 15. Tests

- Phase 11 baseline: 537
- Phase 12 新增: 15
- Total: 552
- Passed: 552
- Failed: 0
- Skipped: 0

新增覆盖 StartPlayback handle、Playing/Paused/Idle、Completion、event cursor、等待中 Pause、暂停期间无注入、Resume 剩余时间、重复 Pause、多个独立暂停周期累加、重复 Resume、speed + pause、Idle 控制拒绝、并发控制、Paused concurrent Play rejection、clock failures、Injector failure、Macro 不变性和 PlaybackResult 兼容性。

ManualPlaybackScheduler 只推进虚拟 monotonic time，不使用真实长等待；Fake Injector 可观察暂停期间调用数。

## 16. Manual Keyboard Verification

安全 Macro 为 A Down @0、A Up @1,000 ms。A Down 后约 100 ms Pause，暂停约 611 ms：

- A Up during pause: false
- Resume 后 A Up: true
- TotalPausedDurationUs: 610,646
- ActualElapsedUs: 1,618,212

所有键盘 Down 都有配对 Up，验证器 finally 额外执行安全 KeyUp。

## 17. Manual Mouse Verification

Macro 先移动到专用按钮，然后 LeftDown @600 ms、LeftUp @650 ms。约 100 ms Pause，暂停约 714 ms：

- Click count during pause: 0
- Click count after resume: 1
- TotalPausedDurationUs: 713,859

验证器 finally 额外执行安全 LeftButtonUp。

## 18. Manual Mixed Verification

Macro 顺序：MouseMove @0、A Down @100 ms、A Up @150 ms、Wheel @400 ms。在 A Up 后 Pause 500 ms：

- Wheel during pause: false
- Observed after resume: `Move → A-Down → A-Up → Wheel`

恢复没有打乱不同输入类型的原事件顺序。

## 19. Recording Isolation

真实 UnifiedRecorder 活跃时执行 2× 的 A Down / A Up Playback，并在中间 Pause 约 200 ms：

- Playback InjectedEventCount: 2
- RecordingResult.EventCount: 0

Pause/Resume 没有改变 SendInput injected flags 或 Recorder filtering。本阶段没有增加 Recording / Playback coordinator。

## 20. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

完整 Solution Release Build 通过。

## 21. Dependency Check

新增 PackageReference：否。实现只使用 .NET BCL 的 Task、TaskCompletionSource、lock 和现有抽象。App / Core / Infrastructure 仍没有第三方生产包；测试包版本未修改。

## 22. Schema Compatibility

**No Macro Schema Change.** 未修改 Macro、InputEvent、TimestampUs、RecordingMetadata、PlaybackMetadata、SchemaVersion 或 JSON contract。Pause/Resume、cursor 和 paused duration 全部只存在于 PlaybackSession / PlaybackResult runtime objects。

## 23. Security Boundary

- 未增加后台录制、隐藏功能、网络、遥测、权限提升或远程控制。
- 真实验证只使用专用窗口、安全 A 键、专用按钮和滚轮。
- 所有 Down 均配对 Up，并执行 finally cleanup。
- 临时验证项目、输出、输入数据和日志已删除。
- 暂停期间不注入新事件，用户仍掌握恢复时机。

## 24. Known Limitations

以下属于后续 Phase：

- Stop 尚未实现。
- Emergency Stop 尚未实现。
- CancellationToken 尚未引入。
- PressedKeys / PressedMouseButtons 跟踪与 cleanup 尚未实现。
- Loop / Repeat / Queue 尚未实现。
- Recording / Playback coordinator 尚未实现。
- WPF Playback 控制与进度尚未实现。
- Window targeting 与 environment adaptation 尚未实现。

暂停不能回滚在 Pause 调用取得 state lock 之前已经完成的同步 Inject；Pause 返回后保证后续事件被 gate 阻止。

## 25. Git Status

提交前检查完整 diff、diff --check、静态禁用项、依赖、Schema、临时文件和 status。提交后最终状态及 Commit Hash 在对话报告确认。提交不包含人工验证器、stdout/stderr、Macro JSON、截图、输入日志、bin、obj、TestResults、dump、SDK 或个人绝对路径。

## 26. Next Phase

只建议 **Phase 13 — Stop / Emergency Stop**。本阶段提交后停止，不实现 Phase 13、Phase 14 或 WPF UI / Macro Editor / Environment Adaptation。
