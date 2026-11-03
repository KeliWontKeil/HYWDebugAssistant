# 外部接入边界纠正与全 8 模块包瘦身（执行契约）

> 本文件是本次改动的**唯一任务契约**。实现子代理每个阶段先读本文件；所有关键约束以本文件为准，不依赖对话记忆。
> 目标：把外部接入的适配器/参数类型/catalog 所有权移回框架；模块只声明“使用哪个标准”并通过中性存取面收发；宿主提供契约/UI/外部接入实现；8 个模块包只含模块自有文件；旧组合件配置不迁移；最终通过架构、模块、运行时、包内容与完整门禁。

---

## 0. 基线状态与纪律

- 当前工作树有大量**已存在的未提交改动**（`git status` 可见）。实现子代理**不得** revert、checkout、stash、commit、amend 或清理这些改动。
- 只允许修改本文件“阶段文件白名单”列出的文件；需要越界时先停止并报告，由规划者更新本契约。
- 禁止用 PowerShell 对源码做字符串替换；使用编辑工具按文件一次改完。
- 不修改 `docs/` 正文（本文件除外）；代码注释可随代码改，但需在阶段报告中列出。
- 不运行完整 T3；阶段内只跑必要的 T0/T1/T2 并把命令与结果写进报告。最终 T3/Release 检查/文档审计由规划者执行。
- 不引入新依赖；不升级 Avalonia；不改变产品版本（`Directory.Build.props` 的 `Version` 保持 0.1.0）。

---

## 1. 结论与目标架构

### 1.1 现状问题

- `serial` / `tcp-client` / `tcp-server` / `udp` 的包各约 5.5 MB、42 个文件，含 Avalonia、`HYWDebugAssistant.Access`、`System.IO.Ports` + RID 分片、`Sdk`、`Sdk.Ui`；模块自身 DLL 约 20 KB。
- `src/Access` 被注释与构建脚本固化为“模块侧、随模块包”；四个模块直接引用它；`CopyLocalLockFileAssemblies=true` 把整个传递闭包放进包；`verify-module-packages.ps1` 强制要求 Access/Ports。
- 宿主当前不引用 `Access`，输出中也没有 Access/Ports；框架自身无法提供四个标准。
- `ModuleDependencyResolver` 全局登记所有包目录，可从包内加载宿主同名程序集；存在加载顺序依赖、跨模块隐式依赖、版本遮蔽。
- `frameworkCompatibility` 写死 1.0.0，程序集实际是 0.1.0；兼容检查没有真实约束。
- CODEX 把 Access 写成模块侧、包自带全部依赖；STYLE 仍写“模块只引 Sdk”；原始 ADR 的边界是 L6 只引 L1；`EXCEPTIONS.md` 未登记这次反转。

### 1.2 目标

| 角色 | 负责 |
|---|---|
| 模块 | 声明“接入名 + 标准标识 + 单元上限”；通过 `IExternalAccess` 请求开关、读写、读快照；不接触适配器、参数类型、`System.IO.Ports`、Avalonia |
| 框架 | 按标准标识解析 `ExternalAccessDescriptor`、创建适配器、登记参数类型、持有会话/队列/状态机、转 Issue |
| `App` | 唯一装配 `BuiltInExternalAccessCatalog` 的位置 |
| `src/Access` | **框架侧**四个标准适配器 + 参数类型 + catalog；只有 `App` 引用 |
| `Sdk.AccessKit` | 模块侧共用骨架（`ExternalAccessModule` / `AccessModuleSpec` / `RawBytesFraming`），只依赖 `Sdk`，由宿主随附 |
| 8 个模块包 | 只含模块自有 DLL、`module.json`、可选资源、模块私有第三方依赖 |

API 形态：

```csharp
public sealed record ExternalAccessDeclaration(string Name, string StandardId)
{
    public const int DefaultUnitBytes = 1472;
    public int MaxUnitBytes { get; init; } = DefaultUnitBytes;
}

public sealed record ExternalAccessDescriptor(
    string StandardId,
    string DisplayName,
    Type ParametersType,
    Func<IExternalAccessAdapter> CreateAdapter);

public interface IExternalAccessCatalog
{
    IReadOnlyList<ExternalAccessDescriptor> Standards { get; }
    bool TryResolve(string standardId, out ExternalAccessDescriptor descriptor);
}
```

