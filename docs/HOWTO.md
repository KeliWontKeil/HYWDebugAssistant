# 怎么去做

> 顶层工作流：**怎么验证** · **怎么跑命令** · **怎么写测试** · **怎么写文档** · **怎么改设计**。
> 文档只有三类，本文属第三类；另外两类见 [`README.md`](README.md)。

---

## 1. 分层验证：别每改一行就跑全量

| 层级 | 何时用 | 命令 | 耗时 |
|---|---|---|---|
| **T0 单项目编译** | 每次小改（改完即编，最早暴露编译/分析器错误） | `.\tools\dev.ps1 -Mode Build -Project Core` | 1–3 s |
| **T1 单类测试** | 改了某块逻辑 / 某个测试类 | `.\tools\dev.ps1 -Mode Test -Filter 端口标签` | 3–6 s |
| **T2 单测试项目** | **一个增量收尾**（如「修好闪烁」） | `.\tools\dev.ps1 -Mode Test` | 4–8 s |
| **T3 完整门禁** | 仅下面三种情况 | `.\tools\dev.ps1 -Mode Gate`（= `build.ps1`） | 25–40 s |
| 只编 + 跑起来看 | 纯 XAML / 样式 / 文案 / 间距 | `-Mode Build -Project FrameworkUi` → `-Mode Run` | 3–8 s |

**这批改动的四步固定动作**：① 一次改完（**不要改一个验一个**）→ ② T0 编改动项目 → ③ 一次 T2（或 T3）→ ④ 报告里写清**跑了哪一层**。

**T3 必须跑的三种情况**（其余一律 T0/T1/T2）：

1. 要把东西**交给用户手工验证之前**；
2. **改了验收事实源**：`Sdk` 公开面 · `Core.Api` 契约 · 设置/组合件文件格式 · 架构规则与项目引用（AR-0~AR-24）· 依赖（`Directory.Packages.props`）；
3. **一个步骤宣布完成时**（写状态回写之前）。

> 现行例外：**「最小验收指标就绪」之前，UI / 交互 / 模块改动只跑 T0 + 受影响的 T1/T2**，T3 与手工验收一起推迟。

**验证的优先级**：① 人类可感知 → ② 测试只用来发现难发现的问题 → ③ **最小测试面**。
测试不能替代实际使用：功能正确性由用户手工验证（见 [`ACCEPTANCE.md`](ACCEPTANCE.md)）。

---

## 2. 命令

**开发内环**（命令与开关的权威定义 = `tools/dev.ps1` 头注释）：

```
.\tools\dev.ps1 -Mode Build|Test|Gate|Tree|Stop|Run [-Project <名片段>] [-Filter <词>]
                [-TestProject <名>] [-AllTests] [-Configuration Debug|Release]
                [-KeepDesignerHost] [-StopApp] [-SkipAppRebuild]
```

| 开关 | 作用 |
|---|---|
| `-Project` | Build：项目名片段（`Core` / `Ui` / `FrameworkUi` …）或 csproj 路径；不传则编整个解决方案 |
| `-Filter` | Test：xUnit 过滤词，**只写片段**（脚本自己拼 `FullyQualifiedName~`）；匹配不到任何用例 ⇒ **直接失败**（拒绝把「零用例」当通过） |
| `-TestProject` | Test：换测试项目（默认主测试项目 `HYWDebugAssistant.Tests`） |
| `-AllTests` | Test：跑全部测试项目（**不含**许可审计） |
| `-SkipAppRebuild` | 应用正在跑时的应急：只重编测试项目（前提：改动过的项目已 T0 编过） |
| `-StopApp` | 直接停掉正在运行的应用（**会丢失运行现场**） |
| `-KeepDesignerHost` | 不先停 VS 的 Avalonia 设计器宿主 |

**完整门禁**：`.\build.ps1 [-StopDesignerHost] [-RunBenchmarks]`
= 文档审计 → 颜色口径审计 → **资源与文本审计** → 还原 → 构建（**0 error / 0 warning**）→ 模块包校验 → **资源包校验** → 许可审计 → 全部测试。

**资源、SDK / 独立编译 / release**：

