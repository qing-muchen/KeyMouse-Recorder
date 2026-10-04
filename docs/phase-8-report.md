# Phase 8 — Macro Repository / Library Report

## 1. Status

Completed。本地单目录 Macro Library 已具备稳定列表、摘要、Load + Validation、受验证 Save、Save As、Rename、Delete、路径边界、损坏资产隔离和 CancellationToken 支持。Release Build、406 项全量测试和真实临时 Library 工作流通过。未进入 Phase 9。

## 2. Git

- Branch: `main`
- Previous Commit: `9d13dc1f2ec9a815db87233b32f287f7dc971b64`
- New Commit: 本报告随 `feat(library): add local macro repository` 提交；最终哈希在对话阶段报告中给出，避免提交自引用。
- Commit Message: `feat(library): add local macro repository`
- Remote Tracking: 开始时为 `main...origin/main [ahead 7]`。没有 push、rebase、reset、amend 或历史重写。

## 3. Implemented

- `MacroRepository`：管理一个明确绝对路径的本地 Library root。
- `MacroLibraryEntry` / `MacroLibrarySummary`：不持有完整 Events 的列表摘要。
- `MacroLibraryEntryStatus`：Valid / Invalid / Unreadable。
- `MacroLibraryError`：只保留 ErrorType 和 Message。
- `MacroLoadResult`：完整 Macro + ValidationResult，IsValid 派生。
- `MacroValidationException`：Save invalid Macro 时携带 ValidationResult。
- `ListAsync`、`LoadAsync`、`SaveAsync`、Save As semantics、`RenameAsync`、`DeleteAsync`。
- filename 规范化、库根边界、遍历/绝对/嵌套路径拒绝。
- broken / invalid / warning-only Macro 相互隔离。

未实现 Playback、SendInput、FileSystemWatcher、cache、database、nested folders、WPF Library UI 或 Schema Migration。

## 4. Files Changed

新增生产文件：

- `src/MacroRecorder.Infrastructure/Library/MacroRepository.cs`
- `src/MacroRecorder.Infrastructure/Library/MacroLibraryEntry.cs`
- `src/MacroRecorder.Infrastructure/Library/MacroLibraryEntryStatus.cs`
- `src/MacroRecorder.Infrastructure/Library/MacroLibrarySummary.cs`
- `src/MacroRecorder.Infrastructure/Library/MacroLibraryError.cs`
- `src/MacroRecorder.Infrastructure/Library/MacroLoadResult.cs`
- `src/MacroRecorder.Infrastructure/Library/MacroValidationException.cs`

新增测试：`tests/MacroRecorder.Core.Tests/Library/MacroRepositoryTests.cs`。修改：`README.md`。新增文档：`docs/phase-8-report.md`。删除：无。人工验证目录已删除。

## 5. Architecture

```text
Macro Library Directory
        ↓
MacroRepository
   ├── MacroFileStore
   │     └── IMacroSerializer
   └── MacroValidator
        ↓
ImmutableArray<MacroLibraryEntry>
```

显式 Load：

```text
logical filename
  ↓ normalize + safe root path
MacroFileStore.LoadAsync
  ↓ Macro
MacroValidator.Validate
  ↓
MacroLoadResult(Macro, ValidationResult)
```

Core 不引用 Infrastructure。MacroFileStore 仍只负责单文件 IO + serialization；MacroValidator 仍是纯业务规则。

## 6. Library Entry Design

Entry 保存 canonical FullPath、FileName、Status，以及 readable 文件的 `MacroLibrarySummary` 和 ValidationResult；Unreadable 文件保存小型 `MacroLibraryError`。

Summary 包含 ID、Name、Description、DurationUs、EventCount、CreatedAt、UpdatedAt。它没有 Macro 或 Events 属性。List 扫描时完整 Macro 只作为局部变量用于 Validate / summary extraction，不会被 Entry 长期引用。显式 Load 才返回完整 Macro。

