#Requires -Version 7.0
<#
.SYNOPSIS
  资源与文本审计（源侧）：出厂种子的形状、主题包、文本表、图标表、迁移锁死、`{loc:Tr}` 键与死键。

.DESCRIPTION
  判据（与 docs/zh/concepts/resources.md 的规则对齐；**违规一律打印 `文件:行号` 并 exit 1**）：

    ① **出厂资源的目录形状**
       · `resources/` 下的每个子树都必须在脚本里**登记访问模式**（当前：`seeds` = 投放、
         `i18n` = 文本表、`icons` = 图标表，后两者都就地读）；
         出现未登记的子树即判红（新增资源类别必须登记）；
       · `seeds/` 下只允许**已登记的目标子树**（当前：`settings/`、`composites/`）；
       · 每个**投放单元**都必须有清单：主题包 = `theme.json`，组合件 = `composite.json`；
       · 组合件的 `assets/` 是单元的资产目录，不是独立单元；它必须是纯文件叶子；
       · 设置基线不手工入库：仓库里不得出现 `seeds/settings/settings.json`
         （默认值的唯一来源是代码；发布时由 `--emit-default-settings` 生成到导出目录）。

    ② **种子主题包合法**（每个 `seeds/settings/appearance/themes/<id>/`）
       · `theme.json` 可解析、字段合法；
       · `base ∈ {light, dark}`；
       · `palette` 的每个键都是色板契约（`src/Ui/Styles/palette.json` 的 tokens）里登记过的 token；
       · 清单里的 `id` **只是说明**，不要求与目录名一致（id 的权威是目录名：复制文件夹后改名仍可用）；
       · `wallpaper` 是**同目录单文件名**且文件存在、是 PNG（magic bytes）、体积不超过上限。
         上限与 `src/Core/Themes/ThemeStore.cs` 的 `MaxWallpaperBytes` **同值**（本脚本会读那份定义核对，
         两处不一致即判红——否则"审计通过"与"运行期拒收"会各说各话）。

    ②b **种子组合件合法**（每个 `seeds/composites/<名称>/`）
       · `composite.json` 可解析；`name` 非空；`instances` 非空；
       · 每个实例有 `instanceId`（单元内唯一）与两段 `moduleId`；
       · `asset` 是**单元内相对路径**，不得越界、必须存在——否则示例在用户机器上是空框；
       · 只查结构闭合，不查内容意图：出厂示例与用户保存的组合件是同一格式。

    ③ **文本表**（每份 `resources/i18n/<程序集简名>/*.json`）
       · 可解析、`entries` 是对象；
       · `language` 与文件名首段一致（`zh-Hans.json` / `zh-Hans.settings.json` → `zh-Hans`）；
       · 键格式 `^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$`，值是字符串；
       · 同一拥有者同一语言的**跨分片键不重复**（运行期是"先者胜"，重复即是第二份事实）；
       · 同一拥有者各语言的**键集合**与每个键的**占位符集合**（`{0}` / `{1}`…）一致；
       · 值里不得出现**字面反斜杠 + n**（两个字符）——换行必须写成 JSON 转义，否则界面原样显示 `\n`
         （白名单：路径示例 `hosting.package.assemblyMissing` 的 `bin\{1}\net10.0`；
         协议语法示例 `proto.fireWater.def` / `proto.fireWater.sample`；
         C 转义提示 `moduleText.format.escaped.hint` —— 都需要用户看见字面 `\n`）。

    ③b **框架图标清单**（`resources/icons/<程序集简名>/icons.json`）
       · 可解析、`entries` 是对象、文件名固定 `icons.json`（一个拥有者一张清单）；
       · 键必须是 `icon.xxx` 形式，值是**相对路径的 `.svg`**（相对、不得跳出拥有者目录、必须存在）；
       · 每个 SVG 只认子集：根 `<svg>` + `viewBox`（或 width/height）+ 至少一个 `<path d>`；
         不许 `use` / `script` / `image` / `foreignObject` / 外部 `href` / `<path transform>`；
       · 同一张清单内键不重复；拥有者目录必须能找到同名项目。

    ③c **框架文本表**（`resources/i18n/<程序集简名>/*.json`）——③ 只审框架根，模块的文本在模块包里（见 ③d）。

    ③d **模块自有资源包**（`src/Modules/**/module.json` 的 `resources`）
       · 声明的每个路径必须是**模块目录下的相对路径**、不得越界、目录必须存在；
       · `resources/` 下的目录必须在清单里声明（不留"没人知道"的数据）；
       · 资源目录里的 `icons.json` 指向的文件必须存在、路径不得越界；
       · 框架 `resources/{i18n,icons}` 里**不得出现模块拥有者**（两套根不串）；
       · **不套**框架的文本 / 图标表规则：模块可以用任意 kind，内容由模块自己负责。

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

    ⑤b **模块视图不得自带路径图标**（`src/Modules/**/*.axaml`）
       · 不得出现 `<Path …>` / `PathIcon`：图标必须走 `ModuleButton.Icon` + 图标表（图标是数据，不是几何）；
       · `Icon="…"` 的值必须是 `{x:Static …}` 常量：键来自代码常量，拼错是编译错误；不许写字面量或绑定表达式。

    ⑤c **源码不得硬编码字形**（`src/` 下的 `.axaml` / `.cs`）
       · 不得出现 `&#xE…;`（XML 字符引用）、`\uE…`（转义码位）、`Glyph[A-Z]…` 旧 token、`SetGlyph` 旧接口；
       · 图标清单在 `resources/icons/<owner>/icons.json`、图形在 `.svg`，界面只写键（`ShellIcons` / 模块 `ViewKeys`）；
       · 豁免：`ShellIcons` 与图标清单 key（是键不是图形）、`InputChrome.axaml` 的 `PathIcon`（控件装饰）、
         `tests/`（测试自己的探针数据）。

    ⑥ **死键**：表里的每个键都必须在 `src/` 里被引用（XAML 的 `{loc:Tr X}` 或 C# 字符串字面量 `"X"`）。
       允许 C# 通道（例如动态拼选项文案的键）；图标表没有 XAML 通道，只认 C# 字符串字面量。
       只存在表里、没人取用的键是过期数据。

    ⑦ **测试不得以文案为判据**（`tests/**/*.cs`）
       · 断言（`ShouldBe` / `ShouldContain` / `ShouldStartWith` / `ShouldEndWith` / `ShouldNotBe` /
         `ShouldNotContain` / `ShouldMatch`）的**第 0 个参数**里，字符串字面量含中文 ⇒ 判红；
       · 该字面量与任一产品文本表的值**整值相等** ⇒ 判红（覆盖未来的英文文案）；
       · 断言说明文字（第 1 个参数起）不算；原始字符串 / 逐字字符串 / 注释先被遮掉再抽参数；
       · 行级豁免：断言行或上一行写 `// i18n-test-data: <理由>`，脚本结束打印命中数供复核。

    ⑧ **字体默认值单源**：`AppearanceSettings` 的 `UiFonts` / `DataFonts` 组合值必须与
       `Ui/Styles/Tokens.axaml` 的 `FontUi` / `FontData`、`Sdk.Ui/Themes/DesignTokens.axaml` 的 `FontData` 逐字一致
       （默认值只在设置定义里；token 是首帧/预览兜底，不允许各说各话）。

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


# 文本值里允许出现"反斜杠 + n"字面量的键。两类例外：
#   ① 路径示例：`bin\{1}\net10.0` 的 `\n` 是分隔符 + 目录名，不是换行；
#   ② 协议语法示例：FireWater 的 `...\n` 必须让用户看见字面 `\n`（不是换行）；
#   ③ 转义输入提示：C 转义语法本身的示例必须让用户看见 `\n`。
$literalBackslashNewlineAllowed = @(
    'hosting.package.assemblyMissing',
    'proto.fireWater.def',
    'proto.fireWater.sample',
    'moduleText.format.escaped.hint')

