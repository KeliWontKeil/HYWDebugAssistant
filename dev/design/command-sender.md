# 指令发送器（多组字符串发送器）：模块与列表控件设计计划

> 框架层只新增了 `ModuleValueList` 控件与主题，没有新增列表数据通道、上下文成员、宿主遍历或持久化 API。
>
> 已裁决：D1 / D2 / D3 / D4 / D5 / D6 / D8 / D10。D7 明确为空内容不发送、不报错、不特殊处理。D9 收敛为“只新增控件”。「合并发送」：全部发送与连续发送都按该开关决定“合并一次”还是“逐条”。
>
> 通用设计取舍（为什么没有列表数据通道、固定值池的边界）见 `docs/zh/project/module-design.md`。

---

## 0. 结论摘要

1. **模块定位**：它不是协议工具，本质就是“**多组字符串发送器**”。每一行等价于文本/HEX 发送器的一页；`RawBytes` 的 1472 字节包、拆包、字符边界都是底层机制，与“用户眼里的这一条指令”无关。
2. **不新增任何数据通道**。行数据、行开关、隐藏换行档、选择顺序、搜索词、间隔、连续开关都是**声明值**；行内动作（单发、删除）走**已有的输入 JSON**（`SubmitInput` + `OnInput`）；持久化、红框、实时校验、按钮禁用全部由**已有的值机制**吸收。
3. **唯一框架改动**：在 `Sdk.Ui` 新增一个通用行容器控件 `ModuleValueList`（含行模型与动作事件）。它把现有 `ModuleTextSurface` / `ModuleCheckBox` / `ModuleButton` 按“值名表”排成行；**不新增 `IModuleViewContext` 成员，不改宿主遍历，不动 Core/Hosting/Sdk**。
4. **行槽位固定为 256**：值名必须在 `OnRegistered` 一次声明，因此采用“固定槽位 + `order` 值记录哪些槽在用”。只有活动槽位会写入视图状态，未用槽位不产生持久化内容；256 是“尽可能高”的实用上限（1536 个声明值）。
5. 之前提出的“列表源 / `DeclareList` / `ListEdit` / `IViewListAccess` / `listSelections` 持久化 / 行作用域 / 宿主遍历改动”**全部删除**。它们与“只新增控件接口”的约束冲突，也不是现有模块的做法。
6. 之前列出的“每次失焦写配置”“框架 API 增量过大”**不再是风险**：本设计没有模块配置写入，也没有运行时/持久化通道改动。HEX↔文本转换只是普通模块逻辑，不需要新机制。

---

## 1. 现状：已有机制足够

| 需求 | 已有的、已在别的模块验证过的做法 |
|---|---|
| 行数据持久化 | `DeclareValue(ValueEditor.Text/Boolean, Persistent=true)`；随组合件/工作区保存（TextSender 的 `text1…text5`、WaveformGenerator 的当前组/通道字段） |
| 行内编辑 | `ModuleTextSurface` / `ModuleCheckBox`，按值名绑定；`CommitMode="OnChange"` 时逐键写视图状态，模块在 `OnStep` 读 `Values.ReadText`（WaveformGenerator 的组名/频率等） |
| 实时校验 / 红框 | 模块 `InputValidity.Set(值名, …)`；控件只画结论（两个发送器、WaveformGenerator 同一口径） |
| 行内动作 | 视图 `SubmitInput("action", JSON)`；模块 `OnInput` 用 `JsonDocument` 派发（WaveformGenerator 的 `selectGroup` / `selectChannel` 就是这条） |
| 动态结构 | 结构本身存配置/值，视图从快照或值重建（WaveformGenerator 用 `snapshot` 文本源 + 当前组/通道值） |
| 选择顺序 | 一个文本值存 JSON 数组；控件写、模块读。**不需要**新增视图状态 API |
| 搜索过滤 | 一个瞬态文本值；控件按它过滤，模块不参与 |
| 文件导入/导出 | `Files.Request` + 输入源回投；模块自己读写（DataMonitor / WaveformDisplay 同一口径） |
| 连续发送 | `continuous` 值 + `IModuleTimer` + `interval` 值（TextSender/HexSender） |
| 拆包 | 文本按字符边界、HEX 按 1472 字节；每个包 `{len,data}`（两个发送器） |

**唯一缺口**：没有一个控件能把“一行里的多个现有编辑器”按值名表排成可变列表。`ModuleTabs` 是固定页签，`ModuleSegmentedControl` 是选择器，`ItemsControl` 无法为动态行提供值名。因此只新增 `ModuleValueList` 这一件控件，其他一律复用。

---

## 2. 裁决状态

