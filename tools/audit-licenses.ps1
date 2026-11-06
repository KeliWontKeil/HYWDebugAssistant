#Requires -Version 7.0
<#
.SYNOPSIS
    依赖许可审计（ADR-0024 §6 / ADR-0025 §7 的构建期门禁）。

.DESCRIPTION
    遍历解决方案内所有项目的直接 + 传递依赖，读取本地 NuGet 缓存中的 .nuspec，
    校验许可是否为宽松许可白名单；出现 GPL/LGPL/商业/无许可声明 时以非零退出码失败。

    许可来源解析顺序：
      1) <license type="expression">SPDX</license>
      2) <license type="file">FILE</license> → 读取许可文件文本并识别常见许可族
      3) <licenseUrl> 中的 licenses.nuget.org/<SPDX>
      4) tools/license-allowlist.json 中的显式豁免（人工核对并注明理由）

    包清单来源：优先读 restore 产物 `obj/project.assets.json`（与你 `dotnet list` 看到的是同一份数据，
    34 个项目读文件 <2 s）；assets 缺失或不可解析时回退到逐项目 `dotnet list package --include-transitive`。
    **回退不是静默通过**：两条路径都会把“读不到任何包”当成空集合并照常继续校验。

.NOTES
    数据来源：~/.nuget/packages/<id>/<version>/（离线，无需网络）。
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string[]]$Allow = @(
        'MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC', 'Zlib',
        'MS-PL', 'Unlicense', '0BSD', 'CC0-1.0'
    )
)

$ErrorActionPreference = 'Stop'

# NuGet 全局包目录只向 `dotnet` 要（不拼 `$env:USERPROFILE`：非 Windows 上它为空，
# 会让"找不到 nuspec"退化成静默通过）。见 tools/dotnet-env.ps1 头注释。
. (Join-Path $PSScriptRoot 'dotnet-env.ps1')
$globalPackages = Get-NuGetGlobalPackagesDirectory

# 同一个包会被多个项目/框架列出：许可解析（读 nuspec + 许可文件）只做一次。
$script:licenseCache = @{}

<#
.SYNOPSIS
    取某包版本的许可（带缓存）。
#>
function Get-PackageLicense {
    param([string]$Id, [string]$Version)

    $key = "$Id/$Version"
    if ($script:licenseCache.ContainsKey($key)) { return $script:licenseCache[$key] }

    $resolved = Resolve-PackageLicense -Id $Id -Version $Version
    $script:licenseCache[$key] = $resolved
    return $resolved
}

# ---------------- 许可文本识别（用于 type="file" 的包） ----------------

function Get-LicenseFromText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    if ($Text -match '(?i)Apache License\s*,?\s*Version 2\.0') { return 'Apache-2.0' }
    if ($Text -match '(?i)Permission is hereby granted, free of charge') { return 'MIT' }
    if ($Text -match '(?i)Redistribution and use in source and binary forms') {
        if ($Text -match '(?i)Neither the name') { return 'BSD-3-Clause' }
        return 'BSD-2-Clause'
    }
    if ($Text -match '(?i)Permission to use, copy, modify, and(/or)? distribute this software') { return 'ISC' }
    if ($Text -match '(?i)altered source versions must be plainly marked') { return 'Zlib' }
    return $null
}

# ---------------- 豁免清单（人工核对后登记） ----------------

function Get-Allowlist {
    param([string]$Root)

    $path = Join-Path $Root 'tools\license-allowlist.json'
    if (-not (Test-Path $path)) { return @() }
    try {
        return (Get-Content $path -Raw | ConvertFrom-Json)
    }
    catch {
        Write-Host "[license-audit] 警告：无法解析 $path（$($_.Exception.Message)）" -ForegroundColor Yellow
        return @()
    }
}

# ---------------- 包与许可解析 ----------------

<#
.SYNOPSIS
    从 restore 产物 `obj/project.assets.json` 读包清单（含传递依赖）。
.OUTPUTS
    失败返回 $null（调用方回退到 `dotnet list`）；成功返回包清单（可能为空数组）。
