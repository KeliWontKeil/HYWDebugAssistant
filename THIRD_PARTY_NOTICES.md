# 第三方组件与素材声明

本仓库与发行物包含下列第三方素材。项目本体许可见 `LICENSE.txt`。

## Fluent UI System Icons

- **版权**：Copyright (c) 2020 Microsoft Corporation
- **许可**：MIT —— 全文见 [`resources/icons/LICENSE`](resources/icons/LICENSE)；
  发布时随框架资源整棵树投递到 `<应用输出>/resources/icons/LICENSE`（`package-release` 的
  `resources/` 清单会逐个文件列出它，`verify-resources` 按 SHA256 核对）。
- **来源**：<https://github.com/microsoft/fluentui-system-icons>
- **版本 / 获取方式**：`main` 分支的 **20px filled** 图标
  （`assets/<名称>/SVG/ic_fluent_<名称>_20_filled.svg`），2026-10 一次性取回后**以文件形式入库**：
  四个外部接入模块在各自的 `resources/icons/<程序集简名>/refresh.svg`（清空计数按钮，
  与 24×20 的小按钮适配）。运行期不联网、不依赖图标字体，只读仓库里这份文件。
- **用途**：模块视图的清空按钮。
  图形由自研最小读取器解析（只认 `<svg viewBox>` + `<path d>` + `fill-rule`，见
  `src/Sdk/Resources/IconCatalog.cs` 与 `src/Sdk.Ui/Controls/Icon.cs`）。
- **修改**：未改动图形几何；只做了"键 → 相对路径"的清单登记（`icons.json`）与文件重命名
  （模块内的 `refresh.svg`）。

## Fluent UI MDL2 Icons

- **版权**：Copyright (c) Microsoft Corporation
- **许可**：MIT —— 与上一节同一份 MIT 全文（[`resources/icons/LICENSE`](resources/icons/LICENSE)）；
  MIT 允许复制、修改、再分发，条件是保留版权与许可声明，本节即是该声明。
- **来源**：<https://github.com/microsoft/fluentui> 的 `packages/react-icons-mdl2`
  （`@fluentui/react-icons-mdl2` 图标集）。
- **版本 / 获取方式**：`master`（v8）的 MDL2 图标，2026-10 一次性取回其 `<path d>` 后
  **以文件形式入库**：框架侧 17 个图标在 `resources/icons/HYWDebugAssistant.Ui/{shell,catalog}/*.svg`。
  这些是旧版（Fluent 1 / MDL2）观感，`viewBox` 为 `0 0 2048 2048`，与 Windows 系统字体
  `Segoe MDL2 Assets` 同一套形状；运行期不联网、不依赖任何字体。
- **用途**：外壳活动栏、模块窗口按钮簇、模块清单分类与占位（框架的 17 个图标）。
  图形由同一套自研读取器解析。
- **修改**：未改动图形几何；只做了"键 → 相对路径"的清单登记（`icons.json`）与文件重命名
  （`shell/*.svg`、`catalog/*.svg`）。

## NuGet 依赖

NuGet 包的许可由构建期门禁 `tools/audit-licenses.ps1` 审计（遍历 `.nuspec` 的 SPDX 表达式 /
许可文件 / `licenses.nuget.org` 链接，只放行宽松许可白名单）。**图标素材不是 NuGet 包**，
因此不在那份审计的覆盖范围内，单列于本文件。
