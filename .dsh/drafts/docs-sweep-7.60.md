# 模块侧文档扫除 · 对齐 `decisions.md` 7.60（交付报告）

任务来源：父代理消息 `m00460`。范围 = `docs/implementation/06` + `docs/modules/*.md`（含 `_模板.md`）；未碰 `docs/decisions.md` / `docs/roadmap.md` / `docs/architecture/**` / `src/` / `tests/`。

**校准基准**：`docs/architecture/02-module-sdk.md` 的「视图：模块只声明，框架渲染」小节（现行正文）· 活 schema `src/Hosting/Packages/schema/view.schema.json` · **四个模块的真实声明文件**：`src/Modules/Business/TextSender/view.json`（340×340 · `edge: "bottom"`）· `src/Modules/Business/HexSender/view.json`（340×340 · `edge: "bottom"`）· `src/Modules/Business/DataMonitor/view.json`（460×500 · `edge: "top"` · grid）· `src/Modules/Business/HexStringConverter/view.json`（520×600 · 无 port）。

## 1. 改动清单（文件 · 行区间 · 一句话）

### `docs/implementation/06-module-standard-and-development-process.md`（630 → 637 行）
- `:202` 包目录树 —— `view.json` 那行改为「界面声明（**可选**：控件树 + 语义 + 框架级那几行，见 §C.1′）」。
- `:215–240` **§C.1′ 整段重写**（原 21 行 ⇒ 26 行）：一个文件两个顶层段（`frame` / `sources` + `views`）、结构不再写在 C# 里、可选与构建规则、三种排布模式 + 四种容器、权威正文指针、**新键表 14 行**（`version` / `frame.window.width`·`height`·`minWidth`·`minHeight`·`maxWidth`·`maxHeight` / `ports[].name`·`edge`·`offset` / `tabs` 页签标题 / `views[].title`·`root` / `overflow` / `padding`·`spacing` / `feedbackSource` / `showInlineResult`）、三条 bullet（顺序 = 视觉顺序且**唯一来源**；每元素尺寸与 `grid` 位置指向 `architecture/02`；坏了会怎样 = 硬伤/警告全清单 + 打包门禁）。
- `:247` C.2 固定四段表 ② 行 —— 删 `DeclareView`，改「**视图不在代码里声明** —— 见包内 `view.json` / §C.1′」。
- `:251–261` **C.2′ 重写**：标题改「声明按控件分块（已裁 · 用户；落点自 `decisions.md` 7.60 起 = 包内 `view.json`）」；用户原话**逐字保留**；新增一行「（**仅落点变更**：7.60 起控件声明只有 `view.json` 一处；下表规则逐条不变。）」；表 5 行 ⇒ 4 行（删已失效的「汇总处只做汇总」，其余四条改写为声明口径）。
- `:469` §G.3「框架能力」行 —— 追加「（该批如此；`Sdk` 的视图声明面后来按 7.60 整体换掉 —— `ViewElement`/`ViewDeclaration`/`ModuleViewLayout` 已删，`ViewElementKind` 目录保留）」。
- `:487` §H.1 容器行 —— 补 `flow`/`grid`/`absolute` 与 `overflow`；标「7.60 起 `Scroll` 不再是可声明的 kind，滚动由 `overflow: "scroll"` 承担、尺寸按每轴一个对象声明」。
- `:489` §H.1 表后 —— 新增 blockquote「**未渲染清单的唯一登记处** = schema 里 `rendered: false` 的 6 项……声明合法，只给一条警告（C-2）」。

### `docs/modules/_模板.md`
- `:61` 默认尺寸 —— `<宽×高>` 改为「写在包内 `view.json` 的 `frame.window`（省略 = schema 缺省 320×220）」。

### `docs/modules/hyw-text-sender.md`
- `:82` 元素行 —— 改为声明口径（`kind` + 角色键），补 `textAlign: "right"`（与实文件一致）。
- `:83` 数据源行 —— `interval` 由 `kind = Value` 更正为 `kind = Text`（+「是不是数字」由 `inputKind: "number"` 表达），消掉与 `:296` 契约自检记录的自相矛盾。
- `:87` 默认尺寸 —— 声明 `frame.window` = 340×340；算式与「按实际控件排布去做」原话保留。
- `:170` T3-载体行 —— 追加「**载体已变**：7.60 起该字段是 `view.json` 的 `inputKind` 键（`ViewElement` 已删），三条指示不变」。
- `:199` §9.4 表后 —— 新增「**载体已变**」blockquote（三方案讨论作历史保留，结论不变）。
- `:297` E2 —— 落点改为包内 `view.json` 的 `frame.ports` 里声明 `edge: "bottom"`。

