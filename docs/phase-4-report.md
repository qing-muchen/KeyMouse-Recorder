# Phase 4 — Mouse Recorder Report

## 1. Status

Completed。全局低级鼠标 Hook、managed event source、MouseRecorder、8 ms MouseMoveSampler、注入事件过滤和资源生命周期均已实现；Release Build、206 项自动测试和真实普通权限鼠标验收通过。未进入 Phase 5。

## 2. Git

- Branch: `main`
- Previous Commit: `0cb312cf26a611b509536b3d7b5a3b2f9805df48`
- New Commit: 本报告随 `feat(input): add global mouse recording hook` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Commit Message: `feat(input): add global mouse recording hook`
- Remote Tracking: 开始时为 `main...origin/main [ahead 3]`。没有 push、rebase、reset、amend 或改写历史。

## 3. Implemented

- `MouseHookService`：可重启的 Windows `WH_MOUSE_LL` managed event source。
- `MouseCaptureEvent` / `IMouseEventSource`：Core 与 Win32 adapter 之间的轻量非持久化契约。
- `MouseRecorder`：injected 策略、事件映射、采样决策和 RecordingSession 集成。
- `MouseMoveSampler`：独立、可测试、默认 8,000 us 的确定性时间采样。
- Move、Left/Right/Middle/XButton1/XButton2 Down/Up、Vertical Wheel、Horizontal Wheel。
- Stopped / Running / Disposed 生命周期、Session 隔离和 callback/Stop 并发保护。
- 键盘与鼠标各自保留独立 Hook 线程，同时复用内部 `LowLevelHookThread` 和集中 Native API。

没有实现 Unified Recorder、JSON persistence、Validator、SendInput、Playback、Global Hotkeys 或 WPF 录制入口。

## 4. Files Changed

新增 Core 文件：

- `src/MacroRecorder.Core/Recording/Mouse/IMouseEventSource.cs`
- `src/MacroRecorder.Core/Recording/Mouse/MouseCaptureEvent.cs`
- `src/MacroRecorder.Core/Recording/Mouse/MouseCaptureKind.cs`
- `src/MacroRecorder.Core/Recording/Mouse/MouseMoveSampler.cs`
- `src/MacroRecorder.Core/Recording/Mouse/MouseRecorder.cs`
- `src/MacroRecorder.Core/Recording/Mouse/MouseRecorderState.cs`

新增 Infrastructure 文件：

- `src/MacroRecorder.Infrastructure/Windows/WindowsHookNativeMethods.cs`
- `src/MacroRecorder.Infrastructure/Windows/LowLevelHookThread.cs`
- `src/MacroRecorder.Infrastructure/Windows/Mouse/NativeMouseMethods.cs`
- `src/MacroRecorder.Infrastructure/Windows/Mouse/LowLevelMouseData.cs`
- `src/MacroRecorder.Infrastructure/Windows/Mouse/LowLevelMouseFlags.cs`
- `src/MacroRecorder.Infrastructure/Windows/Mouse/MouseHookMessageMapper.cs`
- `src/MacroRecorder.Infrastructure/Windows/Mouse/MouseHookService.cs`

修改 Infrastructure 文件：

- `src/MacroRecorder.Infrastructure/Windows/Keyboard/KeyboardHookService.cs`
- `src/MacroRecorder.Infrastructure/Windows/Keyboard/NativeKeyboardMethods.cs`

新增测试文件：

- `tests/MacroRecorder.Core.Tests/TestDoubles/FakeMouseEventSource.cs`
- `tests/MacroRecorder.Core.Tests/Recording/Mouse/MouseMoveSamplerTests.cs`
- `tests/MacroRecorder.Core.Tests/Recording/Mouse/MouseRecorderTests.cs`
- `tests/MacroRecorder.Core.Tests/Windows/Mouse/MouseHookMessageMapperTests.cs`
- `tests/MacroRecorder.Core.Tests/Windows/Mouse/MouseHookServiceTests.cs`

新增文档：`docs/phase-4-report.md`。修改：`README.md`。删除：无。

## 5. Architecture

```text
Windows mouse messages
          ↓
MouseHookService (Infrastructure / Win32)
          ↓ MouseCaptureEvent
MouseRecorder (Core / policy and lifecycle)
          ├── MouseMoveSampler (Move only)
          ↓ AddMouseEvent(...)
RecordingSession (timeline owner)
          ↓
MouseInputEvent
```