# 壁纸体积上限的**来源**是 ThemeStore.MaxWallpaperBytes；下面只是缺省值，
# 读到源码定义后会覆盖它，并核对两者一致（不接受"脚本比运行期松"）。
$maxWallpaperBytes = 32L * 1024 * 1024

$themeStoreRel = 'src/Core/Themes/ThemeStore.cs'
$paletteRel = 'src/Ui/Styles/palette.json'
$storagePathsRel = 'src/Infrastructure/Storage/StoragePaths.cs'
$resourceLayoutRel = 'src/Sdk/Resources/ResourceLayout.cs'
$resourceKindsRel = 'src/Sdk/Resources/ResourceKinds.cs'

# 审计自己的口径：resources/ 子树名与两个资源类别名（访问模式登记见下方 $resourceSubtrees）。
$resourcesDirectoryName = 'resources'
$textPacksDirectoryName = 'i18n'
$iconsPacksDirectoryName = 'icons'

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
    'icons' = '就地读（只读；运行时按 <拥有者>/glyphs.json 加载）'
}

# seeds/<目标子树>/ 与程序目录同构：键就是目标目录名，取自路径常量（不手写），
# 每种单元说清"投放成什么"。新增种子类别必须在这里登记，否则叶子单元判定会判红。
$storageComposites = Get-StringConstant -Path (Join-Path $root $storagePathsRel) -Name 'CompositesDirectoryName'
$compositesDirectoryName = 'composites'

