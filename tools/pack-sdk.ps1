#Requires -Version 7.0
<#
.SYNOPSIS
    打包三个 SDK 程序集到本地 NuGet 源（`artifacts/sdk-packages`）。

.DESCRIPTION
    产物只含三个包（包版本 = 产品四段版本，取自仓库根 `version.props`）：
      · HYWDebugAssistant.Sdk
      · HYWDebugAssistant.Sdk.Ui（依赖 Avalonia）
      · HYWDebugAssistant.Sdk.AccessKit（依赖 Sdk）
    版本不在本脚本维护：`version.props` 是唯一事实源（见 docs/architecture/VERSIONING.md）。
    该目录是**构建产物**（每次清空重建），示例模块/独立验证脚本从这里还原。

.EXAMPLE
    ./tools/pack-sdk.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot 'dotnet-env.ps1')

$version = (& (Join-Path $PSScriptRoot 'version.ps1') -Value).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "无法从 version.props 取得四段版本（得到 '$version'）"
}

foreach ($name in @('Platform', 'Platform(x86)')) {
    if (Test-Path "Env:$name") {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        Write-Host "[env] 已清除被污染的 $name" -ForegroundColor Yellow
    }
}

$output = Join-Path $root 'artifacts\sdk-packages'

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

# NuGet 以 id+version 为缓存键：同版本重打包后，全局缓存里的旧 nuspec 会遮蔽新包。
# 只清这三个第一方包目录，不动整个缓存。
$globalPackages = Get-NuGetGlobalPackagesDirectory
foreach ($packageId in @('hywdebugassistant.sdk', 'hywdebugassistant.sdk.ui', 'hywdebugassistant.sdk.accesskit')) {
    $cached = Join-Path $globalPackages $packageId
    if (Test-Path -LiteralPath $cached) {
        Write-Host "[pack] 清除旧缓存：$cached" -ForegroundColor Yellow
        Remove-Item -LiteralPath $cached -Recurse -Force
    }
}

$projects = @(
    'src\Sdk\HYWDebugAssistant.Sdk.csproj',
    'src\Sdk.Ui\HYWDebugAssistant.Sdk.Ui.csproj',
    'src\Sdk.AccessKit\HYWDebugAssistant.Sdk.AccessKit.csproj'
)

foreach ($project in $projects) {
    Write-Host "[pack] $project" -ForegroundColor Cyan
    dotnet pack $project -c Release -o $output -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "SDK 打包失败：$project"
    }
}

# 校验按**产物**枚举：版本取自 version.props，改版本不需要改本脚本。
$packageIds = @(
    'HYWDebugAssistant.Sdk',
    'HYWDebugAssistant.Sdk.Ui',
    'HYWDebugAssistant.Sdk.AccessKit'
)

function Test-SdkPackageName {
    param([string]$Name)

    foreach ($id in $packageIds) {
        if ($Name -match ('^' + [regex]::Escape($id) + '\.\d+\.\d+\.\d+\.\d+\.nupkg$')) {
            return $true
        }
    }

    return $false
}

$produced = @(Get-ChildItem $output -File)

$missing = @()
foreach ($id in $packageIds) {
    $expectedName = "$id.$version.nupkg"
    $hit = @($produced | Where-Object { $_.Name -eq $expectedName })
    if ($hit.Count -ne 1) {
        $missing += "$expectedName（匹配到 $($hit.Count) 个）"
    }
}

if ($missing.Count -gt 0) {
    throw "SDK 包缺失或重复：$($missing -join '，')（输出目录：$output）"
}

$extra = @($produced | Where-Object { -not (Test-SdkPackageName -Name $_.Name) })
if ($extra.Count -gt 0) {
    throw "SDK 输出目录只允许三个包，发现额外文件：$($extra.Name -join ', ')"
}

$produced | Sort-Object Name |
    ForEach-Object { Write-Host ("[pack]   {0,-48} {1,8:N1} KB" -f $_.Name, ($_.Length / 1KB)) }

Write-Host "[pack] 三个 SDK 包已生成（$version）：$output" -ForegroundColor Green
