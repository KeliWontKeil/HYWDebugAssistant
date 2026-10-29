#Requires -Version 7.0
<#
.SYNOPSIS
  资源与文本审计（源侧）：出厂种子的形状、主题包、文本表、迁移锁死、`{loc:Tr}` 键与死键。

.DESCRIPTION
  判据（与 docs/design/resource-architecture.md 阶段 E 对齐；**违规一律打印 `文件:行号` 并 exit 1**）：

    ① **出厂资源的目录形状**
       · `resources/` 下的每个子树都必须在脚本里**登记访问模式**（当前：`seeds` = 投放、`i18n` = 就地读）；
         出现未登记的子树即判红（新增资源类别必须登记）；
       · `seeds/` 下只允许**已登记的目标子树**（当前仅 `settings/`）；
       · 每个**叶子目录单元**都必须有清单：主题包 = `theme.json`；
       · 设置基线不手工入库：仓库里不得出现 `seeds/settings/settings.json`
         （默认值的唯一来源是代码；发布时由 `--emit-default-settings` 生成到导出目录）。

    ② **种子主题包合法**（每个 `seeds/settings/appearance/themes/<id>/`）
       · `theme.json` 可解析、`schemaVersion` 是认识的版本；
       · `base ∈ {light, dark}`；
       · `palette` 的每个键都是色板契约（`src/Ui/Styles/palette.json` 的 tokens）里登记过的 token；
       · 清单里的 `id` 与目录名一致（id 的权威是目录名，写错说明它是从别处抄来的）；
       · `wallpaper` 是**同目录单文件名**且文件存在、是 PNG（magic bytes）、体积不超过上限。
         上限与 `src/Core/Themes/ThemeStore.cs` 的 `MaxWallpaperBytes` **同值**（本脚本会读那份定义核对，
         两处不一致即判红——否则"审计通过"与"运行期拒收"会各说各话）。

    ③ **文本表**（每份 `resources/i18n/<程序集简名>/*.json`）
       · 可解析、`schemaVersion` 认知；
       · `language` 与文件名首段一致（`zh-Hans.json` / `zh-Hans.settings.json` → `zh-Hans`）；
       · 键格式 `^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$`，值是字符串；
       · 同一拥有者同一语言的**跨分片键不重复**（运行期是"先者胜"，重复即是第二份事实）；
       · 同一拥有者各语言的**键集合**与每个键的**占位符集合**（`{0}` / `{1}`…）一致。

    ④ **可本地化层零中文**（终态）
       · `src/Core` `src/Hosting` `src/DataPlane` `src/Access` `src/Core.Api` `src/Ui` `src/FrameworkUi`
         `src/Sdk.Ui` `src/App` `src/Modules` 下的 `.cs` 字符串字面量与 `.axaml` 属性值/文本不得含中文；
       · 文件级豁免（脚本内写明理由）：`src/Sdk/**`、`src/Infrastructure/**`、`src/Sdk.Testing/**`、
         `src/Sdk.AccessKit/**`、`src/**/Design/*`、`src/App/Program.cs`；
       · 行级豁免：该行或紧邻上一行含 `// i18n-exempt: <理由>` ⇒ 跳过该行并打印清单。脚本结束打印
         "行级豁免 N 处"并逐条列出，供人工复核；
       · C# 跳过 `//` / `///` 注释、XAML 跳过 `<!-- -->`；`RequiresUnreferencedCode` 行按注释同级跳过。

    ⑤ **XAML 的 `{loc:Tr X}` 键存在**：`X` 必须在该 XAML **所属程序集**（最近一层 csproj）的任一份表里。
       XAML 缺表 / 键拼错 ⇒ 运行期只会显示 `⟦key⟧`，编译期完全合法，因此必须在这里拦。

    ⑥ **死键**：表里的每个键都必须在 `src/` 里被引用（XAML 的 `{loc:Tr X}` 或 C# 字符串字面量 `"X"`）。
       允许 C# 通道（例如动态拼选项文案的键）；只存在表里、没人取用的键是过期数据。

.PARAMETER Root
  被审计的根目录（默认 = 脚本所在目录的上一级，即仓库根）。
  负向自检时可指向临时副本，用来证明"坏输入确实判红"。

.EXAMPLE
  ./tools/audit-resources.ps1
  ./tools/audit-resources.ps1 -Root D:\tmp\hyw-audit-fixture
#>
[CmdletBinding()]
param(
    [string]$Root
)

$ErrorActionPreference = 'Stop'

$root = if ($Root) { [System.IO.Path]::GetFullPath($Root) } else { Split-Path -Parent $PSScriptRoot }
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    Write-Host "[FAIL] 审计根不存在：$root" -ForegroundColor Red
    exit 1
}

# 主题清单 / 文本表认识的结构版本（与 ThemeStore.CurrentSchemaVersion、TextCatalog.SchemaVersion 同步）
$themeSchemaVersion = 1
$textSchemaVersion = 1

# 壁纸体积上限的**来源**是 ThemeStore.MaxWallpaperBytes；下面只是缺省值，
# 读到源码定义后会覆盖它，并核对两者一致（不接受"脚本比运行期松"）。
$maxWallpaperBytes = 32L * 1024 * 1024

