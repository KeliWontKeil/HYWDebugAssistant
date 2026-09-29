# UI 调整指南（改哪儿 → 看哪段 → 怎么验证）

> 目标：让你**不读代码**也能定位"某一处观感想改"应该动哪个文件；并且**在 VS 设计器里直接改**。
> 配套：**`LESSONS.md`**（开发经验总结——改 UI 前建议先扫一遍"UI 开发方式"与"流程纪律"两节）。
> 任何改动后：`.\tools\dev.ps1 -Mode Run` 重启应用看效果；改了 `src/Ui`/`src/FrameworkUi` 的 XAML 后**只需重编对应项目**（见 §8）。

## 0. UI 开发方式（**约定 · 用户第九轮反馈后确立**）

| 原则 | 具体含义 |
|---|---|
| **① 尺寸默认值写在 XAML** | 窗口/控件的默认尺寸、最小尺寸、内边距、间距**全部写在 axaml 里**（如 `ModuleWindow.axaml` 的 `Width/Height/MinWidth/MinHeight`、`DialogWindow.axaml` 的 `Width/Height`）。代码里**没有**"按内容估算尺寸"的逻辑。 |
| **② 外层容器尺寸固定** | 外层窗口尺寸**不随信息量变化**。内容排不下 → **子容器的 ScrollViewer 出滚动条**（不是把窗口撑大）。 |
| **③ 按钮排版固定** | 按钮行/工具栏的位置与排列是静态布局，不因内容增删而移动。 |
| **④ 设计时可预览** | 每个视图都有 `d:DataContext`（设计时样例数据）→ **VS 设计器里能看到有内容的界面**并直接调样式。 |
| **⑤ 代码只管"事实"，不管"外观"** | 代码只做三件事：把**用户拖过的**几何从组合件恢复、把**设置里选的**比例/主题应用上、把**领域标准件**（端口标记/连线/参考线）画出来。其余一切外观都在 XAML。 |
| **⑥ 帮助一律弹窗、区域一律有边框**（第十轮） | 帮助 = 小号**带边框**问号按钮（`Button.helpIcon`，16×16）+ `Flyout` 文本弹窗——**不展开、不改变布局**，各分区**右侧/右下角且 X 对齐**；列表/分区用 `Border.pane` / `ListBox` 的 1 px 边框表达边界。 |

**由此得到的两条硬规则（改代码时也要守）**：
1. 想改"某个屏幕上的尺寸" → 改 **XAML 里的字面值**，不要在 C# 里算；
2. 数据多到放不下 → 加 `ScrollViewer`，不要把外层改大。

> 例外（**必须**留在代码里的三处，已在文档登记）：① 用户拖动/缩放过的几何（随组合件保存）；② 外壳比例设置（你要求做成设置项）；③ 端口标记/叠加连线/参考线的自绘（贴在预留带内的领域标准件）。

---

## 1. 一条总原则：**颜色与尺寸都走 token**

| 想改什么 | 改这个文件 | 关键键名 |
|---|---|---|
| **全部配色**（浅/深主题、语义色、背景层次） | `src/Ui/Styles/Tokens.axaml` → `ThemeDictionaries`（Light / Dark 各一套） | `BgWorkspace`（工作区/缩略图底）· `BgModule`（模块底）· `BgChrome`（外壳/工具区）· `BorderDefault`/`BorderStrong` · `TextPrimary`/`TextSecondary`/`TextMuted` · `AccentPrimary`/`AccentSecondary` · `SemanticOk`/`Warn`/`Error`/`Idle` |
| **字体** | 同上 | `FontUi`（界面对话字体）· `FontData`（数据/日志等宽字体） |
| **控件高度（输入框/按钮）** | 同上 | `ControlHeight`（当前 **27**）· `ControlFontSize`（12） |
| **模块窗口尺寸相关** | 同上 | `ModuleHandleSize`（缩放手柄 **6**）· `ModuleBandThickness`（拖动带 **6**）· `ModuleCornerRadius`（圆角，类型必须是 `CornerRadius`！） |
| **端口标记规格**（尺寸的**唯一来源**） | `src/Ui/Geometry/PortMarkerMetrics.cs`（**2026-09 起**；`Tokens.axaml` 的 `PortMarkerWidth`/`PortMarkerLength` 是**死资源** —— 真正生效的一直是视图模型那份 —— 已删除） | `Width`(**20**，= 预留带厚度) · `Length`(**150**，沿边长度；由 114 加长以容下「（直连）」+ 速率) · `FixedChrome`(49，固定槽位合计) · `MinWindowExtent`(160，窗口最小边长由它推出来)。**标记内元素尺寸仍看 `Tokens.axaml`**：`PortMarkerFontSize`(11) · `PortMarkerArrowFontSize`(18) · `PortMarkerLampSize`(8) · `PortMarkerStripWidth`(6) · `PortMarkerRateFontSize`(10) |
| **图标（字形）** | `src/Ui/Styles/Tokens.axaml` 的 `FontIcons` + `Glyph*` | 图标用**系统图标字体**（Segoe Fluent Icons / Segoe MDL2 Assets），码位见该处注释表；换图标 = 改 `Glyph*` 一行 |
| **全局控件样式**（输入框内边距、图标按钮大小、活动栏图标字号） | `src/Ui/Styles/Controls.axaml` | `TextBox`/`ComboBox`/`Button`/`ToggleButton`/`CheckBox` 样式；`TextBlock.icon`；`Button.icon`；`Button.rail` |

> **改颜色/尺寸只改这两处**；如果某个界面写死了数值，那是我该修的地方——指给我即可。

---

## 2. 文件 → 界面部位对照

