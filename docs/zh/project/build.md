# 构建与门禁

本仓库的全部构建、验证与打包都通过 PowerShell 脚本进行；门禁是一条固定的步骤链，任一步失败即终止。

## 环境

| 项 | 要求 |
|---|---|
| 操作系统 | Windows（脚本按 Windows 编写） |
| .NET SDK | 版本由仓库根 `global.json` 固定 |
| PowerShell | 7 及以上 |
| 文档站（可选） | Python 3.11 及以上，安装 `docs/requirements.txt` 依赖 |

只改代码时不需要安装文档站的 Python 依赖。

## 常用命令

| 命令 | 作用 |
|---|---|
| `.\build.ps1` | 完整门禁 |
| `.\tools\dev.ps1 -Mode Build -Project <名称片段>` | 编译单个项目 |
| `.\tools\dev.ps1 -Mode Test` | 运行默认测试项目（不含压力 / 性能） |
| `.\tools\dev.ps1 -Mode Test -Filter <词>` | 运行匹配的测试 |
| `.\tools\dev.ps1 -Mode Test -Pressure` | 只跑压力类别 |
| `.\tools\dev.ps1 -Mode Snapshots` | 生成渲染截图（人眼验收用） |
| `.\tools\dev.ps1 -Mode Gate` | 等价于 `build.ps1` |
| `.\tools\dev.ps1 -Mode Run` | 构建并启动应用 |
| `.\docs\build.ps1 -Mode Check` | 文档站结构检查 |
| `.\docs\build.ps1 -Mode Build -Languages all` | 构建中英文文档站 |
| `.\tools\release.ps1 -OutputDirectory <空目录>` | 官方发布：门禁 → 自包含产品目录 + 独立 SDK 目录 |

## 发布

`tools/release.ps1` 是官方入口：要求工作树干净，先跑完整门禁，再产出两个**目录**（当前不压缩）：

```powershell
.\tools\release.ps1 -OutputDirectory D:\dist\<版本>
```

- 产品 `HYWDebugAssistant-<版本>-<rid>/`：`dotnet publish` 自包含目录，含程序、内置模块（含模块运行期 `views/*.axaml`）、资源、设置种子、许可与 `release.json`；不含 SDK、样例、可编译源码、用户数据与开发符号/XML。
- SDK `HYWDebugAssistant.Sdk-<版本>/`：五个作者包、模块模板、本地 NuGet 源、README 与 `sdk.json`；不随产品发布。
- 默认 `rid=win-x64`（承诺目标）；其他 RID 可以尝试，但不承诺编译/运行，元数据标记 `validation=unverified`。
- `tools\package-release.ps1` 与 `tools\package-sdk.ps1` 是分步入口；`tools\verify-release.ps1` 校验已产出的目录。
- 安装器、压缩包、签名与自动更新暂不做；正式推送时再补。

## 分层验证

按改动范围选择最小必要的一层，不要为一处改动跑全量，也不要让有风险的改动绕过门禁。

| 层级 | 适用时机 | 命令 |
|---|---|---|
| 文档改动 | 只改页面、导航或链接 | `docs\build.ps1 -Mode Check` |
| T0 单项目编译 | 每次小改，尽早暴露编译问题 | `dev.ps1 -Mode Build -Project …` |
| T1 单类测试 | 改了某块逻辑或某个测试类 | `dev.ps1 -Mode Test -Filter …` |
| T2 单测试项目 | 一个增量收尾 | `dev.ps1 -Mode Test` |
| T3 完整门禁 | 交付前、改事实源（SDK 面 / 契约 / 格式 / 架构规则 / 依赖）后、功能完成准备提交前 | `build.ps1` |

固定顺序：一次改完再验证；先编译受影响项目；跑一次 T2，有风险时直接 T3；报告里写明实际跑了哪一层。

## 门禁步骤

`build.ps1` 依次执行：

1. 文档站结构检查。
2. 颜色口径审计。
3. 资源与文本审计。
4. 源码注释的布局数值审计。
5. 版本纪律审计。
6. 截图工程零断言审计（截图工具不是测试，断言搬进主测试项目）。
7. 还原与构建，要求 0 警告、0 错误。
8. 内置模块包校验。
9. 模块作者包生成与校验（`-SkipPack` 可跳过，发布前不得跳过）。
10. 模块模板：用作者包构建模板，并由仓库内 `StandaloneModuleHost` 装入（门禁专用；该宿主不随产品/SDK 发布）。
11. 输出资源包校验（逐文件与仓库一致）。
12. 依赖许可审计。
12. 测试：按项目逐个串行执行，默认排除压力、性能与截图。

## 产物位置

| 产物 | 位置 |
|---|---|
| 项目输出 | `bin\<配置>\net10.0` |
| 模块包 | `artifacts/modules/<配置>/`，构建时自动归拢 |
| 应用输出中的 `modules/`、`resources/` | 构建镜像，不手工放 |
| 模块作者包 | `artifacts/sdk-packages/`；发布时组装为独立 SDK 目录 |
| 产品发布 | `tools\release.ps1` 产出的 `HYWDebugAssistant-<版本>-<rid>/`（自包含） |
| SDK 发布 | 同一次发布产出的 `HYWDebugAssistant.Sdk-<版本>/`（五个包 + 模板） |
| 文档站 | `artifacts/docs-site/` |
| 网络与渲染截图 | `artifacts/snapshots/` |

## 环境问题

| 现象 | 原因 | 处理 |
|---|---|---|
| 构建报平台配置无效 | 环境变量 `Platform` 被其它软件设置 | 用 `build.ps1` / `dev.ps1`，它们会先清除 |
| 输出文件被占用 | 应用或 IDE 预览器占用输出 | 关闭后重试；应急用 `-SkipAppRebuild` |
| 文档站提示 mkdocs 版本不一致 | 本机版本与 `docs/requirements.txt` 不同 | 按该文件重新安装 |

## 相关

- [代码结构](code-structure.md)：项目与程序集边界
- [测试体系](tests.md)：测试项目、按需类别与预算
- [文档体系](docs.md)：写文档与结构检查的规则