$themeStoreRel = 'src/Core/Themes/ThemeStore.cs'
$paletteRel = 'src/Ui/Styles/palette.json'
$storagePathsRel = 'src/Infrastructure/Storage/StoragePaths.cs'
$textCatalogRel = 'src/Sdk/Text/TextCatalog.cs'

# 审计自己的口径：resources/ 子树名与文本包名（访问模式登记见下方 $resourceSubtrees）。
$resourcesDirectoryName = 'resources'
$textPacksDirectoryName = 'i18n'

# 可本地化层零中文：文本已全部外置；新增中文必须进 i18n 表（或显式行级豁免）。

$script:problems = [System.Collections.Generic.List[object]]::new()
$script:notes = [System.Collections.Generic.List[string]]::new()

function Get-RelPath([string]$fullPath) {
    return [System.IO.Path]::GetRelativePath($root, $fullPath).Replace('\', '/')
}

function Add-Problem {
    param(
        [string]$Location,
        [string]$Message
    )

    $script:problems.Add([pscustomobject]@{ Location = $Location; Message = $Message })
}

function Get-FirstLineNumber {
    param(
        [string]$Path,
        [string]$Pattern
    )

    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $lineNumber++
        if ($line -match $Pattern) { return $lineNumber }
    }

    return 0
}

# 从 C# 源码里读一个字符串常量（`Name = "value"`）；读不到返回 $null。
function Get-StringConstant {
    param(
        [string]$Path,
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }

    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $lineNumber++
        $match = [regex]::Match($line, [regex]::Escape($Name) + '\s*=\s*"(?<value>[^"]+)"')
        if ($match.Success) {
            return [pscustomobject]@{ Value = $match.Groups['value'].Value; Line = $lineNumber }
        }
    }

    return $null
}

function Get-LeafDirectories {
    param([string]$Directory)

    $subdirectories = @(Get-ChildItem -LiteralPath $Directory -Directory -Force -ErrorAction SilentlyContinue)
    if ($subdirectories.Count -eq 0) {
        return @($Directory)
    }

    $leaves = @()
    foreach ($subdirectory in $subdirectories) {
        $leaves += Get-LeafDirectories -Directory $subdirectory.FullName
    }

    return $leaves
}

function Test-ChineseText([string]$value) {
    return $value -match '[\u4e00-\u9fff]'
}

function Get-Placeholders([string]$value) {
    $set = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($value, '\{(\d+)\}')) {
        $null = $set.Add($match.Groups[1].Value)
    }

    return (@($set) -join ',')
}

# C# 的中文文案条数：只数字符串字面量；`//` / `///` / `/* */` 与行尾注释不算。
# 含 `RequiresUnreferencedCode` 的行同样跳过：编译期诊断属性不渲染到界面（R2d 裁决）。
function Measure-CSharpChineseLiterals([string]$path) {
    $count = 0
    $firstLine = 0
    $lineNumber = 0
    $inBlockComment = $false
    # 多行诊断属性（`[RequiresUnreferencedCode(...)]`）：从开括号行起到 `)]` 前都按注释同级跳过。
    $inDiagnosticAttribute = $false

    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        $lineNumber++
        $trimmed = $line.TrimStart()

        if ($inDiagnosticAttribute) {
            if ($line.Contains(')]')) { $inDiagnosticAttribute = $false }
            continue
        }

        if ($inBlockComment) {
            if ($trimmed.Contains('*/')) { $inBlockComment = $false }
            continue
        }

        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*')) { continue }
        if ($trimmed.StartsWith('/*')) {
            if (-not $trimmed.Contains('*/')) { $inBlockComment = $true }
            continue
        }

        if ($line.Contains('RequiresUnreferencedCode')) {
            if (-not $line.Contains(')]')) { $inDiagnosticAttribute = $true }
            continue
        }

        $index = 0
        $inString = $false
        $verbatim = $false
        $buffer = [System.Text.StringBuilder]::new()

        while ($index -lt $line.Length) {
            $ch = $line[$index]

            if (-not $inString) {
                if ($ch -eq '"') {
                    $inString = $true
                    $verbatim = $index -gt 0 -and $line[$index - 1] -eq '@'
                    $null = $buffer.Clear()
                }
                elseif ($ch -eq '/' -and $index + 1 -lt $line.Length -and $line[$index + 1] -eq '/') {
                    break   # 行尾注释不算
                }

                $index++
                continue
            }

            if ($verbatim -and $ch -eq '"') {
                if ($index + 1 -lt $line.Length -and $line[$index + 1] -eq '"') {
                    $null = $buffer.Append('"')
                    $index += 2
                    continue
                }

                $inString = $false
                if (Test-ChineseText $buffer.ToString()) {
                    $count++
                    if ($firstLine -eq 0) { $firstLine = $lineNumber }
                }

                $index++
                continue
            }

            if (-not $verbatim -and $ch -eq '\') {
                $index += 2
                continue
            }

            if (-not $verbatim -and $ch -eq '"') {
                $inString = $false
                if (Test-ChineseText $buffer.ToString()) {
                    $count++
                    if ($firstLine -eq 0) { $firstLine = $lineNumber }
                }

                $index++
                continue
            }

            $null = $buffer.Append($ch)
            $index++
        }
    }

    return [pscustomobject]@{ Count = $count; FirstLine = $firstLine }
}