| 界面部位 | 文件 | 备注 |
|---|---|---|
| **外壳主窗口**（活动栏 A + 侧边界面 B + 状态栏） | `src/FrameworkUi/Views/MainWindow.axaml`（+ `.axaml.cs`） | 活动栏按钮样式在该文件的 `Window.Styles`（`Button.rail`、`Rectangle.railMark`） |
| 外壳视图模型（当前页、比例、开关、状态栏文本） | `src/FrameworkUi/ViewModels/ShellViewModel.cs` | 状态栏文案在 `UpdateStatus()` |
| **交互页**（显示/交互/窗口三组开关） | `src/FrameworkUi/Views/InteractionView.axaml` | 就 20 行，随便改 |
| **模块清单页**（缩略图 + 加入现场 + 加载 + 来源诊断） | `src/FrameworkUi/Views/ModuleCatalogView.axaml` | 单列自上而下；缩略图占位在列表项模板里 |
| **设置弹窗**（七类） | `src/FrameworkUi/Views/SettingsView.axaml` | 左分类样式在文件顶部 `UserControl.Styles`（字号 12 / 行高 28 / 宽 132） |
| **拓扑诊断 / Issue / 帮助** 弹窗 | `src/FrameworkUi/Views/{TopologyView,IssuesView}.axaml` + `DialogWindow.axaml` | 弹窗尺寸自适应在 `DialogWindow.axaml`（`SizeToContent` + `Max*`） |
| **模块窗口**（边框、按钮簇、运行指示、端口标记、叠加层） | `src/Ui/Views/ModuleWindow.axaml`（+ `.axaml.cs`） | 窗口级样式在该文件 `Window.Styles`（`handle`、`Cluster`、`Frame.selected`） |
| **模块窗口内容**（数值/曲线/日志/命令/输入/开关的模板） | `src/Ui/Views/ModuleContentView.axaml` | 每种元素一个 `DataTemplate`；**元素之间的间距 = 该文件里 `ModuleElementPanel` 的 `Spacing`**；**内容四周留白 = 外层 `ScrollViewer` 的 `Padding="20,20,20,20"`**（这两处是"布局不紧凑"直接可调的旋钮） |
| **模块内容的"高度分配"**（谁随窗口拉伸、谁按内容高） | `src/Ui/Controls/ModuleElementPanel.cs` | **填满型** = A5 文本日志 + 多行输入（分走剩余高度、内部滚动）；**按内容型** = 其余元素。填满型的**最小高度**：日志 `LogMinHeight = 88`、多行输入取 `TextInputElementViewModel.EditorMinHeight`（60） |
| 配置表单（字段行、控件类型） | `src/Ui/Views/ConfigurationFormView.axaml` | **框架不再自动补**这张表单（2026-09 用户裁定"删除整个配置框"）：只有模块**显式声明** B1 才渲染 |
| 文本日志（显示区） | `src/Ui/Controls/TextSurface.axaml` | 只读虚拟化行列表；**行距/字号**在该文件顶部的 `ListBoxItem` / `SelectableTextBlock` 样式里 |
| **文本框字体**（输入框 + 显示框） | **设置 → 外观与显示 → 「界面字体」/「数据字体」**（设置模型 `ApplicationSettings.UiFontFamily` / `DataFontFamily`；资源键 `FontUi` / `FontData`，落在 `src/Ui/Styles/Tokens.axaml`） | 默认：界面 `Segoe UI, Microsoft YaHei UI`；**数据 `Times New Roman, 宋体`**。下拉读**系统已有字体**、可手输回退列表（逗号分隔）；改动即时生效 |
| 输入框的**光标/文字垂直位置** | `src/Ui/Views/ModuleContentView.axaml` 顶部的 `TextBox` 样式 | **单行垂直居中、多行顶端左对齐**（`Classes.multiline` 区分） |
| 曲线绘制 | `src/Ui/Controls/CurveControl.cs` | 纯绘制代码 |
| **端口标记排布**（贴边、边感知、预留带） | `src/Ui/Controls/PortMarkerPanel.cs` | `BandThickness=20`、`EdgeInset=-1`（标记压到窗口外框上） |
| **对齐参考线 / 缩放高亮** | `src/Ui/Controls/GuideOverlay.cs` | 坐标换算：屏幕物理像素 ÷ `RenderScaling` |
| **叠加信息层**（连接线/流向/对端标签） | `src/Ui/Controls/ConnectionOverlay.cs` | 连线色/箭头色在 `ModuleWindow.axaml` 里绑 `AccentSecondary`/`AccentPrimary` |
| 模块**视图声明**（发送器这类模块长什么样） | `src/Modules/Business/TextSender/TextSenderModule.cs` 的 `OnRegistered` → `DeclareView(...)` | **不是 XAML**：模块只声明元素，框架按模板渲染；**模块默认尺寸**也在同一个调用里（`{ PreferredWidth = 340, PreferredHeight = 230 }`，见 `src/Sdk/Views/ViewDeclaration.cs`） |
| **「还原」范围确认弹窗** | `src/Ui/Views/ResetModuleDialog.axaml`（+ `.axaml.cs`）· `src/Ui/ViewModels/ModuleResetScope.cs` | 三选一（只还原尺寸 / 尺寸和配置都还原 / 取消）；按钮簇在 `ModuleWindow.axaml` 的 `ResetButton` |
| 设置页与主界面的**同名开关** | `src/FrameworkUi/ViewModels/SettingsPanelViewModel.cs`（`Owner` 委托外壳） | 改开关行为只改 `ShellViewModel` 那一处，两个界面自动一致 |
| 活动栏/窗口图标字体可用性 | `src/Ui/Styles/Tokens.axaml` 的注释表 | 若某个字形显示成方块，把对应 `Glyph*` 改成注释表里的其他码位 |

---

## 2.1 你想自己调"布局紧凑度/位置"时的三个旋钮（2026-09 用户要求："告诉我去哪调"）

| 想改什么 | 改哪里 | 现值 |
|---|---|---|
| 模块内**元素之间的间距** | `src/Ui/Views/ModuleContentView.axaml` → `<ctrl:ModuleElementPanel Spacing="2" />` | **2** |
| 模块内容**四周留白 / 上·下预留带** | 同文件 → 外层 `<ScrollViewer Margin="20,22,20,24">` | **左右 20 · 上 22 · 下 24** |
| 显示区（日志）**行距 / 字号** | `src/Ui/Controls/TextSurface.axaml` → `TextBox#LogEditor` 的 `LineHeight` / `FontSize`（**必须与 `TextSurface.axaml.cs` 的 `LineHeightDip` 一致**：窗口行数按它算） | 行高 **14** / 字号 **11** |
| 显示区**分页窗口**的规模与节拍 | `src/Ui/Controls/TextSurface.cs`（**输入框与显示区共用的那一个文本面控件**）→ `MinWindowLines`（20）· `MaxWindowLines`（200）· 刷新节拍（50 ms = 20 Hz）· 滚轮行数（3） | 窗口 = 视口行数 × 2，夹在 **[20, 200]**；**调大窗口 = 每次重排更贵**（实测 ≈ 0.47 ms/行） |
| 现实：**不再有"一行多少字节"这回事** | 显示模块只做"字节 → 文本"（`MonitorLineFormatter`）；**没有 96 字节定宽行、没有填充、没有空闲断行** | 一行多少由**文本框宽度**决定（用户 2026-09 裁定："请不要抛开成熟方案去自创方案"） |
| 显示区**最小高度** | `src/Ui/Controls/ModuleElementPanel.cs` → `LogMinHeight` | **88** |
| 多行输入框**最小高度** | `src/Ui/ViewModels/ViewElementViewModels.cs` → `TextInputElementViewModel.MultilineEditorMinHeight` | **60** |
| **输入结果行**（失败提示那一行）的**常驻高度** | `src/Ui/Views/ModuleContentView.axaml` 的 `TextInput` / `FileSelector` 模板 → 结果 `TextBlock` 的 `MinHeight` | **14**（常驻占位 ⇒ 有无信息都不改布局） |
| **开关滑块**（B7）的行高与排布 | 同文件 → `ToggleSwitch`（**不要写 `Content`**，文字用旁边独立的 `TextBlock`） | 行高 **32**（模板自带）；**写 `Content` 会让它涨到 ~48 并把轨道挤出可视区** ⇒ 真机上只剩一行文字（2026-09 实测缺陷） |
| 文本框**标题**的位置 | 同文件 → `TextInput` / `FileSelector` 模板的第 0 行 | **标题在左上、与框左对齐**（用户 2026-09："需要统一文本框标题于左上角，同样与文本框左对齐"） |
| **文本框字体** | 设置 → 外观与显示（或 `ApplicationSettings` 的 `UiFontFamily` / `DataFontFamily`） | 数据 = `Times New Roman, 宋体` |
| 输入框**行末换行标记 `↵`** 的观感 | `src/Ui/Controls/NewlineMarkOverlay.cs` → `MarkGap`（与文本末尾的间距）、`MarkFontScale`（相对正文的字号比例，现值 **0.85**） | 标记是**绘制层装饰**（不进文本、不参与选择、光标跳过）；**不要**改回"把标记塞进文本"（2026-09 实测：光标规律不可预测，已被用户否定） |
| **标签页数量** | 该模块源码的 `PageCount`（`TextSender` / `HexSender` / `HexStringConverter`） | **5**（2026-09 用户裁定；增删页**不影响窗口尺寸**，只显示当前页） |
| 某个模块的**默认尺寸** | 该模块源码 `OnRegistered` 里的 `PreferredWidth/PreferredHeight`（模块页 §3 有算式）；调完同步模块页 | 文本/HEX 发送器 **340×340** · 数据显示器 **460×500** · 转换器 **520×600** |