| 编号 | 状态 | 问题 | 结论 / 推荐 |
|---|---|---|---|
| **D1** | **已裁决** | 「全部发送」的语义 | **按勾选顺序发送全部勾选项**；无勾选禁用。勾选顺序必须在界面上可见：**序号显示在勾选标记内**（未选=空框，选中=强调底 + 白色 1 基序号） |
| **D2** | **已裁决** | 两个换行勾选的含义 | 与文本发送器完全一致：只决定按 Enter 写入哪种真实换行并归一已有真实换行，**不追加**；HEX 下勾选项禁用/无效 |
| **D3** | **已裁决并落地** | 勾选顺序是否保存 | 保存：`selection` 值声明为 `Persistent=true`；重开窗口仍按原顺序发送。**不需要任何框架改动** |
| **D4** | **已裁决** | 删除 | 行内加 `×` 删除该条；工具栏只保留“新建”（其它动作条按钮不变） |
| **D5** | **已裁决** | 超长 | 照常拆包：文本按字符边界、HEX 按 1472 字节；一条逻辑指令可对应多个数据包；不再有长度非法 |
| **D6** | **已裁决** | 存在非法项 | 直接禁用发送按钮：行内单发看该行；批量/连续看勾选集合；动作边界仍拒绝 |
| **D7** | **已澄清** | 空条目 | **不是问题**：空内容 → 0 个数据包 → 不发送；不报错、不写特殊状态、不改变连续状态。连续 Tick 遇到空行自然什么也不发，下一拍继续 |
| **D8** | **已裁决** | 新建默认值 | 标签“指令 N”，内容空，HEX 关、CR 开、LF 开；不自动勾选 |
| **D9** | **已修正** | 列表数据的承载 | **只用声明值 + 输入 JSON + 新控件**；不新增列表源、不收发结构化事件、不改持久化/视图状态 |
| **D10** | **已裁决** | 切换 HEX 时内容转换 | 文本→HEX 按转义/UTF-8 字节生成 HEX；HEX→文本先解析，合法 UTF-8 才转 C 转义文本，否则保持 HEX 并说明 |

**D6 的范围**：按“这次要发送的集合”理解（勾选集合内的非法才禁用批量/连续；未勾选的坏行不阻塞）。若要“全表一行非法就全禁用”，把闸门改成全表扫描即可。

### 2.1 实现参数（可改，不构成裁决）

| 项 | 初值 | 说明 |
|---|---|---|
| 行槽位上限 | **256** | 值名注册期固定；1536 个声明值。只有活动槽位写入视图状态/持久化；256 是“尽可能高”的实用值。真无上限需要“单 JSON 值 + 控件自绘编辑器”或新数据通道，二者都会偏离本轮约束 |
| 标签长度 | 64 字符 | 单行 |
| 内容编辑框高 | 最小 22 / 最大 96 DIP | 超出在框内滚动 |
| 间隔范围 | 1–86 400 000 ms | 与两个发送器同口径 |
| 行内边距 | 4 DIP | 1 DIP 分隔线 |

### 2.2 合并发送与逐条发送（v1.1 追加）

| 场景 | 行为 |
|---|---|
| 合并 + 全部发送 | 按勾选顺序拼接各条编码后的字节，一次逻辑发送；超过 1472 字节按容量拆包，命令之间无额外间隔 |
| 合并 + 连续发送 | 每个间隔把全部勾选项合并发送一次，不是轮流单发 |
| 不合并 + 全部发送（>1 条） | 首条立即，后续按 `interval` 逐条发送；发送期间主/行内发送按钮与连续开关禁用 |
| 不合并 + 全部发送（=1 条） | 立即发送，不要求间隔 |
| 不合并 + 连续发送 | 保持 v1.0：每拍发送一条，按勾选顺序循环 |

- 合并开关是 UI 模式（`merge` 值，不持久化、不写指令集 JSON）；默认不勾选。
- 逐条发送的快照（槽位与间隔）在启动时捕获；期间改勾选/顺序、删除、新建、导入会取消。
- 连续发送与逐条序列互斥：连续运行期间「全部发送」禁用；逐条期间连续开关禁用。
- `sending` 值是行内发送按钮的总闸：逐条序列或连续发送运行期间为 `true`。
- 合并后的包数仍可能 >1（1472 上限）；“一条”是逻辑一条。

---

## 3. 框架侧：只新增 `ModuleValueList`

### 3.1 它是什么

一个**值名驱动的行容器**：调用方给它一张“行表”，每行给出 6 个**已声明的值名**（标签、内容、HEX、CR、LF、换行档）；它用现有控件把行画出来，并处理勾选顺序、过滤、行内按钮。它不读配置、不读视图状态、不新增上下文成员。

```csharp
namespace HYWDebugAssistant.Sdk.Ui.Controls;

public sealed class ModuleValueList : ModuleElement
{
    public IList<ModuleValueListRow> Rows { get; }         // 由视图代码后置设置
    public string SelectionValue { get; set; } = "";       // 可选：选择顺序写入该值（JSON 数组）
    public string FilterValue { get; set; } = "";          // 可选：过滤词所在值
    public string SendInProgressValue { get; set; } = "";  // 可选：true = 行内发送全禁用（自动发送中）
    public string SendText { get; set; } = "发送";         // 行内发送按钮文案
    public double ContentMaxHeight { get; set; } = 96;
    public int PreviewRowCount { get; set; } = 2;          // 仅设计期

    public event EventHandler<ModuleValueListRowAction>? RowAction;

    public void FocusRow(string id);
}

public sealed record ModuleValueListRow(
    string Id,
    string LabelValue,
    string ContentValue,
    string HexValue,
    string CrValue,
    string LfValue,
    string NewlineValue);

public readonly record struct ModuleValueListRowAction(string RowId, string Action); // "send" | "delete"
```