- `Sdk` 新增 `ExternalAccessStandardIds`、`ExternalAccessFieldNames`。
- 标准标识字面值（阶段 A 已采用，后续不得改名）：`serial` / `tcp-client` / `tcp-server` / `udp`，与四个模块的 access name 一致。
- `IExternalAccess` 增加 `Type? GetParametersType(string accessName) => null;`，用于骨架保留“打开会话时禁用配置字段”的现有行为（ACCEPTANCE 80o）。
- 参数类型仍留在 `src/Access/Parameters`，FullName 保持 `HYWDebugAssistant.Access.Parameters.*`，组合件配置 JSON 不迁移。
- SDK 契约升 2.0.0（`Sdk` / `Sdk.Ui` / `Sdk.AccessKit` 的 `AssemblyVersion=2.0.0.0`）；8 个 `module.json` 的 `frameworkCompatibility` 改 `>=2.0.0 <3.0.0`。

---

## 2. 全 8 模块一致性矩阵

| 模块 | 外部接入 | 代码改动 | 包/清单改动 | 必须回归 |
|---|---|---|---|---|
| `text-sender` | 无 | 无逻辑改动；随 SDK 2.0 重编 | 去 `Sdk`/`Sdk.Ui`；manifest 2.0 | 契约、端到端、视图、包检查 |
| `hex-sender` | 无 | 无 | 同上 | 契约、端到端、视图、包检查 |
| `data-monitor` | 无 | 无；文件能力不受影响 | 同上 | 端到端、保存、视图、包检查 |
| `hex-string-converter` | 无 | 无 | 同上 | 契约、视图、包检查 |
| `serial` | 有 | 去 `Access` / `System.IO.Ports`；加 `Sdk.AccessKit`；`StandardId`；视图字段常量/设计数据 | 去 `Access`/`System.IO.Ports`/`Sdk`/`Sdk.Ui`/Avalonia | 契约、框架、真机、视图、包检查 |
| `tcp-client` | 有 | 同上（无 `System.IO.Ports`） | 同上 | 回环、视图、包检查 |
| `tcp-server` | 有 | 同上 | 同上 | 回环、视图、包检查 |
| `udp` | 有 | 同上 | 同上 | 回环、视图、包检查 |

统一要求：

- 8 个 `module.json` 的 `frameworkCompatibility` 全部为 `>=2.0.0 <3.0.0`。
- 8 个包只含模块自有文件；不含 `Sdk*` / `Sdk.Ui*` / `Sdk.AccessKit*` / `Access*` / Avalonia / `System.IO.Ports*`。
- 8 个模块 csproj 注释统一为“只引 `Sdk` / `Sdk.Ui` / `Sdk.AccessKit`；不引 `Access`、不直接引 UI 框架、不引其他模块”。
- 8 个模块全部重编、重新归拢、重新导出；旧包由加载器/兼容范围明确拒绝。

---

## 3. 执行方式（本阶段采用）

### 3.1 角色

| 角色 | 做什么 | 不做什么 |
|---|---|---|
| 规划者（主代理） | 评估、计划、本文件、审核代码、检查任务执行、最终测试、写 CODEX/STYLE/TODO/ACCEPTANCE/HOWTO | 不写产品代码、不改测试代码、不跑过程性 T0/T1/T2 |
| 实现子代理 | 按阶段做代码、项目文件、测试、脚本；跑自己阶段需要的 T0/T1/T2；报告结果 | 不改架构决策、不改文档正文、不跨阶段抢跑、不顺手改无关文件 |
| 用户 | 语义裁决、真机/回环验收（80j–80n）、最终接受 | — |

### 3.2 阶段门

每阶段结束，子代理提交固定格式报告：

1. 阶段；
2. 改动文件；
3. 完成项；
4. 运行过的命令与结果；
5. 偏离计划之处（必须说明原因）；
6. 未决问题/风险。

