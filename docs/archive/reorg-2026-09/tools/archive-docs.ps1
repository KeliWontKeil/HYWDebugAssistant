# Phase C：文档归档搬移 v2（默认 dry-run；-Execute 才真搬）
#
# 取源规则（关键）——本轮有些文件是**原地改写**的，它们的"原件"不在工作区：
#   · 未改写的旧文件        → 直接从工作区搬（move）
#   · 被改写过的文件的原件   → 从收敛前快照 .reorg\backup-pre-convergence\ 复制（copy）
#   · decisions.md 的原件    → 快照里已是新版（写表早于快照）⇒ 从 **git 索引**取（git show :docs/decisions.md）
# 原则：原文一字不删，只在正文最前面加一行状态标记。幂等：目标已存在则跳过。

[CmdletBinding()]
param([switch]$Execute)

$ErrorActionPreference = 'Stop'
$root = 'D:\project\上位机开发'
$docs = Join-Path $root 'docs'
$archive = Join-Path $docs 'archive'
$backup = Join-Path $root '.reorg\backup-pre-convergence'

$markerAdr = '> ⚠ 历史材料（ADR 原文，2026-09 文档收敛起归档）。**现行决策**见 `docs/decisions.md`；实现层现行值见 `docs/architecture/` 各分册。正文为当时的决策与论证；其中的"待决/待办/计划"一律以现行文档为准。'
$markerArch = '> ⚠ 历史材料（2026-09 文档收敛前的旧版架构分册）。**现行结论**见 `docs/architecture/` 下的 11 份现行分册（索引 `00-architecture-index.md`）；本文件仅作追溯用，其中的数值/口径可能已被后续修订取代。'
$markerProc = '> ⚠ 历史材料（过程记录，2026-09 归档）。**现行状态**见 `docs/roadmap.md`（状态唯一源）与 `docs/README.md`。'
$markerImpl = '> ⚠ 历史材料（工程文档的收敛前版本，2026-09 归档）。**现行版本**见 `docs/implementation/` 下同名文件。'
$markerDec = '> ⚠ 历史材料（收敛前的 `decisions.md`：只含架构例外登记）。**现行** `docs/decisions.md` = 现行决策表 + 例外登记，本文件的例外登记内容已一字不差并入。'

function Write-Marker {
    param([string]$Path, [string]$Marker)
    if (-not $Execute) { return }
    $text = Get-Content -Raw -LiteralPath $Path
    if ($text.StartsWith('> ⚠ 历史材料')) { Write-Host "  [skip] 已有标记" -ForegroundColor DarkYellow; return }
    Set-Content -LiteralPath $Path -Value ($Marker + "`r`n`r`n" + $text) -Encoding UTF8 -NoNewline
}

function Ensure-Dir {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host "  [dir ] $Path"
        if ($Execute) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
    }
}

function Move-Doc {
    param([string]$From, [string]$To, [string]$Marker)
    if (-not (Test-Path -LiteralPath $From)) { Write-Host "  [skip] 源不存在：$From" -ForegroundColor DarkYellow; return }
    if (Test-Path -LiteralPath $To) { Write-Host "  [skip] 目标已存在：$To" -ForegroundColor DarkYellow; return }
    Ensure-Dir (Split-Path -Parent $To)
    Write-Host "  [move] $(Split-Path -Leaf $From) → $($To.Substring($archive.Length + 1))" -ForegroundColor Cyan
    if ($Execute) { Move-Item -LiteralPath $From -Destination $To; Write-Marker -Path $To -Marker $Marker }
}

function Copy-FromBackup {
    param([string]$Rel, [string]$To, [string]$Marker)   # $Rel 相对根，如 docs\implementation\02-….md
    $From = Join-Path $backup $Rel
    if (-not (Test-Path -LiteralPath $From)) { Write-Host "  [skip] 快照里没有：$Rel" -ForegroundColor DarkYellow; return }
    if (Test-Path -LiteralPath $To) { Write-Host "  [skip] 目标已存在：$To" -ForegroundColor DarkYellow; return }
    Ensure-Dir (Split-Path -Parent $To)
    Write-Host "  [copy] 快照原件 $Rel → $($To.Substring($archive.Length + 1))" -ForegroundColor Cyan
    if ($Execute) { Copy-Item -LiteralPath $From -Destination $To -Force; Write-Marker -Path $To -Marker $Marker }
}