#>
function Get-PackageListFromAssets {
    param([string]$Path)

    try {
        $data = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }

    # 没有 targets = 该项目没有任何依赖（合法）；只有“读不出来”才回退（见 catch）。
    if (-not $data.targets) { return , @() }

    $result = @()
    foreach ($tfm in $data.targets.PSObject.Properties) {
        foreach ($library in $tfm.Value.PSObject.Properties) {
            # 只收真实 NuGet 包；项目引用（type=project）不是依赖项，许可审计不覆盖它们。
            if ($library.Value.type -ne 'package') { continue }

            $parts = $library.Name -split '/', 2
            if ($parts.Count -eq 2) {
                $result += [pscustomobject]@{ Id = $parts[0]; Version = $parts[1] }
            }
        }
    }

    # 用 `,$result` 返回数组：空数组也是“成功且没有依赖”，不能塌缩成 $null（那会被误判为读取失败）。
    return , $result
}

function Get-PackageListFromDotnet {
    param([string]$ProjectPath)

    $json = & dotnet list $ProjectPath package --include-transitive --format json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $json) { return @() }

    $data = $json | ConvertFrom-Json
    $result = @()
    foreach ($proj in $data.projects) {
        foreach ($fx in $proj.frameworks) {
            $all = @()
            if ($fx.topLevelPackages) { $all += $fx.topLevelPackages }
            if ($fx.transitivePackages) { $all += $fx.transitivePackages }
            foreach ($p in $all) {
                $result += [pscustomobject]@{ Id = $p.id; Version = $p.resolvedVersion }
            }
        }
    }
    return $result
}

function Get-PackageList {
    param([string]$ProjectPath)

    # 优先读 restore 产物：与 `dotnet list` 同源，但不必为 34 个项目各起一次 MSBuild（约 50 s）。
    $assets = Join-Path (Split-Path -Parent $ProjectPath) (Join-Path 'obj' 'project.assets.json')
    if (Test-Path -LiteralPath $assets) {
        $fromAssets = Get-PackageListFromAssets -Path $assets
        if ($null -ne $fromAssets) { return $fromAssets }
    }

    return Get-PackageListFromDotnet -ProjectPath $ProjectPath
}