- `Id` 用槽位号字符串；行动作 JSON 里也只带槽位号。
- 控件不关心字段语义，只按 `ModuleValueListRow` 建控件。
- 行动作不直接调命令，而是抛事件；视图代码后置转成 `SubmitInput("action", {"op":…,"slot":…})`，与 WaveformGenerator 的 chips 完全相同。

### 3.2 行结构与行为

```text
行（Border/Grid）
 ├─ 选择列（24）：编号勾选标记（未选=空框；选中=强调底 + 白色 1 基序号）
 ├─ 主列（*）：StackPanel
 │    ├─ ModuleTextSurface（Value=LabelValue，单行，TextFormat=Text）
 │    └─ ModuleTextSurface（Value=ContentValue，多行，MinHeight=22，MaxHeight=ContentMaxHeight）
 └─ 侧列（66）：StackPanel
      ├─ 第 1 行：ModuleCheckBox（Value=HexValue，Content="HEX"）+ ModuleButton（"×"）
      ├─ 第 2 行：ModuleCheckBox（Value=CrValue，Content="\r"）+ ModuleCheckBox（Value=LfValue，Content="\n"）
      └─ 第 3 行：ModuleButton（Content=SendText）
```

**编号勾选标记**不是“勾选框 + 旁边序号”，而是常见做法：未选时是 16×16 空框；选中时整块换成强调色底、中间显示白色 1 基序号（`AccentPrimary` / `TextOnAccent`）。它由控件自己绘制（不绑定值），点击/空格切换；序号的唯一来源就是本地勾选顺序。这样 24 DIP 就能同时表达“选中”和“第几个”，不额外占一列。

控件在每个 `Refresh(context)` 里：

1. `Rows` 变化才重建行；值编辑不会重建（编辑器自己按值刷新）。
2. 读 `context.ReadValue(HexValue).Value`：
   - `true`：内容面 `TextFormat=Hex`、`EscapeInput=false`、`EnterNewlineValue=""`；
   - `false`：`TextFormat=Text`、`EscapeInput=true`、`EnterNewlineValue=NewlineValue`。
3. 读 `context.ReadValue(ContentValue).IsInvalid`：禁用该行发送按钮（红框由值结论/控件本身画）。
4. 读 `context.ReadValue(FilterValue).Value`：按标签/内容值做大小写不敏感过滤，只改 `IsVisible`；勾选集合不变。
5. 首次读 `SelectionValue` 恢复勾选顺序；用户勾选/取消时更新本地顺序、重排所有可见/隐藏行的序号，并 `context.CommitValue(SelectionValue, json)`。
6. 行发送 / `×` → `RowAction`；视图转成输入。
7. CR/LF 在 HEX 下是否可点：由模块 `Appearance.SetEnabled(CrValue/LfValue, false)` 控制，控件不判。
8. 可选 `SendInProgressValue` 为 true 时，所有行内发送按钮禁用；模块的自动发送（逐条序列/连续）用这个总闸。

**设计期**：`ModuleView.IsDesignEnvironment` 且 `Rows` 为空时，按 `PreviewRowCount` 造 2 行占位（值名为空、`Text` 写占位文本、复选框默认勾选、编号标记分别显示 1/2），让预览器能定位全部控件；运行期不走这条。

### 3.3 为什么这样不破坏架构

- 控件只经 `IModuleViewContext` 的**已有**成员取数/提交：`ReadValue` / `CommitValue` / `Appearance`。没有新契约。
- 行内编辑器是**真实的已声明值**：宿主遍历用根上下文即可，不需要作用域，不需要改 `ModuleViewHost`。
- 行结构、选择、过滤是控件自己的界面行为，不进入模块/框架状态。
- 与 `ModuleSegmentedControl` 的区别只是“行里放多个编辑器”；与 `ChartSurface` 一样，属于“控件 + 现有契约”的扩展，不是第二套数据面。

### 3.4 改动清单

| 文件 | 改动 |
|---|---|
| `src/Sdk.Ui/Controls/ModuleValueList.cs`（新） | 控件、行模型、动作事件 |
| `src/Sdk.Ui/Themes/Controls.axaml` | 控件主题、行/选择列样式 |
| `resources/i18n/HYWDebugAssistant.Sdk.Ui/zh-Hans.json` | 空态、无匹配、删除提示等框架文案 |
| `docs/zh/module-dev/controls.md` / `ui.md` | 控件用法、值名表、设计期预览 |
| `tests/HYWDebugAssistant.VisualSnapshots/...` | 控件机制探针 + 截图 |

**没有** Core / Hosting / Sdk / Ui 的运行时改动；**没有**新的 `Declare*`、收件箱事件、视图状态字段或持久化字段。

---

## 4. 模块侧设计

### 4.1 身份、端口、类型

