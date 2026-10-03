# Phase 3 — Keyboard Recorder Report

## 1. Status

Completed。全局低级键盘 Hook、Recorder 集成、注入事件过滤、生命周期与异常边界均已实现；Release Build、全量自动测试和真实键盘人工验证通过。未进入 Phase 4。

## 2. Git

- Branch: `main`
- Previous Commit: `d97a112a3755a4b128a048aee93c3554e88bd8e3`
- New Commit: 本报告随 `feat(input): add global keyboard recording hook` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Remote Tracking Status: 开始时为 `main...origin/main [ahead 2]`。本阶段没有 push、rebase、reset、amend 或改写历史。

## 3. Implemented

- `KeyboardHookService`：可重启的 Windows `WH_KEYBOARD_LL` managed event source。
- `IKeyboardEventSource` / `KeyboardCaptureEvent`：Core 与 Win32 adapter 之间的最小平台无关契约。
- `KeyboardRecorder`：把 raw transition 映射到现有 RecordingSession 的类型化键盘 API。
- `LLKHF_INJECTED` / `LLKHF_LOWER_IL_INJECTED` 分类与录制策略过滤。
- Stopped / Running / Disposed 生命周期，以及 Stop 后绑定新 Session 再次录制。
- Native callback 最外层异常边界与 `LastCallbackException` 可观察错误。

没有实现 Mouse Hook、Unified Recorder、SendInput、Playback、全局快捷键、持久化或 WPF 录制入口。

## 4. Files Changed

新增生产文件：

- `src/MacroRecorder.Core/Recording/Keyboard/IKeyboardEventSource.cs`
- `src/MacroRecorder.Core/Recording/Keyboard/KeyboardCaptureEvent.cs`
- `src/MacroRecorder.Core/Recording/Keyboard/KeyboardTransition.cs`
- `src/MacroRecorder.Core/Recording/Keyboard/KeyboardRecorderState.cs`
- `src/MacroRecorder.Core/Recording/Keyboard/KeyboardRecorder.cs`
- `src/MacroRecorder.Infrastructure/Properties/AssemblyInfo.cs`
- `src/MacroRecorder.Infrastructure/Windows/Keyboard/NativeKeyboardMethods.cs`
- `src/MacroRecorder.Infrastructure/Windows/Keyboard/LowLevelKeyboardData.cs`
- `src/MacroRecorder.Infrastructure/Windows/Keyboard/LowLevelKeyboardFlags.cs`
- `src/MacroRecorder.Infrastructure/Windows/Keyboard/KeyboardHookMessageMapper.cs`
- `src/MacroRecorder.Infrastructure/Windows/Keyboard/KeyboardHookService.cs`

新增测试文件：

- `tests/MacroRecorder.Core.Tests/TestDoubles/FakeKeyboardEventSource.cs`
- `tests/MacroRecorder.Core.Tests/Recording/Keyboard/KeyboardRecorderTests.cs`
- `tests/MacroRecorder.Core.Tests/Windows/Keyboard/KeyboardHookMessageMapperTests.cs`
- `tests/MacroRecorder.Core.Tests/Windows/Keyboard/KeyboardHookServiceTests.cs`

新增文档：`docs/phase-3-report.md`。修改：`README.md`。删除：无。

## 5. Architecture

```text
Windows keyboard messages
          ↓
KeyboardHookService (Infrastructure / Win32)
          ↓ KeyboardCaptureEvent
KeyboardRecorder (Core / policy and lifecycle)
          ↓ AddKeyboardEvent(...)
RecordingSession (Core / timeline owner)
          ↓
KeyboardInputEvent
```

Core 不包含 P/Invoke、Windows 类型、WPF、文件 IO 或 Infrastructure 引用。Hook Service 不持有 RecordingSession；Recorder 不知道 user32 或 message loop。

## 6. Win32 Implementation