> 原则（2026-09 用户裁定）：**能靠 `.axaml` / 声明调好的位置与间距问题，由开发者自己调**；框架侧只保证"不遮蔽、不覆盖、随窗口伸缩、内容增长只出内部滚动条"。
>
> ⚠ **改设置页的生效机制**：设置弹窗走 **确认 / 取消 / 应用** 三键（`SettingsView.axaml` 底部三键条 + `SettingsPanelViewModel` 的 `ConfirmCommand`/`CancelCommand`/`ApplyCommand`）——**不要再加"改一下就即时生效"的字段回调**（2026-09 用户裁定撤销了旧口径）。
> 
> ⚠ **"填满型"（多行输入 / A5 日志）的高度由 `ScrollViewer` 拉伸给**：内容比视口矮时，`ScrollViewer` 会把 `ItemsControl`（`VerticalAlignment="Stretch"`）拉到视口高 ⇒ `ModuleElementPanel` 才有多余空间分给填满型。**不要改回 `MinHeight="{Binding Viewport.Height}"`**（首帧视口=0 ⇒ 框只有一个最小高，放大窗口才对；2026-09 实测缺陷）。填满型**最小高**见 `ModuleElementPanel.LogMinHeight` / `TextInputElementViewModel.MultilineEditorMinHeight`。
> 
> ⚠ **上/下预留带必须写成 `ScrollViewer` 的 `Margin`（不能写成它的 `Padding`）**：`Padding` 算在**视口之内**，而内容面板的 `MinHeight` 绑的是**视口高** ⇒ 内容恒比视口高一点点 ⇒ **默认尺寸下常驻一条滚动条**（2026-09 第二轮反馈修掉的真实缺陷）。

---

## 3. 你提过的问题分别改在哪（第七 / 第八 / 第十 / 第十一 / 第十二 / 第十三 / 第十四 / 第十五 / 第十六 / 第十七轮；**第九轮是开发方式，见 §0**）

### 第七轮（像素偏移 / 图标 / 字号 / 排布）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| ① 对齐线/标记/右侧吸附差几像素 | `src/Ui/Controls/GuideOverlay.cs`（**单位**）· `src/Ui/Views/ModuleWindowManager.cs` 的 `PublishShellRect`（**外壳客户区**）· `src/Ui/Controls/PortMarkerPanel.cs` 的 `EdgeInset`（**贴边**） | 参考线：屏幕物理像素 ÷ `RenderScaling` 再以**客户区原点**为基准（且 `Frame` 设 `ClipToBounds`，线不再画到窗外）；外壳吸附基准：`PointToScreen(0,0)` + `ClientSize`（此前用窗口外框位置配客户区尺寸 → 差一个边框宽）；标记 `EdgeInset=-1` 压到外框上（相邻模块标记不再夹 2 px 缝） |
| ② 活动栏选中标记与图标重合 + 图标难看 | `src/FrameworkUi/Views/MainWindow.axaml`（`Grid ColumnDefinitions="3,*"`）· `src/Ui/Styles/Tokens.axaml`（`Glyph*` + `FontIcons`） | 滑块独占最左 3 px 列、图标在剩余列居中（不再同格叠加）；图标改为**系统图标字体**（不再手绘几何） |
| ③ 设置左分类字号太大 | `src/FrameworkUi/Views/SettingsView.axaml` 顶部 `UserControl.Styles` | `TabItem` 字号 12、行高 28、宽 132 |
| ④ 占位实例不该在模块清单；打开目录挤占空间 | `src/FrameworkUi/Views/ModuleCatalogView.axaml`（改单列） · `src/FrameworkUi/Views/TopologyView.axaml` + `TopologyViewModel.Catalog`（占位实例移到诊断） | 清单页自上而下：可用模块 → 加入现场 → 从目录加载 → 来源诊断（「重扫来源/打开目录」在列表**下方**按钮行）；占位实例与「指定替代」移入**拓扑诊断**弹窗 |
| ⑤ 下侧信息显示未对齐 | `src/FrameworkUi/Views/MainWindow.axaml` 状态栏 | 状态栏固定高 28、左右两端都 `VerticalAlignment=Center`、统一 `Padding` |

### 第八轮（共性：信息过载 / 排布松散）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| ① 按钮文本未居中 | `src/Ui/Styles/Controls.axaml` | 全局 `Button`/`ToggleButton` 显式 `HorizontalContentAlignment=Center` |
| ② 教程式描述过多 | `MainWindow.axaml` / `InteractionView.axaml` / `ModuleCatalogView.axaml` / `SettingsView.axaml` | 说明段落全部改 `ToolTip.Tip`（悬停才出现） |
| ③ 长说明挤占空间 | 同上 + `Controls.axaml` 的 `ToggleButton.help` | 问号按钮折叠：`IsVisible="{Binding #HelpName.IsChecked}"` |
| ④ 长文本不换行 | `src/FrameworkUi/Views/DialogWindow.axaml` | 弹窗改 **宽度固定 780 + `SizeToContent=Height`**（此前宽度自适应 → 文本得到无限宽度故不换行） |
| ⑤ 主页面模块来源冗余 | `src/FrameworkUi/Views/ModuleCatalogView.axaml` | 来源诊断与手动加载**默认收起**（问号按钮展开）；首屏只留 分类栏→搜索→列表→加入现场 |
| ⑥ 需要分类菜单与搜索 | `ModuleCatalogViewModel`（`ModuleCategories`/`Categories`/`SelectedCategory`/`SearchText`/`ApplyFilter`） · `Core.Api` 的 `ModuleCatalogEntryView.Tags` · `ModuleCatalogView.axaml` 顶部 | 图标分类按钮（悬停显示名称+计数）+ 搜索框；分类由模块声明的 `Tags` 推导 |
| ⑦ 布局松散 | 上述各页 + `ConfigurationFormView.axaml` | 内边距 12→10、行距 8→6、标签列统一 140、配置字段列 86→76、缩略图 96×54→72×40 |

### 第十轮（**帮助改弹窗 · 加边框 · 固定区域尺寸 · 主页面按 mockup 重排**）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 帮助不是弹窗 | `src/Ui/Styles/Controls.axaml` 的 `Button.helpIcon` · 各视图的 `<Button.Flyout>` | 帮助 = **16×16 带边框问号按钮 + Flyout 文本弹窗**：点击弹出、点外部关闭，**不在页面里展开、不改变布局**；帮助文本**只有文字** |
| 控件没有边框、区域边界不明显 | `Controls.axaml` 的 `ListBox` 全局边框 / `Border.pane` / `TextBlock.paneTitle` | 列表与分区都有 1 px 边框 + 圆角；分区标题小号半粗 |
| 帮助按钮位置/大小 | 各视图 | 分区**右侧/右下角**，右边距统一 **4 px**（X 对齐），16×16 不占视觉空间 |
| 分类切换影响排版 | `ModuleCatalogView.axaml`（分类标签 `Width="64"` 固定；模块列表放在 `*` 行 + `Border.pane` 固定区域，内部滚动） | 切换分类/搜索**不移动任何其它控件**；列表不随模块数量变高矮 |
| 来源与加载被藏进帮助 | `ModuleCatalogView.axaml`（④ 目录操作行 · ⑤ 来源根列表 · ⑥ 加载模块包）+ `ModuleCatalogViewModel.AddSourceDirectoryCommand`/`RemoveSourceDirectoryCommand`/`OpenDirectoryCommand` + `ModuleSourceRowViewModel.Display` | 全部**显式放在页面上**：来源列表一行一个、**路径中间省略**（`…\model\modelA`）、**行尾小按钮在资源管理器中打开**；帮助里只剩文字说明 |

