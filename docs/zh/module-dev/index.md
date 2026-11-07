# 模块开发

目标：只拿得到产品发布的作者包与文档，也能做出**可构建、可预览、可加载**的模块。

## 前置

| 需要 | 说明 |
|---|---|
| 产品发布目录里的 `sdk/` | 五个作者包与四个样例源码；包的获取与版本见 [SDK 与版本](sdk-version.md) |
| .NET SDK | 版本见产品说明；样例的 `TargetFramework` 已写好 |
| 可选：Avalonia 扩展 | VS 或 Rider 的 Avalonia 扩展，用来预览模块界面 |

## 四条路线

| 路线 | 起步样例 | 需要多声明什么 | 典型形态 |
|---|---|---|---|
| 纯数据变换 | `DataModule` | 类型 + 入口/出口 + 角色 | 转换、解析、聚合 |
| 设备接入 | `ExternalModule` | 上面再加一条外部接入（标准标识） | 串口 / TCP / UDP 接入 |
| 带界面 | `UiModule` | 再加显示源 / 输入 / 命令与视图工厂 | 发送器、监视器 |
| 周期任务 | `ActivityModule` | `IActivitySource` 的 `OnStep` + `IModuleTimer` | 周期采样、定时发送 |

## 七步

| 步骤 | 做什么 | 判据 |
|---|---|---|
| 1 取样例 | 复制 `sdk/samples/` 里最接近的样例 | 复制出的目录能在仓库外单独 `dotnet build` |
| 2 改身份 | 改三个名字：工程程序集名、`module.json`、`[ModuleDefinition]` | 三处标识一致 |
| 3 声明 | 类型 → 端口 → 角色 → 显示源 / 输入 / 命令 →（可选）外部接入 | `ModuleContract.Probe` 的 `Problems` 为空 |
| 4 实现 | 收数据、发数据、更新显示、处理命令 | 用例能驱动模块的单步入口 |
| 5 自检 | 契约自检 + 行为测试 | 见 [测试与验证](testing.md) |
| 6 构建 | `dotnet build -c Release` | 产物在 `bin/module-package/Release/<包名>/` |
| 7 装载 | 产品 → 模块清单 → 导入包目录 → 加入现场 | 窗口出现、端口能连、数据能动 |

## 页面

| 页面 | 回答什么 |
|---|---|
| [从零开始](quickstart.md) | 从样例到产品里出现窗口的完整命令序列 |
| [模块结构](anatomy.md) | 模块由哪几层组成，每层定义在哪、失败时是什么现象 |
| [项目与包布局](project-layout.md) | 最小文件集、`module.json` 字段、包内该有什么 |
| [声明面](contracts.md) | 类型、端口、角色、显示源、命令、外部接入怎么声明 |
| [实现](implementation.md) | 钩子与线程、收发、配置、上报、显示、外观、定时器、文件、资源、接入 |
| [自发活动与定时器](activity.md) | 何时用 step、何时用定时器；Push 与 Pull |
| [界面](ui.md) | 视图工厂、绑定键、布局、预览步骤与限制 |
| [控件](controls.md) | 每个控件的用途、关键属性与示例 |
| [资源与文本](resources.md) | 文本表、图标与自定义资源如何随包分发 |
| [测试与验证](testing.md) | 契约自检、行为测试、定时器测试、包检查 |
| [发布与装载](publishing.md) | 打包、装载、重启、被拒原因 |
| [SDK 与版本](sdk-version.md) | 五个包从哪来、版本怎么对齐、何时重编 |

## 卡住时

| 现象 | 先看 |
|---|---|
| 还原或编译失败 | [SDK 与版本](sdk-version.md)、[项目与包布局](project-layout.md) |
| 模块导入被拒 | [发布与装载](publishing.md) 的「被拒原因」 |
| 界面空白、没有样式、配置字段是空 | [界面](ui.md) 的预览与排障 |
| 连不上、收不到数据 | [声明面](contracts.md)、[实现](implementation.md) |

## 相关

- [模块机制](../concepts/modules.md)：框架如何加载与实例化
- [数据面](../concepts/data-plane.md)：契约与兼容的机制