```
.\tools\audit-resources.ps1                        # 源侧：种子包 / 主题清单 / 16 张文本表 / 可本地化层零中文 / {loc:Tr} 键 / 死键
.\tools\verify-resources.ps1                       # 产物侧：输出 resources\ 与仓库 resources\ 逐文件 + SHA256 一致
.\tools\pack-sdk.ps1                               # 生成 artifacts/sdk-packages（三个契约包）
.\tools\verify-standalone-module.ps1               # 示例模块只靠 PackageReference 构建 + 宿主加载
.\tools\verify-module-packages.ps1 -Configuration Release
.\tools\package-release.ps1 -OutputDirectory <仓库外空目录>
```

`package-release.ps1` 只导出程序层（程序文件 + `modules/` + `resources/`），并在导出后调用 `HYWDebugAssistant.App.exe --emit-default-settings` 生成出厂设置基线；`settings/` `workspace/` `logs/` `composites/` `modules/external/` 一律不导出。`release.json` 记录 `layoutVersion` 与 `resources[]`（全树相对路径 + SHA256）。

**加一种语言**（框架与模块各自加，互不牵连）：

1. 框架：在 `resources/i18n/<程序集简名>/` 下加 `<culture>.json`（如 `en.json`；长文案可拆 `<culture>.help.json` 分片，键不重复）。
2. 模块：在该模块的包内 `i18n/<程序集简名>/` 下加同名文件（源码里放 `src/Modules/.../i18n/<程序集简名>/`，构建会随包带走）。
3. 表格式与键集合必须与中文表**逐键一致**（占位符也要一致），否则审计判红；改完重编，或发布后直接替换发布目录里的表文件（文本是数据，不在 DLL 里）。
4. 语言下拉会自动出现新语言（`TextCatalog.SupportedLanguages` 扫表目录得出）；缺某语言的模块按回退链显示中文。

---

## 3. 写测试

| 项 | 规定 |
|---|---|
| 判据重心 | **模块级 + 组合级**；框架机制以**参考组合**（八个正式模块：四个基础模块 + 四个外部接入模块，加判据项目内测试桩）验证。**不做**框架级「MCU 式」单元测试 |
| 覆盖率 | **不设百分比**；判据 = 「机制改动必须有组合级验证」 |
| 命名 | `Method_Scenario_ExpectedResult` |
| 警告 | 测试项目同样 `TreatWarningsAsErrors`；豁免用 `NoWarn` + **在 csproj 注明理由** |
| 架构规则 | `tests/HYWDebugAssistant.ArchitectureTests`，**自研**（程序集元数据 + 源码扫描），**不引第三方架构测试框架**；**目标程序集缺失即失败**（禁止「无对象则跳过」） |
| 模块包边界 | `tools/verify-module-packages.ps1` + AR-24：8 个模块包只含模块自有文件；不得含 `Sdk*` / `Sdk.Ui*` / `Sdk.AccessKit*` / `Access*` / Avalonia / 外部接入第三方依赖；宿主输出必须提供这些程序集 |
| 数值 | **测试不重复声明面的数值**：断言的对象是「**声明被框架正确应用**」（声明 → 模型 / 渲染），不是「数值等于某个常量」。写法 = 读同一份声明的**原始字节**（包内 `.json` / schema）与框架产出的模型逐项比对；观感类数值（尺寸 / 颜色 / 字号 / 间距）不得成为测试的判据，改声明就该继续绿 |
| UI | 自动化只有两处：`tests/HYWDebugAssistant.VisualSnapshots`（真实渲染出 PNG，**必须独立进程**）与无头 UI 用例；其余由用户手工验证 |
| 基准 | `HYWDebugAssistant.Benchmarks` **不进常规门禁**（`build.ps1 -RunBenchmarks` 按需）；热路径改动要给出分配数据 |

**原则：简单快捷** —— 自动化只覆盖主要问题，不追求全覆盖。

---

## 4. 写文档

> **文档的作用是提供一条「快速了解这个项目」的通道，不是交付物，也不是开发日志。** 有了文档你每次开发仍要看代码 —— 所以文档只描述框架，不描述每一处实现。

