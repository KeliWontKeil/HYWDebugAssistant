#Requires -Version 7.0
<#
.SYNOPSIS
    校验已经产出的产品目录与 SDK 目录（只读；不构建、不改文件）。

.DESCRIPTION
    检查发布边界、自包含运行时文件、模块数量、资源与仓库源文件的一致性与许可文件。

    版本与 RID 从**目录名**读取，并与 `version.props` 对照——产物里没有元数据文件，
    所以这里的判据必须来自目录命名与仓库真源，而不是产物自述。

    逐文件对账依赖发布记录（`artifacts\release-manifests\`，由发布脚本写出、不进产物）：
    记录在时比对完整文件集合，记录不在时只做结构与禁止项检查。

    **资源必须与仓库逐文件相同**：`resources\` 是仓库 `resources\` 的纯整拷，发布脚本
    不生成任何文件。这条把"脚本没创作内容"从注释承诺变成会失败的检查——脚本若又偷偷
    写一份种子/说明进产物，这里立刻判红。

    裁剪清单同样只读 `tools\release-profile.ps1`：与 `package-release.ps1` 共用一份数组。
    名单之外还查**程序集引用闭包**：手写清单必然漏，缺了渲染链上的程序集产品仍能启动，
    只是窗口一片空白、退出码 0（没有任何自动信号），只能靠这条结构判据拦住。

    非 win-x64 的产品只做结构检查并提示"未验证"，不承诺可启动。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProductDirectory,

    [Parameter(Mandatory = $true)]
    [string]$SdkDirectory,

    [string]$ManifestDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'release-profile.ps1')

function Fail([string]$message) {
    throw "[verify-release] $message"
}

function RequireFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Fail "缺少文件：$path"
    }
}

$product = [System.IO.Path]::GetFullPath($ProductDirectory)
$sdk = [System.IO.Path]::GetFullPath($SdkDirectory)

if (-not (Test-Path -LiteralPath $product -PathType Container)) { Fail "产品目录不存在：$product" }
if (-not (Test-Path -LiteralPath $sdk -PathType Container)) { Fail "SDK 目录不存在：$sdk" }

if ([string]::IsNullOrWhiteSpace($ManifestDirectory)) {
    $ManifestDirectory = Join-Path $root 'artifacts\release-manifests'
}

# ---------------------------------------------------------------- 命名与真源对照
$productLeaf = Split-Path -Leaf $product
if ($productLeaf -notmatch '^HYWDebugAssistant-(?<version>\d+\.\d+\.\d+\.\d+)-(?<rid>.+)$') {
    Fail "产品目录名不符合 HYWDebugAssistant-<版本>-<rid>：$productLeaf"
}
$version = $Matches['version']
$rid = $Matches['rid']

$propsVersion = (& (Join-Path $PSScriptRoot 'version.ps1') -Value).Trim()
if ($version -ne $propsVersion) {
    Fail "产品目录版本（$version）与 version.props（$propsVersion）不一致"
}

$promised = $rid -eq 'win-x64'
$sdkLeaf = Split-Path -Leaf $sdk
if ($sdkLeaf -ne "HYWDebugAssistant.Sdk-$version") {
    Fail "SDK 目录名应为 HYWDebugAssistant.Sdk-$version（实际 $sdkLeaf）"
}

# ---------------------------------------------------------------- 产品
RequireFile (Join-Path $product 'LICENSE.txt')
RequireFile (Join-Path $product 'THIRD_PARTY_NOTICES.md')

foreach ($forbidden in @('sdk', 'samples', 'settings', 'workspace', 'logs', 'composites', 'framework', 'modules\external')) {
    $path = Join-Path $product $forbidden
    if (Test-Path -LiteralPath $path) { Fail "产品目录出现禁止项：$forbidden" }
}

# 模块包内的 views/*.axaml 是运行期资源，不是待编译源码；其余 .cs/.axaml/工程文件一律拒绝。
$sourceHits = @(Get-ChildItem -LiteralPath $product -Recurse -File |
    Where-Object {
        $_.Extension -in @('.cs', '.csproj', '.sln', '.slnx') -or
        ($_.Extension -eq '.axaml' -and $_.FullName -notmatch '[\\/]modules[\\/][^\\/]+[\\/]views[\\/][^\\/]+\.axaml$')
    })
if ($sourceHits.Count -gt 0) { Fail "产品目录包含源码：$($sourceHits[0].FullName)" }

$devSymbols = @(Get-ChildItem -LiteralPath $product -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.xml') })
if ($devSymbols.Count -gt 0) { Fail "产品目录包含开发符号/XML 文档：$($devSymbols[0].FullName)" }

# 裁剪清单与反向护栏**只读 release-profile.ps1**（与 package-release.ps1 共用一份）。
foreach ($pattern in (Get-ReleaseCullList -Rid $rid)) {
    $hits = @(Get-ChildItem -LiteralPath $product -File -Filter $pattern -ErrorAction SilentlyContinue)
    if ($hits.Count -gt 0) {
        Fail "产品目录含目标平台不需要的文件：$($hits[0].Name)（规则 $pattern）"
    }
}

foreach ($name in (Get-ReleaseCullGuard -Rid $rid)) {
    if (-not (Test-Path -LiteralPath (Join-Path $product $name))) {
        Fail "产品目录缺少 $name（Win32 渲染后端必需；见 tools/release-profile.ps1 的反向护栏）"
    }
}

# 渲染链闭包：名单查不出"被间接引用"的缺件。缺了渲染链上的程序集时产品仍能启动，
# 只是窗口一片空白、退出码 0 —— 没有任何自动信号，只能在这里算闭包。
$renderRoots = Get-ReleaseRendererRoots -Rid $rid
if ($renderRoots.Count -gt 0) {
    $unresolved = @(Test-ReleaseAssemblyClosure -Directory $product -Roots $renderRoots)
    if ($unresolved.Count -gt 0) {
        Fail "产品目录渲染链不闭合：$($unresolved -join '、')（被 $($renderRoots -join '/') 间接引用；缺了窗口会一片空白）"
    }
}

$entryName = if ($rid -like 'win-*') { 'HYWDebugAssistant.App.exe' } else { 'HYWDebugAssistant.App' }
RequireFile (Join-Path $product $entryName)

$runtimeFiles = if ($rid -like 'win-*') {
    @('coreclr.dll', 'hostfxr.dll')
}
elseif ($rid -like 'osx-*') {
    @('libcoreclr.dylib', 'libhostfxr.dylib')
}
else {
    @('libcoreclr.so', 'libhostfxr.so')
}
foreach ($runtime in $runtimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $product $runtime))) {
        if ($promised) { Fail "产品目录缺少自包含运行时文件：$runtime" }
        Write-Host "[verify-release] [warn] 缺少 $runtime（RID 未验证）" -ForegroundColor Yellow
    }
}

$modules = @(Get-ChildItem -LiteralPath (Join-Path $product 'modules') -Directory -ErrorAction SilentlyContinue)
if ($modules.Count -eq 0) { Fail '产品目录没有任何内置模块包' }

$frameworkTable = Join-Path $product 'resources\i18n\HYWDebugAssistant.FrameworkUi\zh-Hans.json'
RequireFile $frameworkTable

# ---------------------------------------------------------------- 资源 = 仓库纯整拷
# 发布脚本**不生成任何文件**：`resources\` 只能是仓库 `resources\` 的整拷。
# 判据与 tools/verify-resources.ps1 同源（文件集相等 + SHA256 相等），
# 区别只在于：那边比的是应用输出，这边比的是发布目录。
# 这条替代了旧版"要求 seeds/settings/settings.json 存在"——那个要求本身就是
# "由发布脚本生成内容"的产物，且让校验器只能验证自家产出的目录。
$repoResources = Join-Path $root 'resources'
$productResources = Join-Path $product 'resources'
if (-not (Test-Path -LiteralPath $repoResources -PathType Container)) {
    Fail "仓库里没有 resources\：$repoResources"
}
if (-not (Test-Path -LiteralPath $productResources -PathType Container)) {
    Fail "产品目录没有 resources\：$productResources"
}

function Get-ResourceHashMap([string]$directory) {
    $map = @{}
    foreach ($file in (Get-ChildItem -LiteralPath $directory -Recurse -File | Sort-Object FullName)) {
        if ($file.Name -in @('desktop.ini', 'Thumbs.db', '.DS_Store')) { continue }
        $relative = [System.IO.Path]::GetRelativePath($directory, $file.FullName).Replace('\', '/')
        $map[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    return $map
}

$expectedResources = Get-ResourceHashMap $repoResources
$actualResources = Get-ResourceHashMap $productResources

$resourceExtra = @($actualResources.Keys | Where-Object { -not $expectedResources.ContainsKey($_) } | Sort-Object)
$resourceAbsent = @($expectedResources.Keys | Where-Object { -not $actualResources.ContainsKey($_) } | Sort-Object)
$resourceDiff = @($expectedResources.Keys | Where-Object {
        $actualResources.ContainsKey($_) -and $actualResources[$_] -ne $expectedResources[$_]
    } | Sort-Object)

if ($resourceExtra.Count -gt 0) {
    Fail "产品 resources\ 有仓库里没有的文件（发布脚本自己生成内容了？）：resources/$($resourceExtra[0])（共 $($resourceExtra.Count) 个）"
}
if ($resourceAbsent.Count -gt 0) {
    Fail "产品 resources\ 缺少仓库里的文件：resources/$($resourceAbsent[0])（共 $($resourceAbsent.Count) 个）"
}
if ($resourceDiff.Count -gt 0) {
    Fail "产品 resources\ 内容与仓库不一致：resources/$($resourceDiff[0])（共 $($resourceDiff.Count) 个）"
}

# ---------------------------------------------------------------- 逐文件对账
# 清单来自发布记录（仓库 artifacts，不进产物）。它拦的是"目录看着还能跑，
# 但已经不是当初校验过的那一份"：就地运行生成的用户数据、手改过的文件都会暴露。
# 记录不在（别人给的目录、只做结构检查）时只提示，不失败。
$manifestPath = Join-Path $ManifestDirectory "$productLeaf.json"
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]$manifest.kind -ne 'product') { Fail "发布记录 kind 不正确：$($manifest.kind)" }

    $expected = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in @($manifest.app)) { [void]$expected.Add([string]$record.path) }
    foreach ($module in @($manifest.modules)) {
        foreach ($file in @($module.files)) {
            [void]$expected.Add("modules/$($module.package)/$([string]$file.path)")
        }
    }
    foreach ($record in @($manifest.resources)) { [void]$expected.Add("resources/$([string]$record.path)") }

    $actual = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in (Get-ChildItem -LiteralPath $product -Recurse -File)) {
        if ($file.Name -in @('desktop.ini', 'Thumbs.db', '.DS_Store')) { continue }
        [void]$actual.Add([System.IO.Path]::GetRelativePath($product, $file.FullName).Replace('\', '/'))
    }

    $extra = @($actual | Where-Object { -not $expected.Contains($_) })
    $absent = @($expected | Where-Object { -not $actual.Contains($_) })
    if ($extra.Count -gt 0) { Fail "产品目录有清单外的文件（就地运行过产物？）：$($extra[0])（共 $($extra.Count) 个）" }
    if ($absent.Count -gt 0) { Fail "产品目录缺少清单内的文件：$($absent[0])（共 $($absent.Count) 个）" }

    if (@($manifest.modules).Count -ne $modules.Count) {
        Fail "modules 数量（$($modules.Count)）与发布记录（$(@($manifest.modules).Count)）不一致"
    }
}
else {
    Write-Host "[verify-release] [warn] 没有发布记录（$manifestPath）：跳过逐文件对账" -ForegroundColor Yellow
}

# ---------------------------------------------------------------- SDK
RequireFile (Join-Path $sdk 'LICENSE.txt')
RequireFile (Join-Path $sdk 'THIRD_PARTY_NOTICES.md')
RequireFile (Join-Path $sdk 'template\ModuleTemplate\HYWDebugAssistant.Template.Module.csproj')

# 模板自带的本地源配置：SDK 目录里不再有第二份 NuGet.config，缺了模板就构建不了。
RequireFile (Join-Path $sdk 'template\ModuleTemplate\NuGet.config')

$packages = @(Get-ChildItem -LiteralPath (Join-Path $sdk 'packages') -Filter *.nupkg -File -ErrorAction SilentlyContinue)
if ($packages.Count -ne 5) { Fail "SDK packages 数量应为 5（实际 $($packages.Count)）" }

# 包版本必须与产品版本一致：版本号从文件名读，不依赖任何清单文件。
$packageVersion = (& (Join-Path $PSScriptRoot 'version.ps1') -PackageValue).Trim()
foreach ($package in $packages) {
    if ($package.BaseName -notmatch '\.(?<v>\d+\.\d+\.\d+(?:\.\d+)?)$') {
        Fail "无法从包名读取版本：$($package.Name)"
    }

    if ($Matches['v'] -ne $packageVersion) {
        Fail "包 $($package.Name) 的版本（$($Matches['v'])）与 version.props 的包版本（$packageVersion）不一致"
    }
}

$productBinaries = @(Get-ChildItem -LiteralPath $sdk -Recurse -File |
    Where-Object { $_.Name -like 'HYWDebugAssistant.App*' -or $_.Name -like 'HYWDebugAssistant.Core*' })
if ($productBinaries.Count -gt 0) { Fail "SDK 目录包含产品二进制：$($productBinaries[0].FullName)" }

Write-Host "[verify-release] 通过：产品 $version（$rid，validation=$(if ($promised) { 'full' } else { 'unverified' })），SDK 5 包 + 模板" -ForegroundColor Green
