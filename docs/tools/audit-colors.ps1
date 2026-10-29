<#
.SYNOPSIS
  颜色口径审计：本项目只允许两种颜色写法 —— **相对色**（主题命名 token）与 **绝对色**（RGB 字面量）。

.DESCRIPTION
  规则（见 docs/architecture/CODEX.md「颜色口径」）：

    · 相对色：写 `{DynamicResource <token>}`（XAML）或 `SurfaceTheme.Xxx(target)` / `TryFindResource`（C#），
      颜色值只住在**唯一一份色板**（`src/Ui/Styles/Tokens.axaml`）里。
    · 绝对色：直接写 RGB 字面量（`#RRGGBB(AA)` / `0xAARRGGBB`）—— **必须**在同一行或**前 3 行**里出现
      「绝对色」这个词，并说明为什么它不能走主题（例：结构值、主题缺失时的兜底、按数据身份算出来的颜色）。
    · 色板定义处、推荐色目录、按数据身份算出来的颜色这三类文件整体豁免（见下面的白名单，每条都写了理由）。
    · `Brushes.Transparent` 是结构值，不算颜色。

  违规 = 出现字面颜色或 `Color.From*` / `Color.Parse` / `Brushes.<非透明>`，且不满足上面任一条。

  另有**两项结构性检查**（它们守的是"色板 + 契约 = 一个事实源"）：

    ② **色板契约 ↔ 色板 XAML 一一对应**：`src/Ui/Styles/palette.json`（契约：有哪几支、谁跟随谁、谁承担哪个角色）
       与色板 XAML（值）必须逐键对齐 —— 少一支、多一支、拼错名、只在浅色里有，都判红。

    ③ **契约与值一致**（这条是"派生"能不能信的全部依据）：
       · 每支的 `derive.from` 必须存在，且**源自己不再派生**（链条只有一层 ⇒ 无环、与顺序无关）；
       · `tint` 支：它的内置色必须**逐通道等于**源的内置色，且内置不透明度 ∈ (0,1)（浓度就是那一档）；
       · `darken` 支：它的内置色必须等于「源的内置色按 amount 变深」（逐通道 ±1，取整容差）；
       · `shift` 支：源与自身都必须有内置色（色相偏移由这一对内置值定义 ⇒ 未改源时结果恒等于内置值）；
       · 角色词：契约里的 `roles` 与 `ViewInkRoles` 的词表**双向一一对应**（少一个词、多一个词、一个词挂两支都判红）；
       · 每支的 `group` 必须在 `groups` 里登记。

.PARAMETER Verbose
  逐条打印（默认只打印按文件汇总的计数与总违规数）。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $root 'HYWDebugAssistant.slnx'))) {
    $root = (Get-Location).Path
}

# 整体豁免的文件（**每条都要有理由**；加白名单等于承认"这里就是颜色的定义处"）
$fileAllow = @{
    'src/Ui/Styles/Tokens.axaml'                = '色板定义处（相对色的唯一家：浅/深两套色值）'
    'src/Core.Api/ThemePresets.cs'              = '推荐色目录（canned colors：递给用户挑的候选，不是主题色值）'
    'src/Ui/ViewModels/PacketTypePalette.cs'    = '按数据身份算出来的颜色（自动色），不属主题观感'
}

# 只认"真的是一个字面颜色"的写法：
#   · `#RRGGBB(AA)` —— 字面量；
#   · `0xAARRGGBB` —— **模块设定路径上写死的色值**（ARGB 版：与 `SetOutlineColor` 同一件事）；
#   · `Color.Parse("…")` —— 字面量；
#   · `Color.FromUInt32(0x…)` / `Color.FromArgb(0x…` —— **参数是数字字面量**才算（传变量是"转换"、不是选色）；
#   · `Brushes.<非透明>` —— 具名画刷。
# 注释行（`//` / `*` / `<!--`）一律不算 —— 文档里写 `#RRGGBB` 是说明，不是用色。
$patterns = @(
    @{ Name = 'RGB 字面量'; Regex = '#[0-9A-Fa-f]{6,8}\b' }
    @{ Name = 'ARGB 字面量'; Regex = '0x[0-9A-Fa-f]{8}\b' }
    @{ Name = 'Color.Parse'; Regex = 'Color\.Parse\s*\(\s*"' }
    @{ Name = 'Color.From*'; Regex = 'Color\.(FromArgb|FromRgb|FromUInt32)\s*\(\s*(0x[0-9A-Fa-f]+|\d+)\s*(,\s*(0x[0-9A-Fa-f]+|\d+)\s*){2,3}\)' }
    @{ Name = '具名画刷'; Regex = 'Brushes\.[A-Za-z]+' }
)