Write-Host '== 1. ADR 原文 + ADR 历史索引 → docs/archive/adr/ ==' -ForegroundColor Green
Get-ChildItem (Join-Path $docs 'adr') -File -ErrorAction SilentlyContinue | ForEach-Object {
    Move-Doc -From $_.FullName -To (Join-Path $archive ('adr\' + $_.Name)) -Marker $markerAdr
}

Write-Host '== 2. 旧架构分册（未改写）→ docs/archive/architecture/ ==' -ForegroundColor Green
Get-ChildItem (Join-Path $docs 'architecture') -File -Filter '*.md' -ErrorAction SilentlyContinue | ForEach-Object {
    Move-Doc -From $_.FullName -To (Join-Path $archive ('architecture\' + $_.Name)) -Marker $markerArch
}

Write-Host '== 3. 过程记录 → docs/archive/ ==' -ForegroundColor Green
Move-Doc -From (Join-Path $docs 'changelog.md') -To (Join-Path $archive 'changelog.md') -Marker $markerProc
Copy-FromBackup -Rel 'docs\roadmap.md' -To (Join-Path $archive 'roadmap.md') -Marker $markerProc   # 原件取自快照
Copy-FromBackup -Rel 'docs\README.md'  -To (Join-Path $archive 'README.md')  -Marker $markerProc   # 原件取自快照

Write-Host '== 4. 收敛前的 decisions.md（例外登记，原件在 git 索引）→ docs/archive/decisions.md ==' -ForegroundColor Green
$decTarget = Join-Path $archive 'decisions.md'
if (Test-Path -LiteralPath $decTarget) { Write-Host '  [skip] 目标已存在' -ForegroundColor DarkYellow }
else {
    Write-Host '  [git ] git show :docs/decisions.md → docs/archive/decisions.md' -ForegroundColor Cyan
    if ($Execute) {
        Push-Location $root
        $orig = git show ':docs/decisions.md'
        Pop-Location
        if (-not $orig) { throw 'git 索引里取不到 docs/decisions.md 的原件' }
        Set-Content -LiteralPath $decTarget -Value (($markerDec) + "`r`n`r`n" + ($orig -join "`r`n")) -Encoding UTF8 -NoNewline
        Write-Host "  [ok  ] 已写出（$((Get-Item $decTarget).Length) B）" -ForegroundColor Green
    }
}

Write-Host '== 5. 选型论证（未改写）→ docs/archive/technology-selection/ ==' -ForegroundColor Green
Get-ChildItem (Join-Path $docs 'technology-selection') -File -Filter '*.md' -ErrorAction SilentlyContinue | ForEach-Object {
    Move-Doc -From $_.FullName -To (Join-Path $archive ('technology-selection\' + $_.Name)) -Marker $markerProc
}

Write-Host '== 6. 工程文档的收敛前版本（原件取自快照）→ docs/archive/implementation/ ==' -ForegroundColor Green
foreach ($name in @('00-pre-implementation-constraints.md', '02-engineering-baseline.md', '03-development-workflow-and-coding-details.md', '04-m1-manual-acceptance.md')) {
    Copy-FromBackup -Rel "docs\implementation\$name" -To (Join-Path $archive "implementation\$name") -Marker $markerImpl
}

Write-Host '== 7. 本轮评估报告与冲突登记 → docs/archive/reorg-2026-09/ ==' -ForegroundColor Green
Get-ChildItem (Join-Path $root '.reorg') -File -Filter '*.md' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'new-README.md' } | ForEach-Object {
        Move-Doc -From $_.FullName -To (Join-Path $archive ('reorg-2026-09\' + $_.Name)) -Marker $markerProc
    }
Get-ChildItem (Join-Path $root '.reorg\skills-draft') -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
    $rel = $_.FullName.Substring((Join-Path $root '.reorg\skills-draft').Length + 1)
    Move-Doc -From $_.FullName -To (Join-Path $archive ('reorg-2026-09\skills-draft\' + $rel)) -Marker $markerProc
}

Write-Host ''
if (-not $Execute) { Write-Host '（dry-run 结束；加 -Execute 才真正搬移）' -ForegroundColor Yellow }
