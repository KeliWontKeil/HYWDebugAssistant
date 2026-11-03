# HYWDebugAssistant

模块化工程上位机平台（HYW 调试助手）。.NET 10 + Avalonia。

- **对外文档站**：`docs/`（MkDocs 工程；中文内容在 `docs/zh/`，英文在 `docs/en/`）
- **开发文档**：`dev/`（约束、工作流、状态、验收；不对外发布）
- **构建与门禁**：`pwsh -File .\build.ps1`
- **文档站**：`.\docs\build.ps1 -Mode Check` 结构检查 · `-Mode Serve` 本地预览（需要 Python 3.11+ 与 `docs/requirements.txt` 中的依赖）