### 第十一轮（**开关同源 · 取色器 · 锁定与随壳 · 还原 · 模块默认尺寸**）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 同一个开关出现两处且互不联动 | `src/FrameworkUi/ViewModels/SettingsPanelViewModel.cs`（**新增 `Owner` = `ShellViewModel`**） | 设置页的「显示叠加信息层 / 应用置顶」**不再各自持有字段**，直接**委托外壳的实时属性**；切换即 `PersistLiveSwitches()` 落盘 → **两处永远一致**（改这里请只改外壳那一处属性） |
| 取色太原始（只有一排预设） | `src/FrameworkUi/Views/SettingsView.axaml`（色片 `Button` + `Flyout` 面板） | 点色片弹出面板：**预设色块（`WrapPanel`）+ 十六进制输入框**（填 `#RRGGBB` 回车生效）；主题色与背景色都是这一套 |
| 锁定后仍会跟着壳体移动 | `src/Ui/ViewModels/ModuleWindowsViewModel.cs` 的 `BeginDrag` / **`FollowShellMove`** | 锁定 = ①不能拖 ②**不进拖动组、不被随壳移动带走**；`FollowShellMove` 只在模块**贴住外壳右边缘**（`\|Left − shell.Right\| ≤ 2` 且竖向有重叠）时整组平移，拖开后自然留在原地 |
| 「停」语义不对，要的是「还原」 | `src/Ui/Views/ModuleWindow.axaml(.cs)` 的 `ResetButton`/`OnReset` + **`ResetModuleDialog`** + `src/Core/Changes/ChangeOperation.cs` 的 `ResetModuleConfigurationOperation` | 按钮簇 = **[还原][锁][隐][×]**；点还原弹**范围确认**（只还原默认尺寸 / 尺寸和配置都还原 / 取消）；配置还原 = **换成同类型新默认实例**（不是清空，否则表单会空） |
| 模块尺寸总是一样 | `src/Sdk/Views/ViewDeclaration.cs` 的 **`PreferredWidth`/`PreferredHeight`**（`init` 属性）+ 各正式模块的 `DeclareView(...) { PreferredWidth = …, PreferredHeight = … }` | 想给模块定默认尺寸：**在模块代码里加两个 `init` 属性**（不要改构造器签名——那是二进制破坏，P30）；未声明的模块仍回退 320×220 |

### 第十二轮（**吸附手感 · 像素对齐 · 外壳成组**；`docs/architecture/07-overlay-and-visual-baseline` + `docs/decisions.md`）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 靠近对齐线时**吸附振荡** | `src/Ui/Geometry/SnapEngine.cs` | 候选统一为**参照线模型**（`docs/decisions.md`）：模块外框 4 边 + 中心线、端口锚点（轴由所在边决定）、外壳边（**与模块同一张线表**）；级别 = 贴合 > 端口锚点 > 模块中心 > 边缘；**同坐标去重**；**没有额外释放带**（离开容差带立刻跟手） |
| **底部/右侧对齐时看不到参考线** | `src/Ui/Controls/GuideOverlay.cs` | 根因是**叠加层被窗口边框内缩 1 DIP**（`Frame` 是 `BorderThickness=1`），而下/右贴边的线在叠加层坐标里是"范围 + 2" ⇒ 被可见性判断否掉（上/左落在 0，所以看得见）。修复 = `IsDrawable`（容忍贴边超出、仍排除离得远的线）+ `InsetForDrawing`（贴边收进半个线宽，否则一半被裁剪）。线色/线宽 = 该控件的 `LineBrush`/`GuideThickness`；排除范围 = `EdgeTolerance` |
| **想调吸附手感** | **设置 → 外观与显示 → 吸附距离（px）** | 默认 **4**（范围 2–8，落 `settings.json` 的 `snapDistance`）：**越小越跟手**（最多滞后一个容差就松手），**越大越容易吸上**；贴合（相邻边接触）永远用它的 **2 倍**。常量默认值在 `SnapEngine.DefaultTolerance` / `FlushTolerance`（改默认才动这里） |
| 移动主窗口时**模块跟不动 / 拉脱** | `ModuleWindowsViewModel.TranslateShellGroup` + `ModuleWindowManager.OnShellMoved` | **外壳是拼图里的一个节点**：单侧吸附（只提供右边缘）完成后，外壳与贴其上的模块同属一个**接触连通分量**；移动外壳 → 只平移**该组**（没拼上的留在原处）；拖动组内模块 → 外壳窗口一起走；**分组判定必须用"移动前"的外壳矩形**（`preMoveShell`），否则"贴上了"会被判成"没贴上" |
| 主窗口与模块吸附**有缝** | `src/Ui/Views/WindowFrameProbe.cs`（新）+ `PublishShellRect` | 参照改用**可见外框**（`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`），而不是客户区右边缘（两者差一个可见边框宽 ≈ 1px ⇒ 以前贴上去总有缝） |
| 拖动中/移动后**像素级错位**、模块被"拉回" | `src/Ui/Geometry/ScreenPixels.cs`（新）+ `ModuleWindowsViewModel` | 几何一律**整数物理像素**（唯一量化点）；整体平移**只改内存 + 180ms 防抖合并落盘**，且**刷新（60Hz）跳过未落盘的成员**（否则会被旧布局一帧帧拉回） |
| 端口/网络标签对齐不生效 | `ModuleWindowsViewModel.BuildPortAnchors` + `SnapEngine.PortAlignReach`(24px) | 端口锚点**恒开**、只在两模块 24px 内接近时参与；**对齐轴由端口所在的边决定**（当前口只在左/右边 ⇒ 恒为 Y），不再"按缝隙大小猜邻接方向" |

