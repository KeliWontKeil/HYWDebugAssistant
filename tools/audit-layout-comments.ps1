#Requires -Version 7.0
<#
.SYNOPSIS
  源码注释的「布局数值」审计（源侧）：`src/**/*.axaml` 的注释不得写当前坐标 / 尺寸 / 像素值。

.DESCRIPTION
  判据（STYLE.md 的注释纪律 / CODEX.md §9.1 AR-19 的布局数值条款）：

    · 扫描范围：`src/` 下所有 `.axaml` 的 XML 注释（`<!-- ... -->`，可跨行；注释外的属性不算）；
    · 命中（任一条即违规）：
        - `<数字> px`（含小数与大小写）；
        - `<数字> × <数字>`（`×` / `x` / `X` 都算）；
        - 布局词附近的数字：宽 / 高 / 左 / 右 / 上 / 下 / 坐标 / 位置 / 行 / 列 / 边 / 间距 / 像素；
        - 注释里出现 `Canvas.Left/Top/Width/Height="..."`；
    · 排除（先把这些片段遮掉再判）：`ADR-` / `M1-` 编号、`§` 引用、日期、结构版本字段、
      三段式版本号；它们不是"当前布局取值"，只是编号与引用；
    · 违规一律打印 `文件:行号: 注释原文` 并 `exit 1`；目标 0 命中。

  为什么：注释里的"现在正好是多少"必然随布局调整过期，并成为代码/主题/声明之外的第二份事实源。
  注释只写**意图 / 关系 / 坑**；具体取值指向唯一来源（声明文件、主题 token、控件属性），
  改布局时注释不需要跟着改 —— 需要跟着改的注释就是写错了地方。

.EXAMPLE
  pwsh -NoProfile -File tools/audit-layout-comments.ps1
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $root 'src'

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    Write-Host "[FAIL] 找不到源码根：$sourceRoot" -ForegroundColor Red
    exit 1
}

$layoutWords = '宽|高|左|右|上|下|坐标|位置|行|列|边|间距|像素'
$layoutNearNumber = [regex]::new(
    "(?:$layoutWords)[^\d\r\n]{0,8}\d|\d[^\d\r\n]{0,8}(?:$layoutWords)",
    [System.Text.RegularExpressions.RegexOptions]::None)

$pixelValue = [regex]::new('\d+(?:\.\d+)?\s*px', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$dimension = [regex]::new(
    '\d+(?:\.\d+)?\s*[×xX]\s*\d+(?:\.\d+)?',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$canvasAttribute = [regex]::new(
    'Canvas\.(Left|Top|Width|Height)\s*=\s*"[^"]*"',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

# 编号 / 引用 / 日期 / 版本号不是"当前布局取值"：先遮掉，避免误报。
# 决定引用常带子项（`ADR-0049 D13/Q11`、`ADR-0039 D1-D3`）：整段一起遮，否则残留的 `D5` 会被
# 当成"布局词附近的数字"。
$exclusions = [regex]::new(
    '(?i)((?:ADR|M1)-\d+(?:\s+[A-Z]\d+(?:\s*[-–/]\s*[A-Z]?\d+)*)?|§[0-9.]*|\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\bschema\w*\b|\bv?\d+(?:\.\d+){2,}\b)')

$commentPattern = [regex]::new('<!--(?<body>.*?)-->', [System.Text.RegularExpressions.RegexOptions]::Singleline)

$files = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.axaml' | Sort-Object FullName)
$comments = 0
$problems = 0

foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
    if ([string]::IsNullOrEmpty($text)) { continue }

    foreach ($match in $commentPattern.Matches($text)) {
        $comments++
        $body = $match.Groups['body'].Value
        $lines = $body -split "`r?`n"
        $offset = $match.Index + 4   # `<!--` 之后

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i].Trim()

            # 行号按"这一行在文件里实际起始位置"算（相对偏移在多行注释上会差一行）。
            $lineStart = $offset
            $offset = $lineStart + $lines[$i].Length
            if ($offset -lt $text.Length -and $text[$offset] -eq "`r") { $offset++ }
            if ($offset -lt $text.Length -and $text[$offset] -eq "`n") { $offset++ }

            if ($line.Length -eq 0) { continue }

            $masked = $exclusions.Replace($line, { param($m) ' ' * $m.Length })

            $hit = $pixelValue.IsMatch($masked) `
                -or $dimension.IsMatch($masked) `
                -or $canvasAttribute.IsMatch($masked) `
                -or $layoutNearNumber.IsMatch($masked)

            if (-not $hit) { continue }

            $lineNumber = ([regex]::Matches($text.Substring(0, $lineStart), "`n")).Count + 1
            $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
            Write-Host ("  x {0}:{1}  {2}" -f $relative, $lineNumber, $line) -ForegroundColor Red
            $problems++
        }
    }
}

$scanned = $files.Count
Write-Host ""
Write-Host ("源码注释布局数值审计：扫描 {0} 个 .axaml · {1} 段注释 · {2} 处违规" -f $scanned, $comments, $problems)

if ($problems -gt 0) {
    Write-Host "[FAIL] 注释不得写当前坐标 / 尺寸 / 像素值（判据见本脚本头部；改写成意图 / 关系 / 坑）" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] 源码注释不含当前布局数值" -ForegroundColor Green
exit 0
