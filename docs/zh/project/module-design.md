# 模块设计取舍：先找现有机制

本页是新增内置模块或改 `Sdk.Ui` 控件前的设计检查表。它回答：一个需求该落在哪条已有通道；什么时候才值得新增控件或框架机制。

案例来自 `hyw/command-sender`（指令发送器）：同一需求先被设计成一条新的“列表数据通道”，最终只用“固定值池 + 一个行容器控件”完成。

## 一句话

**先证明现有机制不够，再新增机制。** 新增 `Declare*`、收件箱事件、持久化字段、视图状态 API 之前，必须能指出“哪条现有通道为什么不够”；指不出就不加。

## 本案：两种设计

| 需求 | 一开始的提案（未采用） | 最终落法 |
|---|---|---|
| 列表数据 + 持久化 | 新“列表源” + `ListSnapshot` + `ListEdit` + `IViewListAccess` | 256 槽 × 6 个声明值（1536 个）+ `order` 值 |
| 行内编辑 + 实时校验 | `PreviewListField` / 提交事件 / 行作用域 | 值 `OnChange` → `InputChanged` → 模块 `OnStep` → `InputValidity` |
| 行内动作（单发 / 删除） | 命令参数 / 新收件箱事件 | `SubmitInput("action", {"op":…,"slot":…})` + `OnInput` |
| 勾选顺序 | `ViewStateSnapshot.listSelections` + 序列化 | `selection` 值 + `Persistent` 标志 |
| 视图行 | 列表快照 + `IModuleViewContextScope` + 宿主遍历改动 | `ModuleValueList`；每行绑定真实值名；根上下文即可 |
| 配置 / 导入导出 | 模块配置保存列表 | 值 + `order`；导入导出读写值 |
| 框架侧改动 | 约 20 个文件（Sdk / Hosting / Core） | `ModuleValueList` + 主题 + 控件文案 + `InputHint` |

> 表中“未采用”的 API 名只用于说明当时的提案，仓库里没有这些类型。

成本对照：

- 1536 个值只是**注册期记录**；只有活动槽位写视图状态 / 持久化；没有运行期成本。
- 一个行容器控件（近 800 行）组合现有编辑器；`ModuleTextSurface.InputHint` 是一个通用属性。
- 没有模块配置、没有新的上下文成员、没有持久化 / 运行时 / 序列化改动。

## 现有通道清单

| 通道 | 适合 | 不适合 |
|---|---|---|
| 值 | 用户可编辑、持久化、实时校验的数据；固定值池可以承载动态行 | 把整张表压成一个 JSON 文本值；为“值太多”自造存储 |
| 显示源 | 只读数值、文本、日志、状态 | 可编辑数据、行字段 |
| 输入 | 一次性动作、事件、JSON 动作、文件路径 | 每次键入的字段值（应是个值） |
| 命令 | 无参按钮动作 | 每行一个命令、带参数命令 |
| 外观 | 启用、只读、颜色、角色 | 业务状态、数据 |
| 文件 / 定时器 / 资源 | 文件选择、周期、包内资源 | 自建线程、轮询、配置 |
| 控件 | 补布局组合 | 新增数据契约、上下文成员 |

## 决策顺序

1. 用户能编辑并持久化吗？→ 值。
2. 行数动态吗？→ 固定值池 + `order` 值；不要新列表源。
3. 需要勾选顺序吗？→ 一个 `selection` 值；`Persistent` 决定保存。
4. 行内动作？→ `SubmitInput` JSON；命令保持无参。
5. 需要实时校验？→ 值 `OnChange` + `InputChanged` + `InputValidity`。
6. 视图需要快照吗？→ 先看能否直接 `ReadValue`；只有只读大数据 / 日志才用显示源。
7. 需要配置吗？→ 只为标量设置 / 表单；不要让配置和值各存一份。
8. 需要新控件吗？→ 只有“现有控件组合不出布局”时。
9. 需要新上下文成员 / 收件箱事件 / 持久化字段？→ 先写出“现有通道失败”的证据；没有证据不加。框架对话框/控件状态服务（如 `ImagePicker`、`ViewImageStore`）按 `ColorPicker` 的 UI 扩展点处理，不算模块数据通道；控件图片写入 `viewState`，与模块壁纸是同一套实例资源槽的两个用途（`Wallpaper` / `ViewImage`），共用 `assets/<hash>.<ext>` 命名，不是新的资产机制。
10. 这是产品语义还是框架机制？→ 发送顺序、默认值、删除位置、文案属于模块，不要做进框架。

