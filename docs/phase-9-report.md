# Phase 9 — Input Injector Report

## 1. Status

Completed。Core 输入注入边界、Windows SendInput 实现、键盘与鼠标映射、当前虚拟桌面坐标归一化、严格批次结果检查、自注入 marker、Fake native 自动测试和真实安全桌面验证均已完成。未进入 Phase 10。

## 2. Git

- Branch: `main`
- Previous Commit: `dd1e5a03661a64cba297a38c890dfbf121bfba43`
- New Commit: 本报告随 `feat(input): add Windows input injector` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Commit Message: `feat(input): add Windows input injector`
- Remote Tracking: 开始时为 `main...origin/main [ahead 8]`。没有 push、rebase、reset、amend 或历史重写。

## 3. Implemented

- Core `IInputInjector.Inject(InputEvent)` 平台无关边界。
- `InputInjectionException`，保留 RequestedInputCount、InjectedInputCount 和 Win32ErrorCode。
- `WindowsInputInjector` 单事件即时注入实现。
- 可替换 `IWindowsInputNativeApi` 和生产 `WindowsInputNativeApi`。
- 正确布局的 INPUT union、KEYBDINPUT、MOUSEINPUT，pointer-sized `nuint ExtraInfo`。
- Scan Code 优先、VirtualKey fallback、KeyUp / ExtendedKey 映射。
- 当前 Virtual Desktop provider、负原点坐标归一化、严格越界拒绝。
- Mouse Move、Left/Right/Middle/XButton Down/Up、Vertical/Horizontal Wheel。
- Button / Wheel 的 move + action 原子 SendInput batch。
- 所有原生 INPUT 的稳定 self-injection marker。
- SendInput 全量计数要求、partial/failure 明确异常。

没有实现 PlaybackEngine、Timeline、等待、速度、Pause/Resume、Stop、Emergency Stop、全局热键或 GUI 扩展。

## 4. Files Changed

新增 Core：

- `src/MacroRecorder.Core/InputInjection/IInputInjector.cs`
- `src/MacroRecorder.Core/InputInjection/InputInjectionException.cs`

新增 Infrastructure：

- `src/MacroRecorder.Infrastructure/Windows/Input/WindowsInputInjector.cs`
- `src/MacroRecorder.Infrastructure/Windows/Input/WindowsInputNativeApi.cs`
- `src/MacroRecorder.Infrastructure/Windows/Input/IWindowsInputNativeApi.cs`
- `src/MacroRecorder.Infrastructure/Windows/Input/NativeInput.cs`
- `src/MacroRecorder.Infrastructure/Windows/Input/KeyboardInputMapper.cs`
- `src/MacroRecorder.Infrastructure/Windows/Input/MouseInputMapper.cs`
- `src/MacroRecorder.Infrastructure/Windows/Input/IVirtualDesktopProvider.cs`
- `src/MacroRecorder.Infrastructure/Windows/Input/WindowsVirtualDesktopProvider.cs`

新增测试：`tests/MacroRecorder.Core.Tests/Windows/Input/WindowsInputInjectorTests.cs`。修改：`README.md`。新增：`docs/phase-9-report.md`。删除：无。临时人工验证项目、输出、日志及测试结果已删除。

## 5. Architecture

```text
InputEvent
    ↓
IInputInjector (Core)
    ↓
WindowsInputInjector (Infrastructure)
   ├── KeyboardInputMapper
   ├── MouseInputMapper
   └── IVirtualDesktopProvider
    ↓
NativeInput[]
    ↓
IWindowsInputNativeApi
    ↓
SendInput
```

注入器每次只接受一个已构造的领域事件并立即执行。它不读取 Macro、不访问文件、不校验完整时间线，也不使用 TimestampUs 调度。

## 6. Native Structures

`NativeInput` 使用 sequential outer struct 和 explicit union。Keyboard / Mouse input 都使用 Win32 对应字段宽度；`ExtraInfo` 为 `nuint`，适配 x64 pointer size。`cbSize` 每次由 `Marshal.SizeOf<NativeInput>()` 取得并传入 native boundary，不硬编码 40。

