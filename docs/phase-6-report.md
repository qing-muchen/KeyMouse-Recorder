# Phase 6 — Macro JSON Persistence Report

## 1. Status

Completed。正式 Macro JSON Stream serializer、冻结的生产 JSON 配置、明确路径 Save / Load、同目录临时文件提交、覆盖保存、取消及失败保护、重复读取和真实磁盘验证均已完成。Release Build、255 项测试和 10,000 事件 smoke test 通过。未进入 Phase 7。

## 2. Git

- Branch: `main`
- Previous Commit: `ac736d7d020670b00d1d0a9ec89a8d66c09030d1`
- New Commit: 本报告随 `feat(persistence): add atomic macro JSON storage` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Commit Message: `feat(persistence): add atomic macro JSON storage`
- Remote Tracking: 开始时为 `main...origin/main [ahead 5]`。没有 push、rebase、reset、amend 或改写历史。

## 3. Implemented

- `IMacroSerializer`：不拥有 Stream 的异步 Macro 编解码边界。
- `MacroJsonSerializer`：System.Text.Json UTF-8 Stream serializer / deserializer。
- 唯一生产 JsonSerializerOptions：camelCase、缩进、string Enum、numeric Enum rejection、required / nullable enforcement，并在创建时 MakeReadOnly。
- `MacroFileStore.SaveAsync(path, macro, cancellationToken)`。
- `MacroFileStore.LoadAsync(path, cancellationToken)`。
- 自动创建调用者明确目标的父目录。
- 同目录唯一 temp、CreateNew、异步写入、FlushAsync、Flush(true)、关闭句柄、单次覆盖移动。
- 序列化错误、取消和 IO 错误传播；失败时 best-effort temp cleanup。
- Phase 1 DomainJson helper 改为引用已冻结的 production options，不再维护第二套配置。

未实现 Validator、Library scan/list/rename/delete、Migration、Playback、SendInput、WPF 文件对话框或自动保存。

## 4. Files Changed

新增生产文件：

- `src/MacroRecorder.Infrastructure/Persistence/IMacroSerializer.cs`
- `src/MacroRecorder.Infrastructure/Persistence/MacroJsonSerializer.cs`
- `src/MacroRecorder.Infrastructure/Persistence/MacroFileStore.cs`

新增测试文件：

- `tests/MacroRecorder.Core.Tests/Persistence/MacroJsonSerializerTests.cs`
- `tests/MacroRecorder.Core.Tests/Persistence/MacroFileStoreTests.cs`
- `tests/MacroRecorder.Core.Tests/TestDoubles/ThrowingMacroSerializer.cs`
- `tests/MacroRecorder.Core.Tests/TestDoubles/BlockingMacroSerializer.cs`

修改：

- `tests/MacroRecorder.Core.Tests/Serialization/DomainJson.cs`
- `README.md`

新增文档：`docs/phase-6-report.md`。删除：无。人工 JSON、large smoke 文件、临时 harness 和构建输出均已清理。

## 5. Architecture

```text
Macro
  ↓
MacroJsonSerializer
  ↓ UTF-8 Stream

Macro
  ↓
MacroFileStore
  ↓
same-directory unique temp
  ↓ serialize + flush + close
File.Move(temp, target, overwrite: true)
  ↓
Macro JSON

JSON File
  ↓ read-only stream
MacroFileStore
  ↓
MacroJsonSerializer
  ↓
Macro
```

Persistence 位于 Infrastructure 并引用 Core model；Core 未增加文件系统、System.Text.Json service 或 Infrastructure 依赖。FileStore 依赖小型 serializer abstraction，使失败和取消可以确定性注入。

## 6. JSON Contract

- `schemaVersion`: 始终输出，当前值为 1。
- Polymorphic discriminator: `kind: "keyboard"` / `kind: "mouse"`。
- Enum: 可读字符串；数字 Enum 被拒绝。
- Property naming: camelCase。
- Formatting: `WriteIndented = true`。
- Encoding: UTF-8 without BOM。
- CLR type information: 不写 `$type`、assembly name 或 PublicKeyToken。

