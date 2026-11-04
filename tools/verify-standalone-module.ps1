#Requires -Version 7.0
<#
.SYNOPSIS
    独立编译 + 独立加载验证：打包作者包 → 用本地源构建示例模块 → 只复制模块自有文件 → 宿主加载。

.DESCRIPTION
    这条验证回答"外部作者只靠作者包能不能写出可被宿主装入的模块"，覆盖三种最典型的模块形态：
      · samples/ExternalModule（外部接入：声明标准标识，自己组帧）
      · samples/DataModule（纯数据变换：两端口 + 一类型 + 角色，无外部接入）
      · samples/ActivityModule（step：OnStep + NextDelay，无端口）
    步骤：
      1. `tools/pack-sdk.ps1` 生成 artifacts/sdk-packages（五个包）；
      2. 三个样例只经 PackageReference 还原并构建（不引用仓库工程）；
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
if ($LASTEXITCODE -ne 0) { throw "作者包打包失败" }

$hostProject = Join-Path $root 'tools\StandaloneModuleHost\HYWDebugAssistant.StandaloneModuleHost.csproj'
$hostOutput = Join-Path $root 'tools\StandaloneModuleHost\bin\Release\net10.0'

Write-Host "[standalone] 构建验证宿主" -ForegroundColor Cyan
dotnet build $hostProject -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "StandaloneModuleHost 构建失败" }

$hostDll = Join-Path $hostOutput 'HYWDebugAssistant.StandaloneModuleHost.dll'
if (-not (Test-Path $hostDll)) { throw "验证宿主 DLL 不存在：$hostDll" }

$samples = @('ExternalModule', 'DataModule', 'ActivityModule')

foreach ($sample in $samples) {
    $sampleProject = Join-Path $root "samples\$sample\HYWDebugAssistant.Samples.$sample.csproj"

    Write-Host "[standalone] 构建样例 $sample（只走 PackageReference）" -ForegroundColor Cyan
    dotnet build $sampleProject -c Release -nologo -p:RestoreForce=true
    if ($LASTEXITCODE -ne 0) { throw "样例构建失败：$sample" }

    # 作者包把模块包直接归拢到 bin\module-package\<配置>\<包名>\：这就是会被分发的那一棵目录。
    $packageRoot = Join-Path $root "samples\$sample\bin\module-package\Release"
    $packageDirs = @(Get-ChildItem -LiteralPath $packageRoot -Directory -ErrorAction SilentlyContinue)
    if ($packageDirs.Count -ne 1) {
        throw "样例 $sample 应产出恰好一个包目录（实际 $($packageDirs.Count) 个）：$packageRoot"
    }

    $packageDir = $packageDirs[0].FullName
    $files = @(Get-ChildItem -LiteralPath $packageDir -File -Recurse |
        ForEach-Object { $_.FullName.Substring($packageDir.Length + 1) } | Sort-Object)
    Write-Host "[standalone] 样例包（$sample）：$($files -join ', ')" -ForegroundColor DarkCyan

    if (-not (Test-Path (Join-Path $packageDir 'module.json'))) {
        throw "样例包缺少 module.json：$packageDir"
    }

    $hostLeak = @($files | Where-Object { $_ -like '*HYWDebugAssistant.Sdk*.dll' -or $_ -like 'HYWDebugAssistant.Access*.dll' })
    if ($hostLeak.Count -gt 0) {
        throw "样例包含宿主提供程序集：$($hostLeak -join '，')"
    }

    & dotnet $hostDll $packageDir
    if ($LASTEXITCODE -ne 0) { throw "独立加载验证失败（$sample，exit=$LASTEXITCODE）" }
}

Write-Host "[standalone] 通过：三个样例都只靠作者包构建出薄包，并被宿主按真实路径装入" -ForegroundColor Green
