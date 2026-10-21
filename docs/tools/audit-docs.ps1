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
#  3. 数值一致性（**只提示，不判负**）：文档不该写具体数值（它们以代码为准、随开发常改，
#     见 HOWTO §4 第 8 条）⇒ 这里只统计同一数值是否在多处出现，供人工判断有没有又长出第二份事实源；
#     数值本身**不作为验收/门禁判据**（"宽度是多少"不重要，"声明有没有被框架正确应用"才重要）
#  4. 结构完整性：**现行文档清单的唯一来源 = 文件系统** —— 本脚本只声明"哪些位置算现行"
#     （`$currentSet`：docs/ 根 · architecture/ · design/ · tools/ · .dsh/skills/），
#     文件名在运行时由 `Get-ChildItem` 解析出来，**不手抄任何文件名**；声明过的位置里一个文件都没有 ⇒ 失败
#     （目录尚未出现的功能位放行：目录存在与否本身由当轮工作决定，不是结构残缺）
#  5. skills 去事实：SKILL.md 与附件不得含本项目专名（索引 README 例外，它含"来源对照"）
#     skills 位置 = <repo>\.dsh\skills（2026-09 由 docs\skills 迁出，用户显式跟踪）
#  6. 表格完整性：同一张表里任何一行的格数**多于**表头格数 ⇒ 失败（单元格里未转义的 `|` 会把整行撑错位）
#     格数少于表头 = 信息项（允许的短行）
#  7. 登记一致性：**现行文档必须有家，而"家"由位置定义、不由手工登记定义** ——
#     `docs/` 一级目录必须在白名单内（`$dirWhite`）；`docs/` 下除 `archive/` 外的每个 `.md`
#     必须落在 `$currentSet` 声明的位置里。清单的唯一来源是文件系统 ⇒ **新增文件自动登记**。
#     失败含义 = 出现了"三不管"内容（新目录 / 放在了没有家的位置，读的人与审计都看不见它）
#  8. 现行正文不得携带脆弱计数：非 `archive/` 的现行文档里出现 `约/共/合计 N 条` ⇒ 失败
#     （带废止/历史标记的行放行）

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# 根目录按脚本位置推导：<repo>\docs\tools\audit-docs.ps1
$docs = Split-Path $PSScriptRoot -Parent          # <repo>\docs
$root = Split-Path $docs -Parent                  # <repo>

