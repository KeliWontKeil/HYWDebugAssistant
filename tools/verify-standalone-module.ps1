#Requires -Version 7.0
<#
.SYNOPSIS
    独立编译 + 独立加载验证：打包 SDK → 用本地源构建示例模块 → 只复制模块自有文件 → 宿主加载。

.DESCRIPTION
    这条验证回答"外部作者只靠三个 SDK NuGet 包能不能写出可被宿主装入的模块"：
      1. `tools/pack-sdk.ps1` 生成 artifacts/sdk-packages；
      2. `samples/ExternalModule` 只经 PackageReference 还原并构建（不引用仓库工程）；
      3. 只复制示例 DLL + module.json（可含 pdb）到临时包目录，模拟真实的薄模块包；
      4. `tools/StandaloneModuleHost` 用 ModulePackageLoader + ModuleInstanceFactory(catalog) 加载并断言。
    任一步失败即非零退出。

.EXAMPLE
    ./tools/verify-standalone-module.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

foreach ($name in @('Platform', 'Platform(x86)')) {
    if (Test-Path "Env:$name") {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        Write-Host "[env] 已清除被污染的 $name" -ForegroundColor Yellow
    }
}

& (Join-Path $PSScriptRoot 'pack-sdk.ps1')

$sampleProject = Join-Path $root 'samples\ExternalModule\HYWDebugAssistant.Samples.ExternalModule.csproj'
$sampleOutput = Join-Path $root 'samples\ExternalModule\bin\Release\net10.0'
$hostProject = Join-Path $root 'tools\StandaloneModuleHost\HYWDebugAssistant.StandaloneModuleHost.csproj'
$hostOutput = Join-Path $root 'tools\StandaloneModuleHost\bin\Release\net10.0'

Write-Host "[standalone] 构建示例模块（只走 PackageReference）" -ForegroundColor Cyan
dotnet build $sampleProject -c Release -nologo -p:RestoreForce=true
if ($LASTEXITCODE -ne 0) { throw "示例模块构建失败" }

$sampleDll = Join-Path $sampleOutput 'HYWDebugAssistant.Samples.ExternalModule.dll'
$sampleManifest = Join-Path $sampleOutput 'module.json'
if (-not (Test-Path $sampleDll)) { throw "示例模块 DLL 不存在：$sampleDll" }
if (-not (Test-Path $sampleManifest)) { throw "示例模块清单不存在：$sampleManifest" }

Write-Host "[standalone] 构建验证宿主" -ForegroundColor Cyan
dotnet build $hostProject -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "StandaloneModuleHost 构建失败" }

$hostDll = Join-Path $hostOutput 'HYWDebugAssistant.StandaloneModuleHost.dll'
if (-not (Test-Path $hostDll)) { throw "验证宿主 DLL 不存在：$hostDll" }

$tempPackage = Join-Path $env:TEMP ("hyw-standalone-" + [Guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Force -Path $tempPackage | Out-Null
    Copy-Item $sampleDll (Join-Path $tempPackage 'HYWDebugAssistant.Samples.ExternalModule.dll')
    Copy-Item $sampleManifest (Join-Path $tempPackage 'module.json')

    $samplePdb = Join-Path $sampleOutput 'HYWDebugAssistant.Samples.ExternalModule.pdb'
    if (Test-Path $samplePdb) {
        Copy-Item $samplePdb (Join-Path $tempPackage 'HYWDebugAssistant.Samples.ExternalModule.pdb')
    }

    $files = @(Get-ChildItem $tempPackage -File | Select-Object -ExpandProperty Name)
    Write-Host "[standalone] 临时薄包：$($files -join ', ')" -ForegroundColor DarkCyan

    & dotnet $hostDll $tempPackage
    if ($LASTEXITCODE -ne 0) { throw "独立加载验证失败（exit=$LASTEXITCODE）" }

    Write-Host "[standalone] 通过：示例模块只靠 SDK 包构建，并被宿主按 catalog 装入" -ForegroundColor Green
}
finally {
    if (Test-Path $tempPackage) {
        Remove-Item $tempPackage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
