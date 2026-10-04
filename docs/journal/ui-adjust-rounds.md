# UI 调参档案：逐轮反馈的改动位置（过程日志 · 不作为依据）

> ⚠ **历史材料**：本文件是原仓库根 `UI-ADJUST.md` §3「你提过的问题分别改在哪」的原文（第七 / 第八 / 第十 ～ 第十七轮，以及第十八 / 第三十三 / 第三十四轮逐节）。**其中的做法多为当时口径，部分已被取代**；原文末尾自认"以上各轮的现行结论均见 `docs/decisions.md`"。
> **现行口径**：UI 开发方式与「文件 → 界面部位对照」见 `docs/architecture/06-ui-shell-and-windows.md` §9；视觉取值与布局旋钮见 `docs/architecture/07-overlay-and-visual-baseline.md` §「取值纪律」；**现行结论唯一落点 = `docs/decisions.md`**。
> **材料来源**：仓库根 `UI-ADJUST.md`（已删除；根目录只保留 `README.md`）。

---

## 逐轮反馈档案（第七 / 第八 / 第十 ～ 第十七轮 · 第十八 / 第三十三 / 第三十四轮）

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

### 第十二轮（**吸附手感 · 像素对齐 · 外壳成组**；`docs/architecture/07-overlay-and-visual-baseline.md` + `docs/decisions.md`）

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
| 编辑弹窗**太大**、提示占版面 | `ModuleWindow.axaml` 的标记 `Popup` | 宽度 **268 → 188**；行内多句提示**移除** → 标题右侧 **`?` 帮助按钮 + `Flyout`**（`{Binding Hint}`，与外壳各页同规则，见 `docs/architecture/06-ui-shell-and-windows.md` §9.3）；失败原因仍**就地**显示且**保持打开** |
| （实施期踩坑）标记里给元素起**同名** | 模板内元素标识 | 竖排/横排**两套分支在同一命名域**，同名 `Name` 会在**首次布局时**抛 `Control with the name 'X' already registered.`（T0 编译看不到）⇒ 模板内元素用 **`Classes`**（`typeStrip`/`markerLamp`），见 `docs/implementation/08-lessons.md` 25 / `docs/implementation/02-engineering-baseline.md` P38 |

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
| **未连接的口** | 同上（`UnconnectedStubLength` = 26） | 短虚线残段（40% 透明）+ `未连` 〔**第十五/十六轮的历史做法；已按 `docs/decisions.md` 的现行结论删除** —— 现在没有「未连」文字，见 `docs/decisions.md`〕 |
| 拼接与未拼接的**收尾** | `PortMarkerViewModel.PeerAdjacent`（`ModuleWindowsViewModel.ResolvePeerAdjacent` 用既有 `AreNodesJoined` 算） | 拼接 ⇒ 不淡出、不标对端；未拼接 ⇒ 淡出 + 标对端 |
| 后续（未做） | — | 正交折线路由 · "静止虚线"开关 · 类型色映射 · 叠加层开启时隐去标记名称文本（**等你复验后定**） |

### 第十六轮（**叠加层细节修正 · 颜色对比度 · 崩溃修复 · 双击加入**）

