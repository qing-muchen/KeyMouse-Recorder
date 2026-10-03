# Phase 0 — Project Foundation

## 范围与环境

- 初始目录为空，没有 Git 仓库、历史提交或已有实现；没有发现适用的 AGENTS.md。
- 系统有 .NET 8 / 10 运行时，但 `dotnet --info` 未发现 SDK。
- 使用微软官方稳定 SDK `10.0.401`（Windows x64），下载到项目内 `.tools/dotnet`，按官方发布元数据校验 SHA-512。
- 官方来源：[.NET 10 下载](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)；[发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)。
- 没有全局安装、没有修改系统环境变量、没有覆盖用户已有文件；没有引入应用第三方运行时依赖。
- 本阶段只构建项目基础，不实现 Phase 1 的完整输入事件领域模型，不实现真实 Hook、输入采集或注入。

## 完成内容与架构决策

1. 初始化 Git `main` 分支和四项目 `.slnx` Solution。
2. 使用 `net10.0` Core / Infrastructure / Tests 和 `net10.0-windows` x64 WPF App。
3. 全项目启用 nullable、内置 analyzers、Recommended 分析规则、warnings-as-errors 和确定性编译。
4. `App → Infrastructure → Core`，App 同时引用 Core；Core 不引用 WPF、Win32 或具体 IO 实现。
5. Macro 仅包含不可变 Id / Name 骨架；RecordingSession / PlaybackSession / MacroFile 留待对应 Phase 实现。
6. 建立 `IAppLogger`、强类型日志级别/事件及本地 JSONL 文件日志；每实例独立文件，写入同步、即时刷新，支持幂等 Dispose。
7. 日志不接受自由文本负载；异常仅保存类型和错误码，避免异常消息包含路径、用户输入或密码。
8. `ApplicationPaths` 集中管理 `data/macros`、`data/config`、`data/logs`；创建幂等，不覆盖已有内容，IO 失败向上报告。
9. 采用程序旁便携数据目录，确保本阶段运行数据留在项目内。`settings.json` 只预留路径，配置持久化仍属于 Phase 19。
10. 基础 WPF 窗口显示 Idle、阶段说明和本地目录，ViewModel 只读。MainWindow code-behind 只初始化组件；App 是启动/退出的组合入口。
11. 小规模基础测试暂置一个测试项目，涵盖 Core 所需的基础设施边界；不为未来模块创建空接口。
12. 提供项目内 SDK/缓存入口、GUI 自动化冒烟脚本、README、Git 忽略规则与统一文本格式。

## 修改文件 / 新增文件

原仓库为空，**修改已有文件：0；所有提交文件均为新增**。