```csharp
[ModuleDefinition("hyw", "command-sender",
    DisplayNameKey = CommandSenderText.Title,
    DescriptionKey = CommandSenderText.Description,
    Tags = ["source", "command", "sender"],
    Capabilities = ModuleCapability.File,
    ViewFactory = typeof(Views.CommandSenderViewFactory))]
public sealed class CommandSenderModule : IModule, IActivitySource
```

| 项 | 值 |
|---|---|
| 模块 id / 包名 | `hyw/command-sender` / `command-sender` |
| 端口 | `out`，`Send`，`RawBytes`，`Edge=Bottom` |
| 类型 | `RawBytes`：`{ len:u16, data:bytes[1472] }`，`Metering.PayloadLengthField="len"` |
| 角色 | `command-send`（纯输出） |
| 窗口 | 400×400（内容区 358×350） |

### 4.2 固定槽位与值表

`MaxSlots = 256`。槽位 `i` 的 6 个值名由同一函数生成，模块声明和视图建行都用它：

```csharp
public enum SlotField { Label, Content, Hex, Cr, Lf, Newline }

public static string SlotValue(int slot, SlotField field) => field switch
{
    SlotField.Label   => $"cmd{slot}Label",
    SlotField.Content => $"cmd{slot}Content",
    SlotField.Hex     => $"cmd{slot}Hex",
    SlotField.Cr      => $"cmd{slot}Cr",
    SlotField.Lf      => $"cmd{slot}Lf",
    _                 => $"cmd{slot}Newline",
};
```

`OnRegistered` 里循环声明：

| 值 | 编辑器 | 持久化 | 重置 | 说明 |
|---|---|---|---|---|
| `cmd{i}Label` | Text | 是 | 否 | 单行标签；搜索匹配 |
| `cmd{i}Content` | Text | 是 | 否 | C 转义文本；内容面 |
| `cmd{i}Hex` | Boolean | 是 | 否 | 是否 HEX |
| `cmd{i}Cr` | Boolean | 是 | 否 | Enter 写入 CR |
| `cmd{i}Lf` | Boolean | 是 | 否 | Enter 写入 LF |
| `cmd{i}Newline` | Text | 否 | 否 | 由 CR/LF 派生的 `crlf/cr/lf/none`；只给 `EnterNewlineValue` 用 |

另加：

| 值 | 编辑器 | 持久化 | 说明 |
|---|---|---|---|
| `order` | Text | 是 | 活动槽位顺序，JSON 数组如 `[0,3,1]`；没有编辑器，模块写 |
| `selection` | Text | 按 D3 | 勾选顺序，JSON 数组；控件写、模块读 |
| `search` | Text | 否 | 过滤词；控件读、模块不读 |
| `interval` | Number | 是 | 连续发送间隔 |
| `continuous` | Boolean | 否 | 连续开关 |

显示源：`sent`（Value/U64，数据包数）、`status`（Text）。
命令：`add`、`sendAll`、`import`、`saveAs`。
输入：`action`（行动作 JSON）、`importPath`、`exportPath`。

> 256×6 = 1536 个行值 + 5 个控制值。数字看起来多，但全部由循环声明，代码只有几行；**只有活动槽位会写入视图状态**，未用槽位不产生持久化内容。这是“值名注册期固定”的直接结果。

### 4.3 新增 / 删除 / 顺序

- **新建**（命令 `add`）：找 `order` 里没出现的最小槽位；若 256 个都在用，禁用按钮并说明。设默认值（D8）→ `AcceptText` 6 个槽位值 → 追加到 `order` → 写 `order`。视图看到 `order` 变化后重建行。
- **删除**（行 `×` → `action` JSON `{op:"delete", slot}`）：从 `order` 移除；清空该槽 6 个值；状态句；视图重建。**不做搬移**，槽位可被下次新建复用。
- **顺序**：列表展示顺序就是 `order` 顺序；发送顺序是 `selection` 顺序，两者独立。`order` 由模块维护，不在界面提供拖拽。
- 视图代码后置按 `order` 文本缓存：**只有 JSON 变化才重新设置 `Rows`**；每帧重建会打断正在编辑的控件。

### 4.4 编辑、校验与实时禁用

全部走值机制，不新增预览/提交通道：

```text
用户键入内容 → ModuleTextSurface（OnChange）CommitValue(cmd i Content)
  → 框架写视图状态并 InputChanged → 模块 OnStep(Input)
  → 模块读 Values.ReadText(cmd i Content) → 判 HEX 是否合法
  → InputValidity.Set(cmd i Content, invalid, reason) → 红框/悬停
  → 重算 selection 闸门 → Appearance.SetEnabled(sendAll / continuous)
```

- 文本内容永远合法；HEX 内容解析失败才非法；空内容合法但不发送。
- 行内发送按钮：`ModuleValueList` 读 `ReadValue(Content).IsInvalid` 直接禁用。
- 批量/连续：模块按 `selection` 读槽位，任一非法就禁用；动作边界再判一次。
- **没有配置写入**：每次键入只写视图状态；持久化由组合件保存时的值快照完成。
- 模块自己 `AcceptText` 写回前先比较当前值，只有变化才写，避免自触发下一拍（与 WaveformGenerator 的 `SetInput` 同一纪律）。
- `OnStep` 顺序：排空输入/命令 → 读活动槽值、校验/转换 → 重算闸门 → 执行需要闸门的动作（`sendAll`、连续 Tick）。刚改完内容就点发送时，发送看到的是最新值。