## 7. Entry Status

- Valid：Deserialize 成功且 ValidationResult.IsValid=true，包括 Warning-only Macro。
- Invalid：Deserialize 成功但至少一个 Error；保留 summary 和完整 ValidationResult。
- Unreadable：JSON 或单文件 IO 无法读取；Summary / Validation 为 null，Error 只含类型和短消息。

Empty Macro 的 `EMPTY_MACRO` 是 Warning，因此 Entry 为 Valid。

## 8. Library Listing

- Root 缺失时由 List 创建。
- 使用 `SearchOption.TopDirectoryOnly`，不递归。
- 扩展名 `.json` 使用 OrdinalIgnoreCase，`.JSON` 会列出。
- `.tmp`、txt、zip 等非 JSON 文件忽略且不删除。
- 嵌套目录 JSON 忽略。
- 每次 List 重新读取磁盘，不缓存。
- 先按 filename OrdinalIgnoreCase，再按 Ordinal tie-break，结果确定。
- 一个 broken 文件转换为 Unreadable Entry，不中断其他文件。

## 9. Path Security

Repository 构造要求 fully-qualified root，并保存 `Path.GetFullPath` canonical root。所有操作只接受 filename：

- 缺扩展名自动补 `.json`。
- 显式扩展名必须是 `.json`，大小写不敏感。
- 名称 stem 不能为空或纯空白。
- 拒绝 directory separator、alt separator、`.`、`..`、invalid filename characters。
- 拒绝绝对路径和 nested path。
- 组合后再次 canonicalize，并要求 parent 与 LibraryRoot 完全一致（OrdinalIgnoreCase）。

测试确认 `../outside.json`、`..\outside.json`、绝对路径和 `subdir/file.json` 无法 Load / Save / Rename / Delete Library 外资产。

## 10. Load Semantics

- Valid：返回完整 Macro 和 valid ValidationResult。
- Invalid：正常返回原 Macro 和 invalid ValidationResult，不修复、不抛业务异常。
- Unreadable explicit load：JsonException / IO exception 传播。
- Missing：Repository 统一抛 FileNotFoundException，即使 root 尚未创建。
- 同一文件可重复 Load，源 bytes 不变。
- Cancellation 在读取前传播。

## 11. Save Semantics

- Save 前调用 MacroValidator。
- Valid / Warning-only Macro 可保存。
- Invalid Macro 抛 `MacroValidationException`，通过 `ValidationResult` 查看 Issues；异常 Message 不拼接全部问题。
- invalid new save 不创建文件；invalid overwrite 不触碰已有目标。
- valid overwrite 直接复用 Phase 6 MacroFileStore 的同目录 temp + flush + move 提交。
- `SaveAsync(newFilename, macro)` 即 Save As，不创建重复方法。
- Save 不修改 Macro 或 Events。

## 12. Rename Semantics

- old / new 使用完全相同的 filename normalization 和 root boundary。
- Source 必须存在；Target 已存在时抛 IOException，不覆盖。
- 使用同目录 `File.Move`，不执行 copy + delete。
- 只改变文件资产名称，不 deserialize / serialize，因此 Macro.Name、UpdatedAt、ID 和全部 bytes 不变。
- Invalid / Unreadable 文件同样可 Rename。
- 完全相同名称是安全 no-op；case-only rename 在 Phase 8 明确拒绝并保持源文件不变。

## 13. Delete Semantics

- 只删除 root 直接子级中明确指定的 `.json` filename。
- Missing file 抛 FileNotFoundException。
- 使用 `File.Delete` 永久删除，不接入 Recycle Bin。
- Invalid / Unreadable 文件允许删除。
- 不在 List 中自动删除 broken、invalid 或 temp 文件。

## 14. Broken / Invalid Isolation

人工与自动测试的同目录结果：

```text
broken.json  → Unreadable
invalid.json → Invalid
valid-a.json → Valid
valid-b.json → Valid
```

