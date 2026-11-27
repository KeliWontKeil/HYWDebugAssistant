# 项目与包布局

一个模块 = 一个类库工程 + 一个 `module.json` + 可选 `resources/`。本页是这些文件与目录的唯一清单；构建与导入步骤见[从零开始](quickstart.md)与[发布与装载](publishing.md)。

## 最小文件集

```
MyModule/
  HYWDebugAssistant.Vendor.MyModule.csproj
  module.json
  MyModule.cs
  NuGet.config               # 指向 SDK 的 packages/（模板里已写好）
  Views/                     # 可选：模块 XAML 与视图工厂
  resources/                 # 可选：文本表 / 图标 / 自定义资源
```

推荐直接复制 SDK 目录里的 `template/ModuleTemplate`：工程文件、清单、`NuGet.config` 与打包配置都已就位，只需改三个名字（见[从零开始](quickstart.md)）。模板在 SDK 目录内时本地源是 `../../packages`；把模板移出 SDK 目录后要改本地源。从零手写时按下面照抄。

## 工程文件

外部工程只引作者包；版本取 SDK `packages/` 里包文件名中的四段版本（模板里已经写好，不需要手填）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <!-- 外部工程不使用仓库的中央包管理，版本在这里显式给出。 -->
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <!-- 固定输出平台名：机器上被别的软件设置的 Platform 环境变量不应把产物目录带歪。 -->
    <PlatformName>AnyCPU</PlatformName>
    <AssemblyName>HYWDebugAssistant.Vendor.MyModule</AssemblyName>
    <RootNamespace>HYWDebugAssistant.Vendor.MyModule</RootNamespace>
    <!-- 包目录名；缺省取项目名最后一个点段。 -->
    <ModulePackageName>MyModule</ModulePackageName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="HYWDebugAssistant.Sdk" Version="<当前版本>" />
    <!-- 有界面时加 Sdk.Ui；用外部接入时加 Sdk.AccessKit；写测试时加 Sdk.Testing。 -->
    <PackageReference Include="HYWDebugAssistant.Module.Build" Version="<当前版本>" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- 文本表随构建输出：预览器与行为测试按“程序集旁”的 resources 读取。 -->
    <Content Include="resources\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`Module.Build` 只参与构建（打包规则），不是运行时依赖，也不进模块包。

依赖边界：

- 可以引 `Sdk`、`Sdk.Ui`（有界面时）、`Sdk.AccessKit`（用设备接入时）、以及模块私有的第三方包；
- 不得引 `Sdk.Testing`（只给测试项目，运行时引用会被加载器拒绝）；
- 不得引本应用其它程序集（`Access`、`Core`、`Ui` 等），也不得引其它模块；
- 私有第三方依赖会随包分发，宿主提供的程序集不会。

## NuGet.config

本地源指向 SDK 目录里的 `packages/`（模板里写的是相对路径，移动模板后按实际位置调整）：

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="hyw-sdk" value="..\..\packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

只复制模板而不保留 SDK 目录时，把 `hyw-sdk` 改成 SDK `packages/` 的绝对路径。

## module.json

清单只有五个字段：

| 字段 | 必填 | 含义 |
|---|---|---|
| `id` | 是 | 模块标识 `作者/模块名`：两段均为 `[a-z0-9-]`，不能为空、不能以 `-` 开头或结尾；必须与代码里的 `[ModuleDefinition]` 一致 |
| `entryAssembly` | 是 | 实现程序集文件名（相对包目录） |
| `entryType` | 是 | 入口类型全名（含命名空间） |
| `dependsOn` | 否 | 依赖的模块标识列表；缺失即拒绝加载 |
| `resources` | 否 | 加载期必须存在的资源目录（相对包目录）；目录不存在、绝对路径或 `..` 即拒绝加载。它是“目录存在性清单”，不是资源枚举：`i18n` / `icons` 按目录约定读取，自定义 kind 由模块自己读 |

```json
{
  "id": "vendor/my-module",
  "entryAssembly": "HYWDebugAssistant.Vendor.MyModule.dll",
  "entryType": "HYWDebugAssistant.Vendor.MyModule.MyModule",
  "dependsOn": [],
  "resources": ["resources/i18n"]
}
```

- JSON 允许注释与尾随逗号，属性名不区分大小写；空文件或语法错误会让导入失败。
- `resources` 只登记实际存在的目录；声明了却不存在会在加载期被拒（不等到界面缺图标才发现）。
- 清单与代码的标识必须一致，否则加载器拒绝加载，避免同名歧义。
- `dependsOn` 只保证“依赖的模块已安装”，不提供代码引用；模块之间仍只经端口通信。