$files = Get-ChildItem (Join-Path $root 'src') -Recurse -Include *.axaml, *.cs, *.json -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\artifacts\\' }

$total = 0
$byFile = [ordered]@{}

foreach ($file in $files) {
    $rel = $file.FullName.Replace($root + [IO.Path]::DirectorySeparatorChar, '').Replace('\', '/')
    if ($fileAllow.ContainsKey($rel)) { continue }

    $lines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
    $inXmlComment = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        # XML 注释块（`<!--` … `-->`）整段不算：里面的 `#RRGGBB`/`Brushes.X` 是说明文字
        if ($line -match '<!--') { $inXmlComment = $true }
        if ($inXmlComment) {
            if ($line -match '-->') { $inXmlComment = $false }
            continue
        }

        # 注释行不算（文档里的 `#RRGGBB`、`Brushes.X` 都是说明）
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('///')) { continue }

        # 相对色：引 token 的写法天然不含字面颜色，不用逐条判
        $hit = $null
        foreach ($p in $patterns) {
            if ($line -match $p.Regex) {
                if ($p.Name -eq '具名画刷' -and $line -match 'Brushes\.Transparent') { continue }
                $hit = $p.Name
                break
            }
        }
        if (-not $hit) { continue }

        # 绝对色标记：本行或前 3 行里出现「绝对色」
        $marked = $false
        for ($k = [Math]::Max(0, $i - 3); $k -le $i; $k++) {
            if ($lines[$k] -match '绝对色') { $marked = $true; break }
        }
        if ($marked) { continue }
        if ($line -match 'Brushes\.Transparent') { continue }

        $total++
        if (-not $byFile.Contains($rel)) { $byFile[$rel] = @() }
        $byFile[$rel] += ('{0}:{1}  [{2}]  {3}' -f $rel, ($i + 1), $hit, $line.Trim())
    }
}

foreach ($entry in $byFile.GetEnumerator()) {
    Write-Host ("{0}  ({1} 处)" -f $entry.Key, $entry.Value.Count) -ForegroundColor Yellow
    if ($VerbosePreference -ne 'SilentlyContinue') {
        $entry.Value | ForEach-Object { Write-Host ("    " + $_) }
    }
}

# ---------------------------------------------------------------- ② 契约 ↔ 色板 XAML

$xamlNs = 'https://github.com/avaloniaui'
$xKeyNs = 'http://schemas.microsoft.com/winfx/2006/xaml'
$paletteFiles = @(
    'src/Ui/Styles/Tokens.axaml'
)
$manifestRel = 'src/Ui/Styles/palette.json'

$problems = @()

# 白名单条目必须指向**真实存在**的文件：死条目会让审计悄悄放过它本该放过的地方
# （文件已经删了 = 配置过期；不检查的话，"这里已无颜色"与"这里没人审"看起来一样）。
foreach ($allowedPath in ($fileAllow.Keys | Sort-Object)) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $allowedPath) -PathType Leaf)) {
        $problems += "白名单条目指向的文件不存在：$allowedPath（$($fileAllow[$allowedPath])）"
    }
}

function ConvertTo-Rgb([string]$hex) {
    if ([string]::IsNullOrWhiteSpace($hex)) { return $null }
    $digits = $hex.Trim().TrimStart('#')
    if ($digits.Length -eq 8) { $digits = $digits.Substring(2) }     # 去掉 alpha
    if ($digits.Length -ne 6) { return $null }
    return @{
        R = [Convert]::ToInt32($digits.Substring(0, 2), 16)
        G = [Convert]::ToInt32($digits.Substring(2, 2), 16)
        B = [Convert]::ToInt32($digits.Substring(4, 2), 16)
        Text = '#' + $digits.ToUpperInvariant()
    }
}

function Read-ThemeDictionaries([string]$path) {
    [xml]$doc = Get-Content -Raw -LiteralPath $path
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace('a', $xamlNs)
    $result = [ordered]@{}
    foreach ($dict in $doc.SelectNodes('//a:ResourceDictionary.ThemeDictionaries/a:ResourceDictionary', $ns)) {
        $theme = $dict.GetAttribute('Key', $xKeyNs)
        $entries = @{}
        foreach ($child in $dict.ChildNodes) {
            if ($child.NodeType -ne 'Element') { continue }
            $k = $child.GetAttribute('Key', $xKeyNs)
            if (-not $k) { continue }
            $opacity = 1.0
            $opacityText = $child.GetAttribute('Opacity')
            if ($opacityText) { $opacity = [double]::Parse($opacityText, [Globalization.CultureInfo]::InvariantCulture) }
            $entries[$k] = [pscustomobject]@{
                Color = $child.GetAttribute('Color')
                Rgb = ConvertTo-Rgb $child.GetAttribute('Color')
                Opacity = $opacity
            }
        }
        $result[$theme] = $entries
    }
    return $result
}

