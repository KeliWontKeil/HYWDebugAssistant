# 模块结构

一个模块由七层组成。每层只回答一件事，失败时的现象各不相同；写模块就是把这七层填满，不需要的层留空。

## 运行形态（硬约束）

- 模块的交付物是**类库包**：只有程序集、`module.json` 与资源；没有 `Main`/`Program`，不是 exe，不能被双击或 `dotnet run` 直接启动。
- `bin/module-package/Release/<包名>/` 只能被主应用装载；对作者和用户，模块唯一的运行形态是**主应用装入后的实例**。
- 预览宿主只给 Avalonia 设计器加载 XAML，不创建模块实例；它不是交付物，也不能用于装载/运行验收。
- 本仓库的 `tools/StandaloneModuleHost` 是门禁校验器，不是产品形态，也不随 SDK 发布。
- 术语归属见[术语表](../concepts/glossary.md)；预览与装载的边界见[界面](ui.md)与[发布与装载](publishing.md)。

## 七层

| 层 | 定义在哪 | 谁读 | 缺了或写错的现象 |
|---|---|---|---|
| 包 | `module.json` + 程序集 + `resources/` | 加载器 | 导入被拒，模块不出现在列表里 |
| 入口与元数据 | `[ModuleDefinition]`：标识、显示名、视图工厂、能力、依赖 | 加载器、模块清单、窗口 | 清单与代码标识不一致 → 拒绝加载；缺无参构造 → 不可实例化 |
| 声明面 | `OnRegistered` 里的 `Declare*`：类型、角色、显示源、输入、命令、外部接入；端口在视图 XAML 的 `ModuleView.Ports` | 实例化期校验 | 自检 `Problems` 非空；加载期 Issue；建网连不上 |
| 生命周期 | `IModule` 的六个钩子 | 状态机 | 初始化时机错（在注册期假定数据已流动） |
| 活动与调度 | `IActivitySource`：`OnStep`、`ReceiveMode`、定时器 | 实例线程 | 收不到命令/输入、周期不触发、数据没人取 |
| 视图 | 视图工厂 + 模块 XAML（含 `ModuleView.Ports` 端口声明） | 窗口与呈现层 | 窗口空白、控件无样式、读数不刷新、端口加载失败 |
| 资源 | `resources/i18n`、`resources/icons`、自定义 kind | 文本/图标目录、模块自己 | 界面显示键名；图标缺失；清单目录校验失败 |

每层的细节在对应页面：包与清单见[项目与包布局](project-layout.md)，声明规则见[声明面](contracts.md)，钩子与上下文见[实现](implementation.md)，调度见[自发活动与定时器](activity.md)，视图见[界面](ui.md)与[控件](controls.md)，资源见[资源与文本](resources.md)，成员签名见[接口参考](reference.md)。

## 一次实例的生命线

```
创建实例
  → OnRegistered（管理线程；声明全部成员，实例线程尚未启动）
  → 校验声明 → 进入 Active → OnActivated（实例线程）
  → OnStep(Start)
  → 之后：接收回调 / OnStep(Data|Session|Command|Input) / 定时器 Tick（同一实例线程，串行）
  → Disabled：OnDisabled（框架暂停定时器）
  → Enabled：OnEnabled（恢复；定时器从恢复时刻重新计）
  → Removed：OnRemoved（释放模块自己的资源）
```

这条线由框架驱动：模块只实现钩子与单步，不拥有线程、不写循环。线程与队列的完整机制见[运行期](../concepts/runtime.md)。

## 四种最小形态需要哪几层

| 形态 | 必须有 | 可以没有 |
|---|---|---|
| 纯数据变换 | 包、入口、类型 + 端口 + 角色、接收回调 | 视图、资源、定时器 |
| 带界面 | 上面 + 显示源/输入/命令 + 视图工厂 + 文本表 | 定时器 |
| 设备接入 | 包、入口、类型 + 端口、外部接入声明、会话读写 | 视图（可加） |
| 周期任务 | 包、入口、显示源、`IActivitySource` + 定时器 | 端口、视图 |

## 声明与读取的边界

- 类型/角色/界面通道的 `Declare*` 只在 `OnRegistered` 发生一次；端口在视图 XAML 的 `ModuleView.Ports` 声明。框架在加载期各读一次并转成不可变元数据，运行期不再重读。
- 视图与命令都按**名字**寻址：名字是模块在声明里给出的字符串常量，代码与 XAML 共用同一份（`{x:Static}`），不各写一份字面量。
- 类型与形状必须自洽：端口的形状要与它引用的类型完全一致，否则加载期报错。规则见[声明面](contracts.md)。
- 模块只通过 `IModuleContext` 接触框架；不持有框架对象引用，不引用、不查询其它模块。

## 相关

- [从零开始](quickstart.md)：按这七层走一遍
- [模块机制](../concepts/modules.md)：加载与实例化的机制
- [接口参考](reference.md)：每层的成员清单