### 第十三轮（**端口标记内容重排**；`docs/decisions.md`）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 标签名长了**溢出错位 / 抖动** | `src/Ui/Views/ModuleWindow.axaml`（标记模板）+ `src/Ui/Styles/Tokens.axaml` | 槽位改为**固定四段** `ColumnDefinitions="Auto,*,20,Auto"` = **类型窄色条（`PortMarkerStripWidth`=6）/ 网络名（`*` + `CharacterEllipsis`）/ 方向箭头（槽 20）/ 连接状态灯**；名称吃掉剩余宽度 → **内容变化不再引起排版抖动**。改尺寸只改 token |
| **箭头太小** | 同上（`PortMarkerArrowFontSize`） | 箭头字号 **11 → 16**；颜色仍是"发送=主色 / 接收=次色"（`AccentPrimary`/`AccentSecondary`） |
| 指示灯**太小 / 缩在里面** | 同上（`PortMarkerLampSize`） | **6×6 → 8×8**（`StateIndicator` 半径 = 边长/2 − 1 ⇒ 2 → 3）且 **`Margin=0` 贴到标记端部**（此前留 3 px） |
| 标记上**不该有连接状态文字** | `src/Ui/ViewModels/PortMarkerViewModel.cs` 的 `NetworkText` | **删掉"（未连）"后缀**：状态**只由指示灯**表达。`NetworkText` = 标签名 / 已连但无标签 `（直连）` / 无标签未连 `无网络`（后两者是**网络名占位**，不是状态提示） |
| 标记上**放不下数据包类型** | `ModuleWindow.axaml` 的色条 + `PacketTypePalette` | **类型文本移出标记** → ① 标记上=**窄色条**（6 px，跨满标记内宽）；② 悬停提示里显式给出 `类型：X`。**颜色映射待定**：现在走 `PacketTypePalette.Placeholder`，将来**只改这个类里的 `Map`**（VM 与 XAML 都不动） |
| 悬停里**不该有"上次操作"** | `PortMarkerViewModel.Tooltip` + `ModuleWindowsViewModel.CommitPortLabel` | `EditResult` **字段整体删除**（数据不再保存），悬停不再有那一行；提交结果仍 `RaiseNotice` **送状态栏**（反馈闭环不丢） |
| 编辑弹窗**太大**、提示占版面 | `ModuleWindow.axaml` 的标记 `Popup` | 宽度 **268 → 188**；行内多句提示**移除** → 标题右侧 **`?` 帮助按钮 + `Flyout`**（`{Binding Hint}`，与外壳各页同规则，见 §4）；失败原因仍**就地**显示且**保持打开** |
| （实施期踩坑）标记里给元素起**同名** | 模板内元素标识 | 竖排/横排**两套分支在同一命名域**，同名 `Name` 会在**首次布局时**抛 `Control with the name 'X' already registered.`（T0 编译看不到）⇒ 模板内元素用 **`Classes`**（`typeStrip`/`markerLamp`），见 `LESSONS.md` 25 / `implementation/02` P38 |

### 第十四轮（**方向语义色与主题色解耦**；`docs/decisions.md`）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 发送/接收的颜色绑在**主题色**上（改主题色 = 改语义色） | `src/Ui/Styles/Tokens.axaml`（新增 `DirectionSend`/`DirectionReceive`，深浅各一套） | 「方向」是**稳定语义**、`accent` 是**交互强调**，两者**完全解耦**：浅色 发送 `#EA580C` / 接收 `#0891B2`，深色 `#F59E0B` / `#22D3EE`（**刻意避开红/绿对**） |
| **想改方向色** | **设置 → 外观 → 发送色 / 接收色** | 色片 + 弹窗（预设 + 十六进制）；**清空 = 跟随主题默认值**（右侧显示 `跟随主题`）；**不校验、不提示**（两色设成一样也随你，裁决 D6）；下方有 `↑发送`/`↓接收` 实时预览 |
| 标记**箭头**颜色 | `src/Ui/Views/ModuleWindow.axaml`（箭头 4 处 `Foreground`） | `{DynamicResource DirectionSend}` / `DirectionReceive`（两条分支各 2 处） |
| 标记**边框**颜色 | 同上（`Border.portMarker` + `Window.Styles`） | 边框色**从元素上移进样式**（Avalonia 里本地值优先于样式）：`Border.portMarker` = 接收色、`Border.portMarker.send` = 发送色；元素上只写 `Classes.send="{Binding IsSend}"`。**改了别再在元素上写内联 `BorderBrush`**，否则样式失效 |
| **建网预览**描边颜色 | 同上（`Border.dockHighlight` / `.send`） | 用**同一个方向色**，仅以**线宽 2（常态 1）**区分（D3b）；同样别再写内联 `BorderBrush` |
| 叠加层**流向箭头**颜色 | `src/Ui/Controls/ConnectionOverlay.cs` + `ModuleWindow.axaml` 的 `SendArrowBrush`/`ReceiveArrowBrush` | 按**口的方向**取色。⚠️ 顺带修掉两个既有缺陷：箭头**从来没画出来**（`DrawArrow` 建了几何却没 `DrawGeometry`）、`ArrowBrush` 是**死属性**；并补了 `AffectsRender`（改画刷不重绘）。**连线**仍是单色（是否按方向分色归"叠加层专项"） |
| 边界（**别越界**） | — | ① 状态色（`SemanticOk/Warn/Error/Idle`）与交互强调（`AccentPrimary`）**不参与**方向色；② **主题色只影响模块共有/框架性颜色**，模块内部特有绘图与文本颜色**由模块自己决定**（D7）；数据-颜色绑定归后续协议专项，**当前只做预留** |

### 第十五轮（**叠加信息层重设计**；`docs/decisions.md`）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 叠加层**不清晰、像出了 bug** | `src/Ui/Views/ModuleWindow.axaml`（层级 + 遮罩）+ `src/Ui/Controls/ConnectionOverlay.cs`（重写） | **根因**：线只从"端口标记中心"画到窗口边缘（= **10px 残段**）+ 叠加层排在**端口标记之后**（压在标记上）。**现在**：遮罩 → 图形层 → **端口标记**（标记在最上、常显可双击） |
| **想开关叠加层** | **快捷键 `L`**（`HotkeyCatalog.ToggleMarkers`）或 交互页/设置页的「叠加信息层」 | **默认关闭**（它是调试/观测层）；**旧配置里显式设过 true 的仍然开** |
| **"灰玻璃"浓度** | `src/Ui/Styles/Tokens.axaml` 的 `OverlayScrim`（浅 `#0F172A`@55% / 深 `#000000`@45%） | 一层半透明深色（**同"弹窗变暗"的最简做法**，不做模糊）；叠层文字固定用浅色墨水 `OverlayInk`/`OverlayInkMuted`。**故意压暗模块内容**是设计意图 |
| 线的**粗细 / 虚线 / 流动** | `ConnectionOverlay` 顶部常量：`LineThickness`(2.5) · `DashLength`(6) · `DashGap`(4) · `ArrowSize`(6) | 相位由 **`OverlayAnimationClock`（共享 20Hz）** 推进：发送口向外、接收口向内（`DashOffsets` 互为相反数） |
| 线的**颜色与渐变** | `ConnectionOverlay.LineOf` / `MidColor` + `ModuleWindow.axaml` 的 `SendArrowBrush`/`ReceiveArrowBrush` | 本端方向色 → **中点色**（发送色与接收色的 50% 混合）渐变；**两个窗口各画一半，靠同一条中点色规则在公共边颜色相接**（拼接时**不淡出**，未拼接时末端 14px 淡出 + 标对端） |
| **中心方块**（每个数据类型一个） | `ConnectionOverlay.TypeBoxesOf`（纯函数）+ `TypeBoxSize`(14) / `TypeBoxGap`(10) | 颜色 = `PacketTypePalette.For(type)`（**仍是占位色，等你给映射**）；方块下 11px 类型文本；整组在模块中心、超宽换行 |
| 线的**起点** | `ConnectionOverlay.LineOf` | 起点 = **该口类型对应的方块中心**（不是模块中心一个点、更不是标记中心）；终点 = **窗口边界** |
| **未连接的口** | 同上（`UnconnectedStubLength` = 26） | 短虚线残段（40% 透明）+ `未连` 〔**第十五/十六轮的历史做法；已按 `docs/decisions.md` 的现行结论删除** —— 现在没有「未连」文字，见 §18〕 |
| 拼接与未拼接的**收尾** | `PortMarkerViewModel.PeerAdjacent`（`ModuleWindowsViewModel.ResolvePeerAdjacent` 用既有 `AreNodesJoined` 算） | 拼接 ⇒ 不淡出、不标对端；未拼接 ⇒ 淡出 + 标对端 |
| 后续（未做） | — | 正交折线路由 · "静止虚线"开关 · 类型色映射 · 叠加层开启时隐去标记名称文本（**等你复验后定**） |

