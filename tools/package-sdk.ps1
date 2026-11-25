#Requires -Version 7.0
<#
.SYNOPSIS
    SDK 发布：五个作者包 + 模块模板 + 许可，输出独立目录（不压缩）。

.DESCRIPTION
    外部作者拿到的是一整个目录：
      packages/                 五个 nupkg
      template/ModuleTemplate/  可构建模板（源码，含自己的 NuGet.config）
      LICENSE.txt / THIRD_PARTY_NOTICES.md

    **本脚本不创作任何内容**：包来自 pack-sdk，模板从仓库复制，许可从仓库复制。
    唯一的改写是把模板工程里的包版本占位与本地源路径换成发布目录里的实际值——
    这是让模板脱离仓库后仍能构建的必要适配，不是新增内容。

    打包后会复制到临时目录，用本地包构建模板并由 StandaloneModuleHost 装入；失败不产出最终目录。

    发布记录（版本、提交、逐文件哈希）写在仓库的 `artifacts\release-manifests\`，不进发布目录。

    默认先跑完整门禁 `build.ps1 -Configuration Release`；`-SkipGate` 只用于本地迭代。
    门禁本身已经打包并校验过作者包（`build.ps1` 4c/4d），所以 `release.ps1` 会传
    `-SkipPack` 复用那批产物，避免同一次发布里把同一件事做两遍。
    `-SkipPack` 只跳过「打包 + 包内容校验」，**不跳过**模板的真实构建/装入验证。

.PARAMETER OutputDirectory
    **父目录**（不是 SDK 目录本身）：SDK 会落在 `<父目录>\HYWDebugAssistant.Sdk-<版本>\`。
    注意与 `tools\package-release.ps1` 不同：那个要的是**产品目录本身**。

.PARAMETER SkipPack
    复用 `artifacts\sdk-packages` 里已有的五个包，不重新 pack。
    包不存在或不完整时报错，不会静默退化成"用旧包"。

.EXAMPLE
    ./tools/package-sdk.ps1 -OutputDirectory 'D:\project\c# project\dist-0.2.1.0'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipGate,

    [switch]$SkipPack,

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

$packageVersion = (& (Join-Path $PSScriptRoot 'version.ps1') -PackageValue).Trim()
if ($packageVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "无法从 version.props 取得 NuGet 包版本：'$packageVersion'"
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
    throw "拒绝输出：目标目录在仓库内部（$parent）——请指定仓库外、工程目录下的新目录（如 <工程目录>\<版本>）"
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

    # 门禁里已经跑过 pack-sdk + verify-sdk-packages（build.ps1 4c）：
    # release.ps1 传 -SkipPack 复用那批产物，避免同一次发布把同一件事做两遍。
    # 跳过时**仍校验包产物**——复用不等于免检，"用了一批不知道是什么的包"才是真风险。
    if ($SkipPack) {
        Write-Host '[sdk] 复用已有作者包（-SkipPack），仅校验产物' -ForegroundColor Cyan
        if (-not (Test-Path -LiteralPath (Join-Path $root 'artifacts\sdk-packages'))) {
            throw '已指定 -SkipPack，但 artifacts\sdk-packages 不存在：请去掉 -SkipPack 重新打包'
        }

        & (Join-Path $PSScriptRoot 'verify-sdk-packages.ps1')
        if ($LASTEXITCODE -ne 0) { throw '已有作者包校验失败（-SkipPack）；请去掉该开关重新打包' }
    }
    else {
        Write-Host '[sdk] 生成作者包（pack-sdk）' -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot 'pack-sdk.ps1') -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw '作者包打包失败' }

        & (Join-Path $PSScriptRoot 'verify-sdk-packages.ps1')
        if ($LASTEXITCODE -ne 0) { throw '作者包内容校验失败' }
    }

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
            $text = $text.Replace('Version="$(Version)"', ('Version="' + $packageVersion + '"'))
            $text = $text.Replace('../../artifacts/sdk-packages', '..\..\packages')
            Set-Content -LiteralPath $destination -Value $text -Encoding utf8 -NoNewline
        }
        else {
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }

    # 只搬运仓库里已有的文件。SDK 根不放 NuGet.config：模板自带的那份（已被改写成
    # ../../packages）就够——它从 template/ModuleTemplate 往上两级正好落在 SDK 根上。
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

    # 发布记录写到仓库 artifacts\（构建产物区），不进发布目录——
    # 发布物只应包含包、模板、许可，不该混进脚本拼的元数据。
    $sdk = [pscustomobject]@{
        kind = 'sdk'
        directory = Split-Path -Leaf $target
        version = [string]$version
        sourceCommit = [string]$commit
        sourceDirty = [bool]$dirty
        packages = $packageRecords
        template = $templateRecords
    }

    $manifestDirectory = Join-Path $root 'artifacts\release-manifests'
    New-Item -ItemType Directory -Force -Path $manifestDirectory | Out-Null
    $manifestPath = Join-Path $manifestDirectory ((Split-Path -Leaf $target) + '.json')
    Set-Content -LiteralPath $manifestPath -Value ($sdk | ConvertTo-Json -Depth 8) -Encoding utf8

    $written = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]$written.version -ne $version) { throw "发布记录的 version 不正确：$($written.version)" }
    if (@($written.packages).Count -ne 5) { throw "发布记录的包数量不正确：$(@($written.packages).Count)" }

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    Move-Item -LiteralPath $temp -Destination $target
    Write-Host "[sdk] SDK 发布完成：$target" -ForegroundColor Green
    Write-Host "[sdk] version=$version packageVersion=$packageVersion packages=5 template=$($templateRecords.Count) files" -ForegroundColor Green
    Write-Host "[sdk] 发布记录：$manifestPath" -ForegroundColor Green
}
catch {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $verifyWork) {
        Remove-Item -LiteralPath $verifyWork -Recurse -Force -ErrorAction SilentlyContinue
    }

    # 记录先于落位写出：失败时一并撤掉，免得 artifacts 里留下指向不存在目录的清单。
    if ($manifestPath -and (Test-Path -LiteralPath $manifestPath)) {
        Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $verifyWork) {
        Remove-Item -LiteralPath $verifyWork -Recurse -Force -ErrorAction SilentlyContinue
    }
}