Core 不包含 P/Invoke、Windows 类型、WPF、文件 IO 或 Infrastructure 引用。Hook Service 不持有 RecordingSession，Sampler 不创建 Domain timestamp。

## 6. Win32 Implementation

- Hook type: `WH_MOUSE_LL`。
- 集中 API: SetWindowsHookExW、UnhookWindowsHookEx、CallNextHookEx、GetMessageW、PeekMessageW、PostThreadMessageW、GetModuleHandleW。
- `MSLLHOOKSTRUCT` 等价结构保存 POINT、mouseData、flags、time 和 pointer-sized ExtraInfo；POINT 使用有符号 `int`。
- Hook callback delegate 由 Service 和内部 Hook thread 保持强引用。
- `nCode < 0` 在 marshal 前跳过处理；未知、正常和异常路径都继续 CallNextHookEx，不阻止鼠标输入。
- 每个 Hook 使用自己的专用 background thread、消息队列和有界 shutdown；Keyboard/Mouse 不共用运行线程。
- 内部 `LowLevelHookThread` 从 Phase 3 生命周期代码提取，原 Keyboard lifecycle tests 全部继续通过。

## 7. Message Mapping

| Windows Message | Domain Event | Button |
|---|---|---|
| `WM_MOUSEMOVE` | MouseMove | None |
| `WM_LBUTTONDOWN/UP` | MouseButtonDown/Up | Left |
| `WM_RBUTTONDOWN/UP` | MouseButtonDown/Up | Right |
| `WM_MBUTTONDOWN/UP` | MouseButtonDown/Up | Middle |
| `WM_XBUTTONDOWN/UP` + XBUTTON1 | MouseButtonDown/Up | XButton1 |
| `WM_XBUTTONDOWN/UP` + XBUTTON2 | MouseButtonDown/Up | XButton2 |
| `WM_MOUSEWHEEL` | MouseVerticalWheel | None |
| `WM_MOUSEHWHEEL` | MouseHorizontalWheel | None |

未知 message 和未知 XButton 高位值不生成事件，但继续 Hook 链。按钮和滚轮都保存事件自身 POINT 坐标。

## 8. Wheel Parsing

Wheel delta 从 `mouseData` 高位 WORD 读取，并先转换为 signed 16-bit `short`，再提升为 Domain `int`。因此 `0xFF88` 得到 `-120`，不会变成 65416。自动测试覆盖垂直和水平的 `+120`、`-120`、`+60`、`-60`，不会假设 120 的整数倍，也不改变水平滚轮正负方向。

## 9. Injected Filtering

Infrastructure 将 `LLMHF_INJECTED` 和 `LLMHF_LOWER_IL_INJECTED` 转换为 `IsInjected` raw fact。MouseRecorder 在 sampling 前拒绝所有 injected event，因此 injected Move 不会推进 sampler baseline，按钮和滚轮也不会进入 RecordingSession。MouseInputEvent schema 没有增加 flags 字段。

## 10. MouseMove Sampling

- Default interval: `8_000 us`，理论上限约 125 recorded Move/s。
- 第一个 Move 总是接受。
- 后续 Move 与 last accepted Move 比较；被拒绝的 Move 不推进 baseline。
- interval 到达或超过时接受；相同坐标不做位置去重。
- interval `0` 接受全部 Move；负 interval 构造时拒绝。
- clock regression 会接受当前 Move 并重置 baseline，避免长期停录。
- 每次 MouseRecorder.Start 和 Stop/Dispose 都 Reset；Session B 第一条 Move 不受 Session A 影响。
- Button 和 Wheel 不调用 sampler，拖拽的 Down / Up 永不被采样丢失。

## 11. RecordingSession Integration

MouseCaptureEvent 不含 TimestampUs。MouseRecorder 只调用现有 `RecordingSession.AddMouseEvent(eventType, x, y, button, wheelDelta)`；RecordingSession 继续独占 Domain 相对微秒、顺序和非递减保证。`MSLLHOOKSTRUCT.time` 没有进入 Macro 或 Domain timeline。

## 12. Thread / Lifecycle

MouseHookService 与 MouseRecorder 均支持 Stopped → Running → Stopped 的重复录制；Start while Running 被拒绝，Stop 幂等，Dispose 自动停止且二次调用安全，Dispose 后禁止 Start。MouseRecorder.Stop 不停止 RecordingSession。

Recorder 的 callback lock 保护 active Session 和 sampler；Stop 先取消订阅并清引用，再停止 Hook。已经进入 callback 的事件完成后 Stop 才返回。上层契约保持：先 MouseRecorder.Stop，再 RecordingSession.Stop。确定性 BlockingMonotonicClock 测试覆盖 callback/Stop 竞争。