P/Invoke 集中在 `WindowsInputNativeApi` 和 `WindowsVirtualDesktopProvider`。产品实现不调用 `keybd_event`、`mouse_event` 或 `SetCursorPos`。

## 7. Keyboard Mapping

- `ScanCode != 0`：`wVk = 0`，保留 16-bit scan code，加入 `KEYEVENTF_SCANCODE`。
- `ScanCode == 0`：使用 16-bit VirtualKey fallback，不设置 ScanCode flag。
- KeyboardKeyUp 根据 EventType 加 `KEYEVENTF_KEYUP`。
- Low-level Extended bit 映射 `KEYEVENTF_EXTENDEDKEY`。
- AltDown、Injected、LowerIntegrityInjected、Up 等 raw flags 不会直接复制到 KEYBDINPUT。
- VirtualKey 或 ScanCode 超过 `ushort.MaxValue` 时明确拒绝。
- TimestampUs 对生成 INPUT 没有影响。

## 8. Mouse Mapping

- Move：一个 `MOVE | ABSOLUTE | VIRTUALDESK` INPUT。
- Button：同一个 SendInput batch 中先移动，再发送 Down / Up。
- Wheel：同一个 batch 中先移动，再发送 WHEEL / HWHEEL。
- XButton1 / XButton2 使用 XDOWN / XUP，并在 mouseData 中分别保存 1 / 2。
- WheelDelta 保留原始 signed int bit pattern，支持 ±120、±60 和其他非零值。
- Move 的 Button / WheelDelta、Button 的 None / WheelDelta、Wheel 的 Button / zero delta 组合会被拒绝。

## 9. Virtual Desktop Coordinates

每个鼠标事件通过 GetSystemMetrics 读取当前 `SM_XVIRTUALSCREEN / YVIRTUALSCREEN / CXVIRTUALSCREEN / CYVIRTUALSCREEN`。录制环境 metadata 不传给注入器，本阶段不做跨环境适配。

归一化采用：

```text
round((coordinate - origin) * 65535 / (length - 1), AwayFromZero)
```

length=1 时结果为 0。使用 long 中间值避免 origin + length 的 int overflow；允许负 Left / Top 与负事件坐标，但严格拒绝当前虚拟桌面外的坐标，不做 clamp。

## 10. SendInput Error Handling

一个领域事件生成 1 或 2 个 NativeInput，并只调用一次 SendInput。返回数量必须与请求数量完全相同；0 或 partial count 均抛 `InputInjectionException`。异常保留请求数、实际注入数和紧邻 P/Invoke 取得的 Win32 error code，不把失败伪装成成功。

## 11. Injection Marker

所有 Keyboard、MouseMove、Button action 和 Wheel action 的 `dwExtraInfo` 都设置同一个 pointer-sized `0x4B4D5243` marker。它只存在于 Infrastructure runtime，不进入 InputEvent、Macro 或 JSON Schema。

Recorder 的主要过滤依据仍是 Windows 的 Injected / LowerIntegrityInjected flags；marker 为额外可识别信息，没有修改 Phase 3/4 Hook 契约。

## 12. Tests

- Phase 8 baseline: 406
- Phase 9 新增: 66
- 总计: 472
- Passed: 472
- Failed: 0
- Skipped: 0

覆盖 Scan Code / VirtualKey、Down / Up、Extended、raw flags 隔离、marker、Timestamp 无关性、主屏与负原点、负 X/Y、大虚拟桌面、全部边界和单像素轴、无效 geometry、五类鼠标按钮及 XButton data、垂直/水平正负精细滚轮、语义防御、1/2 项成功、0/partial failure、Win32 error、泛型路由、未知 subtype、null、领域对象不变性和动态 `Marshal.SizeOf`。

默认测试全部使用 Fake native API；`dotnet test` 不向桌面注入输入。

## 13. Manual Keyboard Verification

