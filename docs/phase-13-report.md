# Phase 13 — Stop / Emergency Stop Report

## 1. Status

Completed。PlaybackSession 现可从 Playing 或 Paused 安全停止，立即打断调度等待，阻止后续 Macro 事件，释放本次运行仍处于按下状态的键盘键与鼠标按钮，随后恢复 Idle。Emergency Stop 使用同一可靠 cleanup 管线并具有更高终止原因优先级。未进入 Phase 14。

## 2. Git

- Branch: `main`
- Previous Commit: `8a2ef6a0f98bec5b33128615b096128c5bfa3830`
- New Commit: 本报告随 `feat(playback): add stop and emergency stop control` 提交；最终哈希在对话报告中给出，避免提交自引用。
- Remote Tracking: 开始时为 `main...origin/main [ahead 12]`。
- 未执行 push、rebase、reset、amend、force push 或历史重写。

## 3. Implemented

- PlaybackState 增加 Stopping。
- PlaybackSession 增加 StopAsync / EmergencyStopAsync。
- 每个 Session 持有内部 CancellationTokenSource，不向调用方暴露取消令牌。
- IPlaybackScheduler 增加必须实现的 CancellationToken wait overload；原二参数 API 保持兼容。
- SystemPlaybackScheduler 的粗等待使用可取消 Task.Delay，细等待循环检查取消。
- 新增 PressedInputTracker，跟踪成功注入后尚未释放的键盘键和鼠标按钮。
- Stop cleanup 合成 KeyboardKeyUp / MouseButtonUp，并以 LIFO 顺序执行。
- 新增 PlaybackCompletionReason：Completed / Stopped / EmergencyStopped。
- PlaybackResult 增加 CompletionReason / ReleasedInputCount，旧构造器保持兼容默认值。
- cleanup 失败仍尝试其余释放；单失败原样传播，多失败聚合。

没有实现 Loop、Repeat、Queue、Recording/Playback Coordinator、WPF UI、Macro Editor、Window Targeting 或 Environment Adaptation。

## 4. Files Changed

新增：

- `src/MacroRecorder.Core/Playback/PlaybackCompletionReason.cs`
- `src/MacroRecorder.Core/Playback/PressedInputTracker.cs`
- `tests/MacroRecorder.Core.Tests/Playback/PlaybackStopTests.cs`
- `docs/phase-13-report.md`

修改：

- `src/MacroRecorder.Core/Playback/IPlaybackScheduler.cs`
- `src/MacroRecorder.Core/Playback/PlaybackResult.cs`
- `src/MacroRecorder.Core/Playback/PlaybackSession.cs`
- `src/MacroRecorder.Core/Playback/PlaybackState.cs`
- `src/MacroRecorder.Infrastructure/Playback/SystemPlaybackScheduler.cs`
- `tests/MacroRecorder.Core.Tests/Playback/PlaybackEngineTests.cs`
- `tests/MacroRecorder.Core.Tests/Playback/PlaybackSessionTests.cs`
- `tests/MacroRecorder.Core.Tests/Playback/SystemPlaybackSchedulerTests.cs`
- `README.md`

删除：无。人工验证器、输出和构建产物均已清理。

## 5. Architecture

```text
PlaybackEngine
  └── PlaybackSession
        ├── state: Playing / Paused / Stopping / Idle
        ├── internal CancellationTokenSource
        ├── pause gate + timeline cursor
        ├── PressedInputTracker
        ├── IPlaybackScheduler (cancel-aware wait)
        └── IInputInjector (scheduled input + synthesized releases)
```

Engine 继续只创建 Session、执行单播放互斥和转发状态。Stop、Emergency Stop、取消、按下状态及 cleanup 全部归属一次 PlaybackSession。IInputInjector 接口未变化；Session 通过现有强类型 InputEvent 边界发送 release，因此 Core 仍不依赖 Win32。

## 6. Playback State

```text
Idle → Playing ↔ Paused
          │         │
          └─→ Stopping ←─┘
                  │
                  └─→ Idle
```

普通完成和失败也在最终收尾期间短暂使用 Stopping，再进入 Idle。Stopping 中 Pause / Resume 被拒绝；同一停止尚未完成时再次 Stop 复用同一 Completion。Session 已 Idle 后 Stop / Emergency Stop 均拒绝。

## 7. Stop Semantics

