#Requires -Version 7.0
<#
.SYNOPSIS
    版本纪律审计（只读）：唯一版本事实源、四段版本、无格式 / 模块 / 类型版本。

.DESCRIPTION
    判据（事实源与规则见 docs/zh/project/versioning.md）：

      1. `version.props` 存在、四段都是 0..65535 的整数、轮次 >= 0（0 = 该版本线首次构建）；
         `Directory.Build.props` 导入它，且**只**派生一个四段 `<Version>`。
      2. 工程 / props / targets 里不得出现版本属性元素
         （`AssemblyVersion` / `FileVersion` / `InformationalVersion` / `PackageVersion` /
         `VersionPrefix` / `VersionSuffix`），也不得在 `Directory.Build.props` 之外出现 `<Version>`。
         XML 注释不参与判定。
      3. 源码 / 资源 / 脚本 / 文档里不得出现三段版本字面量（四段 IP 与 `§` 章节号先遮掉）；
         历史材料 `dev/archive` 与第三方版本清单（`dev/constraints.md`）不参与。
      4. 代码 / 资源 / 脚本里不得出现 `schemaVersion` / `contractVersion` /
         `frameworkCompatibility` / `layoutVersion` / `sdkContractVersion`
         —— 这些是"第二个版本轴"的标记。
      5. 模块清单 `id` / `dependsOn` 必须是两段（作者 / 模块名），必须有 `resources`，
         不得有 `frameworkCompatibility`。
      6. 文本 / 图标 / 主题 / 色板 JSON 根部不得有 `schemaVersion` / `contractVersion`。
      7. `release.json`（若存在）的 `version` 必须等于 `version.props`，且无旧字段。

    违规一律打印 `文件:行号`（最多 50 条）并 `exit 1`；目标 0 命中。

.EXAMPLE
    pwsh -NoProfile -File tools/audit-versions.ps1
#>
[CmdletBinding()]
param(
    # 审计根（缺省 = 本脚本所在仓库）；负向自检时指向临时脚手架。
    [string]$Root
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $root = Split-Path -Parent $PSScriptRoot
}
else {
    $root = [System.IO.Path]::GetFullPath($Root)
}

$problems = [System.Collections.Generic.List[string]]::new()

function Add-Problem([string]$message) {
    $problems.Add($message)
}

function Get-RelativePath([string]$path) {
    return $path.Substring($root.Length + 1).Replace('\', '/')
}

# `.workbuddy` 是助手的工作记忆（已被 .gitignore 排除，不进交付库），它不是产品的一部分，
# 也不受版本纪律约束 —— 把它扫进来只会让"记了发布版本号"变成门禁红灯。
$excludedPath = '\\(\.git|\.vs|\.workbuddy|bin|obj|artifacts|BenchmarkDotNet\.Artifacts|node_modules|TestResults)\\'

# 章节号 / IP 字面量不是"产品版本"，先遮掉再判（只影响这一条规则）。
function Get-MaskedLine([string]$line) {
    $masked = [regex]::Replace($line, '§[0-9.]*', '<section>')
    return [regex]::Replace($masked, '\b\d{1,3}(?:\.\d{1,3}){3}\b', '<address>')
}

function Test-CommentedLine([string]$line, [ref]$inComment) {
    if ($inComment.Value) {
        if ($line -match '-->') { $inComment.Value = $false }
        return $true
    }

    if ($line -match '<!--') {
        if ($line -notmatch '-->') { $inComment.Value = $true }
        return $true
    }

    return $false
}

# ---------------------------------------------------------------- 1. 唯一版本事实源

$versionText = '?'
$versionParsed = $false
$versionFile = Join-Path $root 'version.props'

if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    Add-Problem '缺少唯一版本事实源：version.props'
}
else {
    try {
        [xml]$versionXml = Get-Content -LiteralPath $versionFile -Raw -Encoding utf8
        $group = @($versionXml.Project.PropertyGroup)[0]
        $numbers = @{}

        foreach ($name in @('HywVersionMajor', 'HywVersionMinor', 'HywVersionPatch', 'HywVersionRound')) {
            $raw = [string]$group.$name
            $number = 0
            if (-not [int]::TryParse($raw, [ref]$number) -or $number -lt 0 -or $number -gt 65535) {
                Add-Problem "version.props 的 $name 不合法：'$raw'（必须是 0..65535 的整数）"
                continue
            }

            $numbers[$name] = $number
        }

        if ($numbers.Count -eq 4) {
            $versionText = '{0}.{1}.{2}.{3}' -f $numbers['HywVersionMajor'],
                $numbers['HywVersionMinor'], $numbers['HywVersionPatch'], $numbers['HywVersionRound']
            $versionParsed = $true
        }
    }
    catch {
        Add-Problem "version.props 无法解析：$($_.Exception.Message)"
    }
}