- Hook type: `WH_KEYBOARD_LL`。
- 集中 API: `SetWindowsHookExW`、`UnhookWindowsHookEx`、`CallNextHookEx`、`GetMessageW`、`PeekMessageW`、`PostThreadMessageW`、`GetModuleHandleW`。
- `KBDLLHOOKSTRUCT` 使用 pointer-sized `nuint ExtraInfo`，其 `time` 只保留在 Infrastructure struct 中。
- Service 构造时创建 callback delegate field 并在整个 Hook 生命周期保持强引用。
- `nCode < 0` 在 marshal 前被拒绝处理；已知、未知和异常路径最终都调用 `CallNextHookEx`，Recorder 不阻止用户输入。
- `SetWindowsHookExW` 返回零时抛出带 Win32 error 的 `Win32Exception`；线程退出前尝试且只尝试一次 Unhook。

## 7. Message Mapping

| Windows message | Managed transition | Domain event type |
|---|---|---|
| `WM_KEYDOWN` | KeyDown | KeyboardKeyDown |
| `WM_SYSKEYDOWN` | KeyDown | KeyboardKeyDown |
| `WM_KEYUP` | KeyUp | KeyboardKeyUp |
| `WM_SYSKEYUP` | KeyUp | KeyboardKeyUp |

未知消息不发布 managed event，但继续 Hook 链。Key repeat 的每个 KeyDown 都原样提交，不去重。

## 8. Injected Event Filtering

Infrastructure 把官方 flags 解析为 `IsInjected` raw fact；只要存在 `LLKHF_INJECTED` 或 `LLKHF_LOWER_IL_INJECTED` 任一位，KeyboardRecorder 就不向 Session 提交。Extended、AltDown 以及它们的普通组合仍被接受。Domain `KeyboardInputEvent.Flags` 对已接受事件保留未经改写的 `uint` 位值。

所有 injected keyboard input 默认不录制。本阶段没有使用 SendInput 制造测试事件；过滤测试直接构造 managed raw event 和 flags 组合。

## 9. RecordingSession Integration

KeyboardCaptureEvent 不含 TimestampUs。KeyboardRecorder 只调用：

```text
RecordingSession.AddKeyboardEvent(eventType, virtualKey, scanCode, flags)
```

因此相对微秒、非递减保证、并发顺序仍完全由 Phase 2 RecordingSession 与 IMonotonicClock 生成。`KBDLLHOOKSTRUCT.time` 没有进入 Domain timeline，Phase 1 schema 也没有新增字段。

## 10. Thread / Lifecycle Design

KeyboardHookService 使用专用、命名的 background thread。线程先用 PeekMessage 建立消息队列，再安装 Hook 并通过同步信号通知 Start；Start 只有安装成功后才返回。GetMessage 驱动 callback，Stop 通过 `WM_QUIT` 请求退出并最多等待 10 秒，禁止无限 Join 或 Thread.Abort。

```text
Stopped → StartCapture → Running → StopCapture → Stopped
                                      ↓
                                  Dispose → Disposed
```

Start while Running 被拒绝；Stop 幂等；Stop 后可重新 Start；Dispose 自动停止并禁止后续 Start。KeyboardRecorder 采用相同的可重启语义，但每次 Start 必须绑定一个新的 active RecordingSession，并且 Stop 不停止 Session。

## 11. Thread Safety

Service 用生命周期锁串行化 Start / Stop / Dispose，并用状态锁保护 thread context。Recorder 分离生命周期锁与 callback 锁：Stop 先在 callback lock 下取消订阅、清 active Session，再停止来源；已经进入临界区的 callback 完成后 Stop 才返回。因此上层遵循 `KeyboardRecorder.Stop()` 后再 `RecordingSession.Stop()` 时，不会有迟到 callback 写入已停止 Session。

确定性并发测试用 BlockingMonotonicClock 卡住 callback，确认 Stop 等待该 callback 完成。Session A / B 测试确认 Stop 清除旧引用，重启后事件不会串入旧 Session。

## 12. Error Handling

- SetWindowsHookEx / GetModuleHandle / GetMessage / PostThreadMessage / Unhook 的失败都转为带上下文的 Win32Exception；安装失败同步反馈给 Start。
- native callback 最外层有注明理由的 `catch (Exception)`，异常不会穿过 unmanaged boundary；最近异常写入 `LastCallbackException`，然后继续 CallNextHookEx。
- callback 内没有文件 IO、JSON、网络、Thread.Sleep、同步 UI invoke 或逐键日志。
- Recorder Start 失败会回滚订阅、active Session 和状态；生命周期编程错误不会用空 catch 静默吞掉。