function Resolve-PackageLicense {
    param([string]$Id, [string]$Version)

    $idLower = $Id.ToLowerInvariant()
    $pkgDir = Join-Path (Join-Path $script:globalPackages $idLower) $Version
    $nuspec = Join-Path $pkgDir "$idLower.nuspec"
    if (-not (Test-Path $nuspec)) { return @{ License = '<nuspec not found>'; Source = 'missing' } }

    [xml]$doc = Get-Content $nuspec -Raw
    $meta = $doc.package.metadata

    $licenseText = $null
    $licenseType = $null
    if ($meta.license) {
        $licenseType = $meta.license.type
        $licenseText = $meta.license.'#text'
        if (-not $licenseText) { $licenseText = $meta.license.InnerText }
    }

    # 1) SPDX 表达式
    if ($licenseType -eq 'expression' -and $licenseText) {
        return @{ License = $licenseText.Trim(); Source = 'expression' }
    }

    # 2) 许可文件 → 文本识别
    if ($licenseType -eq 'file' -and $licenseText) {
        $licFile = Join-Path $pkgDir ($licenseText -replace '/', '\')
        if (Test-Path $licFile) {
            $detected = Get-LicenseFromText (Get-Content $licFile -Raw)
            if ($detected) { return @{ License = $detected; Source = "file:$licenseText" } }
            return @{ License = "<unrecognized: $licenseText>"; Source = "file:$licenseText" }
        }
        return @{ License = "<missing file: $licenseText>"; Source = 'file-missing' }
    }

    # 3) licenseUrl（识别常见开源许可 URL）
    $url = $meta.licenseUrl
    if ($url) {
        if ($url -match 'licenses\.nuget\.org/(?<expr>[^/]+)$') { return @{ License = $Matches['expr']; Source = 'licenseUrl:nuget' } }
        if ($url -match '(?i)apache\.org/licenses/LICENSE-2\.0') { return @{ License = 'Apache-2.0'; Source = 'licenseUrl:apache' } }
        if ($url -match '(?i)opensource\.org/licenses/MIT') { return @{ License = 'MIT'; Source = 'licenseUrl:osi' } }
        if ($url -match '(?i)opensource\.org/licenses/BSD-3-Clause') { return @{ License = 'BSD-3-Clause'; Source = 'licenseUrl:osi' } }
        if ($url -match '(?i)opensource\.org/licenses/BSD-2-Clause') { return @{ License = 'BSD-2-Clause'; Source = 'licenseUrl:osi' } }
        if ($url -match '(?i)opensource\.org/licenses/ISC') { return @{ License = 'ISC'; Source = 'licenseUrl:osi' } }
        if ($url -match '(?i)mit-license\.org') { return @{ License = 'MIT'; Source = 'licenseUrl:mit-license.org' } }
        return @{ License = "<unresolved url: $url>"; Source = 'licenseUrl' }
    }

    return @{ License = '<none declared>'; Source = 'none' }
}

function Test-LicenseAllowed {
    param([string]$Expression, [string[]]$AllowList)

    if ([string]::IsNullOrWhiteSpace($Expression)) { return $false }
    $tokens = ($Expression -replace '[()]', ' ') -split '\s+(?i)(AND|OR|WITH)\s+|\s+' |
        Where-Object { $_ -and $_ -notin @('AND', 'OR', 'WITH', 'and', 'or', 'with') } |
        ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique
    if (-not $tokens) { return $false }
    foreach ($t in $tokens) { if ($AllowList -notcontains $t) { return $false } }
    return $true
}

# ---------------- 主流程 ----------------

$projects = Get-ChildItem -Path $Root -Recurse -Filter *.csproj -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
if (-not $projects) { Write-Host "[license-audit] 未找到项目" -ForegroundColor Yellow; exit 0 }

$allowlist = Get-Allowlist -Root $Root

$rows = @()
foreach ($proj in $projects) {
    foreach ($pkg in (Get-PackageList -ProjectPath $proj.FullName)) {
        # 自家发布的包（宿主 / 作者包）按仓库许可（LICENSE.txt = MIT）判定，不查 NuGet 缓存：
        # pack-sdk.ps1 每次打包前会清掉它们的缓存目录，而许可审计在打包之前跑 ⇒ 查缓存会误报“缺 nuspec”。
        $firstParty = $pkg.Id.StartsWith('HYWDebugAssistant.', [System.StringComparison]::Ordinal)
        $info = if ($firstParty) { @{ License = 'MIT'; Source = 'first-party（仓库 LICENSE.txt）' } }
            else { Get-PackageLicense -Id $pkg.Id -Version $pkg.Version }
        $ok = Test-LicenseAllowed -Expression $info.License -AllowList $Allow
        $note = $info.Source

        if (-not $ok) {
            $exempt = $allowlist | Where-Object { $_.package -eq $pkg.Id -and $_.version -eq $pkg.Version }
            if ($exempt) {
                $ok = $true
                $note = "exempt: $($exempt.reason)"
                $info.License = $exempt.license
            }
        }

        $rows += [pscustomobject]@{
            Package = $pkg.Id
            Version = $pkg.Version
            License = $info.License
            Allowed = $ok
            Note    = $note
        }
    }
}

$rows = $rows | Sort-Object Package, Version -Unique
$violations = $rows | Where-Object { -not $_.Allowed }

Write-Host ""
Write-Host "===== 依赖许可审计（$($rows.Count) 个包）=====" -ForegroundColor Cyan
$rows | Format-Table Package, Version, License, Allowed -AutoSize | Out-String | Write-Host

if ($violations) {
    Write-Host "❌ 许可审计未通过（白名单：$($Allow -join ', ')）：" -ForegroundColor Red
    $violations | Format-Table Package, Version, License, Note -AutoSize | Out-String | Write-Host
    Write-Host "如需豁免：在 tools/license-allowlist.json 中登记（package/version/license/reason），并说明核对证据。" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ 许可审计通过：全部依赖均为宽松许可（白名单内）" -ForegroundColor Green
exit 0
