> ⚠ 历史材料（ADR 原文，2026-09 文档收敛起归档）。**现行决策**见 `docs/decisions.md`；实现层现行值见 `docs/architecture/` 各分册。正文为当时的决策与论证；其中的"待决/待办/计划"一律以现行文档为准。

# ADR-0041：第十一轮 UI/交互修订（「还原」取代运行/停用 · 锁定语义与随壳移动 · 开关同源 · 取色器 · 模块默认尺寸声明）

> **状态**：**Accepted（2026-09 · 依据用户第十一轮实测反馈 5 条 + "我同意你的改法，可以开始修改"）· 已实现**
> **关联**：ADR-0031（UI 架构）· ADR-0038（外壳侧边栏）· ADR-0039（模块视觉与叠加信息层）· ADR-0040（SDK 执行通道）· `architecture/11` §10.4（**本 ADR 修订其第 2 项**）· `architecture/14-ui-rework-m1-16.md` §15
> **修订对象**：`05` §5.15 第 2 项（按钮簇「停/启」）· `05` §5.18（按钮簇与图标 token）· `05` §5.21（**新增落地表**）· `implementation/04`（验收清单 → 修订版 4）· `implementation/02`（新增 P30）· 主文档 §10.1（模块窗口按钮簇）/§9.2（配置还原）

---

## 1. 背景：用户第十一轮实测的 5 个问题

| # | 用户原话要点 | 性质 |
|---|---|---|
| 1 | 同一个开关出现两处，且两处不联动 | **接线缺陷**（开关存在但无接线——"死开关"的变种） |
| 2 | 颜色选择太原始，只能点预设 | 可用性不足 |
| 3 | 锁定应当有两层含义：不许拖动 **+ 不再跟随壳体移动** | 语义缺失（锁定只实现了一半） |
| 4 | 模块没有"暂停"这件事，我要的是**还原**；还原要能区分范围 | **语义错误**（上一轮把"运行/停用"塞进模块窗口是错的） |
| 5 | 模块应当有自己的默认尺寸 | 能力缺失（一律 320×220） |

其中 #1 是第二次出现同类问题（`showPortMarkers` 死开关是第一次，见 ADR-0039 D5）→ 提升为**纪律项**（`LESSONS.md` 第 9 条"绝不允许静默失败"）。

## 2. 决策

### D1. 模块窗口按钮簇：**运行/停用 → 还原**
- 按钮簇固定为 **[还原][锁定/解锁][隐藏][关闭]**；**删除** `OnToggleEnabled` / `ApplyRunState` / `RunStopIcon` 与"停"图标 token。
- **理由**：模块的生命周期由框架权威判定（`Registered → Active ⇄ Disabled → Removed`，ADR-0014），而"暂停"在数据面没有对应语义——上一轮加「停/启」是为了让"无界面模块撤销"后仍有一个"停止"出口，属**为解决授权问题引入的错误概念**。**撤销 `architecture/11` §10.4 第 2 项的"窗口按钮簇增加「停/启」"**（该项其余部分——每个实例一律开窗、框架补配置表单、删除无界面模块区——**继续有效**）。
- **还原 = 范围可确认**：点「还原」弹出确认对话框，三选一：
  - **只还原默认尺寸**（几何回模块声明的默认尺寸，位置保持）；
  - **尺寸和配置都还原**（几何回默认 + 配置回默认值）；
  - **取消**（什么都不做）。
- **配置还原的实现语义（硬要求）**：`IModuleContext.ResetConfiguration()` = **用同类型的新默认实例替换**每个已登记配置，**不得**用 `Clear()`——清空会让"按登记对象生成"的 Schema 表单失去数据来源（表现为空表单 + `Sequence contains no matching element`）。

### D2. 新增 Core 命令：`ResetModuleConfiguration`
- 走既有两阶段变更模型：`Core/Changes/ChangeOperation.cs` 新增 **`ResetModuleConfigurationOperation`** → `ModuleRuntime` 在 Prepare 校验（配置类型可实例化）/ Commit 应用到 `IModuleContext` → **`NotifyConfigurationChanged`** + Notice（用户可见，不静默）。
- 公开面：`IRuntimeServices.ResetModuleConfiguration(instanceId)`。
- **不扩 SDK 面**：还原是框架对"自己持有的配置与几何"的处置，不需要模块参与。

### D3. 锁定 = 两层含义
- ① 不可拖动 / 缩放 / 选中（已有）；
- ② **不参与拖动组、不随壳体移动**（新增）：
  - `BeginDrag` 组织拖动组时**排除锁定成员**（拖相邻模块不再带走锁定模块，拖锁定模块也不带走他人）；
  - 新增 **`FollowShellMove(dx, dy)`**：以"**锚定**"判定（`|模块.Left − 壳体.Right| ≤ 2` 且与壳体竖向有重叠）取出**贴壳平铺组**，整组平移（仍排除锁定），平移后经 `UpdateModuleLayouts` **落盘**。
- **不做**：把模块位置改为"相对壳体的坐标"存储（布局格式保持不变 = 绝对屏幕像素）；"跟随"只是贴壳时的行为，不是全局联动。