### 第十六轮（**叠加层细节修正 · 颜色对比度 · 崩溃修复 · 双击加入**）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 未连接时 **"未连"标签压住中心方块** | `ConnectionOverlay.UnconnectedLabelOrigin`（新增纯函数）+ `UnconnectedStubLength`(26) | 文字按**口所在边**朝**窗口外**一侧摆（左边缘 → 文字在残段左侧；右边缘 → 右侧），并钳制在窗口内；文字改用**不透明**方向色（原来 60% 太暗）。**改位置就改这个函数**（测试：`叠加信息层_未连标签必须朝窗口外且不压住中心方块`） |
| **叠加层里颜色太暗、分辨不清** | `src/Ui/Styles/Tokens.axaml` 的 `OverlayScrim` | **实测根因**：浅色主题下 55% 遮罩把底子压成中灰（亮度≈0.21），方向色与它对比度只有 **1.1 : 1**（等于看不见）。**遮罩浓度 0.55 → 0.85**（浅 `#0B1220`、深 `#05070D`）⇒ 方向色对叠层底 **3.3–3.4 : 1**（旧 1.1），深色主题 9–10 : 1；底部内容仍隐约可见。叠层文字色 `OverlayInk`/`OverlayInkMuted` 不变 |
| 出现**第二条自动建网链路就崩** | `ModuleWindowsViewModel.RefreshMarkerStatus` + `TopologyViewModel.Refresh` + `Core.Api` 的 `NetworkView.Key`/`BindingDiagnosticView.Key` | **根因不是异常处理**：诊断/网络视图只给"网络名"，而**匿名网络名为空串** ⇒ 两条匿名网络撞同一个键。**框架补身份键**（= 内部既有的"源 → 宿集合"），两处视图一律按 `Key` 配对、字典用 `TryAdd`（宁可少显示也不崩）。见 `LESSONS.md` 38 / `implementation/02` **P40** |
| **双击列表项直接加入现场** | `ModuleCatalogView.axaml`（`DoubleTapped`）+ `.axaml.cs` + `ModuleCatalogViewModel.AddSelected()` | 双击"可用模块"里的某一项 = 等同"选中 → 加入现场"；**只双击到项上才生效**（空白处不动作、不报错）；按钮与双击**共用同一条实现**。`SelectedCatalogEntry` 仍可单击选中 |

### 第十七轮（**连线跨过标记 · 新模块摆放 · 拖动中 Ctrl 拆分**）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 连线**从网络标签连到中心点**、缺少意义 | `src/Ui/Views/ModuleWindow.axaml`（层级）+ `ConnectionOverlay.DrawPeer`（对端标签内收） | **根因**：连线本来就从中心画到窗口边界（几何是对的），但标记是**不透明块（20px 宽）**且压在连线之上 ⇒ 拼接处**两个标记遮掉 40px**，看起来就是"从标签连到中心"。**层级改为**：内容 → 遮罩 → **端口标记** → **连线层** → 运行指示/按钮簇 ⇒ 连线**跨过网络标签直达对端**（跨过公共边与对端半段相接）。对端标签内收到**标记带以内**，避免又被标记压住。测试：`叠加信息层_遮罩在标记之下而连线在标记之上` |
| 新模块都落在**左上角、压住主窗口、互相堆叠** | `ModuleWindowsViewModel.NextPlacement()`（新增） | 起点 = **外壳右边缘 + 12px**；与**已有模块逐个做相交检查**，冲突就往下挪"模块高 + 12"；一列到底（超出工作区下沿）→ **换右边一列**（工作区不可用时不换列，仍保证不堆叠）。有 256 次循环上限兜底（宁可重叠也不死循环）。测试：`新模块摆放_应在外壳右侧排开且互不堆叠` |
| 拼接后单独拖动**必须拖动前按 Ctrl** | `ModuleWindow.axaml.cs`（拖动中每帧判定）+ `ModuleWindowsViewModel.DetachDrag()`（新增） | 拖动**期间**按下 Ctrl ⇒ **立刻拆分**：等价于"一开始就按住 Ctrl" —— **其余成员/外壳回到本次拖动起点**（外壳经 `ShellTranslationRequested` 反向位移还回真实窗口），被拖模块继续跟指针；**单向**（松开 Ctrl 不会重新合组）；**幂等**（每帧调用无害）。测试 2 项（成员回原位 · 外壳回原位） |

### 第十八轮（**叠加层改成「角色投影」**）→ **第三十三轮：角色 = 模块声明的功能分块** → **第三十四轮 + 二次修正：两个信息维度** → **第三十四轮二次：颜色映射表 + 调色板窗口**（以上各轮的现行结论均见 `docs/decisions.md`）