规划者只读审核：`git diff --stat`、关键 diff、`rg` 硬不变量、报告中的测试结果；给出 PASS 或 RETURN + 编号问题。PASS 才发下一阶段。RETURN 时把问题写成可执行清单交回子代理；规划者不直接改代码，也不在该阶段跑测试。

### 3.3 防漂移

- 子代理上下文接近压缩、报告与计划不符、或出现“顺手改”时，立即停止。
- 新开一个实现子代理，从本文件 + 当前仓库状态恢复；关键约束不留在被压缩的对话里。
- 每阶段结束把状态写回本文件末尾的“执行记录”；下一阶段启动先读文件。

---

## 4. 阶段 A：SDK 契约与模块侧工具包

### 4.1 任务

1. 新建 `src/Sdk.AccessKit/HYWDebugAssistant.Sdk.AccessKit.csproj`，只引用 `Sdk`。
2. 从 `src/Access` 移入并改命名空间到 `HYWDebugAssistant.Sdk.AccessKit`：
   - `ExternalAccessModule.cs`
   - `AccessModuleSpec.cs`（新增 `StandardId`）
   - `RawBytesFraming.cs`
3. 加入 `HYWDebugAssistant.slnx`。
4. `Sdk`：
   - `ExternalAccessDeclaration` 改为 `(Name, StandardId)`，删除 `Adapter` / `ParametersType`；
   - 新增 `ExternalAccessStandardIds`、`ExternalAccessFieldNames`、`ExternalAccessDescriptor`、`IExternalAccessCatalog`；
   - `IExternalAccess` 增加 `GetParametersType` 默认实现；
   - 保留 `IExternalAccessAdapter` / `IExternalAccessReady`。
5. `Sdk` / `Sdk.Ui` / `Sdk.AccessKit` 的 `AssemblyVersion` 设为 `2.0.0.0`。
6. `FrameworkCompatibility.CurrentFrameworkVersion` 从 `Sdk` 程序集版本派生。
7. 8 个 `module.json` 的 `frameworkCompatibility` 改为 `>=2.0.0 <3.0.0`。
8. 4 个外部模块：
   - csproj 删除 `Access`、`System.IO.Ports`，增加 `Sdk.AccessKit`，删除 `CopyLocalLockFileAssemblies`；
   - 模块类删除 `CreateAdapter`；`AccessModuleSpec` 增加 `StandardId`；
   - `*ViewKeys.cs` 字段名改用 `ExternalAccessFieldNames.*`；
   - `*View.cs` 删除 `using HYWDebugAssistant.Access.Parameters`；`[ModuleViewDesignData]` 改标准标识重载；
   - XAML 不改。
9. 4 个基础模块：逻辑/视图不改；csproj 注释统一；确认只引 `Sdk` / `Sdk.Ui`；随 SDK 2.0 重编。
10. 8 个模块 csproj 全部确认：无 `Access`、无直接 Avalonia 包引用、无模块互引。

### 4.2 文件白名单

- `HYWDebugAssistant.slnx`
- `src/Sdk/**`
- `src/Sdk.Ui/**`（仅版本/设计数据属性所需）
- `src/Sdk.AccessKit/**`（新建）
- `src/Access/ExternalAccessModule.cs`、`src/Access/AccessModuleSpec.cs`、`src/Access/RawBytesFraming.cs`（移出/删除）
- `src/Modules/Business/**` 的 `*.csproj`、`*.cs`、`Views/*.cs`、`module.json`
- `src/Hosting/Installation/FrameworkCompatibility.cs`
- 必要的测试 csproj 引用调整（若编译需要）

### 4.3 阶段 A 审核硬不变量

- `src/Modules` 中不存在 `using HYWDebugAssistant.Access` / `ProjectReference ...Access`。
- 8 个模块只引用 `Sdk` / `Sdk.Ui` / `Sdk.AccessKit`。
- `ExternalAccessDeclaration` 不再携带适配器。
- `Sdk.AccessKit` 能独立编译，零第三方依赖。
- 子代理报告 T0 编译通过（至少受影响项目）。