### 4.5 HEX 切换时的内容转换（D10）

`ModuleCheckBox` 的布尔值提交会进入输入收件箱（`ModuleValuePresenter` 对 Boolean 会 `SubmitInput`），模块在 `OnInput` 里处理：

| 方向 | 做法 | 失败 |
|---|---|---|
| 文本 → HEX | `UTF8(TextEscapes.Normalize(content))` → `"48 65 6C ..."`；`AcceptText(content, hexText)` | 不会失败 |
| HEX → 文本 | 解析 HEX；字节是严格合法 UTF-8 → `AcceptText(content, EscapeText(bytes))` | HEX 解析失败：`AcceptText(hex, "true")` 回弹 + 状态句；非 UTF-8：回弹 + 说明 |

`EscapeText`：`\`→`\\`、CR/LF/TAB→`\r\n\t`、其余 C0/C1 控制字符→`\xHH`、其余字符原样。这样 `HEX→文本→HEX` 字节一致；文本→HEX→文本只保证字节一致，不保证原记法（`\x41` 会变成 `A`）。

同时：

- `Appearance.SetEnabled(cmd i Cr/Lf, !hex)`：HEX 下两个换行勾选变灰；
- `cmd i Newline` 仍按 CR/LF 派生（HEX 下内容面清空 `EnterNewlineValue`，用不到它）；
- 内容面由控件的 `Refresh` 根据 `ReadValue(Hex)` 切换 `TextFormat` / `EscapeInput` / `EnterNewlineValue`。

### 4.6 发送引擎（多组字符串发送器）

**一条逻辑指令 → 字节 → 若干数据包**：

```csharp
internal static class CommandBytes
{
    // 文本：Normalize 后按字符边界拆 <=1472 字节；HEX：按 1472 字节切。
    // packets 是已按 { len, data } 组装好的 1474 字节载荷。
    public static bool TryBuildPackets(string content, bool hex, out List<byte[]> packets, out string? failure);
}
```

1. 文本：`TextEscapes.Normalize` → 按字符边界拆块；每块 UTF-8 ≤1472；**不追加任何结尾**；
2. HEX：两位一段、空白分隔解析 → 按 1472 字节切；
3. 0 个包：合法但无内容，不发送；
4. 唯一非法：HEX 解析失败；**长度不再产生非法**；
5. 每个包 `{len:u16, data:bytes[1472]}`，`len` 是该包有效字节数。

**CR/LF 不参与发送**：它们只通过 `cmd i Newline` 影响内容面的 `EnterNewlineValue`，即“按 Enter 写什么、已有真实换行归一成什么”。真实换行作为内容的一部分随文本发送。

**三种发送**：

| 动作 | 集合 | 顺序 | 行为 |
|---|---|---|---|
| 单条发送 | 该行 | — | 该行全部数据包依次 `TrySend`；空内容自然 0 包、不发、不报错；非法按钮已禁用 |
| 全部发送 | `selection` | 勾选顺序 | 逐行展开；空内容自然 0 包、不发、不报错；门已挡非法；`TrySend` 失败计包数，不重试 |
| 连续发送 | `selection` | 勾选顺序循环 | 每 Tick 发一条逻辑指令的全部包；空内容自然 0 包、不发、不改变连续状态；非法/间隔坏停止并复原 |

`sent` 按数据包累计；状态句同时说明逻辑条数。连续状态机沿用 TextSender 的简化版：间隔合法、勾选非空、无非法项才成立；用户拨开不成立立即写回 `false`。

### 4.7 选择与搜索

- 控件维护勾选顺序，写 `selection`（JSON 数组）。模块在 `OnStep` 读 `Values.ReadText(selection)`，过滤已删除槽位，作为发送集合。
- 搜索框是普通 `ModuleTextSurface Value=search`；控件读它过滤标签/内容。过滤只影响可见性，不影响 `selection`。
- 勾选变化使 `InputChanged` 唤醒模块，模块重算按钮闸门/状态句；不需要额外事件。

### 4.8 导入 / 另存为

走 `Files.Request` + `importPath` / `exportPath` 输入源（与 DataMonitor/WaveformDisplay 同一条）：

```json
{
  "format": "hyw/command-sender",
  "version": 1,
  "intervalMs": 500,
  "commands": [
    { "label": "读版本", "content": "AT+VER?", "hex": false, "cr": true, "lf": true }
  ]
}
```

- 导入：先完整解析；`commands` ≤256；然后清空所有槽位与 `order`，按顺序写槽位值、写 `order`、清 `selection`；`intervalMs` 可选，非法忽略并说明；HEX 非法**不拒绝**，按普通非法项标红并禁用发送（与直接编辑一致）。
- 导出：按 `order` 顺序输出当前列表；不写槽位号。
- 文件 IO 失败：状态句 + Issue（`ModuleError`/`Warning`），不抛；与现有模块一致。

### 4.9 界面布局（400×400）

内容区 358×350；根 Grid 外边距 `8,6,8,4` → 342×340。

| 区域 | 高度 | 内容 |
|---|---|---|
| 动作条 1 | 28 | 新建、全部发送、连续发送开关 + 文字、间隔、`ms` |
| 动作条 2 | 26 | 搜索文字 + 搜索框、导入、另存为 |
| 列表 | `*`（约 256） | `ModuleValueList` |
| 提示栏 | 18 | 状态句（左）+ 已发送 N 包（右） |

行：

```text
[选择 24][主列 224][侧列 66]
主列：标签（22） / 内容（22..96，自适应）
侧列：第 1 行 HEX + × / 第 2 行 CR LF / 第 3 行 发送
行最小高度约 64–68；内容变高时整行变高，列表滚动
```

视觉：行间 1 DIP `BorderDefault`；选择列是编号勾选标记（序号在框内）；非法内容框 `SemanticError`；HEX 下 CR/LF 变灰；内容用 `FontData`；文本模式的真实换行显示 `↵/←/↓`；空列表/无匹配居中提示。

### 4.10 文案

模块文本表：`module.title`、`module.description`、`view.add`（新建）、`view.sendAll`、`view.continuous`、`view.intervalUnit`、`view.search`、`view.import`、`view.saveAs`、`view.send`、`view.sentTitle`、`view.sentUnit`（包）、`view.statusPreview`、`field.hex/cr/lf`、`item.defaultLabel`、以及状态句 `status.*`（就绪、已发送、无勾选、非法项、拆包、转换失败、间隔错误、导入/导出成功/失败、达到上限、连续启停）。**没有“空内容”状态句**：空就是不发送。

框架文本表补充：列表空态、无匹配、删除提示。

### 4.11 模块改动清单

| 文件 | 内容 |
|---|---|
| `src/Modules/Business/CommandSender/HYWDebugAssistant.Modules.CommandSender.csproj` | 引 `Sdk` / `Sdk.Ui`；包名 `command-sender` |
| `module.json` | `hyw/command-sender`，资源 `resources/i18n` |
| `CommandSenderModule.cs` | 声明值循环、输入/命令、校验、转换、发送、连续、导入导出 |
| `CommandSenderCodec.cs` | 转义/HEX/拆包/HEX↔文本转换/载荷 |
| `CommandSenderText.cs` | 文本键 |
| `Views/CommandSenderView.axaml` / `.cs` | 动作条、`ModuleValueList`、状态栏；代码后置读 `order` 建行、转发行动作 |
| `Views/CommandSenderViewKeys.cs` | 视图键（`{x:Static}`） |
| `resources/i18n/.../zh-Hans.json` | 文案 |
| `HYWDebugAssistant.slnx` | 加入 Business |
| `tests/HYWDebugAssistant.Modules.Tests/CommandSenderTests.cs` | 行为测试 |
| `tests/HYWDebugAssistant.Modules.Tests/BusinessModulesContractTests.cs` | 声明面 |
| `tests/HYWDebugAssistant.Tests/BusinessModulesIntegrationTests.cs` | 端到端 |
| `tests/HYWDebugAssistant.VisualSnapshots/ModuleViewSnapshots.cs` | 视图 + 控件截图/探针 |

---

## 5. 关键时序

### 5.1 编辑与实时禁用

```text
键入 → ModuleTextSurface（OnChange）CommitValue(cmd i Content)
  → 视图状态 + InputChanged → 模块 OnStep
  → 读值 → HEX 判定 → InputValidity.Set → 红框/悬停
  → 重算勾选闸门 → SetEnabled(全部发送/连续)
  → ModuleValueList 读 IsInvalid → 行内发送按钮禁用