> **⚠️ 口径已换代**：下表做法一栏已按 **`docs/decisions.md` 的现行结论（含二次修正）** 写全；
> 凡涉及**旧做法**的东西（分侧摆放 · `×N` · 残段 + `未连` · `RoleLineOf` · 结构文字 · **线上的信息卡与 `OverlayCard`** · **"缩方块"档** ·
> **叠加层里给标记调色的降权样式**）**都已删除**，改后位置见各行。

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 叠加层里的方块**代表什么** | `ConnectionOverlay.LayoutRoles`（纯函数）+ `RoleBoxViewModel` | 方块 = **模块声明的功能分块**（`SDK DeclareRole`：一个 `Id` + 一种数据类型 + 输入口/输出口）⇒ **声明几个就画几个**；**单列竖直堆叠、居中**，**宽度随文本自适应**（`RoleTextMetrics.BoxWidth`，夹 [64,176]；块高 34）⇒ 放不下就只画得下的 + 底部 `+K`。**框架不派生、不发明角色** |
| 方块内容/配色 | `ConnectionOverlay.DrawRoleBoxes` + `PacketTypePalette` | 两行：**功能名**（无则类型短名，11px）+ **`m/n`** = 输入/输出侧**连接的对端角色数**（10px；无该侧口 ⇒ `--`、有口未连 ⇒ `0`）；**填充 = 类型色**（按 `TypeKey` 稳定哈希）、**左边缘 = 接收方向色（有输入口才画）· 右边缘 = 发送方向色（有输出口才画）**、**>1 口才画刻痕**；**不带状态色** |
| **配色**想固定某类型的颜色 | `PacketTypePalette.SetOverride("RawBytes@v1", Color.Parse("#…"))`（或后续的侧边栏"数据包类型表"） | 默认 **FNV-1a 稳定哈希**（同一类型恒同色、跨进程一致）；覆盖表可注入。端口标记的窄色条与角色方块**共用同一入口** ⇒ 颜色天然一致 |
| 线的**条数/聚合** | `ModuleWindowsViewModel.RefreshRoleLinks`（`GetRoleFlows`） | 同一（本端角色, 方向, 对端实例, 对端角色）⇒ **一条线**（`PortPairCount` 只作内部量，**线上不再标 `×N`、也不标网络名**——网络标签属接口层）；多源网络的**不同模块的源 = 各自一条线** |
| 线的**几何与流向** | `ConnectionOverlay.LinkSegment`（纯函数）+ `OverlayAnimationClock.DashOffset` | 端点 = **两个角色方块的中心**；画出来的那段 = **本窗内 ∩ 本端方块之外**（视觉上"从方块中心出发"、不横穿方块）；**虚线沿数据流方向流动**：两侧共用 `-相位 ÷ 线宽`（`DashStyle.Offset` 单位是**笔宽倍数**）⇒ **发送向外、接收向内**。想改走向 ⇒ 改锚点，**不要加方位分类** |
| **角色自身的信息**（**角色维度**） | `ConnectionOverlay.DrawRoleInfo` + `RoleBoxViewModel.InfoLines` | **纯文字、不画文本框**，贴在方块**上方、左对齐**：① **速率**（**大字** 15px，主行）· ② `输入 m / 输出 n`（**口数**，10px）· ③ `类型：<短标签>`（10px，只取第一个语义标签并截断 8 字 —— 用户："禁止 UI 外显过长描述"）。数字来自 `RoleView.PayloadRateBytesPerSecond`（**框架的角色维度聚合**，产出侧优先：有输出口取输出网络、纯汇取输入网络；**2026-09 口径 = 有效载荷字节速率**，用户："速率只要有效数据"） |
| **通路速率**（**通路维度**） | `PortMarkerViewModel.RateText` + `ModuleWindow.axaml` 的 `markerRate` 槽 | **常驻**在标记上（10px，比名称小一号；未入网 `---`、有连接无数据 `0.00 B/s`）—— 用户："速率显示**常驻**于网络标签上，**它不属于叠加层**" ⇒ 与叠加层开关**无关**。数值 = `PortMetricsView.PayloadBytesPerSecond`（**有效载荷口径**） |
| **标记里的网络名与速率谁让谁** | `ModuleWindow.axaml` 的标记模板（两套分支）+ `PortMarkerViewModel.NameMaxWidth` | **名称优先**（2026-09 用户裁定）：名称列拿自己需要的宽度（上限 = 标记长度 − 固定槽位），**速率吃剩余、不够就省略**。想改长短 ⇒ 改 `PortMarkerMetrics.Length`（不要只改模板） |
| 接口层**沉下去**（"不高亮"） | `ModuleWindow.axaml` 的 **Grid 子元素顺序**（不是样式） | **做减法**：`内容 → 端口标记 → 遮罩 → 连线/角色层 → 运行指示/按钮簇` ⇒ 叠加层开启时标记被灰玻璃压暗（用户："网络标签不高亮**指的不是在上面覆盖一层**……是做减法，把它埋入灰色玻璃下方"）。**不要**再给标记加降权样式（那是做加法，已被否决）；标记仍在命中层之上 ⇒ 照样可双击建网 |
| 信息**分区域 + 按字号分层** | `ConnectionOverlay.Render` + `RoleOverlayInfoBuilder.Build` | 左上**身份 12px**（`实例 · 模块名`）· 左上第二行 **10px 仅在需要说明时非空**（`无数据口` / `未声明角色（N 个数据口）`）· 方块上的角色信息（见上）。**字号常量**：`CaptionFontSize`（12）/ `RoleTextMetrics.TitleFontSize`（11）/ `DetailFontSize`（10） |
| 模块**内部处理顺序**（原"内部关系线"） | SDK `DeclareFlow(FlowDeclaration(FromRoleId, ToRoleId))` + `ConnectionOverlay.DrawRoleOrder` | 声明的是**块与块之间**的顺序（角色已含 in/out ⇒ 块内部不必声明）；画成**细实线 + 中性色 `OverlayFlow` + 不流动**（结构 ≠ 流量）；**零声明 ⇒ 不画**（**当时**参考模块全是单块 ⇒ 本轮图上零内部线；**该载体已于 2026-09 按用户裁定删除**，现行四个正式模块同样是单块 ⇒ 仍零内部线） |
| 未连接 / 没声明角色 | `RoleOverlayInfoBuilder.Build` + `GetRoles` | **没有「未连」文字**（没连上时端口标记的指示灯会灭，文字多余且抢宽度）——未连的事实看方块第二行的 `m/n`（`0` vs `--`）；**没有声明角色 ⇒ 不画方块**（框架不发明角色），左上第二行给 `未声明角色（N 个数据口）` |
| 开机就想看效果（**不必启动应用**） | 独立项目 `tests/HYWDebugAssistant.VisualSnapshots` | `dotnet test tests\HYWDebugAssistant.VisualSnapshots\...` ⇒ 在**真实渲染栈**下把带叠加层的窗口渲染成 PNG（`%TEMP%\hyw-overlay-snapshot\`）。**调叠加层观感时先跑它、直接看 PNG**；它同时是"自绘层真的出像素"的渲染冒烟，并含一条**虚线偏移语义**的实测用例（改流向/相位前先看它） |
| **新模块要在叠加层里出现**（新增一节用法） | 模块 `OnRegistered` | `DeclareDataType(...)` + `DeclarePort(...)`（口要带 `Type = new DataTypeRef(id, ver)`）+ `DeclareRole(RoleDeclaration.Output/Input/Both(...))`。**注意**：角色引用的类型必须**在本模块已声明**，否则声明被拒（运行期报 Issue + SDK 自检也会报）⇒ 现场表现就是"叠加层里没有这个模块的方块" |
| **数据结构 → 颜色映射表**（侧边栏 交互 页底部 · `docs/decisions.md`） | `ShellViewModel.ApplyDataTypeColor` + `PacketTypePalette` + `DataTypeColorTableViewModel` + `InteractionView.axaml` | 一行 = 一种**类型身份**（色块 / 身份 / 形状 / 用量）；点色块改色 = 打开**调色板窗口**（可任选颜色 / **恢复自动**）。**颜色只有一处来源**：色板 `PacketTypePalette`（端口标记的窄色条与叠加层角色方块的填充**共用**）⇒ 改一处两处同步；**颜色存在应用设置 `typeColors`**（缺省不写 = 自动哈希色，跨重启同色）；**改色入口唯一** —— 别在别处直接 `SetOverride`（那样不落盘、也不重画） |
| **所有"选颜色"的地方**（表的色块 + 设置页 4 处 · `docs/decisions.md`） | `ColorPickerDialog.ShowAsync(...)`（`FrameworkUi/Views`） | **只有这一个取色窗口**：色板方块 + 色相条 + 分量滑块 + 十六进制 + 一排**推荐色**（= `ThemeApplier.Presets`），底部**确定/取消** + 可选**恢复**（`ColorPickerResult.Reset` 的语义由**调用方**解释：类型色 = 移除覆盖、方向色 = 置空跟随主题）。**新增取色需求就调它，不要再写 `Flyout` 内联色片**（那会立刻产生第二套行为）。**精简边界**：不做自定义色格、不带透明度 |
| 颜色表的**刷新纪律**（`docs/decisions.md`） | `ShellViewModel.RefreshDataTypeColors` + `DataTypeColorRowViewModel.Update` | 表**就地改值**，行对象**只在类型集合增删时**变动 —— 每次刷新重建 `Rows` 会拆掉用户正在悬停/正在取色的控件（现场表现 = "鼠标指到色块上不停闪烁"） |

---

## 4. 界面文本的三种写法（**第八轮 / 第十轮反馈后统一**）
| 场景 | 写法 | 示例 |
|---|---|---|
| 一句短语（≤ 12 字） | 直接显示 | `<CheckBox Content="几何吸附" .../>` |
| 一句解释（一句话说清） | **悬停提示**，不占版面 | `<CheckBox Content="几何吸附" ToolTip.Tip="拖动时…" />` |
| 长说明（多句 / 教程） | **问号按钮 + 弹窗**（第十轮起：不再就地展开） | `Button.helpIcon` + `Button.Flyout` |

**问号按钮 + 弹窗文本**（纯 XAML，**不需要改视图模型**，且关闭后不留痕）：

```xml
<Grid ColumnDefinitions="*,Auto">
    <TextBlock Grid.Column="0" Classes="paneTitle" VerticalAlignment="Center" Text="分区标题" />
    <Button Grid.Column="1" Classes="helpIcon" ToolTip.Tip="说明">
        <Button.Flyout>
            <Flyout Placement="TopEdgeAlignedRight">
                <TextBlock MaxWidth="320" TextWrapping="Wrap" FontSize="11" Text="这里是说明文本……" />
            </Flyout>
        </Button.Flyout>
    </Button>