if ($null -eq $storageComposites) {
    Add-Problem $storagePathsRel '读不到 CompositesDirectoryName（种子目标目录名必须是常量，审计要核对它）'
}
elseif ($storageComposites.Value -ne $compositesDirectoryName) {
    Add-Problem "${storagePathsRel}:$($storageComposites.Line)" `
        ("CompositesDirectoryName = '{0}'，审计口径是 '{1}'" -f $storageComposites.Value, $compositesDirectoryName)
}

$registeredSubtrees = [ordered]@{
    'settings'                = '设置（单文件 settings.json + 外观主题包）'
    $compositesDirectoryName  = '组合件（<名称>/composite.json + 可选 assets/）'
}

# ---- 跨层一致性：`resources` 目录名与两个类别名（i18n / icons）在三处出现，必须同值 ----
# Infrastructure 与 Sdk 零依赖、不能共享常量；这里用正则从源码里读常量值，
# 任何一处漂移都判红（否则"发布布局"与"开发布局"会各读各的目录）。
$storagePathsPath = Join-Path $root $storagePathsRel
$resourceLayoutPath = Join-Path $root $resourceLayoutRel
$resourceKindsPath = Join-Path $root $resourceKindsRel

$storageResources = Get-StringConstant -Path $storagePathsPath -Name 'ResourcesDirectoryName'
$storageTextPacks = Get-StringConstant -Path $storagePathsPath -Name 'TextPacksDirectoryName'
$layoutResources = Get-StringConstant -Path $resourceLayoutPath -Name 'ResourcesDirectoryName'
$kindsText = Get-StringConstant -Path $resourceKindsPath -Name 'Text'
$kindsIcons = Get-StringConstant -Path $resourceKindsPath -Name 'Icons'

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

if ($null -eq $layoutResources) {
    Add-Problem $resourceLayoutRel '读不到 ResourcesDirectoryName（默认根解析的资源根名）'
}
elseif ($layoutResources.Value -ne $resourcesDirectoryName) {
    Add-Problem "${resourceLayoutRel}:$($layoutResources.Line)" `
        ("ResourcesDirectoryName = '{0}' 与审计口径 '{1}' 不一致（默认根会指向别的目录）" -f `
            $layoutResources.Value, $resourcesDirectoryName)
}

if ($null -eq $kindsText) {
    Add-Problem $resourceKindsRel '读不到文本类别的常量 Text（默认根解析的文本包名）'
}
elseif ($kindsText.Value -ne $textPacksDirectoryName) {
    Add-Problem "${resourceKindsRel}:$($kindsText.Line)" `
        ("Text = '{0}' 与审计口径 '{1}' 不一致（默认根会指向别的目录）" -f `
            $kindsText.Value, $textPacksDirectoryName)
}

if ($null -eq $kindsIcons) {
    Add-Problem $resourceKindsRel '读不到图标类别的常量 Icons（默认根解析的图标包名）'
}
elseif ($kindsIcons.Value -ne $iconsPacksDirectoryName) {
    Add-Problem "${resourceKindsRel}:$($kindsIcons.Line)" `
        ("Icons = '{0}' 与审计口径 '{1}' 不一致（默认根会指向别的目录）" -f `
            $kindsIcons.Value, $iconsPacksDirectoryName)
}

if ($null -ne $storageTextPacks -and $null -ne $kindsText `
    -and $storageTextPacks.Value -ne $kindsText.Value) {
    Add-Problem "${resourceKindsRel}:$($kindsText.Line)" `
        ("Text = '{0}' 与 StoragePaths.TextPacksDirectoryName = '{1}' 不一致（显式根与默认根会分叉）" -f `
            $kindsText.Value, $storageTextPacks.Value)
}

foreach ($kind in @($textPacksDirectoryName, $iconsPacksDirectoryName)) {
    if (-not $resourceSubtrees.Contains($kind)) {
        Add-Problem 'tools/audit-resources.ps1' `
            ("资源类别子树 '{0}' 没有登记访问模式（resources/ 子树登记表：{1}）" -f `
                $kind, ($resourceSubtrees.Keys -join ', '))
    }
}

$themePackageCount = 0
$compositeUnitCount = 0

if (-not (Test-Path -LiteralPath $resourcesRoot)) {
    $script:notes.Add('没有 resources/：视为"没有出厂种子"（契约允许的合法状态）')
}
elseif (-not (Test-Path -LiteralPath $resourcesRoot -PathType Container)) {
    Add-Problem 'resources' 'resources 不是目录'
}
else {
    foreach ($entry in (Get-ChildItem -LiteralPath $resourcesRoot -Force | Sort-Object Name)) {
        if (-not $entry.PSIsContainer) {
            Add-Problem (Get-RelPath $entry.FullName) 'resources/ 下只允许已登记的子树目录（seeds / i18n / icons）'
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
            Add-Problem (Get-RelPath $entry.FullName) `
                ("seeds/ 下只允许已登记的目标子树（当前：{0}）" -f ($registeredSubtrees.Keys -join ', '))
        }
        elseif (-not $registeredSubtrees.Contains($entry.Name)) {
            Add-Problem (Get-RelPath $entry.FullName) `
                ("seeds/ 下出现未登记的目标子树 '{0}'（已登记：{1}）" -f $entry.Name, ($registeredSubtrees.Keys -join ', '))
        }
    }

    foreach ($directory in @(Get-LeafDirectories -Directory $seedsRoot)) {
        $relative = Get-RelPath $directory
        $unit = [System.IO.Path]::GetRelativePath($seedsRoot, $directory).Replace('\', '/')

        # 组合件单元与它的 assets/ 由下面的组合件循环校验（单元不是"叶子=单元"的形状：
        # 有 assets/ 时单元本身不是叶子，没有 assets/ 时又是叶子，两种都得放行到这里之外）。
        if ([regex]::IsMatch($unit, "^$compositesDirectoryName/[^/]+(/assets)?$")) { continue }

        $match = [regex]::Match($unit, '^settings/appearance/themes/(?<id>[^/]+)$')
        if (-not $match.Success) {
            Add-Problem $relative `
                ('未登记的叶子目录单元（没有清单；主题包必须是 settings/appearance/themes/<id>/，组合件必须是 <名称>/composite.json）')
            continue
        }

        $themePackageCount++
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


        $base = [string]$manifest.base
        if ($base -ne 'light' -and $base -ne 'dark') {
            $line = Get-FirstLineNumber -Path $manifestPath -Pattern '"base"'
            Add-Problem "${manifestRel}:${line}" ("base 必须是 light 或 dark（当前 '{0}'）" -f $base)
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

    # 组合件单元：`seeds/composites/<名称>/composite.json`（+ 可选 `assets/`）。
    # 出厂示例与用户保存的组合件是同一格式，所以这里只查**结构闭合**，不查内容意图：
    # 清单可解析、实例有标识且模块标识成两段、引用的资产在单元内且存在。
    $compositesSeedRoot = Join-Path $seedsRoot $compositesDirectoryName
    if (Test-Path -LiteralPath $compositesSeedRoot -PathType Container) {
        $compositeUnitFullRoot = [System.IO.Path]::GetFullPath($compositesSeedRoot) + [System.IO.Path]::DirectorySeparatorChar

        foreach ($unitDirectory in (Get-ChildItem -LiteralPath $compositesSeedRoot -Directory -Force | Sort-Object Name)) {
            $relative = Get-RelPath $unitDirectory
            $manifestPath = Join-Path $unitDirectory 'composite.json'
            $manifestRel = "$relative/composite.json"

            if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                Add-Problem $relative '组合件缺少清单 composite.json（一个目录 = 一个投放单元，必须有清单）'
                continue
            }

            $raw = ''
            try {
                $raw = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8
                $manifest = $raw | ConvertFrom-Json
            }
            catch {
                Add-Problem $manifestRel "composite.json 无法解析：$($_.Exception.Message)"
                continue
            }

            $compositeUnitCount++

            if ([string]::IsNullOrWhiteSpace([string]$manifest.name)) {
                Add-Problem "$($manifestRel):$(Get-FirstLineNumber -Path $manifestPath -Pattern '"name"')" `
                    '组合件缺少 name（示例在用户界面上没有名字）'
            }

            $instances = @($manifest.instances)
            if ($instances.Count -eq 0) {
                Add-Problem "$($manifestRel):$(Get-FirstLineNumber -Path $manifestPath -Pattern '"instances"')" `
                    '组合件没有实例（instances 为空，投放后是空组合）'
            }

            $instanceIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($instance in $instances) {
                $instanceId = [string]$instance.instanceId
                $moduleId = [string]$instance.moduleId

                if ([string]::IsNullOrWhiteSpace($instanceId)) {
                    Add-Problem $manifestRel '有实例缺少 instanceId'
                }
                elseif (-not $instanceIds.Add($instanceId)) {
                    Add-Problem $manifestRel ("实例标识重复：'{0}'（实例标识是连接与保存的指认依据）" -f $instanceId)
                }

                if ($moduleId -notmatch '^[a-z0-9-]+/[a-z0-9-]+$') {
                    Add-Problem $manifestRel ("实例 '{0}' 的 moduleId 不是两段标识：'{1}'" -f $instanceId, $moduleId)
                }
            }

            # 资产是单元内的相对路径：越界或缺失会让示例在用户机器上变成空框。
            foreach ($assetMatch in [regex]::Matches($raw, '"asset"\s*:\s*"(?<path>[^"]*)"')) {
                $asset = $assetMatch.Groups['path'].Value
                if ([string]::IsNullOrWhiteSpace($asset)) { continue }

                $assetPath = [System.IO.Path]::GetFullPath((Join-Path $unitDirectory $asset.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
                if (-not $assetPath.StartsWith($compositeUnitFullRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    Add-Problem $manifestRel ("资产路径不得越出组合件目录：'{0}'" -f $asset)
                }
                elseif (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
                    Add-Problem $manifestRel ("资产文件不存在：'{0}'" -f $asset)
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

# 框架的文本表只有一处表根：`resources/i18n/<拥有者>/`（模块的文本在模块包里，见 ③d）。
$tableFiles = [System.Collections.Generic.List[object]]::new()

foreach ($file in (Get-ChildItem -LiteralPath $i18nRoot -Recurse -File -Filter *.json -ErrorAction SilentlyContinue)) {
    $tableFiles.Add([pscustomobject]@{ File = $file; Root = $i18nRoot })
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

    $firstSegment = $file.Name.Split('.')[0]

    try {
        $document = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        Add-Problem $relative "文本表无法解析：$($_.Exception.Message)"
        continue
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

        # 换行必须写成 JSON 转义（解析后的值是**真正的换行**）。若值里出现"反斜杠 + n"两个字面字符，
        # 界面会原样显示 `\n`——`TextCatalog` 不做二次反转义（实测端口标记的悬停提示就是这样坏掉的）。
        # 白名单只放**确实需要用户看见字面 `\n`** 的示例：路径、协议语法、C 转义提示。
        # 地址见 $literalBackslashNewlineAllowed，逐键登记；不是全局豁免。
        if ($value -match '\\n' -and $literalBackslashNewlineAllowed -notcontains $key) {
            Add-Problem "${relative}:${line}" ("键 '{0}' 含字面反斜杠 + n（两个字符）——换行必须写成 JSON 转义" -f $key)
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

# ---------------------------------------------------------------- ③e 框架文本表的分片归属

# 框架文本表按**用途**分三层（模块表不拆，见 ③d）：
#   · `zh-Hans.json`              用户可见的中短文本：短语、按钮、标签、状态句 —— **精调对象**
#   · `zh-Hans.diagnostics.json`  专业级/框架级报错与判定原因：求精准，不求好读
#   · `zh-Hans.help.json`         长描述性文本：帮助正文、说明段落
#
# 判据只看**拥有者 + 键路径**（不看值、不看长度）：值会随译文变化，长度判据在译文间会漂移
# ⇒ 同一个键在不同语言里可能被判进不同分片（那会让译文各自为政）。键路径是身份，跨语言稳定。
#
# 规矩与实现顺序（命中即止）：
#   ① help        末段 ∈ { body, note, tagline, tips }
#   ② diagnostics 任一段 ∈ { diag, diagnostics, reason, failure }
#   ③ diagnostics 末段含报错/约束词（见 $diagWords）；
#                 例外：界面层拥有者的 `*.empty` 是空状态占位（「暂无模块」）⇒ 留 base
#   ④ diagnostics 整个命名空间都是诊断（见 $diagPrefixes）
#   ⑤ diagnostics 拥有者整体：Hosting / DataPlane / Access（这三个拥有者没有面向用户的中短文本）
#   ⑥ 其余         base
#
# 这三份判据表是**唯一事实源**：新键落错分片即判红，并给出"该去的文件名"。
$shardHelpSegments = @('body', 'note', 'tagline', 'tips')
$shardDiagSegments = @('diag', 'diagnostics', 'reason', 'failure')
$shardDiagWords = @(
    'failed', 'failure', 'invalid', 'missing', 'mismatch', 'threw', 'rejected', 'unsupported',
    'toolarge', 'toodeep', 'toolong', 'conflict', 'unreadable', 'notfound', 'malformed',
    'duplicate', 'illegal', 'exceeds', 'exceeded', 'outofrange', 'unavailable',
    'notpersisted', 'notopen', 'notdeclared', 'notregistered', 'notconfigured',
    'undeclared', 'unresolved', 'notincluded', 'notactive', 'notdisabled',
    'empty', 'notarray', 'notobject', 'notmodule', 'containswhitespace', 'occupied',
    'samename', 'dualconsumer', 'nobindings', 'internalerror', 'unbound', 'exhausted',
    'nonobject', 'nosources', 'folderlimit', 'coverexists', 'targetexists',
    'onlydefaultdirectory', 'nomodulepackages'
)
$shardDiagPrefixes = @(
    'core.settings', 'core.compat', 'core.seed', 'core.workspace',
    'core.composite.store', 'core.composite.inspect', 'core.composite.load',
    'core.services.schema', 'core.services.config', 'core.services.restartSnapshot'
)
$shardDiagnosticOwners = @(
    'HYWDebugAssistant.Hosting', 'HYWDebugAssistant.DataPlane', 'HYWDebugAssistant.Access'
)
$shardInterfaceOwners = @(
    'HYWDebugAssistant.Ui', 'HYWDebugAssistant.Sdk.Ui', 'HYWDebugAssistant.FrameworkUi', 'HYWDebugAssistant.App'
)

# **模块拥有者不参与**：模块的文本表在模块包里，它们不拆分（作者侧只有一张表，就近维护最省事）。
$moduleOwnerNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($moduleManifest in (Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules') -Recurse -Filter module.json -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $moduleProject = Get-ChildItem -LiteralPath $moduleManifest.Directory.FullName -File -Filter *.csproj -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($moduleProject) { $null = $moduleOwnerNames.Add([System.IO.Path]::GetFileNameWithoutExtension($moduleProject.Name)) }
}

function Get-ExpectedTextShard([string]$Owner, [string]$Key) {
    $segments = $Key.Split('.')
    $lastLower = $segments[-1].ToLowerInvariant()

    if ($shardHelpSegments -contains $lastLower) { return 'help' }

    foreach ($segment in $segments) {
        if ($shardDiagSegments -contains $segment.ToLowerInvariant()) { return 'diagnostics' }
    }

    if (-not ($shardInterfaceOwners -contains $Owner -and $lastLower -eq 'empty')) {
        foreach ($word in $shardDiagWords) {
            if ($lastLower.Contains($word, [StringComparison]::Ordinal)) { return 'diagnostics' }
        }
    }

    foreach ($prefix in $shardDiagPrefixes) {
        if ($Key -eq $prefix -or $Key.StartsWith($prefix + '.', [StringComparison]::Ordinal)) { return 'diagnostics' }
    }

    if ($shardDiagnosticOwners -contains $Owner) { return 'diagnostics' }

    return 'base'
}

foreach ($table in $tables) {
    if ($moduleOwnerNames.Contains($table.Owner)) { continue }

    $tablePath = Join-Path $root $table.File
    $fileName = Split-Path -Leaf $table.File

    # 分片名白名单：`<culture>.json` / `<culture>.diagnostics.json` / `<culture>.help.json`。
    # 名字写错不会被运行期报错（`TextCatalog` 见 `<culture>.*.json` 就当分片读），
    # 只会静默生效 ⇒ 必须在这里拦（新增一层目录结构之前先想清楚）。
    $shard = 'base'
    if ($fileName -match '^\S+\.diagnostics\.json$') { $shard = 'diagnostics' }
    elseif ($fileName -match '^\S+\.help\.json$') { $shard = 'help' }
    elseif ($fileName -notmatch '^\S+\.json$') { continue }

    if ($shard -eq 'base' -and $fileName -ne ($table.Language + '.json')) {
        Add-Problem $table.File '分片名不在白名单里（只允许 <语言>.json / <语言>.diagnostics.json / <语言>.help.json）'
        continue
    }

    foreach ($key in $table.Entries.Keys) {
        $expected = Get-ExpectedTextShard $table.Owner $key
        if ($expected -eq $shard) { continue }

        $expectedFile = if ($expected -eq 'base') {
            $table.Language + '.json'
        }
        else {
            "{0}.{1}.json" -f $table.Language, $expected
        }

        $line = Get-FirstLineNumber -Path $tablePath -Pattern ([regex]::Escape('"' + $key + '"'))
        Add-Problem ("{0}:{1}" -f $table.File, $line) `
            ("键 '{0}' 应在 {1}（当前在 {2}）—— 分片按用途分：base = 用户可见的中短文本，diagnostics = 专业报错，help = 长说明" -f `
                $key, $expectedFile, $fileName)
    }
}

# 分片形状报告（不是判据）：让开发者一眼看到"用户能看到的文本"到底有多少。
# 每次审计打印一行，数字变了就是有人挪了键 —— 比逐条翻文件直观。
$shardSummary = [System.Collections.Generic.SortedDictionary[string, string]]::new([StringComparer]::Ordinal)
foreach ($table in $tables) {
    if ($moduleOwnerNames.Contains($table.Owner)) { continue }
    $fileName = Split-Path -Leaf $table.File
    $kind = if ($fileName -match '\.diagnostics\.json$') { 'diagnostics' }
    elseif ($fileName -match '\.help\.json$') { 'help' }
    else { 'base' }

    $bucketKey = "$($table.Owner)|$kind"
    if (-not $shardSummary.ContainsKey($bucketKey)) {
        $shardSummary[$bucketKey] = "0|$($table.Owner)|$kind"
    }

    $parts = $shardSummary[$bucketKey].Split('|')
    $shardSummary[$bucketKey] = "{0}|{1}|{2}" -f ([int]$parts[0] + $table.Entries.Count), $table.Owner, $kind
}

Write-Host '[文本表分层]（base = 用户可见中短文本；diagnostics = 专业报错；help = 长说明）'
$shardRows = foreach ($value in $shardSummary.Values) {
    $parts = $value.Split('|')
    [pscustomobject]@{ Owner = $parts[1]; Kind = $parts[2]; Count = [int]$parts[0] }
}

# 按"拥有者 → 三层的固定次序"打印：同一拥有者的三层挨在一起，便于对照。
$kindOrder = @{ base = 0; diagnostics = 1; help = 2 }
foreach ($row in ($shardRows | Sort-Object Owner, @{ Expression = { $kindOrder[$_.Kind] } })) {
    Write-Host ("  {0,-34} {1,-12} {2,4}" -f $row.Owner, $row.Kind, $row.Count)
}

# ---------------------------------------------------------------- ③b 配置文本的属主

# 外部接入的 `config.*` 文本由**声明该接入的模块**拥有：参数类型住在框架侧，文本不跟着类型走。
# 因此框架 Access 表只剩底层诊断（`access.diag.*`），其余任何拥有者的表里都不许出现 `config.*`。
$accessTextOwner = 'HYWDebugAssistant.Access'
$configTextOwners = @(
    'HYWDebugAssistant.Modules.Serial',
    'HYWDebugAssistant.Modules.TcpClient',
    'HYWDebugAssistant.Modules.TcpServer',
    'HYWDebugAssistant.Modules.Udp'
)

foreach ($table in $tables) {
    $tablePath = Join-Path $root $table.File

    if ([string]::Equals($table.Owner, $accessTextOwner, [StringComparison]::Ordinal)) {
        foreach ($key in $table.Entries.Keys) {
            if ($key -match '^access\.diag\.') { continue }

            $line = Get-FirstLineNumber -Path $tablePath -Pattern ([regex]::Escape('"' + $key + '"'))
            Add-Problem ("{0}:{1}" -f $table.File, $line) `
                ("Access 表只允许 access.diag.*（当前键 '{0}'）" -f $key)
        }

        continue
    }

    if ($configTextOwners -contains $table.Owner) { continue }

    foreach ($key in $table.Entries.Keys) {
        if ($key -notlike 'config.*') { continue }

        $line = Get-FirstLineNumber -Path $tablePath -Pattern ([regex]::Escape('"' + $key + '"'))
        Add-Problem ("{0}:{1}" -f $table.File, $line) `
            ("config.* 键 '{0}' 只允许出现在四个外部接入模块表（当前拥有者 '{1}'）" -f $key, $table.Owner)
    }
}

# ---------------------------------------------------------------- ③c 图标表

# 框架的图标清单只有一处表根：`resources/icons/<拥有者>/icons.json`（模块的在模块包里，见 ③d）。
$iconsRoot = Join-Path $resourcesRoot $iconsPacksDirectoryName
$iconOwnerDirectories = [System.Collections.Generic.List[object]]::new()

if (Test-Path -LiteralPath $iconsRoot -PathType Container) {
    foreach ($ownerDirectory in (Get-ChildItem -LiteralPath $iconsRoot -Directory -Force | Sort-Object Name)) {
        if (-not $projectNames.Contains($ownerDirectory.Name)) {
            Add-Problem (Get-RelPath $ownerDirectory.FullName) `
                ("icons 拥有者目录 '{0}' 找不到同名项目（src/ 下必须存在 {0}.csproj）" -f $ownerDirectory.Name)
        }

        $iconOwnerDirectories.Add([pscustomobject]@{ Directory = $ownerDirectory })
    }
}

$iconTables = [System.Collections.Generic.List[object]]::new()   # Owner / File / Entries
$iconKeyLocations = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)

# SVG 只认一个子集（`viewBox` + `<path d>`）：不引第三方 SVG 引擎，也不执行文件里的任何东西。
# 返回问题描述（没问题返回 $null）。
function Test-IconSvg([string]$path) {
    try {
        $raw = Get-Content -LiteralPath $path -Raw -Encoding utf8
    }
    catch {
        return "SVG 无法读取：$($_.Exception.Message)"
    }

    if ([string]::IsNullOrWhiteSpace($raw)) { return 'SVG 是空文件' }
    if ($raw -notmatch '<svg[\s>]') { return 'SVG 根元素不是 <svg>' }
    if ($raw -match '<(use|script|image|foreignObject)[\s/>]') {
        return 'SVG 含不支持的构造（use / script / image / foreignObject）'
    }

    if ($raw -match '(?i)(xlink:)?href\s*=\s*"[^"]*://') { return 'SVG 含外部引用（href 指向网络）' }

    try {
        $document = [xml]$raw
    }
    catch {
        return "SVG 无法解析：$($_.Exception.Message)"
    }

    $root = $document.DocumentElement
    if ($null -eq $root -or $root.LocalName -ne 'svg') { return 'SVG 根元素不是 <svg>' }

    if ([string]::IsNullOrWhiteSpace($root.GetAttribute('viewBox'))) {
        $width = $root.GetAttribute('width')
        $height = $root.GetAttribute('height')
        if ([string]::IsNullOrWhiteSpace($width) -or [string]::IsNullOrWhiteSpace($height)) {
            return 'SVG 缺少 viewBox（也没有 width/height）'
        }
    }

    $paths = @($document.SelectNodes("//*[local-name()='path']"))
    if ($paths.Count -eq 0) { return 'SVG 里没有 <path>（图标必须由路径画出来）' }

    foreach ($node in $paths) {
        if ([string]::IsNullOrWhiteSpace($node.GetAttribute('d'))) { return 'SVG 里有 <path> 缺 d' }
        if ($null -ne $node.Attributes['transform']) { return 'SVG 里有 <path transform>（不支持）' }
    }

    return $null
}

foreach ($iconOwner in $iconOwnerDirectories) {
    $ownerDirectory = $iconOwner.Directory
    $owner = $ownerDirectory.Name
    $relativeDirectory = Get-RelPath $ownerDirectory.FullName

    if ($null -ne $iconOwner.ExpectedOwner `
        -and -not [string]::Equals($owner, $iconOwner.ExpectedOwner, [StringComparison]::Ordinal)) {
        Add-Problem $relativeDirectory `
            ("模块图标表的拥有者目录必须等于模块项目名 '{0}'（当前 '{1}'）" -f $iconOwner.ExpectedOwner, $owner)
    }

    $files = @(Get-ChildItem -LiteralPath $ownerDirectory.FullName -File -Filter *.json -ErrorAction SilentlyContinue)

    if ($files.Count -eq 0) {
        $script:notes.Add("图标表目录为空：$relativeDirectory（该拥有者的图标键显示键名）")
        continue
    }

    foreach ($file in $files) {
        $relative = Get-RelPath $file.FullName

        if (-not [string]::Equals($file.Name, 'icons.json', [StringComparison]::Ordinal)) {
            Add-Problem $relative '图标清单文件名必须是 icons.json（一个拥有者一张清单）'
            continue
        }

        try {
            $raw = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
            $document = $raw | ConvertFrom-Json
        }
        catch {
            Add-Problem $relative "图标表无法解析：$($_.Exception.Message)"
            continue
        }


        if ($null -eq $document.entries) {
            Add-Problem $relative '图标表缺少 entries'
            continue
        }

        $entries = [ordered]@{}
        foreach ($property in $document.entries.PSObject.Properties) {
            $key = $property.Name
            $value = [string]$property.Value
            $line = Get-FirstLineNumber -Path $file.FullName -Pattern ([regex]::Escape('"' + $key + '"'))

            if ($key -notmatch '^icon\.[a-z0-9]+(\.[a-zA-Z0-9]+)*$') {
                Add-Problem "${relative}:${line}" ("图标键格式非法：'{0}'（必须是 icon.xxx 形式）" -f $key)
            }

            # ConvertFrom-Json 遇到重复键会静默"后者覆盖前者"，所以用原文计数补一刀。
            if (([regex]::Matches($raw, '"' + [regex]::Escape($key) + '"\s*:')).Count -gt 1) {
                Add-Problem "${relative}:${line}" ("图标表里键重复：'{0}'（同一张表只能出现一次）" -f $key)
                continue
            }

            if ([string]::IsNullOrWhiteSpace($value)) {
                Add-Problem "${relative}:${line}" ("图标键 '{0}' 的路径为空" -f $key)
                continue
            }

            if ([System.IO.Path]::IsPathRooted($value) -or $value -match '^[\\/]' -or $value.Contains(':')) {
                Add-Problem "${relative}:${line}" `
                    ("图标键 '{0}' 的路径必须是相对路径（当前 '{1}'）" -f $key, $value)
                continue
            }

            if ($value -match '(^|[\\/])\.\.([\\/]|$)') {
                Add-Problem "${relative}:${line}" `
                    ("图标键 '{0}' 的路径不得跳出拥有者目录（当前 '{1}'）" -f $key, $value)
                continue
            }

            if (-not $value.EndsWith('.svg', [StringComparison]::OrdinalIgnoreCase)) {
                Add-Problem "${relative}:${line}" `
                    ("图标键 '{0}' 必须指向 .svg 文件（当前 '{1}'）" -f $key, $value)
                continue
            }

            $svgPath = Join-Path $ownerDirectory.FullName ($value -replace '[\\/]', '\')
            if (-not (Test-Path -LiteralPath $svgPath -PathType Leaf)) {
                Add-Problem "${relative}:${line}" ("图标键 '{0}' 指向的 SVG 不存在：{1}" -f $key, $value)
                continue
            }

            $svgProblem = Test-IconSvg $svgPath
            if ($null -ne $svgProblem) {
                Add-Problem (Get-RelPath $svgPath) $svgProblem
            }

            if ($entries.Contains($key)) {
                Add-Problem "${relative}:${line}" ("图标清单里键重复：'{0}'" -f $key)
                continue
            }

            $entries[$key] = $value
            if (-not $iconKeyLocations.ContainsKey($key)) {
                $iconKeyLocations[$key] = "${relative}:${line}"
            }
        }

        $iconTables.Add([pscustomobject]@{
            Owner   = $owner
            File    = $relative
            Entries = $entries
        })
    }
}

# ---------------------------------------------------------------- ③d 模块自有资源包

# 模块的 `resources/` 是**模块自己的资源包**（声明在 `module.json` 的 `resources` 里）：
# 这里只校验"声明的目录在不在、清单指向的文件在不在、路径相对且不越界"，
# **不套框架的文本 / 图标表规则**（模块可以用任意 kind，框架不解释它的内容）。
$moduleOwners = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($moduleManifest in (Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules') -Recurse -Filter module.json -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $moduleDirectory = $moduleManifest.Directory.FullName
    $relativeManifest = Get-RelPath $moduleManifest.FullName

    $projectFile = Get-ChildItem -LiteralPath $moduleDirectory -File -Filter *.csproj -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -eq $projectFile) {
        Add-Problem $relativeManifest '模块清单的上级目录里没有 csproj（无法确定拥有者项目名）'
        continue
    }

    $null = $moduleOwners.Add([System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name))

    try {
        $manifest = Get-Content -LiteralPath $moduleManifest.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        Add-Problem $relativeManifest "模块清单无法解析：$($_.Exception.Message)"
        continue
    }

    $declaredDirectories = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($resourcePath in @($manifest.resources | Where-Object { $null -ne $_ })) {
        $value = [string]$resourcePath
        $line = Get-FirstLineNumber -Path $moduleManifest.FullName -Pattern ([regex]::Escape('"' + $value + '"'))

        $invalidPath = [string]::IsNullOrWhiteSpace($value) -or
            [System.IO.Path]::IsPathRooted($value) -or
            $value -match '^[\\/]' -or
            $value.Contains(':') -or
            $value -match '(^|[\\/])\.\.([\\/]|$)'

        if ($invalidPath) {
            Add-Problem "${relativeManifest}:${line}" ("模块资源路径必须是模块目录下的相对路径（当前 '{0}'）" -f $value)
            continue
        }

        $directory = Join-Path $moduleDirectory ($value -replace '[\\/]', '\')
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            Add-Problem "${relativeManifest}:${line}" ("模块资源目录不存在：{0}" -f $value)
            continue
        }

        $null = $declaredDirectories.Add([System.IO.Path]::GetFullPath($directory))

        # 资源目录里的清单（如 icons.json）指向的文件必须存在、路径必须不越界——存在性是模块资源的硬伤。
        foreach ($innerManifest in (Get-ChildItem -LiteralPath $directory -Recurse -File -Filter icons.json -ErrorAction SilentlyContinue)) {
            try {
                $iconDocument = Get-Content -LiteralPath $innerManifest.FullName -Raw -Encoding utf8 | ConvertFrom-Json
            }
            catch {
                Add-Problem (Get-RelPath $innerManifest.FullName) "图标清单无法解析：$($_.Exception.Message)"
                continue
            }

            foreach ($property in $iconDocument.entries.PSObject.Properties) {
                $iconPath = [string]$property.Value
                $invalidIconPath = [string]::IsNullOrWhiteSpace($iconPath) -or
                    [System.IO.Path]::IsPathRooted($iconPath) -or
                    $iconPath -match '(^|[\\/])\.\.([\\/]|$)'

                if ($invalidIconPath) {
                    Add-Problem (Get-RelPath $innerManifest.FullName) ("图标清单路径不得越界：'{0}'" -f $iconPath)
                    continue
                }

                $resolved = Join-Path $innerManifest.Directory.FullName ($iconPath -replace '/', '\')
                if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
                    Add-Problem (Get-RelPath $innerManifest.FullName) ("图标清单指向的文件不存在：'{0}'" -f $iconPath)
                }
            }
        }
    }

    # 磁盘上有 `resources/` 却没在清单里声明 ⇒ 包里带了一份"没人知道"的数据。
    $resourcesDirectory = Join-Path $moduleDirectory 'resources'
    if (Test-Path -LiteralPath $resourcesDirectory -PathType Container) {
        foreach ($kindDirectory in (Get-ChildItem -LiteralPath $resourcesDirectory -Directory -ErrorAction SilentlyContinue)) {
            if (-not $declaredDirectories.Contains([System.IO.Path]::GetFullPath($kindDirectory.FullName))) {
                Add-Problem (Get-RelPath $kindDirectory.FullName) '模块 resources/ 下的这个目录没有在 module.json 的 resources 里声明'
            }
        }
    }
}

# 框架的 `resources/` 不得出现模块拥有者（两套根不串）。
foreach ($kindName in @('i18n', 'icons')) {
    $frameworkKindRoot = Join-Path $resourcesRoot $kindName
    if (-not (Test-Path -LiteralPath $frameworkKindRoot -PathType Container)) { continue }

    foreach ($ownerDirectory in (Get-ChildItem -LiteralPath $frameworkKindRoot -Directory -ErrorAction SilentlyContinue)) {
        if ($moduleOwners.Contains($ownerDirectory.Name)) {
            Add-Problem (Get-RelPath $ownerDirectory.FullName) `
                ("框架 resources/{0} 里出现了模块拥有者 '{1}'（模块资源必须住在模块包里）" -f $kindName, $ownerDirectory.Name)
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

# 模块的文本键：从模块清单声明的 `resources/i18n` 目录读**键名**——只服务于 ⑤ 的"键是否存在"，
# 不套框架表结构规则（模块资源包的别的校验归 ③d 与模块包校验）。
foreach ($moduleManifest in (Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules') -Recurse -Filter module.json -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $moduleDirectory = $moduleManifest.Directory.FullName
    $projectFile = Get-ChildItem -LiteralPath $moduleDirectory -File -Filter *.csproj -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -eq $projectFile) { continue }

    $moduleOwner = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)

    try {
        $moduleDocument = Get-Content -LiteralPath $moduleManifest.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        continue
    }

    foreach ($resourcePath in @($moduleDocument.resources | Where-Object { $null -ne $_ })) {
        $kindDirectory = Join-Path $moduleDirectory ([string]$resourcePath -replace '[\\/]', '\')
        if (-not (Test-Path -LiteralPath $kindDirectory -PathType Container)) { continue }
        if ((Split-Path -Leaf $kindDirectory) -ne 'i18n') { continue }

        foreach ($tableFile in (Get-ChildItem -LiteralPath $kindDirectory -Recurse -File -Filter *.json -ErrorAction SilentlyContinue)) {
            try {
                $document = Get-Content -LiteralPath $tableFile.FullName -Raw -Encoding utf8 | ConvertFrom-Json
            }
            catch {
                continue
            }

            if ($null -eq $document.entries) { continue }

            if (-not $tablesByOwner.ContainsKey($moduleOwner)) {
                $tablesByOwner[$moduleOwner] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            }

            foreach ($property in $document.entries.PSObject.Properties) {
                $null = $tablesByOwner[$moduleOwner].Add($property.Name)
            }
        }
    }
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

# ---------------------------------------------------------------- ⑤b 模块视图不得自带路径图标

# 两条禁令：图标是**数据**（`ModuleButton.Icon` + 图标表），不是视图里的路径几何。
foreach ($file in ($sourceFiles | Where-Object { $_.Extension -eq '.axaml' -and $_.FullName -match '\\src\\Modules\\' })) {
    $relative = Get-RelPath $file.FullName
    $lineNumber = 0
    $inComment = $false

    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++

        if ($line.Contains('<!--')) { $inComment = $true }
        if ($inComment) {
            if ($line.Contains('-->')) { $inComment = $false }
            continue
        }

        if ($line -match '<\s*Path[\s/>]') {
            Add-Problem "${relative}:${lineNumber}" `
                '模块视图不得画路径几何（`<Path`）——图标必须走 `ModuleButton.Icon` + 图标表'
        }

        if ($line -match 'PathIcon') {
            Add-Problem "${relative}:${lineNumber}" `
                '模块视图不得用 `PathIcon`——图标必须走 `ModuleButton.Icon` + 图标表'
        }

        foreach ($match in [regex]::Matches($line, '(?<![\w.])Icon\s*=\s*"(?<value>[^"]*)"')) {
            $value = $match.Groups['value'].Value
            if ($value -notmatch '^\{x:Static\s[^}]+\}$') {
                Add-Problem "${relative}:${lineNumber}" `
                    ("`Icon` 的值必须是 `{{x:Static …}}` 常量（当前 '{0}'）" -f $value)
            }
        }
    }
}

# ---------------------------------------------------------------- ⑤c 不得硬编码字形

# 图标是**数据**（`resources/icons/<owner>/icons.json` → `.svg`）：源码里写码位 / 旧 `Glyph*` token /
# 旧 `SetGlyph` 接口，就是把同一份事实写成两处（换图标时必然漂移）。
# 豁免：`ShellIcons` 与图标清单 key 是**键**不是图形；
# `InputChrome.axaml` 的 `PathIcon` 是下拉箭头这类**控件装饰**，不属于内容图标。
$glyphRuleExemptFiles = @('src\Ui\Styles\InputChrome.axaml')
$glyphRulePatterns = @(
    '&#xE[0-9A-Fa-f]{3,5};',
    '\\u[Ee][0-9A-Fa-f]{3}',
    '\bGlyph[A-Z][A-Za-z0-9]*',
    '\bSetGlyph\b'
)

foreach ($file in ($sourceFiles | Where-Object { $_.Extension -eq '.axaml' -or $_.Extension -eq '.cs' })) {
    $relative = Get-RelPath $file.FullName
    if ($glyphRuleExemptFiles -contains ($relative -replace '/', '\')) { continue }

    $lineNumber = 0
    $inBlockComment = $false

    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++
        $trimmed = $line.TrimStart()

        if ($file.Extension -eq '.axaml') {
            if ($trimmed.StartsWith('<!--')) { $inBlockComment = $true }
            if ($inBlockComment) {
                if ($trimmed.Contains('-->')) { $inBlockComment = $false }
                continue
            }
        }
        else {
            if ($inBlockComment) {
                if ($trimmed.Contains('*/')) { $inBlockComment = $false }
                continue
            }

            if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('///')) { continue }
            if ($trimmed.StartsWith('/*')) {
                if (-not $trimmed.Contains('*/')) { $inBlockComment = $true }
                continue
            }
        }

        foreach ($pattern in $glyphRulePatterns) {
            # `-cmatch`：码位与旧 token 都是大小写敏感的（`icons.json` 不是 `Glyph*` token）。
            if ($line -cmatch $pattern) {
                Add-Problem "${relative}:${lineNumber}" `
                    ("源码里出现硬编码字形（{0}）——字形必须进图标表，界面只写键" -f $pattern)
            }
        }
    }
}

$sourceLiterals = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in ($sourceFiles | Where-Object { $_.Extension -eq '.cs' })) {
    foreach ($literal in (Measure-CSharpLiterals -Path $file.FullName)) { $null = $sourceLiterals.Add($literal) }
}

foreach ($key in $allTableKeys) {
    if ($sourceLiterals.Contains($key)) { $null = $referencedKeys.Add($key) }
}

foreach ($key in ($allTableKeys | Sort-Object)) {
    if (-not $referencedKeys.Contains($key)) {
        $location = if ($tableKeyLocations.ContainsKey($key)) { $tableKeyLocations[$key] } else { 'resources/i18n' }
        Add-Problem $location ("死键：文本表里的 '{0}' 在 src/ 里没有任何引用（{{loc:Tr 或 C# 字面量）" -f $key)
    }
}

# 图标表没有 XAML 通道：图标键只认 C# 字符串字面量引用（常量定义即引用点）。
$allIconKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($table in $iconTables) {
    foreach ($key in $table.Entries.Keys) { $null = $allIconKeys.Add($key) }
}

foreach ($key in ($allIconKeys | Sort-Object)) {
    if (-not $sourceLiterals.Contains($key)) {
        $location = if ($iconKeyLocations.ContainsKey($key)) { $iconKeyLocations[$key] } else { 'resources/icons' }
        Add-Problem $location ("死键：图标表里的 '{0}' 在 src/ 里没有任何字符串字面量引用" -f $key)
    }
}

# ---------------------------------------------------------------- ⑦ 测试不得以文案为判据

# 规则：测试断言（Should*）的**期望值**不得是用户可见文案。
#   · 期望值里的字符串字面量含中文 ⇒ 违规；
#   · 期望值与任一产品文本表的值**整值相等** ⇒ 违规（覆盖未来英文文案）；
#   · 只扫描第 0 个参数；断言说明文字（第 1 个参数起）不受影响；
#   · 行级豁免：断言行或上一行写 `// i18n-test-data: <理由>`（目标 0 处）。
# 说明：C# 的原始字符串（"""…"""）、逐字字符串（@"…"）、普通字符串与字符字面量都先被遮成空格，
# 再在"只剩代码"的文本上抽取断言参数 —— 因此 JSON 夹具里的括号/逗号不会把参数切错。

$tableValues = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($table in $tables) {
    foreach ($value in $table.Entries.Values) {
        $text = [string]$value
        if ($text.Length -ge 2) { $null = $tableValues.Add($text) }
    }
}

$testLiteralPattern = [regex]::new(
    '(?<raw>\$@?"""[\s\S]*?"""|\$?"""[\s\S]*?""")' +
    '|(?<verbatim>@\$?"(?:[^"]|"")*"|\$@?"(?:[^"]|"")*")' +
    '|(?<regular>\$?"(?:[^"\\\n]|\\.)*")' +
    '|(?<char>''(?:[^''\\\n]|\\.)'')' +
    '|(?<line>//[^\n]*)' +
    '|(?<block>/\*[\s\S]*?\*/)',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)

$assertionPattern = [regex]'\b(?:ShouldBe|ShouldContain|ShouldStartWith|ShouldEndWith|ShouldNotBe|ShouldNotContain|ShouldMatch)\s*\('

function Get-LineIndexAt([System.Collections.Generic.List[int]]$LineStarts, [int]$Offset) {
    $low = 0
    $high = $LineStarts.Count - 1
    while ($low -lt $high) {
        $mid = [int](($low + $high + 1) / 2)
        if ($LineStarts[$mid] -le $Offset) { $low = $mid } else { $high = $mid - 1 }
    }

    return $low
}

function Get-TestCopyFindings([string]$Text) {
    $findings = [System.Collections.Generic.List[object]]::new()
    $literals = [System.Collections.Generic.List[object]]::new()
    $masked = [System.Text.StringBuilder]::new($Text.Length)
    $last = 0

    foreach ($match in $testLiteralPattern.Matches($Text)) {
        if ($match.Index -gt $last) {
            $null = $masked.Append($Text.Substring($last, $match.Index - $last))
        }

        # 保留换行、其余字符遮成空格 —— 偏移与行号都和原文一致。
        $segment = $Text.Substring($match.Index, $match.Length)
        $chars = $segment.ToCharArray()
        for ($index = 0; $index -lt $chars.Length; $index++) {
            if ($chars[$index] -ne "`n") { $chars[$index] = ' ' }
        }
        $null = $masked.Append([string]::new($chars))

        $kind = if ($match.Groups['raw'].Success) { 'raw' }
                elseif ($match.Groups['verbatim'].Success) { 'verbatim' }
                elseif ($match.Groups['regular'].Success) { 'regular' }
                else { $null }

        if ($null -ne $kind) {
            $value = $match.Value
            switch ($kind) {
                'raw' {
                    $value = $value -replace '^[\$@]+', ''
                    $value = $value -replace '^"+', ''
                    $value = $value -replace '"+$', ''
                }
                'verbatim' {
                    $value = $value -replace '^[\$@]+', ''
                    $value = $value.Substring(1, $value.Length - 2)
                    $value = $value.Replace('""', '"')
                }
                'regular' {
                    $value = $value -replace '^\$', ''
                    $value = $value.Substring(1, $value.Length - 2)
                }
            }

            $literals.Add([pscustomobject]@{
                    Start = $match.Index
                    End   = $match.Index + $match.Length
                    Value = $value
                })
        }

        $last = $match.Index + $match.Length
    }

    if ($last -lt $Text.Length) {
        $null = $masked.Append($Text.Substring($last))
    }

    $code = $masked.ToString()
    $lineStarts = [System.Collections.Generic.List[int]]::new()
    $lineStarts.Add(0)
    for ($index = 0; $index -lt $Text.Length; $index++) {
        if ($Text[$index] -eq "`n") { $lineStarts.Add($index + 1) }
    }

    foreach ($assertion in $assertionPattern.Matches($code)) {
        $open = $code.IndexOf('(', $assertion.Index)
        if ($open -lt 0) { continue }

        $depth = 0
        $close = -1
        for ($index = $open; $index -lt $code.Length; $index++) {
            $ch = $code[$index]
            if ($ch -eq '(') { $depth++ }
            elseif ($ch -eq ')') {
                $depth--
                if ($depth -eq 0) { $close = $index; break }
            }
        }

        if ($close -lt 0) { continue }

        # 第 0 个参数 = 从开括号到第一个顶层逗号（字符串已被遮掉，逗号不会来自字符串）。
        $argEnd = $close
        $depth = 0
        for ($index = $open + 1; $index -lt $close; $index++) {
            $ch = $code[$index]
            if ($ch -eq '(' -or $ch -eq '[' -or $ch -eq '{') { $depth++ }
            elseif ($ch -eq ')' -or $ch -eq ']' -or $ch -eq '}') { $depth-- }
            elseif ($ch -eq ',' -and $depth -eq 0) { $argEnd = $index; break }
        }

        foreach ($literal in $literals) {
            if ($literal.Start -lt $open + 1 -or $literal.End -gt $argEnd) { continue }

            $isCopy = (Test-ChineseText $literal.Value) -or $tableValues.Contains($literal.Value)
            if (-not $isCopy) { continue }

            $findings.Add([pscustomobject]@{
                    Offset = $literal.Start
                    Line   = (Get-LineIndexAt $lineStarts $literal.Start) + 1
                    Value  = $literal.Value
                })
        }
    }

    return $findings
}

$testCopyCount = 0

foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Sort-Object FullName)) {
    $relative = Get-RelPath $file.FullName
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $lines = $text -split "`n"
    $seen = [System.Collections.Generic.HashSet[int]]::new()

    foreach ($finding in (Get-TestCopyFindings -Text $text)) {
        if (-not $seen.Add($finding.Offset)) { continue }

        $lineText = if ($finding.Line - 1 -lt $lines.Length) { $lines[$finding.Line - 1] } else { '' }
        $previousText = if ($finding.Line - 2 -ge 0) { $lines[$finding.Line - 2] } else { '' }

        if ($lineText -match '//\s*i18n-test-data:\s*\S' -or $previousText -match '//\s*i18n-test-data:\s*\S') {
            continue
        }

        $testCopyCount++
        $display = if ($finding.Value.Length -gt 40) { $finding.Value.Substring(0, 40) + '…' } else { $finding.Value }
        Add-Problem "${relative}:$($finding.Line)" `
            ("测试断言的期望值是用户可见文案：「{0}」——改断言键/结构，或把断言删掉（行级豁免：// i18n-test-data: 理由）" -f $display)
    }
}

# ---------------------------------------------------------------- ⑧ 字体默认值单源

# 默认字体只在 `AppearanceSettings` 定义；`Tokens.axaml` 是首帧/预览兜底，`DesignTokens.axaml` 是设计时兜底。
# 三处的值必须逐字一致 —— 否则"设置里写的是等宽、实际看的是另一套"会各说各话。

function Get-FontAxisDefault([string]$path, [string]$property) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }

    $text = [System.IO.File]::ReadAllText($path)
    $axis = [regex]::Match(
        $text,
        '(?s)' + [regex]::Escape($property) + '\s*\{[^}]*\}\s*=\s*new\(\)\s*\{(?<body>.*?)\};')

    if (-not $axis.Success) { return $null }

    $body = $axis.Groups['body'].Value
    $latin = [regex]::Match($body, 'Latin\s*=\s*"(?<value>[^"]+)"').Groups['value'].Value
    $cjk = [regex]::Match($body, 'Cjk\s*=\s*"(?<value>[^"]+)"').Groups['value'].Value

    if ($latin.Length -eq 0 -or $cjk.Length -eq 0) { return $null }
    if ([string]::Equals($latin, $cjk, [StringComparison]::OrdinalIgnoreCase)) { return $latin }

    return "$latin, $cjk"
}

function Get-FontToken([string]$path, [string]$key) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $text = [System.IO.File]::ReadAllText($path)
    $match = [regex]::Match($text, '<FontFamily x:Key="' + [regex]::Escape($key) + '">(?<value>[^<]+)</FontFamily>')

    if ($match.Success) {
        return $match.Groups['value'].Value.Trim()
    }

    return $null
}

$fontAxisRel = 'src/Core.Api/AppearanceSettings.cs'
$tokensRel = 'src/Ui/Styles/Tokens.axaml'
$designTokensRel = 'src/Sdk.Ui/Themes/DesignTokens.axaml'

$expectedUi = Get-FontAxisDefault (Join-Path $root $fontAxisRel) 'UiFonts'
$expectedData = Get-FontAxisDefault (Join-Path $root $fontAxisRel) 'DataFonts'
$tokenUi = Get-FontToken (Join-Path $root $tokensRel) 'FontUi'
$tokenData = Get-FontToken (Join-Path $root $tokensRel) 'FontData'
$designData = Get-FontToken (Join-Path $root $designTokensRel) 'FontData'

if ($null -eq $expectedUi -or $null -eq $expectedData) {
    Add-Problem $fontAxisRel '读不到 UiFonts / DataFonts 的默认值（字体默认值的唯一来源）'
}
else {
    if ($tokenUi -ne $expectedUi) {
        Add-Problem $tokensRel ("FontUi 默认值 '{0}' 与 AppearanceSettings 的 '{1}' 不一致" -f $tokenUi, $expectedUi)
    }

    if ($tokenData -ne $expectedData) {
        Add-Problem $tokensRel ("FontData 默认值 '{0}' 与 AppearanceSettings 的 '{1}' 不一致" -f $tokenData, $expectedData)
    }

    if ($designData -ne $expectedData) {
        Add-Problem $designTokensRel ("设计时 FontData '{0}' 与运行时默认 '{1}' 不一致" -f $designData, $expectedData)
    }
}
# ---------------------------------------------------------------- 汇总

$tableCount = @($tables).Count
$iconTableCount = @($iconTables).Count
Write-Host ("资源：种子主题包 {0} 个 · 种子组合件 {1} 个 · 文本表 {2} 份（{3} 个拥有者）· 图标表 {4} 份 · 可本地化层扫描文件 {5} 个 · 测试文案断言 {6} 处" -f `
    $themePackageCount, $compositeUnitCount, $tableCount, @($byOwner).Count, $iconTableCount, $localizableScanned, $testCopyCount) -ForegroundColor DarkCyan

foreach ($note in $script:notes) { Write-Host ("[note] " + $note) -ForegroundColor DarkGray }

foreach ($problem in $script:problems) {
    Write-Host ("  x {0}  {1}" -f $problem.Location, $problem.Message) -ForegroundColor Red
}

if ($script:problems.Count -eq 0) {
    Write-Host '[OK] 资源与文本审计通过：种子形状 / 主题包 / 文本表 / 图标表 / 零中文 / {loc:Tr} 键 / 死键全部通过。' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] 资源与文本审计：{0} 处违规（见上方 x 行，格式 文件:行号）。" -f $script:problems.Count) -ForegroundColor Red
exit 1