| 分组 | 新增文件 |
| --- | --- |
| 根目录 | `.editorconfig`、`.gitattributes`、`.gitignore`、`Directory.Build.props`、`global.json`、`NuGet.Config`、`MacroRecorder.slnx`、`README.md` |
| `src/MacroRecorder.App/` | `MacroRecorder.App.csproj`、`App.xaml`、`App.xaml.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`ViewModels/MainWindowViewModel.cs` |
| `src/MacroRecorder.Core/` | `MacroRecorder.Core.csproj`、`Models/Macro.cs`、`Diagnostics/IAppLogger.cs`、`Diagnostics/AppLogLevel.cs`、`Diagnostics/AppLogEvent.cs` |
| `src/MacroRecorder.Infrastructure/` | `MacroRecorder.Infrastructure.csproj`、`Storage/ApplicationPaths.cs`、`Diagnostics/LocalFileLogger.cs` |
| `tests/MacroRecorder.Core.Tests/` | `MacroRecorder.Core.Tests.csproj`、`ApplicationPathsTests.cs`、`LocalFileLoggerTests.cs`、`TestDirectory.cs` |
| `scripts/` | `dotnet.ps1`、`Test-GuiSmoke.ps1` |
| `docs/` | `phase-0-report.md` |

SDK、缓存、构建输出和运行日志只位于 Git 忽略目录，不属于提交内容。

## 测试与人工验证

验证环境：本机 Windows x64（系统版本 `10.0.26200`）、SDK `10.0.401`。

| 检查 | 最终结果 |
| --- | --- |
| 官方 SDK SHA-512 | 匹配，通过后解压 |
| `restore MacroRecorder.slnx` | 4 个项目还原成功 |
| Release build | 成功，0 warnings / 0 errors |
| xUnit | 10 passed / 0 failed / 0 skipped |
| WPF GUI 冒烟集成测试 | 窗口创建、实际控件 `Idle · 未录制` 绑定通过 |
| 启动数据目录 | macros / config / logs 创建通过 |
| 正常关闭 | 窗口正常关闭，进程退出码 0 |
| 日志生命周期 | 本次日志恰含 ApplicationStarted / ApplicationStopped |
| 文件/脚本静态检查 | 9 个 XML 文件及 global.json 解析通过；两个 PowerShell 脚本语法通过 |

最终执行命令（仓库根目录）：

```powershell
./scripts/dotnet.ps1 restore MacroRecorder.slnx --disable-parallel
./scripts/dotnet.ps1 build MacroRecorder.slnx -c Release --no-restore --disable-build-servers '-m:1'
./scripts/dotnet.ps1 test MacroRecorder.slnx -c Release --no-build --no-restore '-m:1' --logger 'trx;LogFileName=phase-0.trx' --results-directory .artifacts/test-results
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-GuiSmoke.ps1
```

测试覆盖：目录重复创建及已有文件保护、三个非法相对/空路径、文件系统失败传播、默认路径独立于工作目录、
日志结构和异常内容排除、100 次并发写入完整性、Dispose 幂等与文件独占重新打开、多个日志实例隔离。
测试临时目录在测试结束后释放并清理；TRX 结果位于忽略目录 `.artifacts/test-results/phase-0.trx`。

验证中修复/处理的问题：

- 受限执行环境中默认 MSBuild 多 worker 调用未给出有效错误而失败；改用单 worker、禁用构建服务器后正常完成，README 已记录可重复的命令。
- WPF Application 的 `CA1001` 所有权检查采用一处有理由的定点抑制：Application 生命周期由 WPF 管理，日志始终由 OnExit 的 finally 释放；没有全局禁用规则。
- 首轮 3 个日志测试因活动文件的读取共享方式失败，改为 Windows 所需的 `FileShare.ReadWrite` 读取器，保留对即时刷新日志的验证。重跑后全部通过。

**人工验证结果**：没有冒充真人操作或外观确认。已通过 UI Automation 实际启动、检查并关闭 WPF 应用；
人工外观、DPI、多显示器和 Windows 10 实机体验仍未验证。当前没有录制逻辑，故没有执行任何真实输入采集测试。

## 已知限制

- 本阶段不能录制、保存/加载 Macro、回放或处理全局快捷键，这是阶段边界。
- 当前窗口仅基础壳；完整状态机、命令、正式 GUI 和 Macro Library UI 分别在后续 Phase 实现。
- 程序所在目录必须可写；当前不支持安装到只读目录后直接保存数据。
- 开发构建数据位于 `bin` 下，清理构建输出会删除它们；正式分发前需要决定长期用户数据位置。
- 暂未提供日志轮转、保留期、安装器或自包含发布。
- 未建立 Windows 10 / Windows 11 双平台实机测试矩阵，不能把本机验证视为两个平台全部验证。
- GUI 自动化检查不能替代人工外观、缩放和多显示器审查。

## Git 与下一阶段

提交格式：`feat(foundation): establish Phase 0 project foundation`。
提交前检查 `git diff --check`、完整 staged diff、文件清单及 `git status`。
29 个新增源代码/配置/文档文件；没有旧文件修改、删除，没有暂存 SDK、缓存、TRX、运行日志、个人路径或输入内容。
最终提交哈希及提交后 Git 状态在本阶段对话报告中提供，避免将提交本身的哈希写入其内容。

下一步建议：收到明确确认后执行 **Phase 1 — Input Event Domain Model**，建立强类型事件、完整 Macro/metadata 和序列化往返测试；仍不实现 Hook。
Phase 0 完成后停止，不自动进入 Phase 1。