# ==== 现行文档清单的唯一来源 = 文件系统 ====
# 这里只声明"哪些位置算现行"（结构性事实：现行文档集由哪几个家组成），**不列任何文件名**。
# 文件系统里实际存在的文件就是清单本身 ⇒ 新增 / 删除文件自动反映，无需改本脚本、无需改文档。
# 因此 docs/README.md §1 不再逐份手抄文件名，只指向这里。
$currentSet = @(
    @{ Dir = $docs;                             Filter = '*.md';     Why = 'docs/ 根（入口 / 工作流 / 代码是什么 / 正在做什么 / 怎么去做）' }
    @{ Dir = (Join-Path $docs 'architecture');  Filter = '*.md';     Why = '系统是什么（单册）' }
    @{ Dir = (Join-Path $docs 'design');        Filter = '*.md';     Why = '正在开发的功能的临时设计记录（做完即并入正文或删除）' }
    @{ Dir = (Join-Path $docs 'tools');         Filter = '*.ps1';    Why = '文档工具' }
    @{ Dir = (Join-Path $root '.dsh\skills');   Filter = 'README.md'; Why = '可迁移技能索引' }
)
$mdHome = @($currentSet | Where-Object { $_.Filter -eq '*.md' } | ForEach-Object { $_.Dir })
$mdHomeNorm = @($mdHome | ForEach-Object { $_.TrimEnd('\') })
$expected = @()                                   # 现行文件全量（FileInfo），由检查 4 从上面这些位置解析得到

function Get-Rel([string]$fullPath) { $fullPath.Substring($root.Length + 1) }

# 现行文档 = 只住在 docs\ 下（仓库根不再承载正文；历史材料一律进 docs\archive\ 而不参与审计）
function Get-CurrentDocs {
    @(
        Get-ChildItem $docs -Recurse -File -Filter '*.md' |
            Where-Object { $_.FullName -notmatch '\\archive\\' }
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

Write-Host '== 3. 数值一致性（只提示，不判负）==' -ForegroundColor Green
# 数值不再作为验收/门禁判据：文档不该写具体数值（见 HOWTO §4 第 8 条），
# 因此这里只统计"同一个数值在多处出现"，供人工判断是否又出现了第二份事实源，**不作为失败条件**。
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
Report-Info "出现的遮罩不透明度取值：$(if ($mask.Keys.Count) { $mask.Keys -join ', ' } else { '（无：文档已不写该数值）' }) —— 取值以代码为准，重复只作提示"
if ($maskHistory -gt 0) { Report-Info "带废止/当时标记的历史取值：$maskHistory 处（有意保留的历史记录）" }

$tol = @{}
foreach ($file in Get-CurrentDocs) {
    foreach ($m in [regex]::Matches((Get-Content -Raw -LiteralPath $file.FullName), '(?:容差|阈值)[^。|\r\n]{0,25}?(\d+)\s*(?:px|DIP)')) {
        $tol[$m.Groups[1].Value] = $true
    }
}
Report-Info "接触判定出现的数值：$(($tol.Keys | Sort-Object { [int]$_ }) -join ', ') —— 分属不同判定（接触间隙 / 标记中心 / 边缘对齐），非冲突"

Write-Host '== 4. 结构完整性（现行清单由文件系统解析，本脚本不手抄文件名）==' -ForegroundColor Green
$expected = @()
foreach ($loc in $currentSet) {
    $relLoc = Get-Rel $loc.Dir
    if (-not (Test-Path -LiteralPath $loc.Dir)) {
        Report-Info "位置尚未出现：$relLoc（$($loc.Why)）—— 功能位放行，不是结构残缺"
        continue
    }
    $found = @(Get-ChildItem -LiteralPath $loc.Dir -File -Filter $loc.Filter)
    if ($found.Count -eq 0) { Report-Fail "位置里没有任何文件：$relLoc（$($loc.Why)）" }
    $expected += $found
}
$expected = @($expected)
foreach ($f in $expected) { Report-Ok (Get-Rel $f.FullName) }
Report-Info "清单来源 = 文件系统：$($currentSet.Count) 个位置解析出 $($expected.Count) 个现行文件（新增 / 删除文件无需改脚本、无需改文档）"

$volumeCount = @($expected | Where-Object { $_.DirectoryName.TrimEnd('\') -ieq (Join-Path $docs 'architecture').TrimEnd('\') }).Count
Report-Ok "architecture/ 目录内容即现行分册清单（$volumeCount 份；新增分册自动纳入）"

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
$dirWhite = @('architecture', 'design', 'archive', 'tools')
$dirStray = @(Get-ChildItem $docs -Directory | Where-Object { $_.Name -notin $dirWhite })
if ($dirStray) {
    $dirStray | ForEach-Object { Report-Fail "docs/ 下出现未登记的一级目录：$($_.Name)（白名单：$($dirWhite -join ' / ')）" }
} else { Report-Ok "docs/ 一级目录均为已登记目录（$($dirWhite -join ' / ')）" }

# "家" = 位置（$currentSet 声明的那几个目录），不是手工清单：文件系统里有什么，现行清单就是什么。
$homeless = @(
    Get-ChildItem $docs -Recurse -File -Filter '*.md' |
        Where-Object { $_.FullName -notmatch '\\archive\\' } |
        Where-Object { $mdHomeNorm -notcontains $_.DirectoryName.TrimEnd('\') }
)
if ($homeless) {
    $homeless | ForEach-Object { Report-Fail "现行正文没有家：$(Get-Rel $_.FullName)（现行位置 = docs/ 根 / architecture/ / design/；archive/ 不参与清单）" }
} else { Report-Ok "docs/ 下（除 archive/）的每个 .md 都落在已声明的位置里 —— 现行清单 = 文件系统，新增文件自动登记" }

Write-Host '== 8. 现行正文不得携带脆弱计数（数量 / 条目数）==' -ForegroundColor Green
$countBad = @()
$countHist = 0
foreach ($file in Get-CurrentDocs) {
    $n = 0
    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $n++
        if ($line -notmatch '(约|共|合计)\s*[0-9]+\s*条') { continue }
        if ($line -match $historyMarkers) { $countHist++; continue }
        $countBad += "$($file.FullName.Substring($root.Length + 1)):$n"
    }
}
if ($countBad) { $countBad | Select-Object -First 20 | ForEach-Object { Report-Fail "现行正文里的条目数计数（应改为「编号区间」）：$_" } }
else { Report-Ok '现行正文无脆弱条目数计数（"约 N 条"只出现在带历史标记的行里）' }
if ($countHist -gt 0) { Report-Info "带历史标记而放行的计数：$countHist 处" }

Write-Host ''
if ($fail -eq 0) { Write-Host '[OK] 机械审计通过' -ForegroundColor Green } else { Write-Host "[FAIL] 机械审计 $fail 项未通过" -ForegroundColor Red }
exit $fail
