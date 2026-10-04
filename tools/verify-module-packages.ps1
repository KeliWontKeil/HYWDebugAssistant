#Requires -Version 7.0
<#
.SYNOPSIS
    校验（或导出）**构建产物中的模块包目录**。

.DESCRIPTION
    模块包由**构建自动归拢**（见 src/Modules/Directory.Build.targets + App 项目的镜像目标）：
      · 模块项目构建  → artifacts\modules\<包名>\
      · App 项目构建  → <应用输出>\modules\<包名>\（**内置包目录**，启动默认扫描）
    本脚本只做两件事：
      ① 默认（-Verify）：校验两处包目录是否齐全（module.json + 实现程序集；**可选的 view.json 也必须能解析**），供 build.ps1 作为门禁的一步；
      ② -ExportDirectory <目录>：把包复制出去，**用于发布打包**（不再维护模块清单——遍历 artifacts\modules 即可）。
    注：原文件名 `stage-reference-modules.ps1` 是"参考模块"时代的产物；参考模块已按 2026-09 用户裁定删除
      ⇒ 改名 `verify-module-packages.ps1`（**包来源零改动**：仍由构建自动归拢，本脚本只校验/导出）。

.EXAMPLE
    ./tools/verify-module-packages.ps1
    ./tools/verify-module-packages.ps1 -ExportDirectory D:\publish\modules
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

        # 控件布局文件是**可选件**（`decisions.md` 7.56）：没有它 = 用框架默认排布；有它就必须能被读出来，
        # 否则运行期只会静默退回默认排布 + 报一条 Issue —— 那种"包看着齐全、布局却没生效"必须挡在门禁里。
        $layout = Join-Path $package.FullName 'view.json'
        if (Test-Path $layout) {
            try {
                $layoutJson = Get-Content $layout -Raw | ConvertFrom-Json
            }
            catch {
                throw "包目录的布局文件无法解析：$layout → $($_.Exception.Message)"
            }

            if ("$($layoutJson.version)" -ne '1') {
                throw "包目录的布局文件版本不受支持（读到「$($layoutJson.version)」）：$layout（本框架只认识 version 1）"
            }
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
    # 【2026-09 安全护栏 · `decisions.md` 7.55 §F】导出**会先清空目标目录** ⇒ 必须挡住"指到程序目录/用户数据"
    # 这类位置（否则一句话就把 `settings.json`、`modules\`、`data\` 全删了）。
    $target = [System.IO.Path]::GetFullPath($ExportDirectory)
    $forbidden = @(
        [System.IO.Path]::GetFullPath($root),
        [System.IO.Path]::GetFullPath((Join-Path $root 'src\App\bin')),
        [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts')),
        [System.IO.Path]::GetFullPath((Join-Path $root 'src'))
    )

    foreach ($bad in $forbidden) {
        if ($target -eq $bad -or $target.StartsWith($bad + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝导出：目标目录在仓库/构建产物内部（$target）——请指定仓库之外的**新目录**（导出会先清空目标目录）"
        }
    }

    # 目标已有内容时要求它是"空的或看起来就是模块包目录"，避免误删用户的其它数据
    if ((Test-Path $target) -and (Get-ChildItem $target -Force | Where-Object { $_.Name -ne 'module.json' })) {
        $looksLikePackages = (Get-ChildItem $target -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'module.json') }).Count -gt 0
        if (-not $looksLikePackages) {
            throw "拒绝导出：目标目录非空且不像模块包目录（$target）——导出会先清空它，请先自行确认"
        }
    }

    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $packageRoot '*') $target -Recurse -Force
    Write-Host "[export] $built 个模块包 → $target" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "模块包来源：$packageRoot" -ForegroundColor Cyan
Write-Host "内置包目录（应用启动默认扫描）：$appModules" -ForegroundColor Cyan
Write-Host "应用内：工具栏「模块清单」→ 选「可用模块」→「加入现场」；「重扫来源」可发现新增的包（改过代码的包需重启应用）。"