- Playing / Paused 均可 Stop。
- Stop 先线性化状态为 Stopping，再打开 pause gate 并取消 Scheduler wait。
- 已开始的同步 Inject 会完成；Stop 取得 Session state lock 后不会再开始新的 Macro event injection。
- 后续 Macro 事件和 tail wait 被取消。
- cleanup 完成后 StopAsync 返回 PlaybackResult，Completion 返回同一结果。
- Stop 后不可 Resume。

正常完成仍保持 Phase 10 语义：只注入 Macro 中的事件。合成 release 只属于显式 Stop / Emergency Stop 安全路径，不计入 InjectedEventCount。

## 8. Emergency Stop Semantics

EmergencyStopAsync 可从 Playing、Paused 或 Scheduler wait 中触发，立即进入 Stopping、取消等待并执行 cleanup。若普通 Stop 尚未完成，Emergency Stop 复用同一 Completion 并把 CompletionReason 升级为 EmergencyStopped；不会执行第二轮 cleanup。

普通 Stop 和 Emergency Stop 都优先避免残留输入；差异体现在调用意图、终止原因和 Emergency 对 in-flight Stop 的优先级。

## 9. Cancellation Model

每个 Session 创建一个内部 CancellationTokenSource，仅传给 pause gate wait 和 IPlaybackScheduler。调用方不接触令牌，也不存在外部任意取消语义。

SystemPlaybackScheduler 在每轮检查取消，并把令牌传入 Task.Delay；Stop 不需要等待原 target。接口原二参数 overload 委托给 CancellationToken.None，Phase 10–12 调用保持兼容。

Session 完成后释放 CancellationTokenSource。类型级 analyzer suppression 明确记录该资源由 mandatory Completion lifecycle 自动 Dispose，而不是交给调用方。

## 10. Input Cleanup

PressedInputTracker 只在 IInputInjector 成功返回后更新：

- KeyboardKeyDown：按 VirtualKey + ScanCode 登记；重复 Down 不重复登记。
- KeyboardKeyUp：移除对应键。
- MouseButtonDown：按 MouseButton 登记。
- MouseButtonUp：移除对应按钮。
- 每个 MouseInputEvent 更新最近成功注入坐标。

Stop 时对 held inputs 逆序释放。键盘 release 保留 VirtualKey、ScanCode、Flags 并切换为 KeyboardKeyUp；鼠标 release 使用最近坐标、原 Button 和 MouseButtonUp。已由 Macro 正常释放的输入不会重复释放。

## 11. Failure Handling

- Stop cancellation 与 pause duration accounting 失败不会跳过 cleanup。
- 每个 held input 都独立尝试释放。
- 一个 playback/control/cleanup failure 使用 ExceptionDispatchInfo 原样传播。
- 多个 failure 形成 AggregateException。
- 无论结果成功或异常，Session 与 Engine 最终恢复 Idle，Engine 可再次创建新 Session。
- Stop / Emergency Stop 的 OperationCanceledException 被识别为成功终止路径，不泄漏给调用方。

## 12. Concurrency

状态转换、事件注入边界、游标和按下状态更新使用同一个短 lock；任何 lock 都不跨 await。CancellationToken 负责唤醒异步 Scheduler / pause wait。

自动测试覆盖 concurrent Stop/Stop、Stop/Resume、Stop/Pause 以及普通 Stop 被 Emergency Stop 升级。竞争结果保持确定的合法状态：控制调用可能在 Stopping 后被拒绝，但 Completion 始终收敛到 Idle，不再注入未来事件。

## 13. Tests

- Phase 12 baseline: 552
- Phase 13 新增: 21
- Total: 573
- Passed: 573
- Failed: 0
- Skipped: 0

新增覆盖：

- Stop during Scheduler wait 和 cancellation observation。
- Playing / Paused → Stop → Idle。
- Stop 阻止全部未来事件并返回 Completion。
- in-flight Stop 幂等复用、完成后 Stop 拒绝。
- 单键、多键、重复 KeyDown cleanup。
- 单鼠标按钮、多鼠标按钮、最近坐标与逆序 cleanup。
- Macro 已执行 Up 时不重复释放。
- Emergency Stop during wait / pause，以及升级普通 Stop。
- cleanup 单失败传播、多失败聚合、失败后 Idle。
- Pause duration 读取失败时仍执行 release，并在 Idle 后传播控制异常。
- concurrent Stop/Resume 和 Stop/Pause。
- Macro / Events 不变。
- Phase 10 正常注入语义、Phase 11 speed、Phase 12 pause 全量回归。
- SystemPlaybackScheduler 长等待被 CancellationToken 立即打断。

