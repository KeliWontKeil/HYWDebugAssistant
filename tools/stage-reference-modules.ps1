#Requires -Version 7.0
<#
.SYNOPSIS
    校验（或导出）**构建产物中的模块包目录**。

.DESCRIPTION
    模块包由**构建自动归拢**（见 src/Modules/Directory.Build.targets + App 项目的镜像目标）：
      · 模块项目构建  → artifacts\modules\<包名>\
      · App 项目构建  → <应用输出>\modules\<包名>\（**内置包目录**，启动默认扫描）
    本脚本只做两件事：
      ① 默认（-Verify）：校验两处包目录是否齐全（module.json + 实现程序集），供 build.ps1 作为门禁的一步；
      ② -ExportDirectory <目录>：把包复制出去，**用于发布打包**（不再维护模块清单——遍历 artifacts\modules 即可）。

.EXAMPLE
    ./tools/stage-reference-modules.ps1
    ./tools/stage-reference-modules.ps1 -ExportDirectory D:\publish\modules
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # 指定则把包复制到该目录（发布打包用）；不指定则只校验。
    [string]$ExportDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$packageRoot = Join-Path $root 'artifacts\modules'
$appModules = Join-Path $root "src\App\bin\$Configuration\net10.0\modules"

function Test-ModulePackages {
    param([string]$Directory, [switch]$Required)

    if (-not (Test-Path $Directory)) {
        if ($Required) { throw "模块包目录不存在：$Directory（请先运行 ./build.ps1）" }
        Write-Host "[verify] 跳过（不存在）：$Directory" -ForegroundColor Yellow
        return 0
    }

    $packages = Get-ChildItem $Directory -Directory
    if ($packages.Count -eq 0) { throw "模块包目录为空：$Directory" }

    foreach ($package in $packages) {
        $manifest = Join-Path $package.FullName 'module.json'
        if (-not (Test-Path $manifest)) { throw "包目录缺少模块清单：$($package.FullName)" }

        $manifestJson = Get-Content $manifest -Raw | ConvertFrom-Json
        $assembly = Join-Path $package.FullName $manifestJson.entryAssembly
        if (-not (Test-Path $assembly)) {
            throw "包目录缺少实现程序集：$($package.FullName) → $($manifestJson.entryAssembly)"
        }
    }

    Write-Host "[verify] $($packages.Count) 个模块包齐全：$Directory" -ForegroundColor Green
    return $packages.Count
}

$built = Test-ModulePackages -Directory $packageRoot -Required
$mirrored = Test-ModulePackages -Directory $appModules

if ($built -ne $mirrored) {
    throw "内置包目录与应用输出不一致（构建产出 $built 个 vs 应用输出 $mirrored 个）——请重新构建 App 项目"
}

if ($ExportDirectory) {
    if (Test-Path $ExportDirectory) { Remove-Item $ExportDirectory -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ExportDirectory | Out-Null
    Copy-Item (Join-Path $packageRoot '*') $ExportDirectory -Recurse -Force
    Write-Host "[export] $built 个模块包 → $ExportDirectory" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "模块包来源：$packageRoot" -ForegroundColor Cyan
Write-Host "内置包目录（应用启动默认扫描）：$appModules" -ForegroundColor Cyan
Write-Host "应用内：工具栏「模块清单」→ 选「可用模块」→「加入现场」；「重扫来源」可发现新增的包（改过代码的包需重启应用）。"
