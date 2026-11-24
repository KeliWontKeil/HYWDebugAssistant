# 新增内置模块

把一个模块从"外部作者包"变成"随产品发布的内置模块"。本页是落地清单：照这个顺序做，门禁的每条规则都能提前满足。

> 动工前先读[模块设计取舍](module-design.md)：先找现有通道，再决定是否新增控件或机制；只有“现有控件组合不出布局”才新增控件。

## 落位与工程

| 项 | 做法 |
|---|---|
| 目录 | `src/Modules/Business/<Name>/`，程序集 `HYWDebugAssistant.Modules.<Name>`，包名写 `<ModulePackageName>` |
| 工程文件 | 照内置模块：只有 `ModulePackageName` + 两个 `ProjectReference`（`Sdk`、`Sdk.Ui`）+ `module.json` 与 `resources/**` 的复制项。**不写** `TargetFramework` / `Nullable` / 版本 / `PackageReference`，这些由根 `Directory.Build.props` 与中央包管理给 |
| 打包 | 不用引 `Module.Build`。`src/Modules/Directory.Build.targets` 已导入同一份 `build/module-package.targets`，构建自动归拢到 `artifacts/modules/<配置>/<包名>/` |
| 清单 | `module.json` 的 `id` 用 `作者/模块名` 两段小写，`entryType` 与代码里的 `[ModuleDefinition]` 逐字一致 |
| 解决方案 | 在 `HYWDebugAssistant.slnx` 的 `/src/Modules/Business/` 下加一行 |

## 从作者包迁入

如果模块先前是外部作者包（只引 SDK 作者包），除挪目录外，这几处必须一起改：

- 去掉 `HYWDebugAssistant.Module.Build` 包引用与显式版本，改成对 `src/Sdk`、`src/Sdk.Ui` 的 `ProjectReference`；
- 端口边从 `Auto`（右/左）改成接收 `Top`、发送 `Bottom`，与内置模块叠放一致；
- 配置标题与枚举协选项改用 `TitleKey`（见上一节"本地化"），异常文案不带中文，优先用框架的 `ThrowIf*`；
- 子目录（`Codec/`、`Views/`）的命名空间补上目录段；
- 测试从独立工程并进 `HYWDebugAssistant.Modules.Tests`（一个模块一个文件），并把新视图登记到 `tools/PreviewHost/ModuleViewAnchors`。

## 接入测试与预览

| 位置 | 要做什么 |
|---|---|
| `tests/HYWDebugAssistant.Modules.Tests/HYWDebugAssistant.Modules.Tests.csproj` | 加一条 `ProjectReference`（按批次分组注释） |
| 同项目 | 新增 `<模块>Tests.cs`，一个模块一个文件；用 **Shouldly**、中文用例名；不写布局/像素断言。用例预算见[测试体系](tests.md)，超预算新增要写明替代了哪条覆盖 |
| 契约对照 | 测试里直接 `ModuleContract.Probe(typeof(内置对端))` 比 `DataTypes`/`Ports`（形状、`Semantics`、端口语义），比"两个新模块互连"更早发现问题 |
| `tools/PreviewHost` | 工程引用按目录 glob 自动包含新模块；但 `ModuleViewAnchors.All` 是**手写锚点清单**，必须补上新视图类型，否则预览器解析 `x:Class` 时可能加载不到模块程序集 |

## 代码约定（门禁会拦）

| 规则 | 具体做法 |
|---|---|
| 视图根 | 继承 `ModuleView`；绑定配置字段的视图必须写 `[ModuleViewDesignData(typeof(配置类型))]`，否则预览器里看不到字段内容；文本/数字用 `ModuleTextSurface`，枚举用 `ModuleSelect`，布尔用 `ModuleToggle` |
| 本地化（审计④） | `src/Modules` 下 `.cs` 字符串与 `.axaml` 属性值**不得含中文**。配置标题用 `[ConfigurationField(TitleKey = ...)]`，枚举协选项用 `[ConfigurationChoice("", TitleKey = ...)]`；键集中在一个 `*Text` 类的常量里，文案进 `resources/i18n/<程序集简名>/zh-Hans.json`；XAML 只写 `{loc:Tr 键}` |
| 命名空间（AR-14） | 命名空间 = 项目名 + 目录名。`Codec/` 下的文件必须是 `<项目命名空间>.Codec`，不要把共享代码放进另一个命名空间 |
| 端口几何 | 在 `Views/*.axaml` 的 `ModuleView.Ports` 里声明：接收口 `Top`、发送口 `Bottom`（与内置发送器/监视器一致，上下叠放即连）；跨窗口仍可用命名网络 |
| 热路径 | 编码/解析路径复用缓冲，稳态不按帧分配；不引 LINQ、不装箱、不用异常做控制流 |
| 契约 | 与内置对端同 `TypeId` + 同形状 + 同传递语义；会让消费方整帧丢弃的字段约束（如 `groupId != 0`、`payloadBytes == 4 × channelCount`）由发送端保证 |

## 文档

| 位置 | 要做什么 |
|---|---|
| `docs/zh/guides/modules/<slug>.md` | 每个模块一册：一句话定位、界面表、协议/配置、状态句、典型用法、相关。`<slug>` 必须等于模块 id 的尾段 |
| `docs/mkdocs.yml` | 在「模块目录」下加导航行；**缺页会让 `--strict` 构建失败** |
| `docs/zh/guides/modules/index.md` | 表格由模块清单生成（页面里的 `gen:modules` 标记），标题与说明取 i18n 的 `module.title` / `module.description`，不手写 |
| 契约页 | 新约定若已有专页（如[波形帧约定](../concepts/waveform-frame.md)），把模块补进"相关" |

## 收尾

1. `.\build.ps1` 全绿（文档、审计、构建 0 警告、模块包、作者包、模板宿主、架构与测试）；
2. 起应用实测：与**至少一个内置对端**建网并看到数据；保存退出再重开，确认恢复；
3. 人工验收：自己跑一遍并看截图（当次要看的现象写在提交说明里，不另建清单文档）；
4. 需要发版时用 `.\tools\version.ps1 -Round` 提升轮次（唯一事实源是 `version.props`）。

## 相关

- [测试体系](tests.md)：测试项目划分与用例预算
- [构建与门禁](build.md)：门禁步骤与产物位置
- [模块开发](../module-dev/index.md)：外部作者视角的完整流程
- [数据面](../concepts/data-plane.md)：类型身份、兼容判定与约定设计
