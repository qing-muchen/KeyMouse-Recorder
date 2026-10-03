# Phase 5 — Unified Recorder Report

## 1. Status

Completed。统一键盘/鼠标录制生命周期、状态机、Session ownership、事务式启动回滚、Stop / Cancel / Dispose 的 best-effort cleanup、并发序列化和 Session 隔离均已实现。Release Build、228 项自动测试及真实 Windows 键鼠统一录制验收通过。未进入 Phase 6。

## 2. Git

- Branch: `main`
- Previous Commit: `5c4cbae5d7157b19227231272a1246839e53d2bf`
- New Commit: 本报告随 `feat(recording): add unified recording coordinator` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Commit Message: `feat(recording): add unified recording coordinator`
- Remote Tracking: 开始时为 `main...origin/main [ahead 4]`。没有 push、rebase、reset、amend 或改写历史。

## 3. Implemented

- `UnifiedRecorder`：一次完整录制的生命周期拥有者。
- `UnifiedRecorderState`：Stopped、Starting、Recording、Stopping、Cancelling、Disposed。
- `IKeyboardRecorder` / `IMouseRecorder`：用于协调和故障注入的最小组件边界，现有具体 Recorder 实现这些接口。
- 每次 Start 创建新的 `RecordingSession`，成功启动 KeyboardRecorder 和 MouseRecorder 后才进入 Recording。
- 启动部分失败时反向停止所有已尝试组件、取消 Session、清除 active session 并恢复 Stopped。
- 正常 Stop 依次停止 Keyboard、Mouse，再停止 Session 并返回 `RecordingResult`。
- Cancel 停止两个 Recorder、取消 Session、丢弃事件且不返回正式结果。
- Dispose 在 Recording 中先 Cancel 语义清理，再释放两个组件；所有清理步骤都会尝试。

未实现 JSON persistence、Macro Validator、Playback、SendInput、Global Hotkeys、环境信息采集或正式 WPF 录制 UI。

## 4. Files Changed

新增生产文件：

- `src/MacroRecorder.Core/Recording/UnifiedRecorder.cs`
- `src/MacroRecorder.Core/Recording/UnifiedRecorderState.cs`
- `src/MacroRecorder.Core/Recording/Keyboard/IKeyboardRecorder.cs`
- `src/MacroRecorder.Core/Recording/Mouse/IMouseRecorder.cs`

修改生产文件：

- `src/MacroRecorder.Core/Recording/Keyboard/KeyboardRecorder.cs`
- `src/MacroRecorder.Core/Recording/Mouse/MouseRecorder.cs`

新增测试文件：

- `tests/MacroRecorder.Core.Tests/Recording/UnifiedRecorderTests.cs`
- `tests/MacroRecorder.Core.Tests/TestDoubles/FakeKeyboardRecorder.cs`
- `tests/MacroRecorder.Core.Tests/TestDoubles/FakeMouseRecorder.cs`

修改文档：`README.md`。新增文档：`docs/phase-5-report.md`。删除：无。人工 harness、汇总文件、构建输出均已删除。

## 5. Architecture

```text
Application / future ViewModel
            ↓
     UnifiedRecorder
       ├── KeyboardRecorder → KeyboardHookService
       ├── MouseRecorder    → MouseHookService
       └── RecordingSession
                  ↓
           RecordingResult
```

UnifiedRecorder 只协调低频生命周期并持有 active Session，不处理 Win32 消息、采样、时间戳或持久化。KeyboardRecorder 和 MouseRecorder 的 callback 继续直接调用同一个 RecordingSession，Core 仍不依赖 Infrastructure、Win32、WPF 或文件系统。

## 6. State Machine

```text
Stopped → Starting → Recording → Stopping   → Stopped
                           └──→ Cancelling → Stopped
Stopped / Recording ───────────────────────→ Disposed
Starting failure → rollback → Stopped
```

`State` 使用单一 enum 表达，避免多个 bool 形成非法组合。同步生命周期 API 的过渡状态很短，但 Starting、Stopping、Cancelling 仍可观察；自动测试在阻塞 fake 中验证 Starting，并在组件 Stop 回调中验证 Stopping / Cancelling。

## 7. Start Semantics