## 反模式（本案实际出现过的）

- 为一个模块新增 `DeclareList` / `ListSnapshot` / `ListEdit` / `IViewListAccess`。
- 为选择顺序改 `ViewStateSnapshot` 和序列化。
- 为实时校验新增预览 / 提交事件。
- 为行编辑改宿主遍历、加作用域上下文。
- 因为“192 个值太多”改用 JSON 文本值 + 自绘编辑器；实际用了 1536 个值。
- 为列表加配置，和值形成两份事实。
- 把“每次失焦写配置”“框架 API 增量”当成不可避免的风险；本案没有配置，也没有运行时 API。
- 把“固定 32 槽位”当保守值；实际 256 槽、只有活动槽位持久化。
- 为搜索框的占位提示另造控件 / 数据源；实际只给 `ModuleTextSurface` 加了通用 `InputHint`。

## 什么时候可以新增控件

- 只有**布局组合**缺失：多字段行、动态行、带序号勾选、行内动作。
- 公共面只接受**真实值名 / 命令**和**不透明行标识**；控件不知道 `send` / `delete` / `hex` 的语义。
- 组合现有编辑器；不要自绘文本编辑、换行、转义、撤销。
- 不新增 `IModuleViewContext` 成员；先试 `ReadValue` / `CommitValue` / `Appearance`。**例外**是框架对话框/控件状态服务（`IModuleColorPicker` / `IModuleImagePicker` / `IModuleViewImageStore`）：它们把既有 UI 窗口或控件自己的持久化状态暴露给视图，不是模块数据通道；控件图片在 `viewState`，与壁纸共用同一个 `assets/` 命名规则（不按用途拆扩展名）。
- 设计期占位行、主题 token、键盘可操作；`Rows` 只在引用变化时重建。
- 创建 / 重建行后显式 `Refresh(context)` 子编辑器；焦点在布局完成后用 `Dispatcher.UIThread.Post`。
- 测试：模块行为走 T1；控件用机制探针 + 截图，不写像素断言。

## 本案真正需要的实现细节

- `Rows` 引用变化才重建；值编辑不重建，避免打断正在编辑的控件。
- 行内发送按钮按 `ReadValue(content).IsInvalid` 禁用；批量 / 连续由模块 `Appearance.SetEnabled`。
- 勾选标记是自定义 `Border`（强调底 + 白色序号 + Space/Enter + 悬停提示），不是 `ModuleCheckBox`。
- `order` / `selection` 是 JSON 整数值；视图代码后置解析 `order` 建行，行动作转成输入 JSON。
- `OnStep` 顺序：输入 → 命令 → 同步 order / selection / interval / slots → 闸门 → 发送。
- HEX 转换在 `OnInput` 里用 `AcceptText` 写回；失败回弹布尔值；没有新机制。
- 导入不拒绝非法 HEX：按普通非法项标红并禁用发送。

## 验收问题

设计评审时至少回答：

- 每条新增机制替代了哪条现有通道的哪个失败？答不出就不加。
- 删掉新增机制后，需求是否真的做不到？能否用“固定值池 + 一个顺序值 + 一个动作输入 + 一个控件”？
- 新增的持久化 / 视图状态 / 收件箱字段，能否换成值的 `Persistent`、一个文本值或一个输入 JSON？
- 新增的控件是否只是组合现有控件？是否引入了新的数据契约？
- 这个决定是产品语义还是框架机制？产品语义留在模块。

## 相关

- [新增内置模块](modules.md)：落地清单
- [控件](../module-dev/controls.md)：`ModuleValueList` 与各编辑器
- [值](../module-dev/values.md)：持久化、提交时机与校验
- [实现](../module-dev/implementation.md)：`OnStep`、收件箱与文件
- 案例设计：`dev/design/command-sender.md`
- 模块册：[指令发送器](../guides/modules/command-sender.md)