### `docs/modules/hyw-hex-sender.md`
- `:22` 摘要⑥ `InputKind = Hex` ⇒ `inputKind: "hex"`。
- `:60` ⇒ 在包内 `view.json` 的 `frame.ports` 显式声明 `edge: "bottom"`（只定默认落点，用户可拖动覆盖）。
- `:72` 元素行 ⇒ 声明口径（`kind` + 角色键）+ 补 `textAlign: "right"`。
- `:76` 默认尺寸 ⇒ `frame.window` = 340×340。
- `:94` 能力表 `InputKind = Hex` ⇒ `inputKind: "hex"`。
- `:119` 契约自检 ⇒ 「**口默认边 = 下边**（`frame.ports` 的 `edge`）· 尺寸与 `inputKind` / 数据源 `unit` 的声明面逐项」。

### `docs/modules/hyw-hex-string-converter.md`
- `:62–75` §3 结构 —— 旧的「tabs 内每页带按钮」示意树换成**实文件的声明树**（根 `container` → `tabs`「字符」`stateKey: "char-pages"`（5 页 `group` 包多行 `textInput`）→ `commandButton`「转成 HEX」`to-hex` → `tabs`「十六进制」`stateKey: "hex-pages"`（5 页，`inputKind: "hex"`）→ `commandButton`「转成字符」`to-text` → `textLog`×2）；新增两条 blockquote（角色键与页签标题取 `group` 的 `title`；**方向按钮落位待裁决**，见 §5 问题 2）。
- `:83` 默认尺寸 —— 520×485 ⇒ **520×600**（与实文件、`architecture/07:65`、`architecture/06:112` 一致）；原「432 + 46 ⇒ 485」作为**内容下限推算**保留。

### `docs/modules/hyw-data-monitor.md`
- `:56` ⇒ `frame.ports` 里声明 `edge: "top"`。
- `:66` 元素行 ⇒ 声明顺序 = `children` 数组序、7 个节点 kind + 角色键；**路径键 `v/0…v/6` 全部删除**；`filter` 按实文件补全为 `文本文件 (*.txt)|*.txt`（表内转义为 `\|`）。
- `:70` 默认尺寸 ⇒ `frame.window` = 460×500（算式与实测保留）。
- `:88–94` **§3.2 重写** —— 标题「声明（包内 `view.json` · `decisions.md` 7.60）」；载体 / 内容 / 视觉结果 / 保存结果位置四段改为声明口径（`grid` 轨道与间距、每轴尺寸对象、`feedbackSource: "savePath"`、`showInlineResult: false`）；守护两条改为「声明面逐项（顺序 = `children` 数组序，唯一来源）+ 像素」。
- `:138` 模块测试 ⑧ ⇒ 声明顺序断言（去掉 `ModuleContract.PathKeys` 字面清单）。
- `:165` M8 ⇒ `view.json` 的 `suggestedFileName`。

## 2. 保留的事实清单（逐项确认未弱化）

- **四模块默认尺寸数值**：文本发送器 340×340 · HEX 发送器 340×340 · 数据显示器 460×500 · HEX 字符串转换器 520×600。
- **尺寸算式**：text-sender / hex-sender 内容最小高 211 + 预留带 46；data-monitor 面板 418×452、显示区 418×380、按钮行 Y=386 / 422、控件 128×30、列距 8；converter 内容下限 432 + 46 ⇒ 485、宽 520 的缘由。
- **全部像素断言**：data-monitor §7 ⑨（128×30 · 列距 142 · 行距 36 · 首列与显示区左对齐 · 显示区跨满）与 §3.2 视觉结果行。
- **算法 / 协议事实**：HEX 解析（空白忽略、大小写都收）· 奇位报错 · 超 1472 字节拆包 · 非法 UTF-8 标注 · 「所见即所存」· 「暂停 = 收下即弃」· converter 的五条写入通道与 D4 裁定 · 「每组 5 页」。
- **契约自检项**：`Problems`/`Advisories` 空 · 声明面逐项（类型/口/角色/视图/命令）· 口默认边 · `inputKind` / `unit` / 多行 / 对齐 / 尺寸声明。
- **用户原话与人工验收点**：T3/T4/T5/E1/E2/M8/M9/M12/D2/D3/D4 等的原话全部保留。
- **历史脉络**：7.57 / 7.58 的记录一律保留，只加「已被 7.60 取代」的一句话标记（`implementation/06` §G.3 · §H.1；text-sender `:170` / `:199`；converter 声明树说明）—— 未整段重写历史。

## 3. 发现但未改的失步点（`docs/architecture/**` 归上位者）