StartRecording 要求当前为 Stopped，然后创建新的 RecordingSession，按 Keyboard → Mouse 顺序启动。两个组件成功后状态才变为 Recording。Keyboard start 失败时 Mouse 不启动；Mouse start 失败时先 Stop Mouse attempt，再 Stop Keyboard，最后 Cancel Session。任何 rollback failure 都保留并与原错误聚合，最终状态仍恢复 Stopped。

## 8. Stop Semantics

StopRecording 只允许在 Recording 状态调用。它先把状态设为 Stopping，再依次调用 KeyboardRecorder.Stop、MouseRecorder.Stop，最后才调用 RecordingSession.Stop。fake callback 明确验证两个组件停止时 Session 仍处于 Recording。成功时返回包含 DurationUs、EventCount 和不可变 Events 的 RecordingResult，并恢复 Stopped。

任一组件 Stop 失败不会短路后续 cleanup；另一个组件和 Session 仍会处理。清理完成后单个异常按原类型和 stack rethrow，多项异常以 AggregateException 汇总。即使抛错，active session 也会清除且状态不会残留 Recording。

## 9. Cancel Semantics

CancelRecording 只允许在 Recording 状态调用。它进入 Cancelling，停止 Keyboard / Mouse，调用 Session.Cancel 清除事件，不构造或返回 RecordingResult，然后清除 active session 并恢复 Stopped。Cancel 失败路径也会继续尝试全部清理；Cancel 后可以立即 Start 新 Session。

## 10. Error Handling

- Keyboard start failure：回滚 Keyboard attempt、取消 Session，Mouse 不启动，状态 Stopped。
- Mouse start failure：反向停止 Mouse attempt 和已启动 Keyboard，再取消 Session，状态 Stopped。
- Recorder stop failure：继续停止另一个 Recorder 并 Stop / Cancel Session。
- 多项 cleanup failure：按执行顺序保存在 AggregateException 中。
- Dispose failure：两个 Stop、Session Cancel 及两个 Dispose 都会尽力执行，最终状态 Disposed。
- 代码没有空 catch；宽泛 catch 只存在于注明理由的事务回滚 / best-effort cleanup 边界。

## 11. Thread Safety

一个私有 lifecycle lock 串行化 Start、Stop、Cancel 和 Dispose，使并发 Start/Start 只能一个成功，并发 Stop/Cancel 只能一个完成业务转换，另一个收到明确 InvalidOperationException。独立 state lock 允许在组件启动或停止期间读取 Starting / Stopping / Cancelling。

虽然组件 Start / Stop 在 lifecycle lock 的范围内执行，它们的 callback 路径不读取 UnifiedRecorder 的任何锁；事件仍按 Recorder → RecordingSession 流动。因此低频生命周期互斥不会加入输入 hot path，也不会形成 callback 等待 coordinator 的锁环。组件自身仍负责等待已进入的 callback 完成。

## 12. RecordingResult

KeyboardRecorder 和 MouseRecorder 持有同一个 RecordingSession 引用，分别直接调用 `AddKeyboardEvent` / `AddMouseEvent`。Session 的单一临界区负责时间戳和 append 顺序，因此结果天然是一条键鼠混合时间线。UnifiedRecorder 没有分列表、Merge、OrderBy、重写 TimestampUs 或重新 normalize；相同 TimestampUs 保留原始 Add 顺序。

## 13. Tests

- 新增测试用例：22
- 原有测试用例：206，全部继续通过
- 总计：228
- Passed: 228
- Failed: 0
- Skipped: 0

覆盖 initial / transitional / final states、共享 Session、键盘与鼠标事件、统一顺序、同 timestamp 顺序、Duration / EventCount、Stop 前置顺序、Cancel 丢弃、Stop/Cancel 后 restart、fresh Session、结果隔离、sampler reset、injected filtering、Keyboard / Mouse start failure、反向 rollback、Stop / Cancel failure、单项和多项异常、Dispose from Stopped / Recording、Dispose 幂等、Disposed 后拒绝操作、并发 Start/Start 及并发 Stop/Cancel。

## 14. Manual Unified Recording Verification

普通用户权限下通过可见 console 显式启动真实 UnifiedRecorder；没有把 Recorder 接入应用启动或 Idle。用户执行 Mouse Move、Left Click、A、Shift+A、Vertical Wheel、Right Click 后停止：

