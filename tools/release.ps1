#Requires -Version 7.0
<#
.SYNOPSIS
    官方发布入口：门禁 → 产品目录 → SDK 目录 → 结构校验。只产出目录，不压缩。

.EXAMPLE
    ./tools/release.ps1 -OutputDirectory D:\dist
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$RuntimeIdentifier = 'win-x64',

    [switch]$SkipGate,

    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

foreach ($name in @('Platform', 'Platform(x86)')) {
    if (Test-Path "Env:$name") {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        Write-Host "[env] 已清除被污染的 $name" -ForegroundColor Yellow
    }
}

$version = (& (Join-Path $PSScriptRoot 'version.ps1') -Value).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "无法从 version.props 取得四段版本（得到 '$version'）"
}

$rid = $RuntimeIdentifier.Trim().ToLowerInvariant()
$parent = [System.IO.Path]::GetFullPath($OutputDirectory)
$repo = [System.IO.Path]::GetFullPath($root)
$separator = [System.IO.Path]::DirectorySeparatorChar
if ($parent -eq $repo -or $parent.StartsWith($repo + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝输出：目标目录在仓库内部（$parent）——请指定仓库之外的目录"
}
if ((Test-Path -LiteralPath $parent) -and (Get-ChildItem -LiteralPath $parent -Force | Select-Object -First 1)) {
    throw "拒绝输出：目标目录非空（$parent）——请指定空目录或不存在的路径"
}

New-Item -ItemType Directory -Force -Path $parent | Out-Null
$productTarget = Join-Path $parent ("HYWDebugAssistant-$version-$rid")

try {
    if (-not $SkipGate) {
        Write-Host '[release] 完整门禁：build.ps1 -Configuration Release' -ForegroundColor Cyan
        & (Join-Path $root 'build.ps1') -Configuration Release
        if ($LASTEXITCODE -ne 0) { throw '门禁未通过' }
    }
    else {
        Write-Host '[release] [warn] 已跳过门禁（-SkipGate）' -ForegroundColor Yellow
    }

    & (Join-Path $PSScriptRoot 'package-release.ps1') `
        -OutputDirectory $productTarget `
        -RuntimeIdentifier $rid `
        -SkipGate `
        -AllowDirty:$AllowDirty
    if ($LASTEXITCODE -ne 0) { throw '产品发布失败' }

    & (Join-Path $PSScriptRoot 'package-sdk.ps1') `
        -OutputDirectory $parent `
        -SkipGate `
        -AllowDirty:$AllowDirty
    if ($LASTEXITCODE -ne 0) { throw 'SDK 发布失败' }

    $sdkTarget = Join-Path $parent ("HYWDebugAssistant.Sdk-$version")

    & (Join-Path $PSScriptRoot 'verify-release.ps1') -ProductDirectory $productTarget -SdkDirectory $sdkTarget
    if ($LASTEXITCODE -ne 0) { throw '发布产物校验失败' }

    Write-Host '[release] 完成：' -ForegroundColor Green
    Write-Host "  产品：$productTarget" -ForegroundColor Green
    Write-Host "  SDK ：$sdkTarget" -ForegroundColor Green
    Write-Host '[release] 产品目录此刻是干净的分发源；就地运行会在其中生成 settings\ / composites\，' -ForegroundColor Yellow
    Write-Host '          运行过的目录不要再拿去分发（verify-release.ps1 会因清单外的文件拒绝）。' -ForegroundColor Yellow
}
catch {
    # 发布失败：清理本次新建的产品/SDK 目录，避免留下半成品。
    $sdkTarget = Join-Path $parent ("HYWDebugAssistant.Sdk-$version")
    foreach ($path in @($productTarget, $sdkTarget)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    throw
}