所有时间测试使用 fake scheduler，不执行真实长等待。

## 14. Manual Keyboard

专用可见 WPF 安全窗口先通过一组完整 Left Down/Up 取得焦点，再执行 A Down @100 ms、5 s tail，并在 A Down 后调用 Stop：

- Hook order: `A-Down → A-Up`
- Window order: `A-Down → A-Up`
- CompletionReason: Stopped
- ReleasedInputCount: 1

finally 额外补发安全 A Up。

## 15. Manual Mouse

在专用窗口中心执行 LeftDown @100 ms，并在 Down 后调用 Stop：

- Hook order: `Left-Down → Left-Up`
- Window order: `Left-Down → Left-Up`
- CompletionReason: Stopped
- ReleasedInputCount: 1

finally 额外补发安全 Left Up。

## 16. Manual Mixed

专用窗口执行 MouseMove → A Down → LeftDown，然后触发 Emergency Stop：

- Hook order: `A-Down → Left-Down → Left-Up → A-Up`
- Window order: `A-Down → Left-Down → Left-Up → A-Up`
- CompletionReason: EmergencyStopped
- ReleasedInputCount: 2

cleanup 按实际按下顺序的逆序释放 Left，再释放 A。

## 17. Recording Isolation

真实 UnifiedRecorder 活跃时执行 A Down + LeftDown，并触发 Emergency Stop。低级观察 Hook 收到完整 injected 顺序：

```text
A-Down → Left-Down → Left-Up → A-Up
```

RecordingResult.EventCount 为 0，说明 Stop cleanup 产生的 injected release 与原 playback input 一样被 Recorder 过滤。没有实现 Phase 14 coordinator。

## 18. Build

- Configuration: Release
- SDK: 10.0.401（项目内现有 SDK）
- Warnings: 0
- Errors: 0

完整 Solution Release Build 通过。

## 19. Dependency Check

新增 PackageReference：否。生产实现仅使用 .NET BCL CancellationTokenSource、Task、集合和现有领域/注入抽象。App / Core / Infrastructure 生产 package 状态未变化；测试 package 版本未修改。

## 20. Schema Compatibility

**No Macro Schema Change.** 未修改 Macro、InputEvent、TimestampUs、RecordingMetadata、PlaybackMetadata、SchemaVersion、JSON options 或持久化契约。CompletionReason、ReleasedInputCount、Stopping、pressed inputs 和 CancellationToken 都是 runtime 数据。

## 21. Security Boundary

- 未增加后台录制、隐藏窗口、网络、遥测、权限提升、远程控制或规避功能。
- Stop / Emergency Stop 只操作调用方主动启动的 PlaybackSession。
- 人工验证使用可见专用窗口、安全 A 键和窗口内鼠标坐标。
- 每次人工验证 finally 强制补发 A Up / Left Up；临时验证器及其输出已删除。
- cleanup 事件继续使用 WindowsInputInjector 的 SendInput marker 和 injected flag，不进入 RecordingResult。

## 22. Known Limitations

- 尚无 Loop / Repeat / Queue。
- 尚无 Recording / Playback Coordinator；两者互斥将在 Phase 14 统一管理。
- 尚无正式 WPF Playback 控件、Stop/Emergency Stop 按钮或全局热键。
- 尚无应用关闭时主动协调 active PlaybackSession 的应用服务。
- 尚无 Window Targeting、Environment Adaptation 或 Macro Editor。
- 同步 IInputInjector 调用一旦开始不能被中途抢占；Stop 会等待该调用返回，然后阻止下一事件并 cleanup。

## 23. Git Status

提交前检查完整 diff、diff --check、静态禁用项、依赖、Schema、临时文件和 status。提交后最终状态及 Commit Hash 在对话报告确认。提交不包含人工验证器、stdout/stderr、Macro JSON、截图、输入日志、bin、obj、TestResults、dump、SDK 或个人绝对路径。

## 24. Next Phase

只建议 **Phase 14 — Recording / Playback Isolation**。本阶段提交后停止，不实现 Phase 14、WPF UI、Macro Editor 或 Environment Adaptation。