---

## 5. 阶段 B：框架接回所有权并接线

### 5.1 任务

1. `src/Access` 转为框架侧：保留 `Adapters/`、`Parameters/`；删除已移出的骨架/framing；新增 `BuiltInExternalAccessCatalog`（四个 descriptor，`CreateAdapter` 每次 new）；适配器中的 `RawBytesFraming.Capacity` 改为 `ExternalAccessDeclaration.DefaultUnitBytes`；csproj 注释改为“框架侧，只有组合根引用”。
2. `Hosting`：
   - `ModuleInstanceFactory.Create` 增加可选 `IExternalAccessCatalog? catalog`；
   - `BuildExternalAccesses` 用 catalog 解析标准、登记参数类型；未知标准 ⇒ Error 并忽略；
   - `ModuleContext.DeclareExternalAccess` 删除按 adapter 登记配置的分支。
3. `DataPlane`：
   - `ExternalAccessHost` 构造增加可选 catalog；`TryActivate` 解析 descriptor、创建 adapter、注入会话；
   - `ExternalAccessSession` 持有 `_adapter` + `_parametersType`，替换 `_declaration.Adapter` / `ParametersType` 用法；
   - 新增 `GetParametersType(instanceId, accessName)`。
4. `Core`：
   - `ModuleRuntime` 构造增加可选 catalog，传给 `ExternalAccessHost` 与 `ModuleInstanceFactory`；
   - `AttachExternalAccess` / `ResolveConfigurationType` 通过 catalog 解析参数类型；
   - `RuntimeExternalAccess` 增加 `GetParametersType` 委托。
5. `App`：
   - `CompositionRoot` 创建 `BuiltInExternalAccessCatalog`，注入 `ModuleRuntime`；
   - `App.csproj` 增加 `Access` 与 `Sdk.AccessKit` 项目引用（后者用于随宿主提供程序集）。
6. `Sdk.Ui` / 预览：
   - `ModuleViewDesignDataAttribute` 增加 `string standardId` 构造；
   - `ModuleView` 优先 `ConfigType`，否则查标准注册表；
   - `tools/PreviewHost` 增加 `Access` 引用；`PreviewApp.Initialize` 注册标准 ID → 配置类型。
7. `Sdk.Testing`：
   - `ModuleContract.Probe` 增加可选 catalog：有 catalog 校验标准/参数类型；无 catalog 只做结构校验；
   - `TestExternalAccess` 增加 `GetParametersType`。

### 5.2 文件白名单

- `src/Access/**`
- `src/Hosting/**`
- `src/DataPlane/**`
- `src/Core/**`
- `src/App/**`
- `src/Sdk.Ui/**`
- `src/Sdk.Testing/**`
- `tools/PreviewHost/**`
- 相关测试（外部接入运行期/合同/预览）与测试 csproj

### 5.3 阶段 B 审核硬不变量

- `App` 构造 catalog；`Core` / `Hosting` / `DataPlane` / `Core.Api` / `Ui` / `FrameworkUi` 不引用 `Access`。
- 串口配置类型 FullName 仍为 `HYWDebugAssistant.Access.Parameters.SerialConfig`。
- 外部接入运行期用例覆盖：打开/关闭/读写/失败不自动重试/禁用启用/移除/参数变更。
- 预览宿主能给出 4 个外部视图的配置字段。
- 子代理报告 T1/T2（受影响项目）通过。

---

## 6. 阶段 C：包内容、加载器与全 8 模块门禁

### 6.1 任务

