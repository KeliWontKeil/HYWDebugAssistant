# 模块开发

面向准备编写扩展模块的开发者。目标是：只拿得到作者包、文档与产品，也能完成从建项目到装载排障的全过程。

## 三条路线

| 路线 | 起步样例 | 需要多声明什么 | 典型形态 |
|---|---|---|---|
| 纯数据变换 | `samples/DataModule` | 类型 + 入口/出口 + 角色 | 转换、解析、聚合 |
| 设备接入 | `samples/ExternalModule` | 上面再加一条外部接入（标准标识） | 串口 / TCP / UDP 接入 |
| 带界面 | 任一 + `Sdk.Ui` | 再加显示源 / 输入 / 命令与视图工厂 | 发送器、监视器 |
| 自发活动 | `samples/ActivityModule` | `IActivitySource` 的 `OnStep` + `IModuleContext.Timers` 周期定时器 | 周期采样、定时发送 |

## 七步

| 步骤 | 做什么 | 判据 |
|---|---|---|
| 1 起项目 | 复制对应的样例工程 | `dotnet build` 通过 |
| 2 声明 | 类型 → 端口 → 角色 → 显示源 / 输入 / 命令 →（可选）外部接入 | `ModuleContract.Probe` 无问题 |
| 3 自检 | 在测试项目里跑契约自检 | `Problems` 为空；`Advisories` 逐条确认 |
| 4 实现 | 收数据、发数据、更新显示源 | 用例能驱动模块的单步入口 |
| 5 打包 | `dotnet build -c Release`，取 `bin/module-package/Release/<模块名>/` | 包内只有模块自有文件 |
| 6 装载 | 产品 → 模块清单 → 选择包目录 → 重启应用 | 模块出现在可用列表并能加入现场 |
| 7 交付 | 写模块自己的使用说明与类型约定 | 对端能按同一约定连上 |

## 页面

| 页面 | 回答什么 |
|---|---|
| [从零开始](quickstart.md) | 从样例到装进产品的第一遍完整路径 |
| [项目与包布局](project-layout.md) | 最小文件集、`module.json` 字段、包内该有什么、不该有什么 |
| [声明面](contracts.md) | 类型、端口、角色、显示源、命令、外部接入怎么声明，哪些会被拒 |
| [实现](implementation.md) | 生命周期钩子、线程纪律、收发数据、发布状态 |
| [自发活动与定时器](activity.md) | `OnStep` / `IModuleTimer` / `ReceiveMode`：周期、数据、会话、输入与停止 |
| [界面](ui.md) | 视图工厂、控件库、绑定键与预览器限制 |
| [资源与文本](resources.md) | 文本表、图标与模块自有资源如何随包分发 |
| [测试与验证](testing.md) | 契约自检与行为测试怎么写，包怎么校验 |
| [发布与装载](publishing.md) | 打包、装载、重启、同标识冲突、被拒原因 |
| [SDK 与兼容](sdk-version.md) | 作者包有哪些、版本怎么对齐、升级后旧模块会怎样 |

## 相关

- [模块机制](../concepts/modules.md)
- [数据面](../concepts/data-plane.md)
- [外部接入](../concepts/access.md)