- Result Event Count: 689
- Duration: 18,609,179 us
- Keyboard events: 30
- Mouse events: 659
- Keyboard / Mouse category transitions: 4
- 指定操作事实：PASS
- Timestamp 非递减：PASS
- Hook callback exception: 无

验证器只在内存中检查 VirtualKey / EventType 事实，屏幕及临时摘要只包含计数、类型状态与时间，没有输出完整键盘文本或保存事件流。

## 15. Restart / Cancel Verification

- 第二次 Start / Stop：314 个事件，包含键盘和鼠标；与第一次使用不同 RecordingResult，第一次计数保持 689，Session isolation PASS。
- Start / 少量操作 / Cancel：没有 RecordingResult 返回，状态恢复 Stopped。
- Cancel 后再次 Start / Stop：339 个事件，包含键盘和鼠标，恢复录制 PASS。
- 最终 Dispose：Keyboard / Mouse Hook 都报告非 Running，State 为 Disposed。

## 16. Short Stability Observation

- 额外持续时间：300,040,659 us（约 5 分钟）
- CPU time: 375 ms
- Working set start: 28,561,408 bytes
- Working set end / observed max: 29,990,912 bytes
- Working set observed minimum: 28,880,896 bytes
- Start-to-end delta: 1,429,504 bytes
- Stop: 正常
- Hook callback exception: 无
- Hook thread cleanup: 正常

该 5 分钟运行发生在后台 PTY 会话，未收到交互桌面的输入事件，因此只验证了空闲 Hook 生命周期、资源曲线和停止清理，不能代表有负载的 5 分钟录制。随后可见交互窗口的三次成功结果共处理 1,342 个真实事件，首段持续约 18.6 秒；正式长时间有负载测试仍留给 Phase 20。

## 17. Build

- Configuration: Release
- SDK: 10.0.401（项目内已存在的 SDK）
- Warnings: 0
- Errors: 0

最终验证对完整 solution build/test，而非只编译新文件。

## 18. Dependency Check

新增 package：否。App、Core、Infrastructure 没有 PackageReference；测试仍只使用既有 Microsoft.NET.Test.Sdk 17.14.1、xUnit 2.9.3 和 xunit.runner.visualstudio 3.1.1。没有引入 Hook、DI、并发或 assertion 第三方库。

## 19. Schema Compatibility

**No Macro Schema Change.** `src/MacroRecorder.Core/Models` 和 `Schema` 相对 Phase 4 无 diff；Macro、InputEvent、JSON discriminator、enum 和 metadata 均未修改。UnifiedRecorder、状态和 recorder interfaces 都是非持久化运行时类型。

## 20. Security / Privacy

- 只有显式调用 StartRecording 才安装两个 Hook；应用启动和 Idle 不录制。
- 没有隐藏录制、后台启动入口、网络、上传、遥测、权限提升或输入阻止。
- 没有 JSON / 文件事件持久化；生产代码没有输入内容日志。
- 人工验证只使用 A、B、Shift、Ctrl 和普通鼠标操作；没有输入密码、Token、账号或聊天文本。
- 验证临时文件全部删除；仓库不保留按键、坐标、宏、日志或个人路径。

## 21. Known Limitations

以下属于后续 Phase，而非本阶段缺陷：

- JSON Persistence 尚未实现。
- Macro Validator 尚未实现。
- Playback / SendInput 尚未实现。
- Global Hotkeys 尚未实现。
- WPF 正式录制 UI 尚未实现。
- Environment Metadata 尚未实现。
- 5 分钟有负载稳定性和正式长时间压力测试尚未完成；Phase 20 将覆盖。

## 22. Git Status

提交前已检查完整 diff、whitespace、依赖、Schema、静态 API 分布和生成物；提交后最终 status 与 Commit Hash 在对话报告确认。提交不包含临时 harness、摘要、事件文件、JSON、bin、obj、TestResults、日志、dump、截图、SDK 或个人绝对路径。

## 23. Next Phase

只建议 **Phase 6 — Macro JSON Persistence**。本阶段提交后停止，不实现 JSON 文件 IO、Validator、Playback 或 SendInput。
