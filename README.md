# HYWDebugAssistant

模块化工程上位机平台（HYW 调试助手）。.NET 10 + Avalonia。发布目录里的 `HYWDebugAssistant.App.exe` 就是主应用，也是模块宿主。

- **对外文档站**：`docs/`（MkDocs 工程；中文内容在 `docs/zh/`，英文在 `docs/en/`）
- **开发文档**：`dev/`（约束、工作流、状态、验收；不对外发布）
- **构建与门禁**：`pwsh -File .\build.ps1`
- **发布**：`pwsh -File .\tools\release.ps1 -OutputDirectory <仓库外的空目录>`（自包含产品目录 + 独立 SDK 目录；当前不压缩）
- **文档站**：`.\docs\build.ps1 -Mode Check` 结构检查 · `-Mode Serve` 本地预览（需要 Python 3.11+ 与 `docs/requirements.txt` 中的依赖）