- `docs/architecture/07-overlay-and-visual-baseline.md:65` 默认尺寸行 —— 仍写「未声明 `PreferredWidth/PreferredHeight` 回退 320×220」，载体口径失步（数值本身与实文件一致）。
- `docs/architecture/07-overlay-and-visual-baseline.md:143`（`PortDeclaration.Edge` 声明默认边）· `:145`（`AnchorDraggable` 三态）· `:428`（`ViewElement.Alignment`）· `:611`（`Width = PreferredWidth * _scale`）。
- `docs/architecture/06-ui-shell-and-windows.md:112`（`ViewDeclaration.PreferredWidth/PreferredHeight`）· `:167`（`ViewDeclaration.Commands`）· `:168`（`ViewElement.Multiline`）· `:276`（窗口默认值说明）。
- `docs/architecture/05-network-semantics.md:44`（`PortDeclaration.Edge`）· `docs/architecture/03-data-protocol-and-typing.md:167`（`ViewDeclaration` 的 `Unit`）· `docs/architecture/02-module-sdk.md:360`（`ViewElement.DataSource`）· `docs/architecture/08-application-settings.md:78`（`ViewElement.Multiline`）。
- 同册自相矛盾：`docs/architecture/02-module-sdk.md:341` 已写「旧 `ViewDeclaration.PreferredWidth/Height` 已随载体切换删除」，而 `:360` 与 `07:65`、`06:112` 仍是旧写法。
- **按纪律未动的历史记录**：`docs/decisions.md` 7.13 / 7.14 / 7.15 / 7.19 / 7.24 / 7.56 / 7.57 / 7.58 · `docs/roadmap.md` 第 12/18/22/23 项 · `docs/modules/hyw-text-sender.md:288`（§9.2 的 E2 历史裁定仍写 `Bottom`/`Top`，未加载体说明；`:297` 已给现行落点）· `docs/modules/hyw-text-sender.md:193`（§9.4 表内旧类型名，已在表后加「载体已变」说明）· `docs/archive/**`（归档）。

## 4. 审计读数

`& .\docs\tools\audit-docs.ps1` ⇒ **`[OK] 机械审计通过`**。

- ① 失效术语黑名单 5 类（快速建网/R0–R6 · 不建立数据流 · 形状子集 · 标题栏 24 · 修订版 N）仅出现在废止/历史说明中；
- ② 引用全部可达；③ 遮罩不透明度唯一 0.60（接触判定 1/3/8 为 1 条 `i` 提示，非冲突）；
- ④ 结构完整性全 `v`（architecture 11 分册 · implementation 00–06 · `docs/modules` · `.dsh/skills`）；
- ⑤ skills 去事实 6 份通过；⑥ 表格格数全部不超表头（2 处短行，允许）。

**最后一次改动（四模块行内补正 + `filter` 转义 + 两处粗体收敛）之后已重跑，读数不变。**

## 5. 需裁决 / 复核的问题

1. **converter 默认尺寸**（模块文档内已收敛）：`docs/modules/hyw-hex-string-converter.md:83` 原写 520×485，实文件 `src/Modules/Business/HexStringConverter/view.json:4` = **520×600**，`architecture/07:65` 与 `architecture/06:112` 也是 600 ⇒ 已把模块文档改为 600，并把「432 + 46 ⇒ 485」降级为内容下限推算。**请复核措辞**：若 485 另有刻意原意，请裁。
2. **⚠ 转换器「方向按钮」落位冲突（必须裁决）**：实文件把两个方向按钮放在**各自标签组之后（每组一个，共 2 个）**；`docs/modules/hyw-hex-string-converter.md:82`（§3「方向按钮的落位」）记录的**用户裁定是「每页内各一个」**（每页一个、共 5 个/方向）。两者视觉与交互结果不同。我按「以实文件为准」写了声明树，并在 `:73–75` 加 ⚠ blockquote 标注冲突；**文档其余部分未擅改裁定**（§9 C1 的讨论原样保留）。**请裁：改声明文件（回到每页一个）还是改文档（确认每组一个）？**
3. **`interval` 数据源类别**：`docs/modules/hyw-text-sender.md:83` 原写 `kind = Value`，与同文件 `:296`（契约自检抓出的原声明错 —— 应声明为 `Text`）矛盾 ⇒ 已按实文件更正为 `Text` + `unit: "ms"`。请确认符合 D2 / 7.26 原意。
4. **`architecture/**` 的旧类型名残留**（§3 清单）需按 7.60 统一收敛；其中 `07:65` 与 `06:112` 的「声明值」行如今是**声明文件**的事实，建议改为引用 `view.json` 而不复述数值。