# XAML 的中文文案条数：只看属性值（`="..."`）与文本节点；`<!-- -->` 注释不算。
function Measure-XamlChineseLiterals([string]$path) {
    $raw = [System.IO.File]::ReadAllText($path)
    $stripped = [regex]::Replace($raw, '<!--.*?-->', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $count = 0

    foreach ($match in [regex]::Matches($stripped, '="(?<value>[^"]*)"')) {
        if (Test-ChineseText $match.Groups['value'].Value) { $count++ }
    }

    foreach ($match in [regex]::Matches($stripped, '>(?<value>[^<>]*)<')) {
        if (Test-ChineseText $match.Groups['value'].Value) { $count++ }
    }

    $firstLine = 0
    $inComment = $false
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        $lineNumber++
        if ($line.Contains('<!--')) { $inComment = $true }
        if ($inComment) {
            if ($line.Contains('-->')) { $inComment = $false }
            continue
        }

        if (Test-ChineseText $line) { $firstLine = $lineNumber; break }
    }

    return [pscustomobject]@{ Count = $count; FirstLine = $firstLine }
}

# C# 文件里的字符串字面量（不含注释）；死键检查用。
function Measure-CSharpLiterals([string]$path) {
    $literals = [System.Collections.Generic.List[string]]::new()
    $inBlockComment = $false
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        $trimmed = $line.TrimStart()
        if ($inBlockComment) {
            if ($trimmed.Contains('*/')) { $inBlockComment = $false }
            continue
        }

        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*')) { continue }
        if ($trimmed.StartsWith('/*')) {
            if (-not $trimmed.Contains('*/')) { $inBlockComment = $true }
            continue
        }

        foreach ($match in [regex]::Matches($line, '"(?<value>(?:[^"\\]|\\.)*)"')) {
            $literals.Add($match.Groups['value'].Value)
        }
    }

    return $literals
}

# ---------------------------------------------------------------- 公共事实：色板 token

$paletteTokens = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$palettePath = Join-Path $root $paletteRel
if (-not (Test-Path -LiteralPath $palettePath -PathType Leaf)) {
    Add-Problem $paletteRel '色板契约不存在（主题 palette 的键必须在这份契约里登记）'
}
else {
    try {
        $palette = Get-Content -LiteralPath $palettePath -Raw -Encoding utf8 | ConvertFrom-Json
        foreach ($token in @($palette.tokens)) {
            if ($token -and $token.key) { $null = $paletteTokens.Add([string]$token.key) }
        }
    }
    catch {
        Add-Problem $paletteRel "色板契约无法解析：$($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------- ② 壁纸上限的来源核对

$themeStorePath = Join-Path $root $themeStoreRel
if (-not (Test-Path -LiteralPath $themeStorePath -PathType Leaf)) {
    Add-Problem $themeStoreRel '主题存储不存在（壁纸上限的来源）'
}
else {
    $storeText = Get-Content -LiteralPath $themeStorePath -Raw -Encoding utf8
    $definition = [regex]::Match(
        $storeText,
        'MaxWallpaperBytes\s*=\s*(?<a>[0-9_]+)L?\s*\*\s*(?<b>[0-9_]+)\s*\*\s*(?<c>[0-9_]+)')

    if (-not $definition.Success) {
        Add-Problem $themeStoreRel '读不到 MaxWallpaperBytes 的定义（审计与运行期的上限必须同值）'
    }
    else {
        $fromSource = [long]$definition.Groups['a'].Value.Replace('_', '') *
            [long]$definition.Groups['b'].Value.Replace('_', '') *
            [long]$definition.Groups['c'].Value.Replace('_', '')
        if ($fromSource -ne $maxWallpaperBytes) {
            Add-Problem $themeStoreRel ("MaxWallpaperBytes = {0}，而审计脚本用 {1}（两处必须同值）" -f $fromSource, $maxWallpaperBytes)
            $maxWallpaperBytes = $fromSource
        }
    }
}

# ---------------------------------------------------------------- ① 出厂种子的目录形状

$resourcesRoot = Join-Path $root $resourcesDirectoryName
$seedsRoot = Join-Path $resourcesRoot 'seeds'
$i18nRoot = Join-Path $resourcesRoot $textPacksDirectoryName

# resources/ 的每个子树都必须登记**访问模式**：投放（复制进用户目录、可编辑）与就地读（只读）
# 是两种语义，混在一起会让"这份文件会不会被写回用户目录"变成猜测。新增第三类必须在这里登记。
$resourceSubtrees = [ordered]@{
    'seeds' = '投放（复制进用户目录，可编辑）'
    'i18n'  = '就地读（只读；运行时按 <拥有者>/<culture>.json 加载）'
}

$registeredSubtrees = [ordered]@{
    'settings' = '设置（单文件 settings.json + 外观主题包）'
}

# ---- 跨层一致性：`resources` / `i18n` 两个目录名在三处出现，必须同值 ----
# Infrastructure 与 Sdk 零依赖、不能共享常量；这里用正则从两份源码里读常量值，
# 任何一处漂移都判红（否则"发布布局"与"开发布局"会各读各的目录）。
$storagePathsPath = Join-Path $root $storagePathsRel
$textCatalogPath = Join-Path $root $textCatalogRel

$storageResources = Get-StringConstant -Path $storagePathsPath -Name 'ResourcesDirectoryName'
$storageTextPacks = Get-StringConstant -Path $storagePathsPath -Name 'TextPacksDirectoryName'
$catalogResources = Get-StringConstant -Path $textCatalogPath -Name 'ResourcesDirectoryName'
$catalogTextPacks = Get-StringConstant -Path $textCatalogPath -Name 'PackDirectoryName'

if ($null -eq $storageResources) {
    Add-Problem $storagePathsRel '读不到 ResourcesDirectoryName（资源根名必须是常量，审计要核对它）'
}
elseif ($storageResources.Value -ne $resourcesDirectoryName) {
    Add-Problem "${storagePathsRel}:$($storageResources.Line)" `
        ("ResourcesDirectoryName = '{0}'，审计口径是 '{1}'" -f $storageResources.Value, $resourcesDirectoryName)
}

if ($null -eq $storageTextPacks) {
    Add-Problem $storagePathsRel '读不到 TextPacksDirectoryName（文本包名必须是常量，审计要核对它）'
}
elseif ($storageTextPacks.Value -ne $textPacksDirectoryName) {
    Add-Problem "${storagePathsRel}:$($storageTextPacks.Line)" `
        ("TextPacksDirectoryName = '{0}'，审计口径是 '{1}'" -f $storageTextPacks.Value, $textPacksDirectoryName)
}

if ($null -eq $catalogResources) {
    Add-Problem $textCatalogRel '读不到 ResourcesDirectoryName（默认根解析的资源根名）'
}
elseif ($catalogResources.Value -ne $resourcesDirectoryName) {
    Add-Problem "${textCatalogRel}:$($catalogResources.Line)" `
        ("ResourcesDirectoryName = '{0}' 与审计口径 '{1}' 不一致（默认根会指向别的目录）" -f `
            $catalogResources.Value, $resourcesDirectoryName)
}

if ($null -eq $catalogTextPacks) {
    Add-Problem $textCatalogRel '读不到 PackDirectoryName（默认根解析的文本包名）'
}
elseif ($catalogTextPacks.Value -ne $textPacksDirectoryName) {
    Add-Problem "${textCatalogRel}:$($catalogTextPacks.Line)" `
        ("PackDirectoryName = '{0}' 与审计口径 '{1}' 不一致（默认根会指向别的目录）" -f `
            $catalogTextPacks.Value, $textPacksDirectoryName)
}

