# 文档收敛机械审计（只读；不修改任何文件）
#
# 用法（在仓库根或任意位置均可）：
#   pwsh -NoProfile -File .\docs\tools\audit-docs.ps1
#
# 判据：
#  1. 失效术语黑名单：现行路径出现黑名单词**且该行没有作废/历史标记** ⇒ 失败
#     （带"作废/取代/历史/归档/当时/原…"等标记的行 = 有意的废止说明，允许）
#  2. 引用可达性：现行文档里的 `docs/**.md` 引用必须存在；`ADR-00XX` 必须能在归档里找到
#     （ADR-0047 例外：它从未产出，文档已注明）
#  3. 数值抽查：遮罩不透明度必须唯一（**带作废/历史/当时标记的行 = 有意的历史取值，跳过**，
#     与第 1 项同一口径）；接触判定阈值为**信息项**（多个阈值分属不同判定，非冲突）
#  4. 结构完整性：11 份分册 + README/DOC-WORKFLOW/decisions/roadmap + implementation 8 份 + modules + skills 齐备；旧文件不在现行路径
#  5. skills 去事实：SKILL.md 与附件不得含本项目专名（索引 README 例外，它含"来源对照"）
#     skills 位置 = <repo>\.dsh\skills（2026-09 由 docs\skills 迁出，用户显式跟踪；见 docs\README.md §1/§2）
#  6. 表格完整性：同一张表里任何一行的格数**多于**表头格数 ⇒ 失败（单元格里未转义的 `|` 会把整行撑错位）
#     格数少于表头 = 信息项（允许的短行）
#  7. 登记一致性：**现行文档必须有家** —— `docs/` 一级目录必须在白名单内（`$dirWhite`）；
#     `docs/` 根下的 `.md` 与 `docs/implementation/` 下的文件必须全部在 `$expected` 里。
#     失败含义 = 出现了"三不管"内容（新目录 / 新文件没登记，读的人与审计都看不见它）
#  8. 现行正文不得携带脆弱计数：非 `journal/` 的现行文档里出现 `约/共/合计 N 条` ⇒ 失败
#     （带废止/历史标记的行放行；`journal/` 里的历史计数只计数不失败）

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# 根目录按脚本位置推导：<repo>\docs\tools\audit-docs.ps1
$docs = Split-Path $PSScriptRoot -Parent          # <repo>\docs
$root = Split-Path $docs -Parent                  # <repo>
$mainDoc = Join-Path $root '上位机架构设计.md'

function Get-CurrentDocs {
    @(
        Get-ChildItem $docs -Recurse -File -Filter '*.md' |
            Where-Object { $_.FullName -notmatch '\\archive\\' }
        Get-Item $mainDoc -ErrorAction SilentlyContinue
    ) | Where-Object { $_ }
}

$fail = 0
function Report-Fail([string]$msg) { Write-Host "  x $msg" -ForegroundColor Red; $script:fail++ }
function Report-Ok([string]$msg) { Write-Host "  v $msg" -ForegroundColor Green }
function Report-Info([string]$msg) { Write-Host "  i $msg" -ForegroundColor DarkGray }

Write-Host "审计根目录：$root" -ForegroundColor DarkGray
Write-Host '== 1. 失效术语黑名单（带作废/历史标记的行允许）==' -ForegroundColor Green
$historyMarkers = '作废|废除|取代|已由|已被|历史|归档|当时|原[""]|原来|不再|已不适用|未使用|从未产出|清除|移除|删除'
$blacklist = @(
    @{ Pattern = '快速建网|R0[–-]R6'; Why = 'ADR-0037 已整体作废的建网方案' },
    @{ Pattern = '不建立数据流'; Why = 'ADR-0022 G3，已被 ADR-0050 取代' },
    @{ Pattern = '形状子集'; Why = '兼容旧口径，已被 ADR-0051（形状完全一致）取代' },
    @{ Pattern = '标题栏\s*24'; Why = '已废止的标题栏尺寸' },
    @{ Pattern = '修订版\s*\d'; Why = '验收清单已改为无版本号引用（里程碑收尾即冻结归档）' }
)
foreach ($item in $blacklist) {
    $hits = @(Get-CurrentDocs | Select-String -Pattern $item.Pattern -Encoding UTF8)
    $bad = @($hits | Where-Object { $_.Line -notmatch $historyMarkers })
    if ($bad.Count -gt 0) {
        Report-Fail "黑名单命中且**无废止标记**「$($item.Pattern)」（$($item.Why)）："
        $bad | Select-Object -First 10 | ForEach-Object { Write-Host "      $($_.Path.Substring($root.Length + 1)):$($_.LineNumber)" -ForegroundColor DarkRed }
    }
    elseif ($hits.Count -gt 0) { Report-Ok "「$($item.Pattern)」仅出现在废止/历史说明中（$($hits.Count) 处）" }
    else { Report-Ok "无「$($item.Pattern)」" }
}