$buildPropsPath = Join-Path $root 'Directory.Build.props'
$expectedVersionLine = '<Version>$(HywVersionMajor).$(HywVersionMinor).$(HywVersionPatch).$(HywVersionRound)</Version>'

if (-not (Test-Path -LiteralPath $buildPropsPath -PathType Leaf)) {
    Add-Problem '缺少 Directory.Build.props'
}
else {
    $buildPropsText = Get-Content -LiteralPath $buildPropsPath -Raw -Encoding utf8
    if ($buildPropsText -notmatch 'version\.props') {
        Add-Problem 'Directory.Build.props 必须导入 version.props'
    }

    $expectedCount = ([regex]::Matches($buildPropsText, [regex]::Escape($expectedVersionLine))).Count
    if ($expectedCount -ne 1) {
        Add-Problem "Directory.Build.props 必须恰好有一行四段 <Version>（期望 $expectedVersionLine，实际 $expectedCount 行）"
    }
}

# ---------------------------------------------------------------- 2. 不得有第二个版本属性

$versionPropertyElement = [regex]::new(
    '<(AssemblyVersion|FileVersion|InformationalVersion|PackageVersion|VersionPrefix|VersionSuffix)>',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$versionTagPattern = [regex]::new('<Version>')
$projectExtensions = @('.csproj', '.props', '.targets')

$projectFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $_.FullName -notmatch $excludedPath -and $projectExtensions -contains $_.Extension.ToLowerInvariant()
    })

foreach ($file in $projectFiles) {
    $relative = Get-RelativePath $file.FullName
    if ($relative -eq 'Directory.Packages.props') {
        continue   # 第三方依赖的唯一来源：`PackageVersion Include=` 是它的正常写法
    }

    $lineNumber = 0
    $inComment = $false
    foreach ($line in (Get-Content -LiteralPath $file.FullName -Encoding utf8)) {
        $lineNumber++
        if (Test-CommentedLine $line ([ref]$inComment)) { continue }

        if ($versionPropertyElement.IsMatch($line)) {
            Add-Problem "${relative}:${lineNumber} 不得单独覆盖版本属性：$($line.Trim())"
        }

        if ($versionTagPattern.IsMatch($line) -and $line.Trim() -ne $expectedVersionLine) {
            Add-Problem "${relative}:${lineNumber} 不得出现独立 <Version>（唯一来源是 version.props）：$($line.Trim())"
        }
    }
}

# ---------------------------------------------------------------- 3. 不得有版本字面量

$versionLiteral = [regex]::new('(?<![\d.])\d+\.\d+\.\d+(?![\d.])')
$dateLiteral = [regex]::new('\b\d{4}[-/.]\d{1,2}[-/.]\d{1,2}\b')
$literalExtensions = @('.cs', '.ps1', '.json', '.md', '.axaml', '.slnx', '.yml', '.yaml')
$allowLiteralFiles = @(
    'version.props',
    'Directory.Packages.props',
    'global.json',
    'tools/license-allowlist.json',
    'dev/constraints.md'
)

$literalFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $_.FullName -notmatch $excludedPath -and $literalExtensions -contains $_.Extension.ToLowerInvariant()
    })

foreach ($file in $literalFiles) {
    $relative = Get-RelativePath $file.FullName
    if ($relative -like 'dev/archive/*') { continue }
    if ($allowLiteralFiles -contains $relative) { continue }

    $isDoc = $relative.EndsWith('.md')
    $lineNumber = 0
    foreach ($line in (Get-Content -LiteralPath $file.FullName -Encoding utf8)) {
        $lineNumber++
        $candidate = Get-MaskedLine $line
        if ($isDoc) {
            $candidate = [regex]::Replace($candidate, $dateLiteral, '<date>')
        }

        if ($versionLiteral.IsMatch($candidate)) {
            Add-Problem "${relative}:${lineNumber} 出现版本字面量（应派生自 version.props）：$($line.Trim())"
        }
    }
}

# ---------------------------------------------------------------- 4. 不得有第二版本轴字段

$tokenPattern = [regex]::new('(?i)\b(schemaVersion|contractVersion|frameworkCompatibility|layoutVersion|sdkContractVersion)\b')
$tokenExtensions = @('.cs', '.json', '.ps1', '.props', '.csproj', '.targets', '.axaml')
$tokenRoots = @('src', 'resources', 'tools', 'tests', 'templates')

