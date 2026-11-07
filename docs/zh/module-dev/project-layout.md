# 项目与包布局

## 最小文件集

```
MyModule/
  HYWDebugAssistant.Vendor.MyModule.csproj
  module.json
  MyModule.cs
  NuGet.config               # 指向产品 sdk/（样例里已写好）
  Views/                     # 可选：模块 XAML 与视图工厂
  resources/                 # 可选：文本表 / 图标 / 自定义资源
```

## 从样例开始

推荐直接复制 `sdk/samples/` 里的样例：工程文件、清单、`NuGet.config` 与打包配置都已就位，只需改三个名字（见[从零开始](quickstart.md)）。从零手写时按下面两段照抄。

## 项目文件

外部工程只引作者包；版本取 `sdk/` 里包文件名中的四段版本（样例里已经写好，不需要手填）：

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
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="HYWDebugAssistant.Sdk" Version="<产品四段版本>" />
    <!-- 有界面时加 Sdk.Ui；用外部接入时加 Sdk.AccessKit；写测试时加 Sdk.Testing。 -->
    <PackageReference Include="HYWDebugAssistant.Module.Build" Version="<产品四段版本>" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- 文本表随构建输出：预览器与测试都按“程序集旁”的 resources 读取。 -->
    <Content Include="resources\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`Module.Build` 只参与构建（打包规则），不是运行时依赖，也不进模块包。

## NuGet.config

本地源指向产品发布目录里的 `sdk/`（样例里写的是相对路径，复制到别处后要按实际位置调整）：

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="hyw-sdk" value="..\..\sdk" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

## module.json

| 字段 | 必填 | 含义 |
|---|---|---|
| `id` | 是 | 模块标识，`作者/模块名` 两段小写；必须与代码里的 `[ModuleDefinition]` 一致 |
| `entryAssembly` | 是 | 实现程序集文件名（相对包目录） |
| `entryType` | 是 | 入口类型全名（含命名空间） |
| `dependsOn` | 否 | 依赖的模块标识列表；缺失即拒绝加载 |
| `resources` | 否 | 资源目录（相对包目录）；目录不存在即拒绝加载 |

```json
{
  "id": "vendor/my-module",
  "entryAssembly": "HYWDebugAssistant.Vendor.MyModule.dll",
  "entryType": "HYWDebugAssistant.Vendor.MyModule.MyModule",
  "dependsOn": [],
  "resources": ["resources/i18n"]
}
```

## 包长什么样

`dotnet build -c Release` 之后，打包规则把模块包归拢到：

```
bin/module-package/Release/<模块名>/
```

包内**只应**有：

- `module.json`
- 实现程序集（可含 `.pdb`）
- `resources/**`（若声明了）
- 模块私有第三方依赖（含 `runtimes/<rid>/…` 平台分片）

包内**不得**有宿主提供的程序集：`HYWDebugAssistant.Sdk*`、`Avalonia*`、`System.IO.Ports`、`SkiaSharp` 等，也不得有 `*.deps.json`。打包规则会自动排除；手工打包时按这份清单剔除。

模块名默认取项目名最后一个点段，可用 `<ModulePackageName>` 覆盖。

## 资源布局

```
resources/
  i18n/<程序集简名>/<culture>.json
  icons/<程序集简名>/icons.json + *.svg
  <自定义 kind>/...
```

`i18n` 与 `icons` 是框架认识的两种类别，由框架按拥有者读取；其他 `kind` 由模块用 `IResourceAccess` 自己读。细节见[资源与文本](resources.md)。

## 进构建序

- 外部工程：不需要登记，`dotnet build` 即可产出模块包。
- 本仓库内的模块：项目目录里放 `module.json` 即被识别；加入解决方案后构建会自动归拢到 `artifacts/modules/<配置>/`。这是仓库开发的事，见[项目开发](../project/index.md)。

## 相关

- [声明面](contracts.md)
- [发布与装载](publishing.md)