1. `src/Modules/Directory.Build.targets`：
   - `ModulePackageRoot` 改为 `artifacts\modules\$(Configuration)\`；
   - `StageModulePackage` 排除 `Sdk*`、`Sdk.Ui*`、`Sdk.AccessKit*`、`Access*`、`Avalonia*`、`System.IO.Ports*`、`SkiaSharp*`、`HarfBuzzSharp*`、`MicroCom.Runtime*`、`*.deps.json`；
   - 保留模块自有 DLL、`module.json`、`preview.png`、`assets/**`、模块私有依赖；
   - 更新 `StageRuntimeSpecificAssets` 注释为“模块私有平台分片”。
2. `App` 的镜像 target 改为读取 `artifacts\modules\$(Configuration)\`。
3. `verify-module-packages.ps1`：
   - `$packageRoot` 改配置相关；
   - 每个包必须有 `module.json`、`entryAssembly`、非空 `frameworkCompatibility`；
   - 包内禁止宿主程序集（名单同 6.1.1）；
   - `artifacts\modules\<Configuration>` 与 `src/App/bin/<Configuration>/net10.0/modules` 的模块 ID 集合必须与 `src/Modules/**/module.json` 期望集合一致；
   - 宿主输出必须包含 `Sdk`、`Sdk.Ui`、`Sdk.AccessKit`、`Access`、`System.IO.Ports`；
   - 打印每个包文件数与大小（只记录，不设阈值）。
4. `ModuleDependencyResolver`：
   - 收集 `AppContext.BaseDirectory` DLL 名作为“宿主已提供”集合；
   - `Resolve` 命中 `HYWDebugAssistant.*` 或宿主集合时返回 null，绝不从包目录加载宿主程序集。
5. `ModulePackageLoader`：
   - 入口程序集引用 `HYWDebugAssistant.Access` ⇒ 拒绝并提示“旧包引用了模块侧 Access，请用 SDK 2.0 重编”；
   - 模块引用的 `Sdk` / `Sdk.Ui` / `Sdk.AccessKit` 版本高于宿主 ⇒ 拒绝并给出具体版本。
6. 架构测试：
   - `AR-0` 加入 `Sdk.AccessKit`；`Access` 归类为框架扩展；
   - `AR-1` 模块只允许 `Sdk` / `Sdk.Ui` / `Sdk.AccessKit`，禁止 `Access`；
   - `AR-3` / `AR-12`：`Access` 只允许被 `App` 引用；
   - 新增 `AR-24`：全 8 模块包不得含宿主提供程序集。
7. 测试更新：
   - `BusinessModulesContractTests`：8 个模块同一 Probe；4 个外部模块传 `BuiltInExternalAccessCatalog`；
   - `BusinessModulesIntegrationTests`：`artifacts/modules` 改配置相关；所有 `ModuleRuntime` 传 catalog；把“`System.IO.Ports` 来自模块包”的断言改为“由宿主提供”；
   - `HostingPackageTests`：TextSender/Serial/TcpServer/Udp 真实包测试传 catalog；断言配置类型 FullName 不变；
   - `ModuleViewContractTests`：8 个视图继续逐键对账；显式引用 `Access` 取参数类型；
   - `ModuleViewSmokeTests`：设计时用例先注册标准 ID → 配置类型映射；
   - `ModuleSourceDiscoveryTests` / `HostingLifecycleTests` 临时清单兼容范围更新到 2.0；
   - 其余 `new ModuleRuntime()` 保持可选参数兼容。

### 6.2 文件白名单

- `src/Modules/Directory.Build.targets`
- `src/App/HYWDebugAssistant.App.csproj`
- `tools/verify-module-packages.ps1`
- `src/Hosting/Packages/**`
- `src/Modules/Business/**/module.json`
- `tests/HYWDebugAssistant.ArchitectureTests/**`
- `tests/HYWDebugAssistant.Tests/**`（包/集成/外部接入/发现/生命周期）
- `tests/HYWDebugAssistant.Modules.Tests/**`
- `tests/HYWDebugAssistant.VisualSnapshots/**`
- 相关测试 csproj

### 6.3 阶段 C 审核硬不变量

- 8 个包全部变薄，ID 集合与源码一致；
- 包内无宿主程序集；
- 旧包被明确拒绝；
- AR-1/3/12/24 覆盖；
- 基础四模块端到端与视图测试不回归；
- 子代理报告受影响 T2 通过。

---

## 7. 阶段 D：独立编译与 release 形态

### 7.1 任务

1. 为 `Sdk` / `Sdk.Ui` / `Sdk.AccessKit` 增加 `dotnet pack` 支持。
2. 新增 `tools/pack-sdk.ps1` 生成本地 NuGet 源。
3. 新增 `samples/ExternalModule/`：只通过 `PackageReference` 引用三个 SDK 包，不引用仓库内工程。
4. 新增 `tools/verify-standalone-module.ps1`：打包 → 临时目录构建示例模块 → 生成模块包 → 宿主测试夹具加载。
5. 新增/扩展 `tools/package-release.ps1`：Release 构建 → 复制 App 输出与 `artifacts/modules/Release` → 写 `release.json`（产品版本、SDK 契约版本、模块 ID/版本/hash）→ 复核包内无宿主程序集。

### 7.2 文件白名单

- `src/Sdk/**`、`src/Sdk.Ui/**`、`src/Sdk.AccessKit/**`（仅打包元数据）
- `samples/**`
- `tools/pack-sdk.ps1`、`tools/verify-standalone-module.ps1`、`tools/package-release.ps1`

### 7.3 阶段 D 审核硬不变量

- 示例模块不引用仓库内工程即可编译；
- 生成的模块包可被宿主加载；
- release 导出目录内没有“包内宿主程序集”。

---

## 8. 阶段 E：最终验证与文档（规划者执行）

- Debug T3：`.\build.ps1 -StopDesignerHost`（需关闭应用）。
- Release 构建 + `tools/verify-module-packages.ps1 -Configuration Release`。
- `tools/pack-sdk.ps1` + `tools/verify-standalone-module.ps1`。
- `docs/tools/audit-docs.ps1`。
- 用户执行 80j–80n 真机/回环。
- 文档回写：
  - `docs/architecture/CODEX.md`：分层与项目表、包形态、外部接入、SDK 兼容、AR-1/3/12/24、交付形态；
  - `docs/STYLE.md`：模块只引 `Sdk` / `Sdk.Ui` / `Sdk.AccessKit`，不引 `Access`、不直接引 Avalonia；
  - `docs/HOWTO.md`：AR 范围到 AR-24，补“包边界”验证；
  - `docs/TODO.md`：记录本次所有权纠正与 8 模块状态；
  - `docs/ACCEPTANCE.md`：增加 release 包内容自动检查项。
- 删除或归档本文件，结论并入 CODEX。

---

## 9. 公共 API、数据与持久化变化

| 项 | 变化 | 兼容性 |
|---|---|---|
| `ExternalAccessDeclaration` | `(Name, StandardId)`；删除 adapter/参数类型 | 破坏性；所有模块包重编 |
| `IExternalAccess` | 新增 `GetParametersType` 默认实现 | 只增 |
| `IExternalAccessCatalog` / `ExternalAccessDescriptor` | 新增 | 只增 |
| `ExternalAccessStandardIds` / `ExternalAccessFieldNames` | 新增 | 只增 |
| `ModuleContract.Probe` | 增加可选 catalog | 源兼容 |
| `ModuleRuntime` / `ExternalAccessHost` 构造 | 增加可选 catalog | 源兼容 |
| `module.json` | `frameworkCompatibility` 升 2.0；无新字段 | 旧包被拒绝 |
| 组合件配置 JSON | 不变：`HYWDebugAssistant.Access.Parameters.*` FullName 保持 | 旧组合件无损 |
| 外部接入数据路径 | 不变：模块 ↔ `IExternalAccess` ↔ 会话 ↔ 适配器 | 行为保持一致 |
| 8 个模块包内容 | 只含模块自有文件 | 旧包不再兼容 |

---

## 10. 失败模式与边界

- 未知标准标识 / 缺 catalog：实例创建时 Error，忽略该接入，模块以未接线状态继续运行。
- 适配器工厂抛异常：`TryActivate` 捕获，Error Issue，实例继续运行。
- 旧包引用 `Access`：加载器拒绝，提示用 SDK 2.0 重编。
- 旧包 `frameworkCompatibility` 不满足：安装器拒绝，不静默降级。
- 包内仍带宿主程序集：加载器忽略包内副本，包门禁拒绝发布。
- 宿主缺 `Sdk.AccessKit` / `Access` / `System.IO.Ports`：门禁失败；运行期给出缺少宿主程序集的明确错误。
- 旧组合件：配置类型 FullName 未变，正常加载。
- 配置字段启用/禁用：通过 `GetParametersType` 保留 80o 行为。
- 多接入/同标准多实例：每会话由 catalog 创建独立适配器。
- Debug/Release 混包：配置相关包根 + 期望 ID 集合检查。
- 不做：不引入每模块 ALC（会破坏与宿主 `Sdk` / `Sdk.Ui` 的类型同一性）；不做进程级沙箱。

---

## 11. 验收标准

1. 8 个模块包都不含 `Sdk*`、`Sdk.Ui*`、`Sdk.AccessKit*`、`Access*`、Avalonia、`System.IO.Ports*`；四个外部包降到与基础模块同档。
2. 8 个模块 csproj 只引 `Sdk` / `Sdk.Ui` / `Sdk.AccessKit`；`src/Modules` 无 `Access` 引用。
3. `App` 输出包含 `Sdk`、`Sdk.Ui`、`Sdk.AccessKit`、`Access`、`System.IO.Ports`；`CompositionRoot` 注入 catalog。
4. 四个外部模块在没有任何模块包提供适配器时仍可打开/关闭/收发；未知标准/缺 catalog 行为符合第 10 节。
5. 四个基础模块端到端链路、显示器保存、转换器、文本/HEX 发送不回归。
6. 旧组合件串口/TCP/UDP 参数重启后恢复；配置字段只在关闭/故障时可编辑。
7. `verify-module-packages.ps1 -Configuration Release` 通过；导出目录无宿主程序集；8 个模块 ID 集合与源码一致。
8. 旧版模块包被明确拒绝（引用 Access 或兼容范围不满足）。
9. 架构测试 AR-0/1/3/12/24 全绿；8 个视图契约、8 个模块契约、外部接入运行期/适配器测试全绿。
10. 独立编译验证通过：示例模块只靠 SDK NuGet 包构建并被宿主加载。
11. Debug T3 全绿；Release 包检查通过；文档审计 `[OK]`。
12. 用户完成 80j–80r 手工验收，无回归。

---

## 12. 执行记录（规划者维护）

| 阶段 | 状态 | 子代理报告摘要 | 审核结论 | 备注 |
|---|---|---|---|---|
| A | **PASS（带后续项）** | 新增 `Sdk.AccessKit`；`ExternalAccessDeclaration(Name, StandardId)`；新增标准/字段/catalog 契约；8 模块 csproj/manifest 统一；4 外部模块去 Access/Ports；SDK 版本 2.0.0；T0：Sdk/Sdk.Ui/Sdk.Testing/8 模块全绿 | 通过；标准标识 `serial/tcp-client/tcp-server/udp` 采用。遗留：`src/Access`/Hosting/Core/DataPlane/App 在 B 前故意为红；`GetParametersType` 宿主实现、`TestExternalAccess`、catalog 校验、标准→配置类型映射留 B；`ExternalAccessStandardIds` 注释里“旧组合件的声明不迁移”措辞需在 B 修正（声明不在组合件里） | 包瘦身/C 仍待做；A 不改 docs 正文 |
| B | **PASS（带后续项）** | 新增 `BuiltInExternalAccessCatalog`；Hosting/DataPlane/Core/App 接线；`GetParametersType` 全链路；`ModuleView` 标准→配置类型注册表；PreviewHost 注册；`Sdk.Testing` catalog 校验；Fake catalog/测试适配；完整解决方案 0 警告 0 错误；T1：ExternalAccessRuntimeTests 9/9、BusinessModulesContractTests 9/9、ExternalAccessAdapterTests 15/15、设计时 4/4 | 通过；`ModuleView.RegisterStandardConfig/TryGetStandardConfig` API 采纳；`DataPlane` 未知标准整实例失败作为防御路径采纳（Hosting 先过滤）。**C 必须补**：`ExternalAccessHost.TryActivate` 部分失败时清理已创建会话/适配器（避免泄漏）；catalog 一致性测试（descriptor.ParametersType 与 adapter.ParametersType 一致、字段名常量与参数类型属性一致）；`ModuleView` 静态注册表测试隔离/清理按需处理；完整 T2 的 catalog/包根/2.0 清单更新 | 旧 1.0 测试与包路径仍红，C 处理；`Sdk.Testing` 不自动回填参数类型 |
| C | **PASS（带交付注记）** | 包根按配置；8 包瘦身到 3 文件（34–54 KB）；`verify-module-packages.ps1` 对账包 ID/禁宿主件/宿主输出；`ModuleDependencyResolver` 宿主集合拦截；`ModulePackageLoader` 拒绝旧 Access 引用与高版本 SDK；AR-0/1/3/12/24；集成/包/发现/生命周期/视图测试更新；B 审核项（激活失败清理、catalog 一致性、测试夹具）完成。Debug 构建 0 警告 0 错误；Modules.Tests 72/72、Tests 328/328、ArchitectureTests 19/19、视图 8/8+4/4、包检查通过 | 通过。**交付注记**：`src/Hosting/Packages/*` 原先被 `.gitignore` 误伤；已加窄范围反忽略，四个源码文件（含 `ModulePackage.cs`）现为**未跟踪**，提交时需 `git add`；子代理未 add/commit。`Tests` 一次 `CompositePageViewModelTests` 偶发失败、单跑/后续全量通过，疑似既有竞态，T3 观察。Release 包根/完整 VisualSnapshots 留 E | 未跑完整 T3/Release；SDK 打包/示例模块/release 脚本留 D |
| D | **PASS（带交付注记）** | 三个 SDK 包装配/打包 2.0.0；`pack-sdk.ps1`；独立 `samples/ExternalModule` 只走 PackageReference；`StandaloneModuleHost` + `verify-standalone-module.ps1` 独立加载通过；`package-release.ps1` Release 构建/包门禁/复制/`release.json` 通过，护栏拒绝仓库内目标；夹具项目移出 `tests/` 到 `tools/`，`build.ps1` 不再误发现 | 通过。独立编译证明 = 示例模块只靠三个 nupkg 构建并被宿主按 catalog 装入。**交付注记**：`samples/`、`tools/StandaloneModuleHost/`、三个脚本、`tools/HYWDebugAssistant.Tests.ModuleFixtures/`、`src/Hosting/Packages/*` 均为**未跟踪**，提交时需 `git add`；脚本未 commit。Release 脚本无 `generatedAt`（可复现） | 完整 T3/VisualSnapshots/Release 复验留 E；`release.json` 时间戳按需 |
| E | **PASS** | 完整 Debug T3：构建 0 警告 0 错误、8 包校验、97 包许可、Architecture 19、Modules 72、Tests 328、Reference 45、VisualSnapshots 47（合计 511）、文档审计 `[OK]`。独立验证：`pack-sdk.ps1` 三包 2.0.0、`verify-standalone-module.ps1` 示例只靠 PackageReference 构建并被宿主加载。Release：`package-release.ps1` 构建 0/0、Release 包检查 8/8、导出含 modules/runtimes/framework、`release.json` product=0.1.0 sdk=2.0.0 modules=8，modules 内无宿主程序集 | 通过。最终 T3 曾暴露两个非本次架构回归的阻塞，均已最小修复并复验：① 第一方 SDK 包缺 MIT 许可元数据 → 补 `PackageLicenseExpression` 并刷新缓存；② 无头测试加载组合封面触发 `IPlatformRenderInterface` → `PreviewImageStore` 对 `InvalidOperationException` 兜底返回 null。**仍需用户执行 80j–80n 真机/回环手工验收**；`src/Hosting/Packages/*`、`samples/`、`tools/StandaloneModuleHost`、`tools/HYWDebugAssistant.Tests.ModuleFixtures`、脚本等新增/未跟踪文件需 `git add`（未 commit） | 计划完成；无代码级阻塞 |