真实输出的脱敏片段：

```json
{
  "schemaVersion": 1,
  "name": "\u6D4B\u8BD5\u5B8F - \u5DF2\u66F4\u65B0",
  "events": [
    {
      "kind": "keyboard",
      "eventType": "KeyboardKeyDown"
    }
  ]
}
```

System.Text.Json 默认转义非 ASCII 字符；UTF-8 Load 后 Name / Description 精确恢复为原 Unicode 内容。

## 7. Serializer Design

Serializer 直接调用 `JsonSerializer.SerializeAsync(Stream)` 和 `DeserializeAsync<Macro>(Stream)`，避免大型 Macro 必须经过完整 string 副本。调用方保留 Stream ownership，serializer 不关闭传入流。

options 是 MacroJsonSerializer 的私有生产状态，通过内部只读入口仅供 friend test assembly 验证；创建后调用 `MakeReadOnly(populateMissingResolver: true)`。Phase 1 的 82+ schema cases 继续使用 JsonSerializer，但 options 现在来自 production serializer，因此测试与实际文件格式只有一个配置来源。Diagnostics logger 的独立 options 只定义日志 JSONL 格式，不参与 Macro schema。

## 8. Atomic Save Design

1. 用 `Path.GetFullPath` 解析调用者路径并验证文件名。
2. 只创建该明确路径的父目录。
3. 在目标同目录创建 `.{filename}.{Guid:N}.tmp`，使用 `FileMode.CreateNew`。
4. Serializer 直接写 temp FileStream。
5. 执行 `FlushAsync(cancellationToken)`，检查取消，再执行 `Flush(flushToDisk: true)`。
6. 关闭 temp handle。
7. 再次检查取消。
8. 使用 `File.Move(temp, target, overwrite: true)` 单次提交。

同目录保证 temp 与 target 位于相同 volume；官方 [.NET `File.Move` API](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-10.0) 说明 overwrite 重载会替换已存在目标。流程从不执行 `Delete(target) → Move`，因此没有用户态的“目标已删除但新文件尚未到位”窗口。

序列化或取消发生在提交前，只会影响唯一 temp。`finally` 删除 temp；清理失败不会覆盖主异常，而是附加在主异常 Data 中。自动测试让 serializer 先写半段 JSON 再失败，证明旧目标字节完全不变，新目标也不会出现。

## 9. Repeated Load Semantics

Load 使用 `FileMode.Open`、`FileAccess.Read` 和 read-only Stream，只进行 Deserialize。它不会更新 UpdatedAt、格式化、迁移、保存回磁盘、移动或删除源文件。

真实和自动验证都对同一 JSON 连续 Load 三次；每次得到等价但独立的 Macro 对象，读取前后源文件 bytes 完全相同，并可继续读取。

## 10. Error Handling

- Missing file：保留 FileNotFoundException，不返回 null 或 empty Macro。
- Empty / truncated / malformed / JSON null：抛出 JsonException。
- Unknown discriminator / enum string / numeric enum：抛出 JsonException。
- Serializer failure：原异常传播；旧目标保持完整，新目标不出现。
- Cancellation：OperationCanceledException 传播；已有目标 bytes 不变，未存在目标仍不存在。
- IO / move failure：异常传播；temp 进行 best-effort cleanup。
- Temp cleanup failure：不替换 primary error，以 `MacroTempCleanupException` 附加诊断上下文。

没有 catch 后返回默认 Macro，也没有记录完整 JSON 或事件内容。

## 11. Validation Boundary

Phase 6 只执行 Deserialize。它不会判断：

- SchemaVersion 是否受支持；测试确认 `schemaVersion = 999` 可被忠实恢复。
- Timestamp 是否排序。
- Recording Duration 或 EventCount 是否与 Events 一致。
- Mouse event 的 Button / WheelDelta 组合是否符合语义。
- metadata 的业务范围是否合法。

Phase 1 构造器已经拒绝的结构级非法状态仍保持原行为。完整 semantic validation 属于 Phase 7。

## 12. Tests

