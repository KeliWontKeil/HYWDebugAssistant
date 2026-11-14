#Requires -Version 7.0
<#
.SYNOPSIS
    版本写入口（全仓唯一）：读写仓库根的 `version.props`。

.DESCRIPTION
    版本 = 四段 `大.小.修订.轮次`（`M.m.p.r`）。事实源只有一个文件（`version.props`），
    本脚本是推荐的唯一写入口；其余位置一律由 MSBuild 派生。

    · `r` = 该版本线的第几次构建，从 0 起（0 = 首次构建）；`-Round` 只加它；
    · 主 / 小 / 修订**只由人显式指定**（`-Major` / `-Minor` / `-Patch` / `-Set`）；
      构建、测试、门禁从不改版本；
    · 提交是**推荐**不是强制：手工提交、跨轮次 / 跨版本提交、中间态临时保存都允许，
      版本号只要求"交付构建那一刻"是正确的（见 docs/zh/project/versioning.md）。

.EXAMPLE
    ./tools/version.ps1                     # 显示当前版本
    ./tools/version.ps1 -Value              # 只打印 M.m.p.r（供其他脚本取用）
    ./tools/version.ps1 -PackageValue       # 只打印 NuGet 规范化包版本（末段 0 时省略）
    ./tools/version.ps1 -Round              # 轮次 +1
    ./tools/version.ps1 -Minor              # 小版本 +1（修订归 0、轮次归 0）
    ./tools/version.ps1 -Set M.m.p.r        # 显式设置四段（允许跨版本线）
    ./tools/version.ps1 -Commit "说明"      # 轮次 +1 并提交（标题 = 版本 + 空格 + 说明）
    ./tools/version.ps1 -Round -DryRun      # 只打印将要发生的变化
#>
[CmdletBinding(DefaultParameterSetName = 'Show')]
param(
    [Parameter(ParameterSetName = 'Show')]
    [switch]$Show,

    [Parameter(ParameterSetName = 'Value')]
    [switch]$Value,

    [Parameter(ParameterSetName = 'PackageValue')]
    [switch]$PackageValue,

    [Parameter(ParameterSetName = 'Round')]
    [switch]$Round,

    [Parameter(ParameterSetName = 'Major')]
    [switch]$Major,

    [Parameter(ParameterSetName = 'Minor')]
    [switch]$Minor,

    [Parameter(ParameterSetName = 'Patch')]
    [switch]$Patch,

    [Parameter(ParameterSetName = 'Set')]
    [string]$Set,

    [Parameter(ParameterSetName = 'Commit')]
    [string]$Commit,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $root 'version.props'

function Read-Version {
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
        throw "缺少版本事实源：$versionFile"
    }

    [xml]$document = Get-Content -LiteralPath $versionFile -Raw -Encoding utf8
    $group = @($document.Project.PropertyGroup)[0]
    if ($null -eq $group) {
        throw "version.props 缺少 PropertyGroup：$versionFile"
    }

    $values = @{}
    foreach ($name in @('HywVersionMajor', 'HywVersionMinor', 'HywVersionPatch', 'HywVersionRound')) {
        $raw = [string]$group.$name
        $number = 0
        if (-not [int]::TryParse($raw, [ref]$number) -or $number -lt 0 -or $number -gt 65535) {
            throw "version.props 的 $name 不合法：'$raw'（必须是 0..65535 的整数）"
        }

        $values[$name] = $number
    }

    return [pscustomobject]@{
        Major = $values['HywVersionMajor']
        Minor = $values['HywVersionMinor']
        Patch = $values['HywVersionPatch']
        Round = $values['HywVersionRound']
    }
}

function Format-Version($value) {
    return "$($value.Major).$($value.Minor).$($value.Patch).$($value.Round)"
}

function Format-PackageVersion($value) {
    # NuGet 规范化：第四段为 0 时省略；其余情况仍保留四段。
    if ($value.Round -eq 0) {
        return "$($value.Major).$($value.Minor).$($value.Patch)"
    }

    return Format-Version $value
}