## 包长什么样

`dotnet build -c Release` 之后，打包规则把模块包归拢到：

```
bin/module-package/Release/<模块名>/
```

包内**只应**有：

- `module.json`
- 实现程序集（可含 `.pdb`）
- `resources/**`（若项目里有）
- `views/**/*.axaml`：端口声明源（Hosting 从同一份 XAML 解析端口；不是资源包）
- 可选的 `preview.png`：模块清单的图块封面（按 9:5 裁切；不是"模块窗口截图"这个概念）
- 模块私有第三方依赖（含 `runtimes/<rid>/lib|native/…` 平台分片）

包内**不得**有宿主提供的程序集：`HYWDebugAssistant.Sdk*`、`HYWDebugAssistant.Access*`、`Avalonia*`、`System.IO.Ports*`、`SkiaSharp*`、`HarfBuzzSharp*`、`MicroCom.Runtime*`，也不得有 `*.deps.json`。打包规则会自动排除；手工打包时按这份清单剔除。

每次构建会先清空包目录再复制；改过 `module.json` 也会触发重新归拢（它被登记为过期判定输入）。

`preview.png` 放在**工程根目录**即可进包，不需要改项目文件——打包规则直接从工程目录取这个文件，
与它有没有被复制进输出目录无关：

```
<模块工程>/preview.png   →   包根 preview.png
```

清单页拿它作图块封面（按 9:5 居中裁切）。缺图不是错：界面显示占位字形，其余一切照常。
必须是真 PNG、体积不超过 1 MiB，`tools/verify-module-packages.ps1` 会校验（扩展名改成 `.png` 不算）。

封面**只属于模块自己的包**：它不是 `Content`/`None` 传递项，经 `ProjectReference` 也不会到下游程序集。
所以不要把 `<None Update="preview.png" CopyToOutputDirectory="PreserveNewest" />` 当成"让封面生效"的手段——
那一条只影响输出目录副本，对包没有任何作用。

换封面不必重启应用：包的"内容已变化、需重启"判定**不看**封面（它不承载代码）。

## 资源布局

```
resources/
  i18n/<程序集简名>/<culture>.json
  icons/<程序集简名>/icons.json + *.svg
  <自定义 kind>/...
```

`i18n` 与 `icons` 是框架认识的两种类别，由框架按拥有者读取；其他 `kind` 由模块用 `IResourceAccess` 自己读。细节见[资源与文本](resources.md)。

## 发现与装载

- 应用只扫描程序目录下的 `modules/`（内置）与 `modules/external/`（外部导入），每个根只扫直接子目录，一个子目录一个包。
- 导入外部包时，框架先校验清单、入口、依赖与同标识冲突，通过后复制进 `modules/external/`；同标识已存在时拒绝导入，不覆盖、不并存。
- 包目录名撞名时**问用户**（替换 / 跳过），不会自动改成 `名字-2`；若那个目录正被已加载的包占用，则直接失败——替换等于删掉正在用的包。目录名与模块标识无关，改目录名不会改标识。
- 已载入的程序集不可替换：改过代码的包要关闭应用重开后生效，不提供热重载。
- 内置模块与外部模块走同一条发现与加载路径，外部包没有结构特权。

## SDK 目录长什么样

SDK 是独立发布目录，**不在产品目录里**：

```
HYWDebugAssistant.Sdk-<版本>/
  packages/                          # 五个 nupkg
  template/ModuleTemplate/           # 模块模板（源码，含自己的 NuGet.config）
  LICENSE.txt
  THIRD_PARTY_NOTICES.md
```

保持整个目录在一起即可开发；模板单独复制时要改本地源。`Sdk.Testing` / `Module.Build` 只给作者的构建与测试使用。

## 进构建序

- 外部工程：不需要登记，`dotnet build` 即可产出模块包。
- 本仓库内的模块：项目目录里放 `module.json` 即被识别；加入解决方案后构建会自动归拢到 `artifacts/modules/<配置>/`。这是仓库开发的事，见[项目开发](../project/index.md)。

## 相关

- [从零开始](quickstart.md)：把这些文件变成能跑的模块
- [发布与装载](publishing.md)：导入、重启与被拒原因
- [SDK 与版本](sdk-version.md)：五个包与模板
