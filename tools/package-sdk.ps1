#Requires -Version 7.0
<#
.SYNOPSIS
    SDK 发布：五个作者包 + 模块模板 + 本地 NuGet 源 + README + 许可，输出独立目录（不压缩）。

.DESCRIPTION
    外部作者拿到的是一整个目录：
      packages/                 五个 nupkg
      template/ModuleTemplate/  可构建模板（源码）
      NuGet.config              本地源指向 packages
      README.txt
      LICENSE.txt / THIRD_PARTY_NOTICES.md
      sdk.json                  版本、提交与文件哈希

    打包后会复制到临时目录，用本地包构建模板并由 StandaloneModuleHost 装入；失败不产出最终目录。

.EXAMPLE
    ./tools/package-sdk.ps1 -OutputDirectory D:\dist
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

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
    throw "无法从 version.props 取得四段版本：'$version'"
}

$commit = (& git rev-parse HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
    throw '无法读取 git 提交号；发布必须能追溯到源码提交'
}

$dirty = -not [string]::IsNullOrWhiteSpace((& git status --porcelain 2>$null | Out-String).Trim())
if ($dirty -and -not $AllowDirty) {
    throw '工作树不干净：请先提交，或显式使用 -AllowDirty'
}

$parent = [System.IO.Path]::GetFullPath($OutputDirectory)
$repo = [System.IO.Path]::GetFullPath($root)
$separator = [System.IO.Path]::DirectorySeparatorChar
if ($parent -eq $repo -or $parent.StartsWith($repo + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝输出：目标目录在仓库内部（$parent）——请指定仓库之外的目录"
}

$target = Join-Path $parent ('HYWDebugAssistant.Sdk-' + $version)
if ((Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)) {
    throw "拒绝输出：目标目录非空（$target）"
}

New-Item -ItemType Directory -Force -Path $parent | Out-Null
$temp = Join-Path $parent ('.HYWDebugAssistant.Sdk-' + $version + '.publishing-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$verifyWork = Join-Path ([System.IO.Path]::GetTempPath()) ('hyw-sdk-verify-' + [guid]::NewGuid().ToString('N').Substring(0, 8))

try {
    if (-not $SkipGate) {
        Write-Host '[sdk] 完整门禁：build.ps1 -Configuration Release' -ForegroundColor Cyan
        & (Join-Path $root 'build.ps1') -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw '门禁未通过' }
    }
    else {
        Write-Host '[sdk] [warn] 已跳过门禁（-SkipGate）' -ForegroundColor Yellow
    }

    Write-Host '[sdk] 生成作者包（pack-sdk）' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'pack-sdk.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw '作者包打包失败' }

    & (Join-Path $PSScriptRoot 'verify-sdk-packages.ps1')
    if ($LASTEXITCODE -ne 0) { throw '作者包内容校验失败' }

    $packagesSource = Join-Path $root 'artifacts\sdk-packages'
    $nupkgs = @(Get-ChildItem -LiteralPath $packagesSource -Filter *.nupkg -File)
    if ($nupkgs.Count -ne 5) {
        throw "作者包数量应为 5（实际 $($nupkgs.Count)）：$packagesSource"
    }

    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    $packagesTarget = Join-Path $temp 'packages'
    New-Item -ItemType Directory -Force -Path $packagesTarget | Out-Null
    foreach ($package in $nupkgs) {
        Copy-Item -LiteralPath $package.FullName -Destination $packagesTarget -Force
    }

    $templateSource = Join-Path $root 'templates\ModuleTemplate'
    if (-not (Test-Path -LiteralPath $templateSource -PathType Container)) {
        throw "模板目录不存在：$templateSource"
    }

    $templateTarget = Join-Path $temp 'template\ModuleTemplate'
    $templateFiles = @(Get-ChildItem -LiteralPath $templateSource -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|module-package)\\' })
    foreach ($file in $templateFiles) {
        $relative = [System.IO.Path]::GetRelativePath($templateSource, $file.FullName)
        $destination = Join-Path $templateTarget $relative
        $destinationDirectory = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $destinationDirectory)) {
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        }

        if ($file.Extension -in @('.csproj', '.config')) {
            $text = Get-Content -LiteralPath $file.FullName -Raw
            $text = $text.Replace('Version="$(Version)"', ('Version="' + $version + '"'))
            $text = $text.Replace('../../artifacts/sdk-packages', '..\..\packages')
            Set-Content -LiteralPath $destination -Value $text -Encoding utf8 -NoNewline
        }
        else {
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }

    $nuget = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <!-- 本 SDK 目录内的本地作者包。 -->
    <add key="hyw-sdk" value="packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath (Join-Path $temp 'NuGet.config') -Value $nuget -Encoding utf8

    $readme = @"
模块开发 SDK（产品版本 $version）

目录：
  packages/                 五个作者包
  template/ModuleTemplate/  模块模板（源码）
  NuGet.config              本地包源（packages）与 nuget.org

用法：
  1. 保持整个 SDK 目录在一起，不要只复制 template/ModuleTemplate（它的本地源指向 ../../packages）。
  2. cd template/ModuleTemplate
  3. dotnet build -c Release
  4. 产物在 bin/module-package/Release/TemplateModule/，在产品「模块清单」导入这一层目录。

带界面的模板需要从 nuget.org（或本机缓存）还原 Avalonia；没有网络时先确保缓存可用。
"@
    Set-Content -LiteralPath (Join-Path $temp 'README.txt') -Value $readme -Encoding utf8 -NoNewline
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination (Join-Path $temp 'LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $temp 'THIRD_PARTY_NOTICES.md') -Force

    # 用暂存副本做真实构建/装入（模板的 NuGet 源会被 verify 脚本改写为绝对本地源）。
    Write-Host '[sdk] 验证模板（本地包构建 + 独立宿主装入）' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'verify-standalone-module.ps1') `
        -SdkRoot $temp `
        -TemplateRoot (Join-Path $temp 'template\ModuleTemplate') `
        -WorkDirectory $verifyWork
    if ($LASTEXITCODE -ne 0) { throw '模板验证失败' }

    if (Test-Path -LiteralPath $verifyWork) {
        Remove-Item -LiteralPath $verifyWork -Recurse -Force -ErrorAction SilentlyContinue
    }

    $packageRecords = [System.Collections.Generic.List[object]]::new()
    foreach ($file in (Get-ChildItem -LiteralPath $packagesTarget -File | Sort-Object Name)) {
        $packageRecords.Add([pscustomobject]@{
            path = $file.Name
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    $templateRecords = [System.Collections.Generic.List[object]]::new()
    foreach ($file in (Get-ChildItem -LiteralPath $templateTarget -Recurse -File | Sort-Object FullName)) {
        $templateRecords.Add([pscustomobject]@{
            path = [System.IO.Path]::GetRelativePath($templateTarget, $file.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    $sdk = [pscustomobject]@{
        kind = 'sdk'
        version = [string]$version
        sourceCommit = [string]$commit
        sourceDirty = [bool]$dirty
        packages = $packageRecords
        template = $templateRecords
    }

    Set-Content -LiteralPath (Join-Path $temp 'sdk.json') -Value ($sdk | ConvertTo-Json -Depth 8) -Encoding utf8

    $written = Get-Content (Join-Path $temp 'sdk.json') -Raw | ConvertFrom-Json
    if ([string]$written.version -ne $version) { throw "sdk.json 的 version 不正确：$($written.version)" }
    if (@($written.packages).Count -ne 5) { throw "sdk.json 的包数量不正确：$(@($written.packages).Count)" }

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    Move-Item -LiteralPath $temp -Destination $target
    Write-Host "[sdk] SDK 发布完成：$target" -ForegroundColor Green
    Write-Host "[sdk] version=$version packages=5 template=$($templateRecords.Count) files" -ForegroundColor Green
}
catch {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $verifyWork) {
        Remove-Item -LiteralPath $verifyWork -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $verifyWork) {
        Remove-Item -LiteralPath $verifyWork -Recurse -Force -ErrorAction SilentlyContinue
    }
}
