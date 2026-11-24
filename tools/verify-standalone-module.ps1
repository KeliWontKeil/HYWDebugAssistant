#Requires -Version 7.0
<#
.SYNOPSIS
    作者工具链冒烟：只用给定 SDK 包目录构建模板模块，复制成薄包，再交给独立宿主装入。

.DESCRIPTION
    这条验证回答“外部作者只靠发布出来的作者包，能否构建并装入一个模块”。
    它不再依赖仓库里的样例：输入是 SDK 包目录 + 一个模板工程。

    参数：
      -SdkRoot        包含 nupkg 的目录，或包含 packages/ 子目录的 SDK 根（默认 artifacts/sdk-packages）
      -TemplateRoot   模板工程目录（默认 templates/ModuleTemplate）
      -WorkDirectory  可选临时工作目录（省略时在系统临时目录下新建）

    任一环节失败即非零退出。
#>
[CmdletBinding()]
param(
    [string]$SdkRoot,

    [string]$TemplateRoot,

    [string]$WorkDirectory
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

if ([string]::IsNullOrWhiteSpace($SdkRoot)) {
    $SdkRoot = Join-Path $root 'artifacts\sdk-packages'
}
if ([string]::IsNullOrWhiteSpace($TemplateRoot)) {
    $TemplateRoot = Join-Path $root 'templates\ModuleTemplate'
}

$sdkRoot = [System.IO.Path]::GetFullPath($SdkRoot)
$packagesRoot = if (Test-Path -LiteralPath (Join-Path $sdkRoot 'packages') -PathType Container) {
    Join-Path $sdkRoot 'packages'
}
else {
    $sdkRoot
}

$templateRoot = [System.IO.Path]::GetFullPath($TemplateRoot)
if (-not (Test-Path -LiteralPath $templateRoot -PathType Container)) {
    throw "模板目录不存在：$templateRoot"
}
if (-not (Test-Path -LiteralPath $packagesRoot -PathType Container)) {
    throw "SDK 包目录不存在：$packagesRoot"
}

$version = (& (Join-Path $PSScriptRoot 'version.ps1') -Value).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "无法从 version.props 取得四段版本（得到 '$version'）"
}

$createdWork = $false
if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('hyw-template-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $createdWork = $true
}
$work = [System.IO.Path]::GetFullPath($WorkDirectory)

try {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    $moduleRoot = Join-Path $work 'module'
    Copy-Item -LiteralPath $templateRoot -Destination $moduleRoot -Recurse -Force
    Get-ChildItem -LiteralPath $moduleRoot -Recurse -Directory -Force |
        Where-Object { $_.Name -in @('bin', 'obj', 'module-package') } |
        Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }

    $project = @(Get-ChildItem -LiteralPath $moduleRoot -File -Filter *.csproj)
    if ($project.Count -ne 1) {
        throw "模板目录应恰好有一个 .csproj（实际 $($project.Count) 个）：$moduleRoot"
    }

    $escaped = [System.Security.SecurityElement]::Escape($packagesRoot)
    $nuget = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="hyw-sdk" value="$escaped" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath (Join-Path $moduleRoot 'NuGet.config') -Value $nuget -Encoding utf8

    Write-Host "[standalone] 构建模板：$($project[0].Name)（只走 PackageReference）" -ForegroundColor Cyan
    dotnet build $project[0].FullName -c Release -nologo -p:Version=$version -p:RestoreForce=true
    if ($LASTEXITCODE -ne 0) { throw '模板构建失败' }

    $packageRoot = Join-Path $moduleRoot 'bin\module-package\Release'
    $packageDirs = @(Get-ChildItem -LiteralPath $packageRoot -Directory -ErrorAction SilentlyContinue)
    if ($packageDirs.Count -ne 1) {
        throw "模板应产出恰好一个包目录（实际 $($packageDirs.Count) 个）：$packageRoot"
    }

    $packageDir = $packageDirs[0].FullName
    $files = @(Get-ChildItem -LiteralPath $packageDir -File -Recurse |
        ForEach-Object { $_.FullName.Substring($packageDir.Length + 1) } | Sort-Object)
    Write-Host "[standalone] 模块包：$($files -join ', ')" -ForegroundColor DarkCyan

    if (-not (Test-Path (Join-Path $packageDir 'module.json'))) {
        throw "模块包缺少 module.json：$packageDir"
    }

    $manifest = Get-Content (Join-Path $packageDir 'module.json') -Raw | ConvertFrom-Json
    foreach ($resource in @($manifest.resources)) {
        if ([string]::IsNullOrWhiteSpace($resource)) { continue }
        if (-not (Test-Path (Join-Path $packageDir $resource))) {
            throw "模块包声明了资源目录 '$resource'，但包里没有：$packageDir"
        }
    }

    $hostLeak = @($files | Where-Object { $_ -like '*HYWDebugAssistant.Sdk*.dll' -or $_ -like 'HYWDebugAssistant.Access*.dll' })
    if ($hostLeak.Count -gt 0) {
        throw "模块包含宿主提供程序集：$($hostLeak -join '，')"
    }

    $hostProject = Join-Path $root 'tools\StandaloneModuleHost\HYWDebugAssistant.StandaloneModuleHost.csproj'
    $hostOutput = Join-Path $root 'tools\StandaloneModuleHost\bin\Release\net10.0'
    Write-Host '[standalone] 构建验证宿主' -ForegroundColor Cyan
    dotnet build $hostProject -c Release -nologo
    if ($LASTEXITCODE -ne 0) { throw 'StandaloneModuleHost 构建失败' }

    $hostDll = Join-Path $hostOutput 'HYWDebugAssistant.StandaloneModuleHost.dll'
    if (-not (Test-Path $hostDll)) { throw "验证宿主 DLL 不存在：$hostDll" }

    & dotnet $hostDll $packageDir
    if ($LASTEXITCODE -ne 0) { throw "模板装入失败（exit=$LASTEXITCODE）" }

    Write-Host '[standalone] 通过：模板只靠作者包构建出薄包，并被宿主按真实路径装入' -ForegroundColor Green
}
finally {
    if ($createdWork -and (Test-Path -LiteralPath $work)) {
        # 宿主进程退出后 Windows 释放文件句柄有延迟：重试几次再放弃（临时目录，不影响验证结论）。
        for ($attempt = 0; $attempt -lt 10; $attempt++) {
            try {
                Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop
            }
            catch {
                # 句柄还没释放：等一下再试。
            }
            if (-not (Test-Path -LiteralPath $work)) { break }
            Start-Sleep -Milliseconds 200
        }
        if (Test-Path -LiteralPath $work) {
            Write-Host "[standalone] 临时目录未能删除（进程句柄延迟释放）：$work" -ForegroundColor Yellow
        }
    }
}