$paletteKeys = @()          # XAML 里出现过的全部色板键
$builtIn = @{}              # "主题/token" → 条目（颜色 + 浓度）

foreach ($rel in $paletteFiles) {
    $full = Join-Path $root $rel
    if (-not (Test-Path -LiteralPath $full)) {
        $problems += "$rel 不存在（色板文件清单写错了？）"
        continue
    }

    $themes = Read-ThemeDictionaries $full
    if ($themes.Count -eq 0) {
        $problems += "$rel 里没有任何主题字典（浅深两套都要有）"
    }

    foreach ($theme in $themes.Keys) {
        foreach ($k in $themes[$theme].Keys) {
            $paletteKeys += [pscustomobject]@{ File = $rel; Theme = $theme; Key = $k }
            $builtIn["$theme/$k"] = $themes[$theme][$k]
        }
    }

    # 浅深成对：同一支 token 两套都要有（缺一套 = 只有一半主题有这支色）
    $names = @($themes.Keys)
    if ($names.Count -ge 2) {
        $first = @($themes[$names[0]].Keys)
        foreach ($other in $names | Select-Object -Skip 1) {
            foreach ($k in $first) {
                if ($k -notin $themes[$other].Keys) { $problems += "$rel 的 [$($names[0])] 有 '$k'，而 [$other] 没有（浅深必须成对）" }
            }
            foreach ($k in @($themes[$other].Keys)) {
                if ($k -notin $first) { $problems += "$rel 的 [$other] 有 '$k'，而 [$($names[0])] 没有（浅深必须成对）" }
            }
        }
    }
}

$manifestFull = Join-Path $root $manifestRel
$tokens = @()
if (-not (Test-Path -LiteralPath $manifestFull)) {
    $problems += "$manifestRel 不存在（色板契约是面板、应用器与文档的唯一事实源）"
}
else {
    $manifest = Get-Content -Raw -LiteralPath $manifestFull | ConvertFrom-Json
    $tokens = @($manifest.tokens)
}

$manifestKeys = @($tokens | ForEach-Object { $_.key })

# ② 一一对应（两个方向都查）+ 同一支色在同一套主题字典里只许定义一次
foreach ($k in ($paletteKeys.Key | Sort-Object -Unique)) {
    if ($k -notin $manifestKeys) { $problems += "色板键 '$k' 在 XAML 里有，但 $manifestRel 里没登记" }
}

foreach ($duplicate in ($paletteKeys | Group-Object File, Theme, Key | Where-Object { $_.Count -gt 1 })) {
    $parts = $duplicate.Name -split ', '
    $problems += "色板键 '$($parts[2])' 在 $($parts[0]) 的 [$($parts[1])] 里定义了 $($duplicate.Count) 次（同一支色同一套只许一处）"
}

foreach ($k in $manifestKeys) {
    if ($k -notin $paletteKeys.Key) { $problems += "$manifestRel 登记了 '$k'，但色板 XAML 里没有这支键" }
}

foreach ($duplicate in ($manifestKeys | Group-Object | Where-Object { $_.Count -gt 1 })) {
    $problems += "$manifestRel 里 '$($duplicate.Name)' 登记了 $($duplicate.Count) 次"
}

Write-Host ("色板契约：XAML {0} 支 · 契约 {1} 条" -f ($paletteKeys.Key | Sort-Object -Unique).Count, $manifestKeys.Count) -ForegroundColor DarkCyan

# ---------------------------------------------------------------- ③ 契约与值一致（派生 / 角色 / 分组）

$groupIds = @($manifest.groups | ForEach-Object { $_.id })
$tokensWithDerive = @($tokens | Where-Object { $_.derive })