## 13. Tests

- 新增测试用例：60
- 原有测试用例：146，全部继续通过
- 总测试用例：206
- Passed: 206
- Failed: 0
- Skipped: 0

覆盖消息映射、负坐标、五种按钮、XButton 高位解析、未知 XButton、signed wheel、非 120 delta、injected flags、RecordingSession timestamp、采样边界、last accepted、interval 0、clock regression、相同坐标、按钮/滚轮绕过、拖拽、Stop、Restart、Session 隔离、sampler reset、Dispose、并发边界，以及真实 Windows Mouse Hook 的安装/卸载/重启。

## 14. Manual Mouse Verification

普通用户权限的定时 harness 没有保存坐标轨迹，仅输出场景和计数：

| Scenario | Result |
|---|---|
| Mouse Move | PASS |
| Left Click | PASS |
| Right Click | PASS |
| Middle Click | PASS |
| Vertical Wheel Up / Down | PASS |
| Horizontal Wheel | NOT_OBSERVED：当前硬件操作期间没有事件 |
| XButton1 | PASS |
| XButton2 | PASS |
| Left Drag | PASS |
| Stop boundary | PASS，停止结果保持 842 个事件 |
| Session restart | PASS，新 Session 517 个事件 |

水平滚轮未使用 SendInput 补测；其正负解析由单元测试覆盖。验证程序正常退出，临时 harness、摘要和构建输出已删除，无残留 Hook 进程。

## 15. Sampling / Performance Observation

- 持续时间：30 秒
- Raw MouseMove count: 23,581
- Recorded MouseMove count: 2,858
- Recorded rate: 约 95.3/s，低于 8 ms 采样的理论上限 125/s
- Reduction: 约 87.9%
- Process CPU time during observation: 1,141 ms
- Working set delta: 3,534,848 bytes（包含首次运行/JIT；30 秒内未见快速异常增长）

验证期间能够完成连续移动与后续所有必测操作，Hook 没有超时、丢失终止状态或阻止鼠标输入。该观察不是正式 benchmark。

## 16. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

最终命令对完整 solution build/test，而非只运行新增测试。

## 17. Dependency Check

新增 package：否。生产项目继续只使用 BCL 和 Win32 P/Invoke；测试沿用既有 Microsoft.NET.Test.Sdk、xUnit 和 VS adapter。没有引入 SharpHook、InputSimulator、WindowsInput 或其他 Hook 框架。

## 18. Schema Compatibility

**No Macro Schema Change.** MacroSchema、InputEvent discriminator、MouseInputEvent JSON fields、MouseButton、InputEventType 和 metadata 均未修改。Mouse flags 只用于运行时过滤，不进入持久化模型；Phase 1 JSON round-trip tests 全部继续通过。

## 19. Security / Privacy Check

- Hook 仅由显式 StartCapture 安装；应用启动和 Idle 不监听鼠标。
- 不隐藏运行、不加入 autorun/service、不提升权限。
- 不保存完整鼠标路径、不写 Mouse event JSON、不上传、不联网、不遥测。
- callback 始终继续 Hook 链，不返回非零阻止输入。
- callback 没有文件 IO、JSON、网络、Thread.Sleep、同步 UI invoke 或逐事件日志。
- 人工验证摘要只包含场景、计数和进程汇总数字，临时数据已删除。

## 20. Known Limitations

以下属于后续阶段而非缺陷：

- Unified Recorder 尚未实现。
- JSON Persistence 尚未实现。
- Macro Validator 尚未实现。
- Playback / SendInput 尚未实现。
- Global Hotkeys 尚未实现。
- WPF 正式录制 UI 尚未实现。
- 高级 path simplification、interpolation、Bezier、adaptive sampling 尚未实现。
- 当前设备没有观察到水平滚轮硬件事件，但纯映射测试已覆盖。

## 21. Git Status

提交前检查完整 diff、whitespace、依赖、API 分布、Schema 和生成物；提交后最终状态与 Commit Hash 在对话报告确认。提交不包含临时鼠标日志、坐标轨迹、harness、bin、obj、TestResults、JSON、dump、截图、SDK、profiling data 或个人绝对路径。

## 22. Next Phase

只建议 **Phase 5 — Unified Recorder**。本阶段提交后停止，不创建 Unified Recorder 或进入 Phase 5。
