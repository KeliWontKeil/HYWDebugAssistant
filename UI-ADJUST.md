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
| **端口标记规格** | 同上 + `src/Ui/ViewModels/PortMarkerViewModel.cs` | token：`PortMarkerWidth`(20) · `PortMarkerLength`(**114**) · `PortMarkerFontSize`(11)；VM 里 `MarkerWidth/MarkerHeight` 用同一组数值 |
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
| **模块窗口内容**（数值/曲线/日志/表单/命令/输入/开关的模板） | `src/Ui/Views/ModuleContentView.axaml` | 每种元素一个 `DataTemplate` |
| 配置表单（字段行、控件类型） | `src/Ui/Views/ConfigurationFormView.axaml` | |
| 文本日志 | `src/Ui/Views/TextLogView.axaml` | |
| 曲线绘制 | `src/Ui/Controls/CurveControl.cs` | 纯绘制代码 |
| **端口标记排布**（贴边、边感知、预留带） | `src/Ui/Controls/PortMarkerPanel.cs` | `BandThickness=20`、`EdgeInset=-1`（标记压到窗口外框上） |
| **对齐参考线 / 缩放高亮** | `src/Ui/Controls/GuideOverlay.cs` | 坐标换算：屏幕物理像素 ÷ `RenderScaling` |
| **叠加信息层**（连接线/流向/对端标签） | `src/Ui/Controls/ConnectionOverlay.cs` | 连线色/箭头色在 `ModuleWindow.axaml` 里绑 `AccentSecondary`/`AccentPrimary` |
| 模块**视图声明**（发送器这类模块长什么样） | `src/Modules/Reference/ManualSender/ManualSenderModule.cs` 的 `OnRegistered` → `DeclareView(...)` | **不是 XAML**：模块只声明元素，框架按模板渲染；**模块默认尺寸**也在同一个调用里（`{ PreferredWidth = 330, PreferredHeight = 330 }`，见 `src/Sdk/Views/ViewDeclaration.cs`） |
| **「还原」范围确认弹窗** | `src/Ui/Views/ResetModuleDialog.axaml`（+ `.axaml.cs`）· `src/Ui/ViewModels/ModuleResetScope.cs` | 三选一（只还原尺寸 / 尺寸和配置都还原 / 取消）；按钮簇在 `ModuleWindow.axaml` 的 `ResetButton` |
| 设置页与主界面的**同名开关** | `src/FrameworkUi/ViewModels/SettingsPanelViewModel.cs`（`Owner` 委托外壳） | 改开关行为只改 `ShellViewModel` 那一处，两个界面自动一致 |
| 活动栏/窗口图标字体可用性 | `src/Ui/Styles/Tokens.axaml` 的注释表 | 若某个字形显示成方块，把对应 `Glyph*` 改成注释表里的其他码位 |

---

## 3. 你提过的问题分别改在哪（第七 / 第八 / 第十 / 第十一 / 第十二 / 第十三轮；**第九轮是开发方式，见 §0**）

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
| 模块尺寸总是一样 | `src/Sdk/Views/ViewDeclaration.cs` 的 **`PreferredWidth`/`PreferredHeight`**（`init` 属性）+ 各参考模块的 `DeclareView(...) { PreferredWidth = …, PreferredHeight = … }` | 想给模块定默认尺寸：**在模块代码里加两个 `init` 属性**（不要改构造器签名——那是二进制破坏，P30）；未声明的模块仍回退 320×220 |