- 新增：27
- 原有：228，全部继续通过
- 总计：255
- Passed: 255
- Failed: 0
- Skipped: 0

覆盖 production options、Stream ownership、空/键盘/鼠标/混合 Macro、Unicode、负坐标、XButton、垂直/水平和非 120 wheel delta、long.MaxValue timestamp、Guid、DateTimeOffset、metadata、runtime type、同 timestamp 顺序、SchemaVersion、Enum / discriminator / camelCase、UTF-8 无 BOM、新文件、空格路径、缺失父目录、重复读取、bytes 不变、覆盖、文件句柄释放、malformed input、partial serializer failure、确定性 cancellation、同目录 temp 和 10,000 events。

## 13. Manual Save / Load Verification

非敏感测试 Macro 使用固定 metadata、A 的 Down / Up、负坐标 Move、XButton1 Down / Up 和非 120 wheel delta：

- Save：PASS，生成 1,617-byte UTF-8 no-BOM JSON。
- Load #1：PASS。
- Load #2：PASS。
- Load #3：PASS。
- 三次读取后文件 bytes unchanged：PASS。
- 覆盖 Name / UpdatedAt 后 Save：PASS。
- Overwrite 后 Load：PASS，新内容存在且其余字段、事件顺序不变。
- Temp files：0。

人工查看确认缩进、camelCase、schemaVersion、字符串 Enum、kind discriminator 和无 CLR type name。测试文件及 harness 已删除。

## 14. Large Macro Smoke Test

- Events: 10,000，键盘与鼠标交替生成，不含用户输入。
- File size: 1,828,270 bytes。
- Save result: PASS，约 13.43 ms。
- Load result: PASS，约 44.17 ms。
- Loaded Event Count: 10,000，首尾 timestamp 正确。
- Working set: 36,798,464 → 40,579,072 bytes，增加 3,780,608 bytes，包含 JIT、serializer buffer 和 loaded object graph。
- Temp files: 0。

这是一次开发机 smoke observation，不是正式 benchmark。

## 15. Build

- Configuration: Release
- SDK: 10.0.401（项目内现有 SDK）
- Warnings: 0
- Errors: 0

最终验证针对完整 solution。

## 16. Dependency Check

新增 package：否。实现只使用 System.Text.Json、System.IO、不可变集合及其他 BCL API；未引入 Newtonsoft.Json 或文件持久化框架。

## 17. Schema Compatibility

**No Macro Schema Change.** MacroSchema、InputEvent hierarchy、InputEventType、MouseButton、metadata fields、property names 和 discriminator 均未修改。Phase 1 JSON contract tests 全部继续通过。

## 18. Security / Privacy

- FileStore 只操作调用者明确传入的 path。
- 不扫描目录、不搜索用户文档、不自动保存、不隐藏持久化。
- 不上传、不联网、不使用云端、数据库、压缩或加密容器。
- 不记录完整 JSON、键盘事件、鼠标轨迹或输入文本。
- 人工验证只使用代码生成的非敏感事件和固定 metadata。
- 人工 JSON、large file 和 harness 已删除。

## 19. Known Limitations

以下属于后续 Phase，而非本阶段缺陷：

- Macro semantic validation 尚未实现。
- Macro Library 尚未实现。
- Playback / SendInput 尚未实现。
- WPF Save/Open workflow 尚未实现。
- Environment Metadata Capture 尚未实现。
- Schema Migration 尚未实现。
- 同一目标的并发 Save 没有全局排序或 lock registry；每次 Save 使用独立 temp，最终内容由最后成功提交者决定。

## 20. Git Status

提交前检查完整 diff、whitespace、依赖、Schema、静态 API、生成物和 status；提交后最终状态与 Commit Hash 在对话报告确认。提交不包含人工 example JSON、large test file、temp、bin、obj、TestResults、SDK、日志、录制事件、dump、截图或个人绝对路径。

## 21. Next Phase

只建议 **Phase 7 — Macro Validation**。本阶段提交后停止，不实现 Validator、Library、Playback、SendInput 或 WPF Save/Open UI。