Write-Host '== 2. 引用可达性 ==' -ForegroundColor Green
$missing = @()
foreach ($file in Get-CurrentDocs) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($m in [regex]::Matches($text, 'docs[\\/][A-Za-z0-9_\-\\/\.]+\.md')) {
        $rel = $m.Value -replace '/', '\'
        if (-not (Test-Path -LiteralPath (Join-Path $root $rel))) { $missing += "$($file.Name) → $rel" }
    }
    foreach ($m in [regex]::Matches($text, '(ADR-\d{4})')) {
        $adr = $m.Groups[1].Value
        if ($adr -eq 'ADR-0047') { continue }
        $found = Get-ChildItem (Join-Path $docs 'archive\adr') -File -Filter "$adr*.md" -ErrorAction SilentlyContinue
        if (-not $found) { $missing += "$($file.Name) → $adr（未找到该 ADR 文件）" }
    }
}
$missing = $missing | Sort-Object -Unique
if ($missing) { $missing | Select-Object -First 20 | ForEach-Object { Report-Fail $_ } }
else { Report-Ok '引用全部可达（ADR-0047 已按"从未产出"豁免）' }

Write-Host '== 3. 数值抽查 ==' -ForegroundColor Green
$mask = @{}
$maskHistory = 0
foreach ($file in Get-CurrentDocs) {
    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        if ($line -match $historyMarkers) {
            $maskHistory += @([regex]::Matches($line, '遮罩浓度[^|\r\n]{0,80}?0\.(\d+)')).Count
            continue
        }
        foreach ($m in [regex]::Matches($line, '遮罩浓度[^|\r\n]{0,80}?0\.(\d+)')) {
            $v = '0.' + $m.Groups[1].Value
            if (-not $mask.ContainsKey($v)) { $mask[$v] = @() }
            $mask[$v] += $file.Name
        }
    }
}
if ($mask.Keys.Count -le 1) { Report-Ok "遮罩不透明度唯一：$($mask.Keys -join ', ')" }
else { Report-Fail "遮罩不透明度出现多值：$(($mask.GetEnumerator() | ForEach-Object { "$($_.Key) <- $($_.Value -join ',')" }) -join ' | ')" }
if ($maskHistory -gt 0) { Report-Info "带废止/当时标记的历史取值跳过：$maskHistory 处（有意保留的历史记录，不参与唯一性）" }

$tol = @{}
foreach ($file in Get-CurrentDocs) {
    foreach ($m in [regex]::Matches((Get-Content -Raw -LiteralPath $file.FullName), '(?:容差|阈值)[^。|\r\n]{0,25}?(\d+)\s*(?:px|DIP)')) {
        $tol[$m.Groups[1].Value] = $true
    }
}
Report-Info "接触判定出现的数值：$(($tol.Keys | Sort-Object { [int]$_ }) -join ', ') —— 分属不同判定（接触间隙 / 标记中心 / 边缘对齐），非冲突"

Write-Host '== 4. 结构完整性 ==' -ForegroundColor Green
$expected = @(
    'docs\README.md', 'docs\DOC-WORKFLOW.md', 'docs\decisions.md', 'docs\roadmap.md',
    'docs\architecture\00-architecture-index.md', 'docs\architecture\01-layering-and-projects.md',
    'docs\architecture\02-module-sdk.md', 'docs\architecture\03-data-protocol-and-typing.md',
    'docs\architecture\04-runtime-mechanisms.md', 'docs\architecture\05-network-semantics.md',
    'docs\architecture\06-ui-shell-and-windows.md', 'docs\architecture\07-overlay-and-visual-baseline.md',
    'docs\architecture\08-application-settings.md', 'docs\architecture\09-test-and-gate.md',
    'docs\architecture\10-delivery-and-ops.md',
    'docs\implementation\00-pre-implementation-constraints.md', 'docs\implementation\01-coding-style-and-dev-conventions.md',
    'docs\implementation\02-engineering-baseline.md', 'docs\implementation\03-development-workflow-and-coding-details.md',
    'docs\implementation\04-m1-manual-acceptance.md', 'docs\implementation\05-two-track-development-workflow.md',
    'docs\implementation\06-module-standard-and-development-process.md',
    'docs\implementation\07-dev-inner-loop.md',
    'docs\implementation\08-lessons.md',
    'docs\modules\README.md', 'docs\modules\_模板.md',
    '.dsh\skills\README.md',
    'docs\tools\audit-docs.ps1'
)
foreach ($rel in $expected) {
    if (Test-Path -LiteralPath (Join-Path $root $rel)) { Report-Ok $rel }
    else { Report-Fail "缺文件：$rel" }
}

$volumeLeaves = $expected | Where-Object { $_ -like 'docs\architecture\*' } | ForEach-Object { Split-Path $_ -Leaf }
$stale = Get-ChildItem (Join-Path $docs 'architecture') -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin $volumeLeaves }
if ($stale) { $stale | ForEach-Object { Report-Fail "现行目录里仍有旧分册：$($_.Name)" } }
else { Report-Ok 'architecture 目录只剩 11 份现行分册' }

if (Test-Path (Join-Path $docs 'adr')) { Report-Fail 'docs/adr 目录仍存在（ADR 应已归档）' } else { Report-Ok 'ADR 原文已全部归档，docs/adr 已移除' }

