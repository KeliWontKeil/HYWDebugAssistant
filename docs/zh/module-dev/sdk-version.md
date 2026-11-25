# SDK 与版本

## 从哪拿

SDK 是独立发布目录，**不在产品目录里**：

```
HYWDebugAssistant.Sdk-<版本>/
  packages/                 五个 nupkg
  template/ModuleTemplate/  模块模板（源码，含 NuGet.config：packages + nuget.org）
  LICENSE.txt
  THIRD_PARTY_NOTICES.md
```

产品发布目录里只有应用程序本体，不包含 SDK 或样例。拿到 SDK 后保持目录完整，在 SDK 根内构建模板；把模板单独复制到别处时，把模板 `NuGet.config` 的 `hyw-sdk` 指向 SDK 的 `packages/`。

当前 SDK 不发布到 NuGet.org，只通过目录内的本地包使用；后续再考虑公共源或压缩包。

## 包有哪些

| 包 | 类别 | 提供什么 | 依赖 |
|---|---|---|---|
| `HYWDebugAssistant.Sdk` | 运行时 | 入口、生命周期、上下文、声明面、数据面、配置、上报、资源、视图契约 | 无 |
| `HYWDebugAssistant.Sdk.Ui` | 运行时（可选） | `IModuleViewFactory`、`IModuleViewContext`、控件库、主题 | `Sdk` + Avalonia |
| `HYWDebugAssistant.Sdk.AccessKit` | 运行时（可选） | 外部接入通道骨架（`ExternalAccessChannel`）与状态模型 | `Sdk` |
| `HYWDebugAssistant.Sdk.Testing` | 作者包 | 契约自检、测试上下文、测试定时器/接入/视图状态；**不含对端装载/比对** | `Sdk`；只给测试项目 |
| `HYWDebugAssistant.Module.Build` | 作者包 | 模块打包规则（`module.json` / `resources` 归拢） | 只参与构建 |

运行时包随主应用发布；`Sdk.Testing` 与 `Module.Build` 只参与构建与测试，**不随主应用运行时加载，也不得进模块包**——模块运行时引用 `Sdk.Testing` 会被加载器拒绝。五个包都带 XML 文档，IDE 里可以直接看成员注释。

带界面的模板需要从 nuget.org（或本机缓存）还原 Avalonia；SDK 目录本身只带第一方包。

## 版本

- 只有一个版本号：`主.小.修订.轮次`，产品、发布清单、SDK 目录与全部作者包共用。
- 一次构建的五个包版本必须相同；不同版本的包混用会在还原阶段就失败。
- 没有“Sdk 版本”这种第二个版本轴：包是产品的派生，不单独编号。

## 兼容

- **0.x 允许破坏性变更**：新增可选声明优先用 `init` 属性或新类型；删除或改签名会提升版本，并要求重编模块。
- **不比较版本区间**：加载器不校验模块编译时用的包版本；模块能加载的前提是程序集能加载、入口实现模块接口、依赖可解析。
- 运行时按**简单名**绑定：按旧包编译的模块可能绑到新主应用，但 API 变化会在类型加载或第一次调用时失败。
- 升级作者包后**重编模块**是唯一正确做法；`0.x` 期间不承诺旧包可加载。
- 加载失败时，错误文案会带上“编译时引用的 SDK 版本”，用于判断是否该重编。

## 什么时候必须重编

- 任何一次包版本提升（作者包与主应用版本对齐）；
- 主应用 API 面变化（新增抽象成员、改签名、删类型）；
- 目标框架变化。

版本提升与兼容处置的完整规则见[版本与提交](../project/versioning.md)，每个版本包含的改动见[更新日志](../about/changelog.md)。

## 相关

- [项目与包布局](project-layout.md)：SDK 目录与模板的实际位置
- [发布与装载](publishing.md)
- [测试与验证](testing.md)
- [接口参考](reference.md)