Future schema 可以 Deserialize，状态为 Invalid 且包含 `SCHEMA_VERSION_UNSUPPORTED`，不会误标 Unreadable。Error 不保存完整 JSON、StackTrace 或事件内容。

## 15. Cancellation

- List：预取消在创建 root 前传播；扫描和每个文件前检查，取消不返回半结果。
- Load：解析路径和 IO 前检查。
- Save：验证前检查；MacroFileStore 写入过程继续使用现有 CancellationToken 保护。
- Rename / Delete：任何文件变更前检查，预取消不会移动或删除文件。

没有随机 Task.Delay cancellation tests。

## 16. Tests

- Phase 7 baseline: 334
- Phase 8 新增: 72
- 总计: 406
- Passed: 406
- Failed: 0
- Skipped: 0

覆盖 missing/empty root、summary memory surface、单/多文件、稳定排序、uppercase extension、非 JSON/temp/nested ignore、valid/invalid/unreadable isolation、future schema、warning-only、duplicate metadata、无缓存 refresh、bytes unchanged、error privacy、100-file smoke、Load、Save、Save As、overwrite、validation gate、Rename、Delete、traversal、absolute path、nested path、cancellation 和 root canonicalization。

## 17. Manual Library Verification

独立临时 Library 使用代码生成的非敏感 A key down/up Macro：

```text
Initial=broken.json:Unreadable,invalid.json:Invalid,valid-a.json:Valid,valid-b.json:Valid
LoadValidA=True
Final=invalid.json,valid-a-copy.json,valid-a.json,valid-renamed.json
IgnoredPreserved=True
```

验证了 List、Load、Rename、Save As、Delete，以及 ignore.txt / leftover.tmp 不被列出或删除。临时 Library、harness、bin 和 obj 均已删除。

## 18. Build

- Configuration: Release
- SDK: 10.0.401
- Warnings: 0
- Errors: 0

最终验证针对完整 solution。

## 19. Dependency Check

新增 PackageReference：否。实现只使用 System.IO、System.Collections.Immutable、System.Text.Json exception 类型和其他 BCL API；没有 database、ORM、watcher 或第三方 validation / serialization package。

## 20. Schema Compatibility

**No Macro Schema Change.** 未修改 MacroSchema、Macro、InputEvent、metadata、discriminator、enum names 或 JSON contract。MacroFileStore 和 MacroValidator 行为未改变。

## 21. Security / Privacy

- 只扫描明确 Library root 的直接子级。
- Repository API 不接受任意路径。
- 不能读取、移动或删除 root 外资产。
- 不递归用户目录，不自动发现其他位置。
- 不联网、不同步、不上传、不记录用户输入内容。
- 不隐藏持久化，不自动清理 broken / invalid / temp 文件。
- 人工验证仅使用生成的非敏感事件。

## 22. Known Limitations

以下属于后续 Phase，而非本阶段缺陷：

- Playback / SendInput 尚未实现。
- WPF Macro Library UI 尚未实现。
- Global Hotkeys 尚未实现。
- Schema Migration 尚未实现。
- Nested Macro folders 尚未实现。
- FileSystemWatcher / live refresh 尚未实现。
- Recycle Bin delete 尚未实现。
- Case-only rename 在 Phase 8 明确不支持。
- Library scan 仍需逐个 Deserialize 文件；当前无 metadata index。

## 23. Git Status

提交前检查完整 diff、静态边界、依赖、Schema、生成物和 status；提交后最终状态与 Commit Hash 在对话报告确认。提交不包含人工 JSON、broken/temp 文件、bin、obj、TestResults、日志、用户 Macro、SDK、dump、截图或个人绝对路径。

## 24. Next Phase

只建议 **Phase 9 — Input Injector**。本阶段提交后停止，不实现 InputInjector、SendInput、Playback、Hotkeys 或 WPF UI。