### 第十二轮（**吸附手感 · 像素对齐 · 外壳成组**；`architecture/15` + ADR-0042）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 靠近对齐线时**吸附振荡** | `src/Ui/Geometry/SnapEngine.cs` | 候选统一为**参照线模型**（ADR-0042）：模块外框 4 边 + 中心线、端口锚点（轴由所在边决定）、外壳边（**与模块同一张线表**）；级别 = 贴合 > 端口锚点 > 模块中心 > 边缘；**同坐标去重**；**没有额外释放带**（离开容差带立刻跟手） |
| **底部/右侧对齐时看不到参考线** | `src/Ui/Controls/GuideOverlay.cs` | 根因是**叠加层被窗口边框内缩 1 DIP**（`Frame` 是 `BorderThickness=1`），而下/右贴边的线在叠加层坐标里是"范围 + 2" ⇒ 被可见性判断否掉（上/左落在 0，所以看得见）。修复 = `IsDrawable`（容忍贴边超出、仍排除离得远的线）+ `InsetForDrawing`（贴边收进半个线宽，否则一半被裁剪）。线色/线宽 = 该控件的 `LineBrush`/`GuideThickness`；排除范围 = `EdgeTolerance` |
| **想调吸附手感** | **设置 → 外观与显示 → 吸附距离（px）** | 默认 **4**（范围 2–8，落 `settings.json` 的 `snapDistance`）：**越小越跟手**（最多滞后一个容差就松手），**越大越容易吸上**；贴合（相邻边接触）永远用它的 **2 倍**。常量默认值在 `SnapEngine.DefaultTolerance` / `FlushTolerance`（改默认才动这里） |
| 移动主窗口时**模块跟不动 / 拉脱** | `ModuleWindowsViewModel.TranslateShellGroup` + `ModuleWindowManager.OnShellMoved` | **外壳是拼图里的一个节点**：单侧吸附（只提供右边缘）完成后，外壳与贴其上的模块同属一个**接触连通分量**；移动外壳 → 只平移**该组**（没拼上的留在原处）；拖动组内模块 → 外壳窗口一起走；**分组判定必须用"移动前"的外壳矩形**（`preMoveShell`），否则"贴上了"会被判成"没贴上" |
| 主窗口与模块吸附**有缝** | `src/Ui/Views/WindowFrameProbe.cs`（新）+ `PublishShellRect` | 参照改用**可见外框**（`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`），而不是客户区右边缘（两者差一个可见边框宽 ≈ 1px ⇒ 以前贴上去总有缝） |
| 拖动中/移动后**像素级错位**、模块被"拉回" | `src/Ui/Geometry/ScreenPixels.cs`（新）+ `ModuleWindowsViewModel` | 几何一律**整数物理像素**（唯一量化点）；整体平移**只改内存 + 180ms 防抖合并落盘**，且**刷新（60Hz）跳过未落盘的成员**（否则会被旧布局一帧帧拉回） |
| 端口/网络标签对齐不生效 | `ModuleWindowsViewModel.BuildPortAnchors` + `SnapEngine.PortAlignReach`(24px) | 端口锚点**恒开**、只在两模块 24px 内接近时参与；**对齐轴由端口所在的边决定**（当前口只在左/右边 ⇒ 恒为 Y），不再"按缝隙大小猜邻接方向" |

### 第十三轮（**端口标记内容重排**；ADR-0043）

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
| **连带改了逻辑**（视图模型 / 命令 / 数据结构） | `.\tools\dev.ps1 -Mode Test -Filter <该逻辑的测试类>`，例如 `-Filter FrameworkUiTests`、`-Filter ModuleWindowVisual` | **只跑受影响的那个测试类**（T1），不跑整套 |
| 把东西交给用户手工验收之前 | `.\tools\dev.ps1 -Mode Gate` | T3 完整门禁（这类时点才跑） |
| 改了 `Sdk` 公开面 / `Core.Api` 契约 / 设置或组合件文件格式 / 依赖清单 | `.\tools\dev.ps1 -Mode Gate` | 同上：影响面大，必须全量 |

> **注意**：应用运行时 `src/App/bin` 下的 DLL 被占用 → 构建会失败（`MSB3026`）。用 `dev.ps1 -Mode Run` 前先关掉应用，或加 `-StopApp` 让脚本停掉它。
> 纯 UI 改动若只编 `Ui`/`FrameworkUi` 而 App 正在跑，用 `-Mode Run` 会被文件锁挡住——**先关应用再运行**。
