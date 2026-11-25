# HYWDebugAssistant

# 何译位串口调试助手
基于.NET 10的模块化工程上位机平台。项目正在前期开发中。
项目未稳定，文档站暂未上线

相关文档/release见以下目录
- **对外文档站**：`docs/`（MkDocs 工程；中文内容在 `docs/zh/`，英文在 `docs/en/`）
- **开发文档**：`dev/`（约束、工作流、状态、验收；不对外发布）
- **构建与门禁**：`pwsh -File .\build.ps1`
- **发布**：`pwsh -File .\tools\release.ps1 -OutputDirectory <工程目录>\<版本>`（自包含产品目录 + 独立 SDK 目录；当前不压缩）
- **文档**：`docs/` 是 MkDocs 格式的文档工程（中文在 `docs/zh/`，英文在 `docs/en/`）；`.\docs\build.ps1 -Mode Serve` 本地预览，`-Mode Check` 结构检查（mkdocs 是预览工具，依赖见 `docs/requirements.txt`）
