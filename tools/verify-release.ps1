#Requires -Version 7.0
<#
.SYNOPSIS
    校验已经产出的产品目录与 SDK 目录（只读；不构建、不改文件）。

.DESCRIPTION
    检查发布边界、自包含运行时文件、模块数量、资源种子、release.json / sdk.json 字段。
    非 win-x64 的产品只做结构检查并提示“未验证”，不承诺可启动。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProductDirectory,

    [Parameter(Mandatory = $true)]
    [string]$SdkDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

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

# ---------------------------------------------------------------- 产品
RequireFile (Join-Path $product 'release.json')
RequireFile (Join-Path $product 'README.txt')
RequireFile (Join-Path $product 'LICENSE.txt')
RequireFile (Join-Path $product 'THIRD_PARTY_NOTICES.md')

$release = Get-Content (Join-Path $product 'release.json') -Raw | ConvertFrom-Json
if ([string]$release.kind -ne 'product') { Fail "release.json kind 不正确：$($release.kind)" }
if ([string]::IsNullOrWhiteSpace([string]$release.version)) { Fail 'release.json 缺少 version' }
if ([string]::IsNullOrWhiteSpace([string]$release.rid)) { Fail 'release.json 缺少 rid' }

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

$entryName = if ([string]$release.rid -like 'win-*') { 'HYWDebugAssistant.App.exe' } else { 'HYWDebugAssistant.App' }
RequireFile (Join-Path $product $entryName)

$runtimeFiles = if ([string]$release.rid -like 'win-*') {
    @('coreclr.dll', 'hostfxr.dll')
}
elseif ([string]$release.rid -like 'osx-*') {
    @('libcoreclr.dylib', 'libhostfxr.dylib')
}
else {
    @('libcoreclr.so', 'libhostfxr.so')
}
foreach ($runtime in $runtimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $product $runtime))) {
        if ([bool]$release.promised) { Fail "产品目录缺少自包含运行时文件：$runtime" }
        Write-Host "[verify-release] [warn] 缺少 $runtime（RID 未验证）" -ForegroundColor Yellow
    }
}

$modules = @(Get-ChildItem -LiteralPath (Join-Path $product 'modules') -Directory -ErrorAction SilentlyContinue)
if ($modules.Count -ne @($release.modules).Count) {
    Fail "modules 数量（$($modules.Count)）与 release.json（$(@($release.modules).Count)）不一致"
}

$seed = Join-Path $product 'resources\seeds\settings\settings.json'
RequireFile $seed
$frameworkTable = Join-Path $product 'resources\i18n\HYWDebugAssistant.FrameworkUi\zh-Hans.json'
RequireFile $frameworkTable

# ---------------------------------------------------------------- SDK
RequireFile (Join-Path $sdk 'sdk.json')
RequireFile (Join-Path $sdk 'README.txt')
RequireFile (Join-Path $sdk 'LICENSE.txt')
RequireFile (Join-Path $sdk 'THIRD_PARTY_NOTICES.md')
RequireFile (Join-Path $sdk 'NuGet.config')
RequireFile (Join-Path $sdk 'template\ModuleTemplate\HYWDebugAssistant.Template.Module.csproj')

$sdkJson = Get-Content (Join-Path $sdk 'sdk.json') -Raw | ConvertFrom-Json
if ([string]$sdkJson.kind -ne 'sdk') { Fail "sdk.json kind 不正确：$($sdkJson.kind)" }
if ([string]$sdkJson.version -ne [string]$release.version) {
    Fail "产品与 SDK 版本不一致：$($release.version) / $($sdkJson.version)"
}

$packages = @(Get-ChildItem -LiteralPath (Join-Path $sdk 'packages') -Filter *.nupkg -File -ErrorAction SilentlyContinue)
if ($packages.Count -ne 5) { Fail "SDK packages 数量应为 5（实际 $($packages.Count)）" }

$productBinaries = @(Get-ChildItem -LiteralPath $sdk -Recurse -File |
    Where-Object { $_.Name -like 'HYWDebugAssistant.App*' -or $_.Name -like 'HYWDebugAssistant.Core*' })
if ($productBinaries.Count -gt 0) { Fail "SDK 目录包含产品二进制：$($productBinaries[0].FullName)" }

Write-Host "[verify-release] 通过：产品 $($release.version)（$($release.rid)，validation=$($release.validation)），SDK 5 包 + 模板" -ForegroundColor Green