```

### 5.2 切换 HEX

```text
点 HEX → CommitValue(cmd i Hex) + SubmitInput("cmd i Hex")
  → OnInput 转换内容 → AcceptText(content, 新文本)
  → SetEnabled(CR/LF, !hex) → 视图刷新
失败（HEX→文本）：AcceptText(hex, "true") 回弹 + 状态句
```

### 5.3 勾选 / 发送 / 连续

```text
勾选 → 控件写 selection（JSON）→ InputChanged → 模块重算闸门
全部发送 → 读 selection → 逐行 TryBuildPackets → 逐包 TrySend → sent/status
连续 → continuous 值 + timer → 每 Tick 一条逻辑指令 → 空内容自然 0 包、不发 → 非法/间隔坏停止并写回 false
```

### 5.4 新建 / 删除

```text
新建 → 找空闲槽 → AcceptText 6 个值 → 追加 order → 视图重建
删除 × → SubmitInput("action", {op:"delete",slot}) → 移除 order、清值 → 视图重建
```

---

## 6. 实施阶段

| 阶段 | 范围 | 退出判据 |
|---|---|---|
| **P0** | 裁决 D1/D3/D7/D8；本文件定稿 | 未决项清零或显式暂定 |
| **P1** | `ModuleValueList` + 主题 + 设计期预览 | 演示视图 3 行可编辑/勾选/过滤/删除/发送事件；机制探针；截图人眼 |
| **P2** | 模块值表、校验、转换、发送/连续、导入导出 | 契约用例 + 行为用例；与监视器端到端 |
| **P3** | 模块视图与联调 | 400×400 布局；人工验收清单；截图 |
| **P4** | `build.ps1` 全量 + 文档回写 | 门禁通过；设计基线冻结 |

> 没有“框架数据通道”阶段：P1 只做控件。

---

## 7. 验证计划

### 7.1 自动化

| 层 | 内容 |
|---|---|
| T0 | `Sdk.Ui` + 模块工程编译 |
| T1 | `CommandSenderTests`：转换、拆包、发送顺序、闸门、导入导出、值声明 |
| T1 | `ModuleValueList` 的纯逻辑（选择顺序、过滤、行动作）抽成可测模型 |
| T2 | `HYWDebugAssistant.Modules.Tests` 整项目 |
| T3 | `build.ps1`（P1/P2/P4 收尾各一次） |
| 截图 | `tools/dev.ps1 -Mode Snapshots`，人眼看，不进判据 |

**用例**：

1. 文本：`Normalize`+UTF-8；CR/LF 只影响 Enter；发送不追加；真实换行随文本；
2. HEX：空白/大小写/奇数位；
3. 空内容：0 包、不发送、不报错、不写特殊状态；
4. 拆包：文本按字符边界、HEX 按 1472；多字节字符不切开；包数/len 正确；
5. 勾选顺序 [3,1] → 包顺序；
6. 勾选集合含非法 → 批量/连续禁用、行内单发禁用、动作边界拒绝；
7. 连续：间隔、循环、空内容 Tick 不发且不改变连续状态、运行中变非法停并复原；
8. 间隔空/0/越界；
9. HEX↔文本转换：文本→HEX 字节正确；HEX→文本→HEX 字节一致；非法 HEX/非 UTF-8 回弹；
10. HEX 下 CR/LF 禁用，切回恢复；
11. 新建/删除/复用槽位、`order` 正确；
12. 导入/导出往返；超 256 拒绝；非法 HEX 按普通非法项；
13. 组合件保存/加载后值、`order`、`selection` 恢复；
14. `ModuleValueList`：Rows 不变不重建、过滤不改选择、行动作事件、选择 JSON 往返。

### 7.2 截图

默认空列表/一行；三条勾选序号；HEX 模式（CR/LF 变灰）；非法 HEX（红框 + 发送禁用）；无匹配；多行内容；连续中。

### 7.3 人工验收

| # | 操作 | 预期 |
|---|---|---|
| 1 | 加入模块 | 400×400；底部端口；默认一条指令 |
| 2 | 新建/编辑/删除 | 行内编辑；内容变高；行内 `×` 删除；动作条无删除 |
| 3 | 勾选 3、1 并全部发送 | 监视器按 3、1 收到；状态句报包数/条数 |
| 4 | 切换 HEX | 内容转换；CR/LF 变灰；切回可恢复文本 |
| 5 | 写非法 HEX | 行红框；行内/批量/连续发送禁用 |
| 6 | 超过 1472 字节 | 拆包；监视器收到多包；顺序正确 |
| 7 | 连续 500 ms | 按勾选顺序循环；空内容的一拍不发、不中断；取消即停 |
| 8 | 搜索 | 只过滤显示；勾选顺序不变 |
| 9 | 另存为 → 删除 → 导入 | 恢复；勾选清空；非法项红框 |
| 10 | 重启 | 列表、间隔、勾选恢复；连续为关 |

---

## 8. 风险（只列仍然存在的）

| 风险 | 影响 | 缓解 |
|---|---|---|
| 256 槽位上限 | 超过 256 条不能新建 | 值名注册期固定导致；256 已是“尽可能高”的实用值。达到上限时按钮禁用并说明。真无上限需要换承载方式（单 JSON 值 + 控件自绘编辑器，或新数据通道） |
| 1536 个值声明 | 声明代码/阅读量 | 循环 + `SlotValue` 统一命名；只有活动槽位写视图状态，无持久化膨胀 |
| 400×400 行密度 | 只看到 3–4 行 | CR/LF 并排、行内边距 4；必要时压缩动作条 |
| 变高行 + 内层滚动 + 外层 ScrollViewer | 滚轮/焦点行为 | P1 机制探针；不可接受时改为“最小 2 行 + 内部滚动” |
| 控件行重建时抢焦点 | 新建/删除后编辑中断 | 只在 `Rows` 变化时重建；`FocusRow` 显式聚焦新行 |

**已消除（按本轮意见）**：

- “每次失焦写配置”：没有模块配置；值写视图状态，组合件保存时统一持久化。
- “框架 API 增量过大”：没有 Core/Hosting/Sdk 改动；只新增一个控件。
- “选择顺序/持久化要新通道”：`selection` 就是一个值，`Persistent` 标志决定是否保存。
- “列表数据需要列表源”：行数据是声明值，视图直接按值名绑定。
- “HEX↔文本转换边界需要新机制”：转换是普通模块逻辑，失败只影响该值的结论/状态句。

---

## 9. 文档回写

| 文档 | 改什么 |
|---|---|
| `docs/zh/module-dev/controls.md` | `ModuleValueList`：行模型、值名表、选择/过滤/行动作、设计期 |
| `docs/zh/module-dev/ui.md` | 值名驱动行容器的用法；固定槽位的取舍 |
| `docs/zh/guides/modules/command-sender.md` | 新模块指南（多组字符串、换行语义、转换、拆包、非法禁用、导入导出） |
| `docs/zh/guides/modules/index.md` | 由 `gen:modules` 生成 |
| `resources/i18n/HYWDebugAssistant.Sdk.Ui/zh-Hans.json` | 列表控件文案 |
| `docs/zh/about/changelog.md` | 版本条目 |

---

## 附录 A：值名与 JSON

```csharp
// 槽位值名
cmd{slot}Label / cmd{slot}Content / cmd{slot}Hex
cmd{slot}Cr / cmd{slot}Lf / cmd{slot}Newline