## 13. Tests

- 新增测试用例：32
- 原有测试用例：114，全部继续通过
- 总测试用例：146
- Passed: 146
- Failed: 0
- Skipped: 0

覆盖四类 Windows message、未知消息、负 hook code、injected flags 组合、raw 字段和 Session timestamp、Shift 序列、重复 KeyDown、不活动边界、Start 失败回滚、Start / Stop / Restart / Dispose、Session 隔离、callback / Stop 并发、真实 Windows Hook 的安装/卸载/重启，以及 subscriber exception containment。

## 14. Manual Keyboard Verification

在普通用户权限的可见 console harness 中，仅使用非敏感测试键：

| Scenario | Expected | Observed |
|---|---|---|
| A | Down → Up | PASS，15 个窗口期事件 |
| Shift+A | Shift Down → A Down → A Up → Shift Up | PASS，6 个事件 |
| Ctrl+C | Ctrl Down → C Down → C Up → Ctrl Up | PASS，8 个事件 |
| Alt+A | System key 路径仍形成完整 chord | PASS，13 个事件 |
| Long press B | 多个 B Down，最终 B Up | PASS，17 个事件 |
| Stop boundary | Stop 后按键不增加结果 | PASS，停止结果 6 个事件 |
| Restart isolation | 新 Session 收到 Z，旧 Session 不含 Z | PASS，新 Session 4 个事件 |

事件数包含每步用于结束 console 输入的 Enter，报告不保存逐键日志或输入文本。验证程序正常退出；临时 harness、摘要和构建输出已删除，没有残留进程或 Hook thread。

## 15. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

最终验证使用仓库脚本进行完整 solution build 和 test，不只运行新增测试。

## 16. Dependency Check

新增 NuGet：否。生产项目继续只使用 BCL；测试继续使用既有 Microsoft.NET.Test.Sdk、xUnit 和 VS adapter。Infrastructure 原有的 Core ProjectReference 没有变化。

## 17. Schema Compatibility

**No Macro Schema Change.** MacroSchema、InputEvent discriminator、KeyboardInputEvent JSON fields、MouseInputEvent 和 metadata 均未修改。全部 Phase 1 JSON round-trip 测试继续通过。KeyboardCaptureEvent 和 recorder state 是非持久化运行时类型。

## 18. Security / Privacy Check

- Hook 只在调用 StartCapture 后安装；应用启动和 Idle 不监听输入。
- 本阶段没有 WPF 自动启动入口、后台隐藏运行、autorun、service 或 elevation。
- Stop 先取消 managed 订阅并卸载 Hook；人工验证确认随后按键不再进入结果。
- 不重建输入文本，不持久化逐键日志，不写 Macro 文件，不联网、不上传事件、不添加遥测。
- callback 始终继续 Hook 链，不过滤或阻止系统按键。

## 19. Known Limitations

以下属于后续阶段而非缺陷：

- Mouse Hook 尚未实现。
- Unified Recorder 尚未实现。
- MouseMove Sampling 尚未实现。
- JSON File IO 尚未实现。
- Macro Validator 尚未实现。
- Playback / SendInput 尚未实现。
- Global Hotkeys 尚未实现。
- 正式 WPF 录制界面尚未接入。

当前 `LastCallbackException` 是进程内诊断状态，尚未接入异步错误通知或 UI。Recorder 与 Session 的上层调用契约是先 Stop Recorder，再 Stop Session；Phase 5 将由 Unified Recorder 统一执行该顺序。

## 20. Git Status

提交前已检查完整 diff、whitespace、依赖、静态 API 分布、生成物和 status；提交后最终状态与 Commit Hash 在对话报告确认。预计只包含本节列出的 17 个 Phase 3 文件，不包含真实按键日志、临时 TXT / JSON、bin、obj、TestResults、SDK、dump、截图或个人绝对路径。

## 21. Next Phase

只建议 **Phase 4 — Mouse Recorder**。本阶段提交后停止，不实现 Mouse Hook 或进入 Phase 4。
