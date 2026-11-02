# HYWDebugAssistant · 文档

> 本目录是文档的**唯一入口**，全部文档只分三类：**当前代码是什么** · **正在做什么** · **怎么去做**。
> 读完本页，你就知道该翻哪一份。

---

## 1. 三类文档

### ① 当前代码是什么（描述性，基本不变）

| 文件 | 讲什么 | 什么时候看 |
|---|---|---|
| [`architecture/CODEX.md`](architecture/CODEX.md) | 这个系统现在的样子：分层与依赖、项目结构、运行期机制、网络语义、UI 外壳、叠加与视觉、设置与数据协议、测试与交付 | 要改任何非平凡东西之前 |
| [`STYLE.md`](STYLE.md) | 代码风格、架构铁律、热路径纪律，以及本项目踩过的坑（**先看这份再动手**） | 写代码时 |
| [`architecture/CODEX.md`](architecture/CODEX.md) §7.7 | **模块界面**：代码声明（窗口/端口/显示源/命令）+ 模块自己的 XAML + 控件库；没有 `view.json` | 写 / 改模块界面时 |

> **怎么写一个模块**：界面 = 代码声明 + 模块 XAML（键用 `{x:Static}`）；控件库只承载标准控件做不到的行为。
> 模块只引 `Sdk` / `Sdk.Ui` / `Sdk.AccessKit`；外部接入只声明标准标识，适配器/参数类型由框架提供，模块包不带宿主程序集。
> 独立编译示例见 `samples/ExternalModule/`：先 `tools/pack-sdk.ps1`，再 `tools/verify-standalone-module.ps1`。
> 模块开发工作流（新建模块的步骤、`module.json` 与生命周期约定）尚未定稿；当前状态见 [`TODO.md`](TODO.md) §3「模块开发工作流」，界面机制见 [`architecture/CODEX.md`](architecture/CODEX.md) §7.7。

### ② 正在做什么

| 文件 | 讲什么 |
|---|---|
| [`TODO.md`](TODO.md) | 当前在开发什么、进行到哪、还差什么（**状态的唯一来源**） |
| [`ACCEPTANCE.md`](ACCEPTANCE.md) | 手工验收清单（由用户本人执行） |

### ③ 怎么去做

| 文件 | 讲什么 |
|---|---|
| [`HOWTO.md`](HOWTO.md) | 工作流：分层验证 T0–T3、命令、写测试、写文档、改设计的流程 |

### 不在三类里（历史，不作为依据）

| 位置 | 内容 |
|---|---|
| [`archive/`](archive/) | ADR 原文、顶层设计原文、旧决策表、被取代的文档与清理清单 |
| [`architecture/EXCEPTIONS.md`](architecture/EXCEPTIONS.md) | **架构例外的唯一登记处**（`ARCH-EXCEPTION`），不属三类、按需查阅 |

**仓库根只放 `README.md`**。顶层设计原文与旧决策表已作为历史材料移入 `docs/archive/`，其现行结论全部并入 `architecture/CODEX.md` —— 文档一律以 `docs/` 为准。

---

## 2. 按任务找入口

| 我要做的事 | 先看 |
|---|---|
| 快速了解这个项目 | 本页 → `architecture/CODEX.md` §1 → §2 |
| 改界面观感 / 视觉数值 | `architecture/CODEX.md` §6、§7 |
| 动建网 / 数据面 / 运行期机制 | `architecture/CODEX.md` §4、§5（改完必须跑 T3） |
| 加设置项 | `architecture/CODEX.md` §8 |
| 修 bug | `TODO.md` → `STYLE.md` §4 的坑表 |
| 不知道该怎么验证 | `HOWTO.md` §1 |
| 门禁包含哪些审计（文档 / 颜色 / 资源 / 注释布局） | `HOWTO.md` §2 |
| 想知道能不能这么写 | `STYLE.md` §2 |

---

## 3. 维护规则

1. **一个事实只写一处**。写第二遍就等于让它分叉 —— 别处只留指针，不复制正文。
2. **代码变了就删掉文档里过时的那句**，不要留下"以前是 X，现在是 Y"。
3. **新的根本性决定**写进 `architecture/CODEX.md` 的对应章节（决定 + 为什么），不写流水账、不写已完成事项。
4. 架构约束确需豁免时：源码写 `// ARCH-EXCEPTION: <原因> · <复查条件>`，**并**登记到 [`architecture/EXCEPTIONS.md`](architecture/EXCEPTIONS.md)（禁止静默绕过）。
5. `docs/tools/audit-docs.ps1` 做机械校验（废止术语、断链接、取值唯一、文件归属…），**改完文档跑一次**，必须打印 `[OK]`。