if ($null -ne $storageTextPacks -and $null -ne $catalogTextPacks `
    -and $storageTextPacks.Value -ne $catalogTextPacks.Value) {
    Add-Problem "${textCatalogRel}:$($catalogTextPacks.Line)" `
        ("PackDirectoryName = '{0}' 与 StoragePaths.TextPacksDirectoryName = '{1}' 不一致（显式根与默认根会分叉）" -f `
            $catalogTextPacks.Value, $storageTextPacks.Value)
}

if (-not $resourceSubtrees.Contains($textPacksDirectoryName)) {
    Add-Problem 'tools/audit-resources.ps1' `
        ("文本包子树 '{0}' 没有登记访问模式（resources/ 子树登记表：{1}）" -f `
            $textPacksDirectoryName, ($resourceSubtrees.Keys -join ', '))
}

$themePackageCount = 0

if (-not (Test-Path -LiteralPath $resourcesRoot)) {
    $script:notes.Add('没有 resources/：视为"没有出厂种子"（契约允许的合法状态）')
}
elseif (-not (Test-Path -LiteralPath $resourcesRoot -PathType Container)) {
    Add-Problem 'resources' 'resources 不是目录'
}
else {
    foreach ($entry in (Get-ChildItem -LiteralPath $resourcesRoot -Force | Sort-Object Name)) {
        if (-not $entry.PSIsContainer) {
            Add-Problem (Get-RelPath $entry.FullName) 'resources/ 下只允许已登记的子树目录（seeds / i18n）'
        }
        elseif (-not $resourceSubtrees.Contains($entry.Name)) {
            Add-Problem (Get-RelPath $entry.FullName) `
                ("resources/ 下出现未登记的子树 '{0}'（已登记：{1}）——新增资源类别必须登记访问模式" -f `
                    $entry.Name, ($resourceSubtrees.Keys -join ', '))
        }
    }
}

$sourceSettingsSeed = Join-Path $seedsRoot 'settings\settings.json'
if (Test-Path -LiteralPath $sourceSettingsSeed) {
    Add-Problem 'resources/seeds/settings/settings.json' `
        '设置基线不手工入库（默认值唯一来源是代码；发布时由 --emit-default-settings 生成）'
}

if (Test-Path -LiteralPath $seedsRoot -PathType Container) {
    foreach ($entry in (Get-ChildItem -LiteralPath $seedsRoot -Force | Sort-Object Name)) {
        if (-not $entry.PSIsContainer) {
            Add-Problem (Get-RelPath $entry.FullName) 'seeds/ 下只允许已登记的目标子树（当前仅 settings/）'
        }
        elseif (-not $registeredSubtrees.Contains($entry.Name)) {
            Add-Problem (Get-RelPath $entry.FullName) `
                ("seeds/ 下出现未登记的目标子树 '{0}'（已登记：{1}）" -f $entry.Name, ($registeredSubtrees.Keys -join ', '))
        }
    }

    foreach ($directory in @(Get-LeafDirectories -Directory $seedsRoot)) {
        $relative = Get-RelPath $directory
        $unit = [System.IO.Path]::GetRelativePath($seedsRoot, $directory).Replace('\', '/')
        $match = [regex]::Match($unit, '^settings/appearance/themes/(?<id>[^/]+)$')
        if (-not $match.Success) {
            Add-Problem $relative '未登记的叶子目录单元（没有清单；主题包必须是 settings/appearance/themes/<id>/）'
            continue
        }

        $themePackageCount++
        $themeId = $match.Groups['id'].Value
        $manifestPath = Join-Path $directory 'theme.json'
        $manifestRel = "$relative/theme.json"

        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            Add-Problem $relative '主题包缺少清单 theme.json（叶子目录 = 一个投放单元，必须有清单）'
            continue
        }

        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
        }
        catch {
            Add-Problem $manifestRel "theme.json 无法解析：$($_.Exception.Message)"
            continue
        }

        $version = 0
        if (-not [int]::TryParse([string]$manifest.schemaVersion, [ref]$version) -or $version -ne $themeSchemaVersion) {
            $line = Get-FirstLineNumber -Path $manifestPath -Pattern '"schemaVersion"'
            Add-Problem "${manifestRel}:${line}" `
                ("schemaVersion 不认识（'{0}'；本应用支持 {1}）" -f $manifest.schemaVersion, $themeSchemaVersion)
        }

        $base = [string]$manifest.base
        if ($base -ne 'light' -and $base -ne 'dark') {
            $line = Get-FirstLineNumber -Path $manifestPath -Pattern '"base"'
            Add-Problem "${manifestRel}:${line}" ("base 必须是 light 或 dark（当前 '{0}'）" -f $base)
        }

        $id = [string]$manifest.id
        if (-not [string]::Equals($id, $themeId, [StringComparison]::Ordinal)) {
            $line = Get-FirstLineNumber -Path $manifestPath -Pattern '"id"'
            Add-Problem "${manifestRel}:${line}" ("清单 id '{0}' 与目录名 '{1}' 不一致" -f $id, $themeId)
        }

        if ($null -ne $manifest.palette) {
            foreach ($property in $manifest.palette.PSObject.Properties) {
                if (-not $paletteTokens.Contains($property.Name)) {
                    $line = Get-FirstLineNumber -Path $manifestPath -Pattern ([regex]::Escape('"' + $property.Name + '"'))
                    Add-Problem "${manifestRel}:${line}" `
                        ("palette 键 '{0}' 不在色板契约 {1} 的 tokens 里" -f $property.Name, $paletteRel)
                }
            }
        }

        $wallpaper = [string]$manifest.wallpaper
        if ($wallpaper.Length -gt 0) {
            $isSingleName = [string]::Equals([System.IO.Path]::GetFileName($wallpaper), $wallpaper, [StringComparison]::Ordinal) `
                -and -not [System.IO.Path]::IsPathRooted($wallpaper) `
                -and $wallpaper -ne '.' -and $wallpaper -ne '..'

            if (-not $isSingleName) {
                $line = Get-FirstLineNumber -Path $manifestPath -Pattern '"wallpaper"'
                Add-Problem "${manifestRel}:${line}" ("wallpaper 必须是同目录单文件名（当前 '{0}'）" -f $wallpaper)
            }
            else {
                $wallpaperPath = Join-Path $directory $wallpaper
                $wallpaperRel = "$relative/$wallpaper"

                if (-not (Test-Path -LiteralPath $wallpaperPath -PathType Leaf)) {
                    $line = Get-FirstLineNumber -Path $manifestPath -Pattern '"wallpaper"'
                    Add-Problem "${manifestRel}:${line}" ("wallpaper 指向的文件不存在：{0}" -f $wallpaperRel)
                }
                else {
                    $length = (Get-Item -LiteralPath $wallpaperPath).Length
                    if ($length -le 0) {
                        Add-Problem $wallpaperRel '壁纸文件为空'
                    }
                    elseif ($length -gt $maxWallpaperBytes) {
                        Add-Problem $wallpaperRel ("壁纸过大（{0} 字节 > 上限 {1} 字节）" -f $length, $maxWallpaperBytes)
                    }
                    else {
                        $header = [byte[]](Get-Content -LiteralPath $wallpaperPath -AsByteStream -TotalCount 8)
                        $pngMagic = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
                        $isPng = $header.Length -eq $pngMagic.Length
                        for ($i = 0; $isPng -and $i -lt $pngMagic.Length; $i++) {
                            if ($header[$i] -ne $pngMagic[$i]) { $isPng = $false }
                        }

                        if (-not $isPng) {
                            Add-Problem $wallpaperRel '不是 PNG（magic bytes 不符）'
                        }
                    }
                }
            }
        }
    }
}

# ---------------------------------------------------------------- ③ 文本表

# 拥有者目录名 = 程序集简名 = 项目名：目录必须能找到同名项目（否则"这张表随谁出货"说不清）。
$projectNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($projectFile in (Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Filter *.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $null = $projectNames.Add([System.IO.Path]::GetFileNameWithoutExtension($projectFile.FullName))
}

if (Test-Path -LiteralPath $i18nRoot -PathType Container) {
    foreach ($ownerDirectory in (Get-ChildItem -LiteralPath $i18nRoot -Directory -Force | Sort-Object Name)) {
        if (-not $projectNames.Contains($ownerDirectory.Name)) {
            Add-Problem (Get-RelPath $ownerDirectory.FullName) `
                ("i18n 拥有者目录 '{0}' 找不到同名项目（src/ 下必须存在 {0}.csproj）" -f $ownerDirectory.Name)
        }
    }
}

# 两处表根，判据一致：
#   ① 框架：`resources/i18n/<拥有者>/`；
#   ② 模块：`src/Modules/<…>/i18n/<拥有者>/` —— **拥有者目录名必须等于模块项目名**（表随包走）。
$tableFiles = [System.Collections.Generic.List[object]]::new()

foreach ($file in (Get-ChildItem -LiteralPath $i18nRoot -Recurse -File -Filter *.json -ErrorAction SilentlyContinue)) {
    $tableFiles.Add([pscustomobject]@{ File = $file; Root = $i18nRoot; ExpectedOwner = $null })
}

foreach ($moduleI18nRoot in (Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules') -Recurse -Directory -Filter 'i18n' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $projectDirectory = Split-Path -Parent $moduleI18nRoot.FullName
    $projectFile = Get-ChildItem -LiteralPath $projectDirectory -File -Filter *.csproj -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -eq $projectFile) {
        Add-Problem (Get-RelPath $moduleI18nRoot.FullName) '模块 i18n 目录的上级目录里没有 csproj（无法确定拥有者项目名）'
        $expectedOwner = $null
    }
    else {
        $expectedOwner = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
    }

    foreach ($file in (Get-ChildItem -LiteralPath $moduleI18nRoot.FullName -Recurse -File -Filter *.json -ErrorAction SilentlyContinue)) {
        $tableFiles.Add([pscustomobject]@{ File = $file; Root = $moduleI18nRoot.FullName; ExpectedOwner = $expectedOwner })
    }
}

$tables = [System.Collections.Generic.List[object]]::new()   # Owner / Language / File / Entries
$tableKeyLocations = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)

foreach ($table in ($tableFiles | Sort-Object { $_.File.FullName })) {
    $file = $table.File
    $relative = Get-RelPath $file.FullName

    # 布局固定两层：`<表根>/<拥有者>/<culture>[.<area>].json`（再深一层就不再是"拥有者目录"）
    $owner = [System.IO.Path]::GetRelativePath($table.Root, $file.Directory.FullName).Replace('\', '/')
    if ($owner -eq '.' -or $owner.Contains('/')) {
        Add-Problem $relative '文本表必须直接住在 <表根>/<程序集简名>/ 下'
        continue
    }

    if ($null -ne $table.ExpectedOwner -and
        -not [string]::Equals($owner, $table.ExpectedOwner, [StringComparison]::Ordinal)) {
        Add-Problem $relative `
            ("模块文本表的拥有者目录必须等于模块项目名 '{0}'（当前 '{1}'）" -f $table.ExpectedOwner, $owner)
    }

    $firstSegment = $file.Name.Split('.')[0]

    try {
        $document = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        Add-Problem $relative "文本表无法解析：$($_.Exception.Message)"
        continue
    }

    $version = 0
    if (-not [int]::TryParse([string]$document.schemaVersion, [ref]$version) -or $version -ne $textSchemaVersion) {
        $line = Get-FirstLineNumber -Path $file.FullName -Pattern '"schemaVersion"'
        Add-Problem "${relative}:${line}" `
            ("schemaVersion 不认识（'{0}'；本应用支持 {1}）" -f $document.schemaVersion, $textSchemaVersion)
    }

    $language = [string]$document.language
    if (-not [string]::Equals($language, $firstSegment, [StringComparison]::OrdinalIgnoreCase)) {
        $line = Get-FirstLineNumber -Path $file.FullName -Pattern '"language"'
        Add-Problem "${relative}:${line}" `
            ("language '{0}' 与文件名首段 '{1}' 不一致" -f $language, $firstSegment)
    }

    if ($null -eq $document.entries) {
        Add-Problem $relative '文本表缺少 entries'
        continue
    }

    $entries = [ordered]@{}
    foreach ($property in $document.entries.PSObject.Properties) {
        $key = $property.Name
        $value = [string]$property.Value
        $line = Get-FirstLineNumber -Path $file.FullName -Pattern ([regex]::Escape('"' + $key + '"'))

        if ($key -notmatch '^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$') {
            Add-Problem "${relative}:${line}" ("键格式非法：'{0}'（应为 a.b.c 形式，首段小写字母开头）" -f $key)
        }

        if ([string]::IsNullOrEmpty($value)) {
            Add-Problem "${relative}:${line}" ("键 '{0}' 的值是空字符串（缺文本必须用 ⟦key⟧ 暴露，不能靠空白）" -f $key)
        }

        $entries[$key] = $value
        if (-not $tableKeyLocations.ContainsKey($key)) {
            $tableKeyLocations[$key] = "${relative}:${line}"
        }
    }

    $tables.Add([pscustomobject]@{
        Owner    = $owner
        Language = $language
        File     = $relative
        Entries  = $entries
    })
}

# 同一拥有者 + 同一语言：跨分片不许重复键（运行期先者胜）
$byShard = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($table in $tables) {
    $shardKey = "$($table.Owner)|$($table.Language)"
    if (-not $byShard.ContainsKey($shardKey)) {
        $byShard[$shardKey] = [pscustomobject]@{
            Owner    = $table.Owner
            Language = $table.Language
            Files    = [System.Collections.Generic.List[string]]::new()
            Entries  = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
        }
    }

    $entry = $byShard[$shardKey]
    $entry.Files.Add($table.File)

    foreach ($key in $table.Entries.Keys) {
        if ($entry.Entries.ContainsKey($key)) {
            Add-Problem $table.File ("跨分片重复键 '{0}'（同一拥有者同一语言里先者胜，第二份即是过期数据）" -f $key)
        }
        else {
            $entry.Entries[$key] = [string]$table.Entries[$key]
        }
    }
}

# 同一拥有者：各语言的键集合 + 每个键的占位符集合一致
$byOwner = $tables | Group-Object Owner
foreach ($ownerGroup in $byOwner) {
    $shards = @($byShard.Values | Where-Object { $_.Owner -eq $ownerGroup.Name } | Sort-Object Language)
    if ($shards.Count -eq 0) { continue }

    $reference = $shards[0]
    foreach ($other in $shards | Select-Object -Skip 1) {
        $missing = @($reference.Entries.Keys | Where-Object { -not $other.Entries.ContainsKey($_) } | Sort-Object)
        $extra = @($other.Entries.Keys | Where-Object { -not $reference.Entries.ContainsKey($_) } | Sort-Object)

        foreach ($key in $missing) {
            Add-Problem $other.Files[0] ("[{0}] 缺少键 '{1}'（[{2}] 里有）" -f $other.Language, $key, $reference.Language)
        }

        foreach ($key in $extra) {
            Add-Problem $other.Files[0] ("[{0}] 多出键 '{1}'（[{2}] 里没有）" -f $other.Language, $key, $reference.Language)
        }

        foreach ($key in $reference.Entries.Keys) {
            if (-not $other.Entries.ContainsKey($key)) { continue }

            $expected = Get-Placeholders $reference.Entries[$key]
            $actual = Get-Placeholders $other.Entries[$key]
            if ($expected -ne $actual) {
                Add-Problem $other.Files[0] `
                    ("键 '{0}' 的占位符不一致：[{1}] = {2}，[{3}] = {4}" -f $key, $reference.Language, $expected, $other.Language, $actual)
            }
        }
    }
}

# ---------------------------------------------------------------- ④ 可本地化层零中文

$localizableRoots = @('Core', 'Hosting', 'DataPlane', 'Access', 'Core.Api', 'Ui', 'FrameworkUi', 'Sdk.Ui', 'App', 'Modules')
$lineExemptions = [System.Collections.Generic.List[string]]::new()

function Test-ExemptPath([string]$relative) {
    if ($relative -match '^src/(Sdk|Infrastructure|Sdk\.Testing|Sdk\.AccessKit)/') { return $true }
    if ($relative -eq 'src/App/Program.cs') { return $true }
    if ($relative -match '/Design/') { return $true }
    return $false
}

$localizableFiles = foreach ($name in $localizableRoots) {
    Get-ChildItem -LiteralPath (Join-Path $root "src/$name") -Recurse -File -Include *.cs, *.axaml -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
}

$localizableScanned = 0

foreach ($file in ($localizableFiles | Sort-Object FullName)) {
    $relative = Get-RelPath $file.FullName
    if (Test-ExemptPath $relative) { continue }

    $localizableScanned++

    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    $inXmlComment = $false

    for ($index = 0; $index -lt $lines.Length; $index++) {
        $lineNumber = $index + 1
        $line = $lines[$index]
        $previous = if ($index -gt 0) { $lines[$index - 1] } else { '' }

        if ($line -match '// i18n-exempt:\s*\S') { continue }
        if ($previous -match '// i18n-exempt:\s*\S') {
            $lineExemptions.Add("${relative}:$lineNumber")
            continue
        }

        if ($file.Extension -eq '.axaml') {
            if ($line -match '<!--') { $inXmlComment = $true }
            if ($inXmlComment) {
                if ($line -match '-->') { $inXmlComment = $false }
                continue
            }

            if ($line -match '[\u4e00-\u9fff]') {
                Add-Problem "${relative}:$lineNumber" '可本地化层仍有中文（XAML 属性值/文本）——请进 i18n 表'
            }

            continue
        }

        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('///')) { continue }
        if ($line -match 'RequiresUnreferencedCode') { continue }
        if ($trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) { continue }

        $code = ($line -split '//', 2)[0]
        if ($code -match '"[^"]*[\u4e00-\u9fff][^"]*"') {
            Add-Problem "${relative}:$lineNumber" '可本地化层仍有中文字符串字面量——请进 i18n 表'
        }
    }
}

if ($lineExemptions.Count -gt 0) {
    Write-Host "[i18n-exempt] 行级豁免 $($lineExemptions.Count) 处（请人工复核）：" -ForegroundColor Yellow
    foreach ($entry in $lineExemptions) {
        Write-Host "  - $entry" -ForegroundColor Yellow
    }
}
else {
    Write-Host '[i18n-exempt] 行级豁免 0 处'
}

# ---------------------------------------------------------------- ⑤ XAML 的 {loc:Tr X} 键存在 + ⑥ 死键

$markerRegex = [regex]'\{loc:Tr\s+(?:Key\s*=\s*)?(?<key>[A-Za-z0-9_.]+)\s*\}'

function Get-ProjectFile([string]$directory) {
    $current = $directory
    while ($current -and $current.Length -ge $root.Length) {
        $project = Get-ChildItem -LiteralPath $current -File -Filter *.csproj -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($project) { return $project.FullName }

        $parent = [System.IO.Path]::GetDirectoryName($current)
        if (-not $parent -or $parent -eq $current) { break }
        $current = $parent
    }

    return $null
}

$tablesByOwner = @{}
foreach ($table in $tables) {
    if (-not $tablesByOwner.ContainsKey($table.Owner)) {
        $tablesByOwner[$table.Owner] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    }

    foreach ($key in $table.Entries.Keys) { $null = $tablesByOwner[$table.Owner].Add($key) }
}

$referencedKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$allTableKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($table in $tables) {
    foreach ($key in $table.Entries.Keys) { $null = $allTableKeys.Add($key) }
}

$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })

foreach ($file in ($sourceFiles | Where-Object { $_.Extension -eq '.axaml' })) {
    if (-not (Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8).Contains('{loc:Tr')) { continue }

    $relative = Get-RelPath $file.FullName
    $projectFile = Get-ProjectFile -Directory $file.DirectoryName
    $assembly = if ($projectFile) { [System.IO.Path]::GetFileNameWithoutExtension($projectFile) } else { $null }
    $ownerKeys = if ($assembly -and $tablesByOwner.ContainsKey($assembly)) { $tablesByOwner[$assembly] } else { $null }
    $ownerLabel = if ($assembly) { $assembly } else { '未找到 csproj' }

    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++
        foreach ($match in $markerRegex.Matches($line)) {
            $key = $match.Groups['key'].Value
            $null = $referencedKeys.Add($key)

            if ($null -eq $ownerKeys) {
                Add-Problem "${relative}:${lineNumber}" `
                    ("XAML 所属程序集（{0}）没有任何文本表：{1}" -f $ownerLabel, $key)
            }
            elseif (-not $ownerKeys.Contains($key)) {
                Add-Problem "${relative}:${lineNumber}" `
                    ("{{loc:Tr 键 '{0}' 不在 {1} 的文本表里（运行期会显示 ⟦{0}⟧）" -f $key, $ownerLabel)
            }
        }
    }
}