foreach ($token in $tokens) {
    if ($token.group -notin $groupIds) {
        $problems += "契约里 $($token.key) 的分组 '$($token.group)' 没在 groups 里登记"
    }

    if (-not $token.derive) { continue }

    $source = $token.derive.from
    if ([string]::IsNullOrWhiteSpace($source)) {
        $problems += "契约里 $($token.key) 的派生规则缺 from"
        continue
    }

    $sourceToken = $tokens | Where-Object { $_.key -eq $source } | Select-Object -First 1
    if (-not $sourceToken) {
        $problems += "契约里 $($token.key) 派生自 '$source'，但契约里没有这一支"
        continue
    }

    if ($sourceToken.derive) {
        $problems += "契约里 $($token.key) 派生自 '$source'，而 '$source' 自己也派生（链条只允许一层 ⇒ 会成环或与顺序有关）"
    }

    if ($token.derive.mode -eq 'shift') { continue }   # 色相偏移由"这一支与源的内置值"这一对定义 ⇒ 结构上成立

    foreach ($theme in @('Light', 'Dark')) {
        $mine = $builtIn["$theme/$($token.key)"]
        $src = $builtIn["$theme/$source"]
        if (-not $mine -or -not $src) { continue }

        switch ($token.derive.mode) {
            'tint' {
                if ($mine.Rgb.Text -ne $src.Rgb.Text) {
                    $problems += "[$theme] $($token.key) 是 tint 支，内置色 $($mine.Rgb.Text) 应等于源 $source 的内置色 $($src.Rgb.Text)"
                }
                if ($mine.Opacity -le 0 -or $mine.Opacity -ge 1) {
                    $problems += "[$theme] $($token.key) 是 tint 支，内置不透明度应 ∈ (0,1)（当前 $($mine.Opacity)）"
                }
            }
            'darken' {
                $amount = [double]$token.derive.amount
                if ($amount -le 0 -or $amount -ge 1) {
                    $problems += "$($token.key) 的 darken amount 应在 (0,1)（当前 $amount）"
                    break
                }

                foreach ($channel in @('R', 'G', 'B')) {
                    $expected = [Math]::Round($src.Rgb[$channel] * (1 - $amount))
                    $actual = $mine.Rgb[$channel]
                    if ([Math]::Abs($expected - $actual) -gt 1) {
                        $problems += "[$theme] $($token.key) 是 darken 支，内置色 $($mine.Rgb.Text) 与「$source 的内置色按 $amount 变深」算出来的不一致（$channel 通道：期望 $expected、实际 $actual）"
                    }
                }
            }
            default {
                $problems += "契约里 $($token.key) 的派生规则种类不认识：'$($token.derive.mode)'"
            }
        }
    }
}

# 角色词：契约里的 roles ↔ ViewInkRoles 词表，双向一一对应
$roleFile = Join-Path $root 'src/Sdk/Views/ViewInkRoles.cs'
$vocabulary = @()
if (Test-Path -LiteralPath $roleFile) {
    $vocabulary = @(Select-String -LiteralPath $roleFile -Pattern 'public const string \w+\s*=\s*"([^"]+)";' |
        ForEach-Object { $_.Matches[0].Groups[1].Value })
}
else {
    $problems += 'src/Sdk/Views/ViewInkRoles.cs 不存在（角色词表是契约的一部分）'
}

$contractRoles = @($tokens | ForEach-Object { $_.roles } | Where-Object { $_ } | ForEach-Object { $_ })

foreach ($role in $vocabulary) {
    if ($role -notin $contractRoles) { $problems += "角色词 '$role' 在 ViewInkRoles 里有，但契约里没有任何一支承担它" }
}

foreach ($role in $contractRoles) {
    if ($role -notin $vocabulary) { $problems += "契约里登记了角色词 '$role'，但 ViewInkRoles 里没有这个词" }
}

foreach ($duplicate in ($contractRoles | Group-Object | Where-Object { $_.Count -gt 1 })) {
    $problems += "角色词 '$($duplicate.Name)' 在契约里挂了 $($duplicate.Count) 支（一个词只承担一支）"
}

Write-Host ("角色词：词表 {0} 个 · 契约 {1} 个 · 派生支 {2} 支" -f $vocabulary.Count, $contractRoles.Count, $tokensWithDerive.Count) -ForegroundColor DarkCyan

foreach ($problem in $problems) { Write-Host ("  x " + $problem) -ForegroundColor Red }

if ($total -eq 0 -and $problems.Count -eq 0) {
    Write-Host '[OK] 颜色口径审计通过：没有未标记的字面颜色；色板契约与值一一对应、派生规则与内置值一致。' -ForegroundColor Green
    exit 0
}

if ($total -gt 0) {
    Write-Host ("[FAIL] 颜色口径审计：{0} 处未标记的字面颜色（要么改成引 token，要么标上「绝对色：理由」）。" -f $total) -ForegroundColor Red
}

if ($problems.Count -gt 0) {
    Write-Host ("[FAIL] 色板契约审计：{0} 处不对账（见上方 x 行）。" -f $problems.Count) -ForegroundColor Red
}

exit 1