| 问题 | 改动位置 | 现在的做法 |
|---|---|---|
| 未连接时 **"未连"标签压住中心方块** | `ConnectionOverlay.UnconnectedLabelOrigin`（新增纯函数）+ `UnconnectedStubLength`(26) | 文字按**口所在边**朝**窗口外**一侧摆（左边缘 → 文字在残段左侧；右边缘 → 右侧），并钳制在窗口内；文字改用**不透明**方向色（原来 60% 太暗）。**改位置就改这个函数**（测试：`叠加信息层_未连标签必须朝窗口外且不压住中心方块`） |
| **叠加层里颜色太暗、分辨不清** | `src/Ui/Styles/Tokens.axaml` 的 `OverlayScrim` | **实测根因**：浅色主题下 55% 遮罩把底子压成中灰（亮度≈0.21），方向色与它对比度只有 **1.1 : 1**（等于看不见）。**当时把遮罩浓度 0.55 上调到 0.85**（浅 `#0B1220`、深 `#05070D`）⇒ 方向色对叠层底 **3.3–3.4 : 1**（旧 1.1），深色主题 9–10 : 1；底部内容仍隐约可见。叠层文字色 `OverlayInk`/`OverlayInkMuted` 不变。**该浓度后来又下调过一次，现行值只在** `docs/architecture/07-overlay-and-visual-baseline.md` §「叠加信息层」**一处定义** |
| 出现**第二条自动建网链路就崩** | `ModuleWindowsViewModel.RefreshMarkerStatus` + `TopologyViewModel.Refresh` + `Core.Api` 的 `NetworkView.Key`/`BindingDiagnosticView.Key` | **根因不是异常处理**：诊断/网络视图只给"网络名"，而**匿名网络名为空串** ⇒ 两条匿名网络撞同一个键。**框架补身份键**（= 内部既有的"源 → 宿集合"），两处视图一律按 `Key` 配对、字典用 `TryAdd`（宁可少显示也不崩）。见 `docs/implementation/08-lessons.md` 38 / `docs/implementation/02-engineering-baseline.md` **P40** |
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
| **速率与箭头的位置** | `ModuleWindow.axaml` 的 `markerRate` / `markerArrow` / `markerLamp` 三处 `Margin` | **速率右对齐**（空档留在「名称—速率」之间、速率贴住箭头）+ 指示灯**左边距 4px** ⇒「速率→箭头」与「箭头→灯」各 **5px**，**箭头在两段空档里居中**（2026-09 用户："箭头贴边，看起来不在中间；速率显示建议靠近箭头"）。改位置只动这三个 `Margin`，并让 `ModuleWindowVisualTests.网络标签_速率应贴箭头_且箭头在速率与灯之间居中` 跟着绿 |
| **状态栏**（外壳最底那条） | `MainWindow.axaml` 的状态栏 `Border` + `ShellViewModel.UpdateStatus/ExpireStatusNotice` | 左 = 摘要（`模块 N · 叠加层… · Issue N`，超长省略），右 = **临时提示**（`TextBlock.notice`，错误态加 `.error`）—— **提示独占一列、不再拼进摘要**，且 **8 秒自动过期**。**尾部那个「Issue N」按钮已删**（入口只留活动栏那个图标）；想调提示存活时长 ⇒ 改 `ShellViewModel.DefaultNoticeLifetime`（或构造参数） |
| **四个弹窗的默认尺寸** | `src/FrameworkUi/Views/DialogWindow.axaml`（**唯一来源**） | **780×620**（清单/Issue/设置/拓扑**四个种类一致**）；代码后置里**不得**再按种类覆盖（曾那样做过 ⇒ 三种大小）。子内容排不下由各自的 `ScrollViewer` 滚动；`ShellChromeTests.弹窗_四个种类的默认尺寸必须一致` 钉住 |
| 接口层**沉下去**（"不高亮"） | `ModuleWindow.axaml` 的 **Grid 子元素顺序**（不是样式） | **做减法**：`内容 → 端口标记 → 遮罩 → 连线/角色层 → 运行指示/按钮簇` ⇒ 叠加层开启时标记被灰玻璃压暗（用户："网络标签不高亮**指的不是在上面覆盖一层**……是做减法，把它埋入灰色玻璃下方"）。**不要**再给标记加降权样式（那是做加法，已被否决）；标记仍在命中层之上 ⇒ 照样可双击建网 |
| 信息**分区域 + 按字号分层** | `ConnectionOverlay.Render` + `RoleOverlayInfoBuilder.Build` | 左上**身份 12px**（`实例 · 模块名`）· 左上第二行 **10px 仅在需要说明时非空**（`无数据口` / `未声明角色（N 个数据口）`）· 方块上的角色信息（见上）。**字号常量**：`CaptionFontSize`（12）/ `RoleTextMetrics.TitleFontSize`（11）/ `DetailFontSize`（10） |
| 模块**内部处理顺序**（原"内部关系线"） | SDK `DeclareFlow(FlowDeclaration(FromRoleId, ToRoleId))` + `ConnectionOverlay.DrawRoleOrder` | 声明的是**块与块之间**的顺序（角色已含 in/out ⇒ 块内部不必声明）；画成**细实线 + 中性色 `OverlayFlow` + 不流动**（结构 ≠ 流量）；**零声明 ⇒ 不画**（**当时**参考模块全是单块 ⇒ 本轮图上零内部线；**该载体已于 2026-09 按用户裁定删除**，现行四个正式模块同样是单块 ⇒ 仍零内部线） |
| 未连接 / 没声明角色 | `RoleOverlayInfoBuilder.Build` + `GetRoles` | **没有「未连」文字**（没连上时端口标记的指示灯会灭，文字多余且抢宽度）——未连的事实看方块第二行的 `m/n`（`0` vs `--`）；**没有声明角色 ⇒ 不画方块**（框架不发明角色），左上第二行给 `未声明角色（N 个数据口）` |
| 开机就想看效果（**不必启动应用**） | 独立项目 `tests/HYWDebugAssistant.VisualSnapshots` | `dotnet test tests\HYWDebugAssistant.VisualSnapshots\...` ⇒ 在**真实渲染栈**下把带叠加层的窗口渲染成 PNG（`%TEMP%\hyw-overlay-snapshot\`）。**调叠加层观感时先跑它、直接看 PNG**；它同时是"自绘层真的出像素"的渲染冒烟，并含一条**虚线偏移语义**的实测用例（改流向/相位前先看它） |
| **新模块要在叠加层里出现**（新增一节用法） | 模块 `OnRegistered` | `DeclareDataType(...)` + `DeclarePort(...)`（口要带 `Type = new DataTypeRef(id, ver)`）+ `DeclareRole(RoleDeclaration.Output/Input/Both(...))`。**注意**：角色引用的类型必须**在本模块已声明**，否则声明被拒（运行期报 Issue + SDK 自检也会报）⇒ 现场表现就是"叠加层里没有这个模块的方块" |
| **数据结构 → 颜色映射表**（侧边栏 交互 页底部 · `docs/decisions.md`） | `ShellViewModel.ApplyDataTypeColor` + `PacketTypePalette` + `DataTypeColorTableViewModel` + `InteractionView.axaml` | 一行 = 一种**类型身份**（色块 / 身份 / 形状 / 用量）；点色块改色 = 打开**调色板窗口**（可任选颜色 / **恢复自动**）。**颜色只有一处来源**：色板 `PacketTypePalette`（端口标记的窄色条与叠加层角色方块的填充**共用**）⇒ 改一处两处同步；**颜色存在应用设置 `typeColors`**（缺省不写 = 自动哈希色，跨重启同色）；**改色入口唯一** —— 别在别处直接 `SetOverride`（那样不落盘、也不重画） |
| **所有"选颜色"的地方**（表的色块 + 设置页 4 处 · `docs/decisions.md`） | `ColorPickerDialog.ShowAsync(...)`（`FrameworkUi/Views`） | **只有这一个取色窗口**：色板方块 + 色相条 + 分量滑块 + 十六进制 + 一排**推荐色**（= `ThemeApplier.Presets`），底部**确定/取消** + 可选**恢复**（`ColorPickerResult.Reset` 的语义由**调用方**解释：类型色 = 移除覆盖、方向色 = 置空跟随主题）。**新增取色需求就调它，不要再写 `Flyout` 内联色片**（那会立刻产生第二套行为）。**精简边界**：不做自定义色格、不带透明度 |
| 颜色表的**刷新纪律**（`docs/decisions.md`） | `ShellViewModel.RefreshDataTypeColors` + `DataTypeColorRowViewModel.Update` | 表**就地改值**，行对象**只在类型集合增删时**变动 —— 每次刷新重建 `Rows` 会拆掉用户正在悬停/正在取色的控件（现场表现 = "鼠标指到色块上不停闪烁"） |