// order / selection：JSON 数组，元素为槽位号
[0, 3, 1]

// 行动作输入（输入源 "action"）
{"op":"send","slot":3}
{"op":"delete","slot":3}
```

导入文件：

```json
{
  "format": "hyw/command-sender",
  "version": 1,
  "intervalMs": 500,
  "commands": [
    { "label": "读版本", "content": "AT+VER?", "hex": false, "cr": true, "lf": true }
  ]
}
```

## 附录 B：`ModuleValueList` 公共面

```csharp
public sealed class ModuleValueList : ModuleElement
{
    public IReadOnlyList<ModuleValueListRow> Rows { get; set; }
    public string SelectionValue { get; set; }
    public string FilterValue { get; set; }
    public string SendText { get; set; }
    public double ContentMaxHeight { get; set; }
    public int PreviewRowCount { get; set; }
    public event EventHandler<ModuleValueListRowAction>? RowAction;
    public void FocusRow(string id);
}

public sealed record ModuleValueListRow(
    string Id,
    string LabelValue,
    string ContentValue,
    string HexValue,
    string CrValue,
    string LfValue,
    string NewlineValue);

public readonly record struct ModuleValueListRowAction(string RowId, string Action);
```

## 附录 C：实现落地（v1.0）

| 项 | 落地 |
|---|---|
| 控件 | `src/Sdk.Ui/Controls/ModuleValueList.cs` + `Themes/Controls.axaml` 的 `ModuleValueList` 主题；框架文本键 `moduleList.*` |
| 模块 | `src/Modules/Business/CommandSender/`：`CommandSenderModule` / `CommandSenderCodec` / `CommandSenderText` / `Views/CommandSenderView.*` |
| 值 | 256 槽位 × 6（`cmd{i}Label/Content/Hex/Cr/Lf/Newline`）+ `order` / `selection` / `search` / `interval` / `continuous` |
| 发送 | 文本按 C 转义 + UTF-8 字符边界；HEX 每段两位、按 1472 字节拆包；`{len,data}` 定长载荷 |
| 勾选 | 1 基序号显示在勾选标记内；`selection` 持久化；批量/连续按勾选顺序；删除/导入后过滤失效槽位 |
| 闸门 | 行内发送按该行内容有效性禁用；批量/连续按勾选集合有效性 + 间隔禁用；动作边界再次检查 |
| 连续性 | `IModuleTimer` 循环勾选集合；空内容 tick 空转、不报错；非法/间隔失效即停止并写回 `continuous=false` |
| 文件 | `Files.Request` 打开/保存；JSON 版本 1；格式/版本/条数/解析失败时整次不改列表；导入清空勾选 |
| 布局 | 行根 `Border + DockPanel`（AR-21 不允许控件代码 `new Grid`）；选择列 24、主列填充、侧列 66 |
| 状态 | 操作结果优先于闸门状态（导入/另存为/发送后不被“未勾选/就绪”覆盖）；勾选合法时回“就绪” |
| 测试 | `CommandSenderTests` 11 条（编码/拆包/多字节/HEX/空/顺序/转换/连续/导入导出/闸门/换行/删除）+ 契约用例 + 包加载用例 |
| 截图 | `CommandSender.png`、`CommandSender-preview.png`、`CommandSender-filled.png`（真实两行、勾选序号 2/1、HEX 行） |
| 门禁 | `tools/dev.ps1 -Mode Gate` 全绿：文档 · 颜色 · 资源文本 · 注释布局 · 版本 · 构建 · 模块包 · 作者包 · 许可 · 24 架构 / 217 模块 / 529 主 / 16 判据 |

已知边界：`ModuleValueList` 只负责“把值名排成行 + 勾选顺序 + 行内动作”；它不删值、不写配置、不做业务校验。真无上限的槽位需要不同承载方式（单 JSON 值 + 自绘编辑器或新数据通道），不在本轮约束内。

