# SDK 与版本

## 从哪拿

五个作者包与四个样例随产品发布目录里的 `sdk/` 提供：

- 包：`HYWDebugAssistant.Sdk.*` 与 `HYWDebugAssistant.Module.Build` 的 `.nupkg` 文件；
- 样例：`sdk/samples/` 下的 `DataModule`、`ExternalModule`、`ActivityModule`、`UiModule`；
- 版本：包文件名里的四段版本就是当前版本，样例已经写好，不需要手填。

外部工程把本地源指向 `sdk/` 即可还原（写法见[项目与包布局](project-layout.md)）：

```xml
<add key="hyw-sdk" value="..\..\sdk" />
```

## 包有哪些

| 包 | 类别 | 用途 |
|---|---|---|
| `HYWDebugAssistant.Sdk` | 运行时 | 声明、生命周期、上下文、上报（模块作者的主要面） |
| `HYWDebugAssistant.Sdk.Ui` | 运行时（可选） | 视图契约与控件库，依赖 Avalonia |
| `HYWDebugAssistant.Sdk.AccessKit` | 运行时（可选） | 外部接入通道：请求、读写原始字节、状态呈现 |
| `HYWDebugAssistant.Sdk.Testing` | 作者包 | 契约自检与测试上下文，只给测试项目 |
| `HYWDebugAssistant.Module.Build` | 作者包 | 模块打包规则（`module.json` / `resources` 归拢） |

运行时包随宿主发布；两个作者包只参与构建与测试，**不随宿主运行时加载，也不得进模块包**——模块运行时引用 `Sdk.Testing` 会被加载器拒绝。

## 版本

- 只有一个版本号：`主.小.修订.轮次`，产品、发布清单与全部作者包共用。
- 一次构建的五个包版本必须相同；不同版本的包混用会在还原阶段就失败。
- 没有"Sdk 版本"这种第二个版本轴：包是产品的派生，不单独编号。

## 兼容

- **0.x 允许破坏性变更**：新增可选声明优先用 `init` 属性或新类型；删除或改签名会提升版本，并要求重编模块。
- **不比较版本区间**：加载器不校验模块编译时用的包版本；模块能加载的前提是程序集能加载、入口实现模块接口、依赖可解析。
- 运行时按**简单名**绑定：按旧包编译的模块可能绑到新宿主，但 API 变化会在类型加载或第一次调用时失败。
- 升级作者包后**重编模块**是唯一正确做法；`0.x` 期间不承诺旧包可加载。
- 加载失败时，错误文案会带上"编译时引用的 SDK 版本"，用于判断是否该重编。

## 什么时候必须重编

- 任何一次包版本提升（作者包与宿主版本对齐）；
- 宿主 API 面变化（新增抽象成员、改签名、删类型）；
- 目标框架变化。

版本提升与兼容处置的完整规则见[版本与提交](../project/versioning.md)，每个版本包含的改动见[更新日志](../about/changelog.md)。

## 相关

- [发布与装载](publishing.md)
- [测试与验证](testing.md)