foreach ($file in ($sourceFiles | Where-Object { $_.Extension -eq '.cs' })) {
    foreach ($literal in (Measure-CSharpLiterals -Path $file.FullName)) {
        if ($allTableKeys.Contains($literal)) { $null = $referencedKeys.Add($literal) }
    }
}

foreach ($key in ($allTableKeys | Sort-Object)) {
    if (-not $referencedKeys.Contains($key)) {
        $location = if ($tableKeyLocations.ContainsKey($key)) { $tableKeyLocations[$key] } else { 'resources/i18n' }
        Add-Problem $location ("死键：文本表里的 '{0}' 在 src/ 里没有任何引用（{{loc:Tr 或 C# 字面量）" -f $key)
    }
}

# ---------------------------------------------------------------- 汇总

$tableCount = @($tables).Count
Write-Host ("资源：种子主题包 {0} 个 · 文本表 {1} 份（{2} 个拥有者）· 可本地化层扫描文件 {3} 个" -f `
    $themePackageCount, $tableCount, @($byOwner).Count, $localizableScanned) -ForegroundColor DarkCyan

foreach ($note in $script:notes) { Write-Host ("[note] " + $note) -ForegroundColor DarkGray }

foreach ($problem in $script:problems) {
    Write-Host ("  x {0}  {1}" -f $problem.Location, $problem.Message) -ForegroundColor Red
}

if ($script:problems.Count -eq 0) {
    Write-Host '[OK] 资源与文本审计通过：种子形状 / 主题包 / 文本表 / 零中文 / {loc:Tr} 键 / 死键全部通过。' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] 资源与文本审计：{0} 处违规（见上方 x 行，格式 文件:行号）。" -f $script:problems.Count) -ForegroundColor Red
exit 1