使用一次性项目专用 WinForms 窗口，非浏览器、聊天窗口、密码框或 IDE。注入顺序：

```text
A Down → A Up
Shift Down → A Down → A Up → Shift Up
```

窗口观察到文本 `aAa`（最后一个 `a` 来自隔离验证），Target KeyDown=4、KeyUp=4；低层 Hook 观察到 8 个 injected keyboard events。CapsLock / keyboard state 会影响字符大小写，但 Down / Up 和 Shift 顺序均被实际窗口接收。清理路径再次发送 A Up / Shift Up，未留下按键状态。

## 14. Manual Mouse Verification

- 100 个安全 MouseMove：Hook 观察到 104 个 injected move（包含 button/wheel 批次的预移动），最终预期坐标与实际 Cursor 坐标均为 `(1389, 841)`。
- LeftDown / LeftUp：专用窗口按钮收到 1 次 Click，`GetAsyncKeyState` 在 Down/Up 之间观察到按下状态；Hook 各观察到 1 次 injected down/up。
- Vertical Wheel +120：测试窗口观察到 delta 120，Hook 观察到 1 次 injected wheel。
- 100 次连续移动没有明显异常延迟或 CPU 异常。
- Horizontal Wheel 与 XButton 未做真实注入；完整自动 mapping tests 通过。

## 15. Recording Isolation Verification

在独立短录制窗口内注入 A Down/Up 和一个 MouseMove。低层 Hook 观察到 2 个 injected keyboard events、1 个 injected mouse event、0 个非 injected 外部事件；停止后的 `RecordingResult.EventCount = 0`。这证明 Phase 3/4 Recorder 依据 injected flags 排除了本软件 SendInput 事件。

## 16. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

最终验证针对完整 solution。

## 17. Dependency Check

新增 PackageReference：否。生产代码只使用 .NET BCL 和 user32.dll；Core 仍不引用 Win32、WPF、文件系统或 Infrastructure。Infrastructure 继续单向引用 Core。

## 18. Static and Schema Checks

- INPUT P/Invoke 只出现在统一 native boundary。
- 新代码没有 Playback、Stopwatch、Thread.Sleep、文件 IO 或 Macro Repository coupling。
- 没有 `keybd_event`、`mouse_event`、`SetCursorPos`、隐藏运行、提权或网络代码。
- **No Macro Schema Change**：Macro、InputEvent JSON、SchemaVersion、discriminator 和 enum contract 均未修改。

## 19. Security / Privacy

- 只注入调用方明确提供的单事件，不监听或保存额外内容。
- 不联网、不同步、不上传、不提升权限、不绕过 UIPI。
- Elevated target / secure desktop 可能按 Windows UIPI 规则拒绝普通权限进程的输入。
- 人工验证只操作项目专用临时窗口，按键和鼠标 Down 均配对 Up；没有使用危险系统组合键。

## 20. Known Limitations

以下属于后续 Phase，而非本阶段缺陷：

- 尚无 PlaybackEngine / PlaybackSession 或 timeline scheduling。
- 尚无播放速度、Pause / Resume、Stop / Emergency Stop。
- 尚无播放期间 pressed-key / mouse-button 状态恢复。
- 尚无正式 WPF 注入或播放入口。
- 尚无跨录制环境坐标适配；注入只使用当前 Virtual Desktop。
- Horizontal Wheel / XButton 的真实硬件效果未人工观察，自动 mapping 已覆盖。
- UIPI 可能阻止向更高完整性进程注入；不尝试提升权限或绕过。

## 21. Git Status

提交前检查完整 diff、静态边界、依赖、Schema、生成物和 status；提交后最终状态与 Commit Hash 在对话报告确认。提交不包含人工验证项目、stdout/stderr、input log、截图、bin、obj、TestResults、用户 Macro、SDK、dump 或个人绝对路径。

## 22. Next Phase

只建议 **Phase 10 — Playback Engine Basic**。本阶段提交后停止，不实现 speed、Pause/Resume、Stop/Emergency Stop、Hotkeys 或 WPF Playback UI。