### D4. 同名开关**单一状态源**
- `SettingsPanelViewModel` 新增 **`Owner`（= `ShellViewModel`）**；设置页的 `ShowInfoOverlay` / `AppTopmost` **委托外壳的实时属性**（不再各自持有字段），任一入口切换即 `PersistLiveSwitches()` 落盘。
- **约定**：此后凡"同一设置出现在多个界面"的开关，一律**只保留一处状态**，其余界面做**委托**；禁止各存一份字段再"同步"。

### D5. 取色器：色片 + 弹出面板
- 主题色 / 背景色由"一排预设按钮"改为 **色片按钮（显示当前色）+ `Flyout` 面板**：面板内 = 预设色块（`WrapPanel`）+ **十六进制输入框**（`#RRGGBB`）；选色即写主题字典并落盘（沿用 ADR-0038 的机制）。
- **不做**：取色环 / HSV 滑杆 / 屏幕吸管（v1 无必要）。

### D6. SDK：模块默认尺寸用 **`init` 属性**声明
- `ViewDeclaration` 新增 **`PreferredWidth` / `PreferredHeight`（`init` 属性）**；`ModuleViewViewModel` 增 `PreferredWidth/Height` + `HasPreferredSize`（未声明回退 `DefaultWidth/Height` = 320×220）。
- **硬规则（本 ADR 的长期约束）**：SDK 的"只增不改"**看签名、不看语义**——**不得**通过增加**构造器可选参数**来扩展声明面（那会改变方法签名，已编译的模块各自带一份 SDK 副本 → 加载即 `MissingMethodException`）。新增可选声明一律用 **`init` 属性**或**新类型**。
- **连带要求**：**改 SDK 后必须重编全部模块**（模块包与 SDK 版本耦合，纳入验收前检查）。
- 参考模块声明值：手动发送器 **330×330**、信息显示模块 **360×460**。

## 3. 理由

1. **概念正确优先**：把"停/用"塞进模块窗口，是因为撤销"无界面模块"时需要一个停止出口——那是**为授权问题引入的错误概念**；正确的表达是"恢复到我声明的默认样子"（还原）。
2. **锁定的用户预期是"别动它"**：用户明确把"跟随壳体移动"也视为"动了它"；因此锁定必须覆盖**拖动组与随壳移动**两条路径，否则"锁"是漏的。
3. **开关不联动属于"接线缺陷"而非"设计取舍"**：同一语义存在两个状态必然漂移 → 用单一状态源从结构上消除。
4. **签名兼容是发给第三方的承诺**：语义上"只增"不能保证二进制兼容；用 `init` 属性把承诺落到签名上（实测已因可选参数导致 3 个参考模块加载失败）。

## 4. 影响面

| 项目 | 影响 |
|---|---|
| `Sdk` | `ViewDeclaration.PreferredWidth/PreferredHeight`（`init`） |
| `Core` / `Core.Api` | `ResetModuleConfigurationOperation` · `IRuntimeServices.ResetModuleConfiguration` · `ModuleContext.ResetConfiguration()` 语义（替换而非清空） |
| `Ui` | `ModuleWindow`（按钮簇「还原」）· `ResetModuleDialog` + `ModuleResetScope` · `ModuleViewViewModel`（Preferred 尺寸 / 命令接线）· `ModuleWindowsViewModel`（锁定排除 / `FollowShellMove`）· `Tokens.axaml`（`GlyphReset`，移除"停"图标） |
| `FrameworkUi` | `SettingsPanelViewModel.Owner`（同名开关同源）· `SettingsView`（取色面板） |
| `Modules.Reference` | `ManualSender` 330×330 · `Monitor` 360×460（**并重编全部 5 个模块**） |
| 测试 | T1 受影响类全绿（`ModuleWindowsTests` 30 · `ModuleWindowUsabilityTests` 8 · `FrameworkUiTests` 14 · `ModuleWindowVisual` 8 · `Tests.Reference` 30）；**T3 留待 M1-14 验收前** |
| 文档 | 本 ADR · `05` §5.15/§5.18/§5.21 · `14` §15 · `08` M1-16 状态与 M1-14 现状 · `implementation/02` P30 · `implementation/04` 修订版 4 · `roadmap.md` v1.27 · `LESSONS.md` 17/18 · `UI-ADJUST.md` §2/§3 |

## 5. 不做

- 布局坐标改为"相对壳体"存储（格式不变；只实现跟随行为）；
- 模块窗口的"暂停/停用"按钮（由框架生命周期与模块自身状态承担，窗口不提供）；
- 取色环 / HSV 滑杆 / 屏幕吸管；
- 通过构造器可选参数扩展 SDK 面（明确禁止）。

---

| 日期 | 修订 | 说明 |
|---|---|---|
| 2026-09 | 初版（Accepted · 已实现） | 用户第十一轮 5 条：按钮簇「停/启」→「还原」+ 范围确认（`ResetModuleConfigurationOperation`，配置还原=替换而非清空）· 锁定覆盖"拖动组 + 随壳移动"（新增 `FollowShellMove`）· 同名开关单一状态源（`SettingsPanelViewModel.Owner`）· 取色器改色片 + 弹出面板（预设 + 十六进制）· SDK 模块默认尺寸用 `init` 属性声明并确立"SDK 只增不改看签名"规则（P30） |