function Write-VersionFile($value) {
    $content = @"
<Project>

  <!-- 全仓唯一的版本事实源（四段 = 大.小.修订.轮次）；只允许 tools/version.ps1 写入。 -->
  <PropertyGroup>
    <HywVersionMajor>{0}</HywVersionMajor>
    <HywVersionMinor>{1}</HywVersionMinor>
    <HywVersionPatch>{2}</HywVersionPatch>
    <HywVersionRound>{3}</HywVersionRound>
  </PropertyGroup>

</Project>
"@ -f $value.Major, $value.Minor, $value.Patch, $value.Round

    Set-Content -LiteralPath $versionFile -Value $content -Encoding utf8
}

function Test-IsLowerVersion($candidate, $baseline) {
    if ($candidate.Major -ne $baseline.Major) { return $candidate.Major -lt $baseline.Major }
    if ($candidate.Minor -ne $baseline.Minor) { return $candidate.Minor -lt $baseline.Minor }
    if ($candidate.Patch -ne $baseline.Patch) { return $candidate.Patch -lt $baseline.Patch }
    return $candidate.Round -lt $baseline.Round
}

$current = Read-Version
$currentText = Format-Version $current

if ($Value) {
    # 只输出值本身：其他脚本（pack-sdk / package-release）直接消费，不重算拼接。
    Write-Output $currentText
    return
}

if ($PackageValue) {
    Write-Output (Format-PackageVersion $current)
    return
}

$next = [pscustomobject]@{
    Major = $current.Major
    Minor = $current.Minor
    Patch = $current.Patch
    Round = $current.Round
}

$changed = $false
$message = ''

if ($Round) {
    $next.Round++
    $changed = $true
}
elseif ($Major) {
    $next.Major++
    $next.Minor = 0
    $next.Patch = 0
    $next.Round = 0
    $changed = $true
}
elseif ($Minor) {
    $next.Minor++
    $next.Patch = 0
    $next.Round = 0
    $changed = $true
}
elseif ($Patch) {
    $next.Patch++
    $next.Round = 0
    $changed = $true
}
elseif ($PSBoundParameters.ContainsKey('Set')) {
    if ($Set -notmatch '^(\d+)\.(\d+)\.(\d+)\.(\d+)$') {
        throw "-Set 需要四段：大.小.修订.轮次（如 M.m.p.r），收到 '$Set'"
    }

    $next.Major = [int]$Matches[1]
    $next.Minor = [int]$Matches[2]
    $next.Patch = [int]$Matches[3]
    $next.Round = [int]$Matches[4]
    $changed = $true
}

if ($PSBoundParameters.ContainsKey('Commit')) {
    $message = $Commit.Trim()
    if ($message.Length -eq 0) {
        throw "-Commit 需要一句非空说明（提交标题 = 版本 + 空格 + 说明）"
    }

    if ($message -match '[\r\n]') {
        throw "-Commit 的说明只能是单行"
    }

    $next.Round++
    $changed = $true
}

$nextText = Format-Version $next

if (-not $changed) {
    Write-Host "[version] 当前：$currentText"
    Write-Host "[version] 推荐提交标题：$currentText <说明>"
    Write-Host "[version] 事实源：$versionFile"
    return
}

if (Test-IsLowerVersion $next $current) {
    Write-Warning "新版本 $nextText 低于当前 $currentText（允许回退，请确认）"
}

if ($DryRun) {
    Write-Host "[version] 将写入：$currentText → $nextText（-DryRun，未写盘）"
    if ($message.Length -gt 0) {
        Write-Host "[version] 将提交：git commit -m `"$nextText $message`""
    }

    return
}

Write-VersionFile $next
Write-Host "[version] 已写入：$currentText → $nextText（$versionFile）"

if ($message.Length -gt 0) {
    Push-Location $root
    try {
        git add -A
        if ($LASTEXITCODE -ne 0) {
            throw "git add 失败（版本文件已写入，未提交）"
        }

        git commit -m "$nextText $message"
        if ($LASTEXITCODE -ne 0) {
            throw "git commit 失败（版本文件已写入，未提交）"
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "[version] 已提交：$nextText $message"
}
else {
    Write-Host "[version] 推荐提交标题：$nextText <说明>"
}