</Grid>
```

要点：
- `Classes="helpIcon"`（`Controls.axaml`）：16×16、圆角、**1 px 边框**、内容自动是 `?`、悬停变主色；
- 放在分区的**右侧/右下角**，右边距保持 4 px → 各按钮 **X 坐标一致**；
- 弹窗内容**只放文字**（不放控件、不放会改变布局的东西）。
- `Classes="note"` = 10 px 弱化色 + 自动换行的说明文本样式。

## 5. 模块清单页的结构（第十轮重排；改这里 = 改主页面样子）

`src/FrameworkUi/Views/ModuleCatalogView.axaml` —— **自上而下 6 段**（改顺序/高度/间距都在这个文件的 `RowDefinitions` 与各段里）：

| 段 | 内容 | 关键约束 |
|---|---|---|
| ① | 搜索框 | 整行宽 |
| ② | **分类标签**（`Button.tab`，`Width=64` 固定，图标 + 数量） | **固定宽度** → 切换分类不推动其它控件 |
| ③ | **模块列表**（`Border.pane` + `ListBox`，占 `*` 行） | **区域固定**、内部滚动；底行左「加入现场」右帮助 |
| ④ | 目录操作行：添加目录… / 删除选中 / 重新加载 | 显式在页面上（不藏帮助里） |
| ⑤ | **来源根列表**（`Border.pane`，`Height=120` 固定；一行一个、路径中间省略、行尾小按钮打开目录） | 高度固定；全路径在悬停提示里 |
| ⑥ | 加载模块包（输入框 + 选择… + 加载） | 显式在页面上 |

| 想改什么 | 改哪里 |
|---|---|
| 分类项（图标 / 名称 / 顺序） | `src/FrameworkUi/ViewModels/ModuleCatalogViewModel.cs` 的 `ModuleCategories.Definitions`（键 · MDL2 字形 · 悬停说明） |
| 标签 → 分类的映射规则 | 同文件的 `ModuleCategories.FromTags(...)`；**模块侧**在 `ModuleDefinitionAttribute(Tags = [...])` 里声明 |
| 分类标签外观 / 宽度 | `ModuleCatalogView.axaml` 第 ② 段（`Width="64"`、样式 `Button.tab` 在 `Controls.axaml`） |
| 列表/来源区高度 | 同文件 `RowDefinitions`（列表 = `*`；来源区 = `Height="120"`） |
| 搜索匹配范围 | `ModuleCatalogViewModel.ApplyFilter()`（当前匹配 展示名 + 描述，忽略大小写） |
| 路径省略规则 | `ModuleSourceRowViewModel.Display`（层级 ≤ 3 原样，否则 `…\倒数第二级\末级`） |

> 分类标签**只用图标 + 数量**（悬停显示名称），列表项**只显示名称**（描述在悬停提示里）——"首屏信息量小、面积给列表、尺寸不随内容变"。

## 6. 弹窗尺寸与换行（第八轮修正）

- `DialogWindow.axaml`：**宽度固定 780** + `SizeToContent="Height"`（高度随内容），`MaxHeight=880`（超出时内容区滚动）。
- **为什么不能都自适应**：`SizeToContent="WidthAndHeight"` 会让带 `TextWrapping` 的文本拿到"无限可用宽度"→ **永不换行**（只有手动缩小窗口才换行）。若你希望某个弹窗更宽/更窄，改这里的 `Width` 即可。

## 7. 想加一个"设置项"或"图标入口"
1. **字段**：`src/Core.Api/ApplicationSettings.cs` 加属性（**只增不改 = 兼容扩展**）；
2. **读写**：`src/Core/Settings/SettingsService.cs` 的 `FromJson` / `MergeInto` 各加一行（新键优先、旧键可回退）；
3. **面板**：`src/FrameworkUi/ViewModels/SettingsPanelViewModel.cs` 加 `[ObservableProperty]` + 在 `Load()`/`Apply()`/回读处各一行；
4. **界面**：`SettingsView.axaml` 对应分类里加控件；
5. **生效**：若要在界面即时生效，参考 `SettingsPanelViewModel.ApplyAppearance()` 的写法。

## 8. 改了之后怎么验证（**分层 · 不要每改一处就跑全量**）

| 改动范围 | 命令 | 说明 |
|---|---|---|
| **纯 UI**（XAML / 样式 / 文案 / 间距 / 颜色） | `.\tools\dev.ps1 -Mode Build -Project FrameworkUi`（或 `-Project Ui`）→ `.\tools\dev.ps1 -Mode Run` | **只编译 + 眼睛看**；**不跑测试、不跑门禁**（用户第十轮明确要求） |
| **叠加层（自绘）观感** | `dotnet test tests\HYWDebugAssistant.VisualSnapshots\HYWDebugAssistant.VisualSnapshots.csproj` ⇒ 打开 `%TEMP%\hyw-overlay-snapshot\*.png` | **不必启动应用**：真实渲染栈把带叠加层的窗口画成 PNG（第三十三轮起）。自绘层的"叠字/画出界/根本没画"只有看图才发现 |
| **连带改了逻辑**（视图模型 / 命令 / 数据结构） | `.\tools\dev.ps1 -Mode Test -Filter <该逻辑的测试类>`，例如 `-Filter FrameworkUiTests`、`-Filter ModuleWindowVisual` | **只跑受影响的那个测试类**（T1），不跑整套 |
| 把东西交给用户手工验收之前 | `.\tools\dev.ps1 -Mode Gate` | T3 完整门禁（这类时点才跑） |
| 改了 `Sdk` 公开面 / `Core.Api` 契约 / 设置或组合件文件格式 / 依赖清单 | `.\tools\dev.ps1 -Mode Gate` | 同上：影响面大，必须全量 |

> **注意**：应用运行时 `src/App/bin` 下的 DLL 被占用 → 构建会失败（`MSB3026`）。用 `dev.ps1 -Mode Run` 前先关掉应用，或加 `-StopApp` 让脚本停掉它。
> 纯 UI 改动若只编 `Ui`/`FrameworkUi` 而 App 正在跑，用 `-Mode Run` 会被文件锁挡住——**先关应用再运行**。
> **改了 `src/Ui` 的代码后**：`-Mode Test -SkipAppRebuild` **只重编测试项目**，用的是 `src/Ui/bin` 里的**旧 DLL** ⇒ 必须先
> `-Mode Build -Project Ui`（第三十三轮实测：忘了这一步会误判成"改了没生效"）。