1. **一个事实只写一处**（写第二遍必然分叉）。别处只留指针。
2. 三类归属：**是什么** → `architecture/CODEX.md` / `STYLE.md`；**正在做** → `TODO.md` / `ACCEPTANCE.md`；**怎么去做** → 本文。进度与缺口**只写在 `TODO.md`**。
3. **不写开发历程**：没有「以前是 X 现在是 Y」，没做成的尝试、逐轮反馈、已收尾的条目一律不进文档。要保留的历史材料搬 `docs/archive/`（头部标一行 `⚠ 历史材料` + 现行锚点），**搬走之后就当它不存在**。
4. **不把代码细节抄进文档**：不写类名、方法名、行号、字段名；正文只写「现在是什么样」。
5. 出现新的**根本性决定**时，写进 `architecture/CODEX.md` 的对应章节（决定 + 为什么）。被取代的旧表述**直接改写** —— 不留「作对照」的第二份。
6. 改完跑 `& .\docs\tools\audit-docs.ps1`，必须打印 `[OK]`。它查八件事：废止术语、引用可达、数值一致性（**只提示、不判负**）、结构完整、skills 去事实、表格完整、文件「有家」、正文无脆弱计数。
   - **改文档是唯一必须跑它的场景**；跑 T3（`build.ps1`）时自动跑，确实不涉及 `docs/` 可用 `-SkipDocAudit` 跳过。
   - 报 FAIL 时**先假设它是真的**（几乎总是「改了 A 忘了 B」）；**不得为了让门禁变绿而放宽/删掉规则**；确属误报才改脚本，并在此处记一行理由。
7. 一段内容**先定类、再定落点**；同一个事实只允许住在一个类里。跨类搬运 = **搬家 + 原处留指针**。
8. **不写具体数值**：尺寸 / 间距 / 字号 / 颜色 / 不透明度 / 容量 / 阈值 / 条目数 / 版本号**一律不进正文** —— 它们随开发常改、写在文档里必然过期，还会被当成交付标准与验收判据。文档写**机制与关系**：谁在何时用哪份数据、谁是唯一来源、谁是派生量、两件事的相对关系（例如写「容差是吸附距离的倍数」，而不是把那个像素值抄进正文），具体取值**指到唯一来源**（包内声明 / schema / 代码）。
   - 自检法：**把这个数字改掉，这句话还对不对？** 还对该留（如「至少一个」「第一个视图」这类序数与存在性），会错就删掉数字、改成指向来源。

**三个待定标记**（标了不等于可以不登记）：`【待裁】` = 语义/结构口径，**只有用户能裁** · `【待评估】` = 推迟到某个可判定时点再定 · `【待细化】` = 方向已定、拆分与顺序未定。**三者都必须登记到 [`TODO.md`](TODO.md) §3**，裁决/评估后立即清除。

---

## 5. 改设计

```
想清楚 → （影响面非平凡时）出方案给用户裁决 → 落地代码 → 同一个改动里回写文档 → 验证
```

- **用户是语义 / 命名 / UI 的唯一权威**；不自行发明平行方案。
- 违反架构约束需要豁免时：源码写 `// ARCH-EXCEPTION: <原因> · <复查条件>`，并登记到 [`architecture/EXCEPTIONS.md`](architecture/EXCEPTIONS.md)（**禁止静默绕过**；每个里程碑复查一次）。
- 新增依赖必须：许可 ∈ {MIT, Apache-2.0, BSD, ISC, Zlib…} · 说明不可替代性 · 记录 · 在 `Directory.Packages.props` **锁定版本**。禁止 GPL/LGPL、需采购或登录授权、**无许可声明**的包。

---

## 6. 环境与已知摩擦

| 症状 | 原因 | 处理 |
|---|---|---|
| `MSB4126` / `MSB3191`（`Platform` 被当成目标平台） | 环境变量 `Platform` 被其它软件污染 | 用 `dev.ps1` / `build.ps1`（会先清空）；必须手工跑 dotnet 时先 `Remove-Item Env:Platform -ErrorAction SilentlyContinue` |
| `MSB3026` / `MSB3027` 文件被锁 | VS 的 Avalonia 设计器宿主锁住输出 DLL | `dev.ps1` 默认先停掉它；手工构建加 `-StopDesignerHost` |
| 构建写 App 输出失败 | 应用正在运行 | 先 T0 编好改动项目，再 `-Mode Test -SkipAppRebuild`；必要时 `-StopApp`（丢现场） |
| 快照用例「一起跑红、单跑绿」 | 无头渲染平台配置是**进程级**的 | 测试项目**逐个串行**跑，`VisualSnapshots` **最后单独进程**跑 |
| 源码被改坏 | 用 PowerShell 对源码做字符串替换 | **禁止**；一律用编辑器 / `edit` 工具 |
| 测试全绿但其实没跑 | `-Filter` 自带 `FullyQualifiedName~` 前缀被拼了两层 | 只写片段（脚本已就地剥离前缀） |

其它依赖 Avalonia 12 / 本项目历史的坑：见 [`STYLE.md`](STYLE.md) §4。