foreach ($directoryName in $tokenRoots) {
    $directory = Join-Path $root $directoryName
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }

    $files = @(Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object {
            $_.FullName -notmatch $excludedPath -and $tokenExtensions -contains $_.Extension.ToLowerInvariant()
        })

    foreach ($file in $files) {
        $relative = Get-RelativePath $file.FullName
        if ($relative -eq 'tools/audit-versions.ps1') { continue }

        $lineNumber = 0
        foreach ($line in (Get-Content -LiteralPath $file.FullName -Encoding utf8)) {
            $lineNumber++
            if ($tokenPattern.IsMatch($line)) {
                Add-Problem "${relative}:${lineNumber} 出现第二版本轴字段（应删除，不迁移）：$($line.Trim())"
            }
        }
    }
}

# ---------------------------------------------------------------- 5. 模块清单：两段标识

$manifestFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'module.json' | Where-Object {
        $_.FullName -notmatch $excludedPath
    })

foreach ($file in $manifestFiles) {
    $relative = Get-RelativePath $file.FullName
    $manifest = $null
    try {
        $manifest = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        Add-Problem "${relative} 无法解析：$($_.Exception.Message)"
        continue
    }

    $names = @($manifest.PSObject.Properties.Name)
    $id = [string]$manifest.id
    if ($id -notmatch '^[a-z0-9-]+/[a-z0-9-]+$') {
        Add-Problem "${relative} 的 id 必须是两段（作者/模块名），实际 '$id'"
    }

    if ($names -contains 'frameworkCompatibility') {
        Add-Problem "${relative} 不得再声明 frameworkCompatibility"
    }

    if ($names -notcontains 'resources') {
        Add-Problem "${relative} 缺少 resources"
    }

    foreach ($dependency in @($manifest.dependsOn)) {
        if ($null -ne $dependency -and ([string]$dependency) -notmatch '^[a-z0-9-]+/[a-z0-9-]+$') {
            Add-Problem "${relative} 的 dependsOn 不是两段标识：'$dependency'"
        }
    }
}

# ---------------------------------------------------------------- 6. 资源表根部无格式版本

$resourceJsonFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
$resourcesDirectory = Join-Path $root 'resources'
if (Test-Path -LiteralPath $resourcesDirectory -PathType Container) {
    foreach ($file in (Get-ChildItem -LiteralPath $resourcesDirectory -Recurse -File -Filter '*.json')) {
        $resourceJsonFiles.Add($file)
    }
}

$paletteJson = Join-Path $root 'src\Ui\Styles\palette.json'
if (Test-Path -LiteralPath $paletteJson -PathType Leaf) {
    $resourceJsonFiles.Add((Get-Item -LiteralPath $paletteJson))
}

foreach ($file in $resourceJsonFiles) {
    $relative = Get-RelativePath $file.FullName
    $document = $null
    try {
        $document = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        Add-Problem "${relative} 无法解析：$($_.Exception.Message)"
        continue
    }

    if ($document -is [System.Array]) { continue }

    $names = @($document.PSObject.Properties.Name)
    foreach ($token in @('schemaVersion', 'contractVersion')) {
        if ($names -contains $token) {
            Add-Problem "${relative} 根部不得有 $token（格式没有版本）"
        }
    }
}

# ---------------------------------------------------------------- 7. release.json

$releaseFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'release.json' | Where-Object {
        $_.FullName -notmatch $excludedPath
    })

foreach ($file in $releaseFiles) {
    $relative = Get-RelativePath $file.FullName
    $release = $null
    try {
        $release = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        Add-Problem "${relative} 无法解析：$($_.Exception.Message)"
        continue
    }

    $names = @($release.PSObject.Properties.Name)
    if ($names -notcontains 'version') {
        Add-Problem "${relative} 缺少 version"
    }
    elseif ($versionParsed -and [string]$release.version -ne $versionText) {
        Add-Problem "${relative} 的 version（$($release.version)）与 version.props（$versionText）不一致"
    }

    foreach ($legacy in @('layoutVersion', 'sdkContractVersion', 'productVersion')) {
        if ($names -contains $legacy) {
            Add-Problem "${relative} 含旧字段 $legacy（已由 version 取代）"
        }
    }
}

# ---------------------------------------------------------------- 结果

if ($problems.Count -gt 0) {
    Write-Host "[FAIL] 版本纪律审计：$($problems.Count) 项违规（事实源与规则见 docs/zh/project/versioning.md）" -ForegroundColor Red
    $problems | Select-Object -First 50 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    if ($problems.Count -gt 50) {
        Write-Host "  …（其余 $($problems.Count - 50) 项略）" -ForegroundColor Red
    }

    exit 1
}

if (-not $versionParsed) {
    Write-Host '[FAIL] 版本纪律审计：version.props 不可用' -ForegroundColor Red
    exit 1
}

Write-Host "[OK] 版本纪律审计通过：$versionText（事实源 version.props）" -ForegroundColor Green
exit 0