Write-Host '== 5. skills 去事实（索引 README 例外）==' -ForegroundColor Green
$names = 'HYWDebugAssistant|ModuleRuntime|ConnectionOverlay|PacketTypePalette|dev\.ps1|MainWindow|SettingsService'
$skills = Join-Path $root '.dsh\skills'
Get-ChildItem $skills -Recurse -File -Filter '*.md' -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'README.md' } | ForEach-Object {
    $text = Get-Content -Raw -LiteralPath $_.FullName
    if ($text -match $names) { Report-Fail "skills 正文含本项目专名：$($_.Name)" }
    else { Report-Ok "去事实检查通过：$($_.Name)" }
}

Write-Host '== 6. Markdown 表格完整性（行的格数不得多于表头）==' -ForegroundColor Green
$tableBad = @()
$tableShort = 0
foreach ($file in Get-CurrentDocs) {
    $lines = Get-Content -LiteralPath $file.FullName
    $inFence = $false
    $inTable = $false
    $headerCells = 0
    $n = 0
    foreach ($line in $lines) {
        $n++
        if ($line -match '^\s*```') { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        $trimmed = $line.TrimStart()
        if (-not $trimmed.StartsWith('|')) { $inTable = $false; $headerCells = 0; continue }
        $cells = ([regex]::Matches($trimmed, '(?<!\\)\|')).Count - 1
        if (-not $inTable) { $inTable = $true; $headerCells = $cells; continue }
        if ($cells -gt $headerCells) { $tableBad += "$($file.Name):$n 格数 $cells > 表头 $headerCells" }
        elseif ($cells -lt $headerCells) { $tableShort++ }
    }
}
if ($tableBad) { $tableBad | Select-Object -First 20 | ForEach-Object { Report-Fail "表格行的格数多于表头（单元格里有未转义的竖线）：$_" } }
else { Report-Ok '所有表格行的格数均不超过所在表的表头' }
if ($tableShort -gt 0) { Report-Info "格数少于表头的行：$tableShort 处（表格允许的短行，非错误）" }

Write-Host '== 7. 登记一致性（现行文档必须"有家"）==' -ForegroundColor Green
$dirWhite = @('architecture', 'implementation', 'journal', 'modules', 'archive', 'tools')
$dirStray = @(Get-ChildItem $docs -Directory | Where-Object { $_.Name -notin $dirWhite })
if ($dirStray) {
    $dirStray | ForEach-Object { Report-Fail "docs/ 下出现未登记的一级目录：$($_.Name)（白名单：$($dirWhite -join ' / ')）" }
} else { Report-Ok "docs/ 一级目录均为已登记目录（$($dirWhite -join ' / ')）" }

$expectedRel = @($expected | ForEach-Object { $_ -replace '/', '\' })
$rootUnregistered = @(Get-ChildItem $docs -File -Filter '*.md' | Where-Object { "docs\$($_.Name)" -notin $expectedRel })
if ($rootUnregistered) {
    $rootUnregistered | ForEach-Object { Report-Fail "docs/ 根下的现行文档未登记：$($_.Name)（登记处 = 本脚本 `$expected + docs/README.md §1 文档树）" }
} else { Report-Ok 'docs/ 根下的现行文档均已登记（README / DOC-WORKFLOW / decisions / roadmap）' }

$implAll = @(Get-ChildItem (Join-Path $docs 'implementation') -File)
$implUnregistered = @($implAll | Where-Object { "docs\implementation\$($_.Name)" -notin $expectedRel })
if ($implUnregistered) {
    $implUnregistered | ForEach-Object { Report-Fail "implementation/ 里仍有未登记文件（收敛前的旧文档？）：$($_.Name)" }
} else { Report-Ok "implementation 目录只剩 $($implAll.Count) 份已登记现行文件" }

Write-Host '== 8. 现行正文不得携带脆弱计数（数量 / 条目数）==' -ForegroundColor Green
$countBad = @()
$countHist = 0
$countJournal = 0
foreach ($file in Get-CurrentDocs) {
    $n = 0
    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $n++
        if ($line -notmatch '(约|共|合计)\s*[0-9]+\s*条') { continue }
        if ($file.FullName -match '\\journal\\') { $countJournal++; continue }
        if ($line -match $historyMarkers) { $countHist++; continue }
        $countBad += "$($file.FullName.Substring($root.Length + 1)):$n"
    }
}
if ($countBad) { $countBad | Select-Object -First 20 | ForEach-Object { Report-Fail "现行正文里的条目数计数（应改为「编号区间」或移入 journal/）：$_" } }
else { Report-Ok '现行正文无脆弱条目数计数（"约 N 条"只出现在 journal/ 或带历史标记的行里）' }
if ($countHist -gt 0) { Report-Info "带历史标记而放行的计数：$countHist 处" }
if ($countJournal -gt 0) { Report-Info "journal/ 里的历史计数：$countJournal 处（过程材料，不作为依据）" }

Write-Host ''
if ($fail -eq 0) { Write-Host '[OK] 机械审计通过' -ForegroundColor Green } else { Write-Host "[FAIL] 机械审计 $fail 项未通过" -ForegroundColor Red }
exit $fail
