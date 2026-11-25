#Requires -Version 7.0
<#
.SYNOPSIS
    官方发布入口：门禁 → 产品目录 → SDK 目录 → 结构校验。只产出目录，不压缩。

.DESCRIPTION
    产出的两个目录里**没有脚本写的文件**：每个文件要么来自 `dotnet publish`、
    要么是从仓库复制的源文件，要么是 `publish` 自带的资源/模块镜像结果。
    脚本只做「删除开发件」与「搬运」。发布记录写在 `artifacts\release-manifests\`，
    不进产物。

    门禁已经打包并校验过作者包，所以 SDK 那一步用 `-SkipPack` 复用产物，
    不在同一次发布里重复 pack。

.PARAMETER OutputDirectory
    **父目录**（不是产品目录）：两个产物会落在
    `<父目录>\HYWDebugAssistant-<版本>-<rid>\` 与 `<父目录>\HYWDebugAssistant.Sdk-<版本>\`。
    必须是仓库**外**的空目录或不存在路径。约定放在工程目录下（仓库的上一级，与仓库并列），
    不要放到其它磁盘目录——版本多了要能一处找到、一处清理。

.EXAMPLE
    ./tools/release.ps1 -OutputDirectory 'D:\project\c# project\dist-0.2.1.0'
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
    throw "拒绝输出：目标目录在仓库内部（$parent）——请指定仓库外、工程目录下的新目录（如 <工程目录>\<版本>）"
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

    # 门禁（build.ps1 4c/4d）已经打包并校验过作者包，SDK 那步复用产物即可：
    # "同一次发布里把 pack 做两遍、把模板验证做两遍"没有第二次收益，只有双倍耗时。
    # `-SkipGate` 时门禁没跑过 ⇒ 不能复用，必须自己打包（否则会用上一轮的旧包）。
    & (Join-Path $PSScriptRoot 'package-sdk.ps1') `
        -OutputDirectory $parent `
        -SkipGate `
        -SkipPack:(-not $SkipGate) `
        -AllowDirty:$AllowDirty
    if ($LASTEXITCODE -ne 0) { throw 'SDK 发布失败' }

    $sdkTarget = Join-Path $parent ("HYWDebugAssistant.Sdk-$version")

    & (Join-Path $PSScriptRoot 'verify-release.ps1') -ProductDirectory $productTarget -SdkDirectory $sdkTarget
    if ($LASTEXITCODE -ne 0) { throw '发布产物校验失败' }

    Write-Host '[release] 完成：' -ForegroundColor Green
    Write-Host "  产品：$productTarget" -ForegroundColor Green
    Write-Host "  SDK ：$sdkTarget" -ForegroundColor Green
    Write-Host '[release] 两个目录只含 publish 结果与仓库源文件副本，没有脚本新写的内容；' -ForegroundColor Yellow
    Write-Host '          发布记录在 artifacts\release-manifests\，不在产物内。' -ForegroundColor Yellow
    Write-Host '          就地运行会在产品目录生成 settings\ / composites\：运行过的目录不要再拿去分发。' -ForegroundColor Yellow
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
