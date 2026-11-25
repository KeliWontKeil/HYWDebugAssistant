#Requires -Version 7.0
<#
.SYNOPSIS
    临时产物审计（只读）：仓库里**只允许一个临时根** `artifacts/`，且其子项必须已登记。

.DESCRIPTION
    判据（规则与登记表见 dev/workflow.md「构建产物」）：

      1. **仓库根只有三个忽略项**：`.vs/`（IDE）、`.workbuddy/`（助手工作记忆）、
         `artifacts/`（唯一临时根）。根上再出现 `*.log` / `BenchmarkDotNet.Artifacts/`
         / 其它忽略目录，就是有人把临时文件丢在了仓库根。
      2. **`artifacts/` 子项必须已登记**（见下表）。未登记的新目录 = 多了一个临时根，
         要么登记（说清谁写、能否重建），要么删掉。
      3. **`artifacts/` 根不许放散文件**：日志、截图、导出件都该进对应子目录。
      4. **仓库里不许有第二个名为 `artifacts` 的忽略目录**：`docs/artifacts`、`src/*/artifacts`
         这类"某个会话在子目录里跑了一次工具"的残留，会被 `.gitignore` 的裸名模式藏起来，
         然后一直冒充"仓库的一部分"。

    为什么要有这条审计：`.gitignore` 只会**藏**东西，不会**清**东西。裸名模式
    `artifacts/` 匹配任意深度，于是"藏起来"和"清干净"看起来一样——直到有人想把
    一个同名目录提交进去、或者去 `docs/` 里找一个本该是源文件的页面。

    本脚本**不删除任何东西**：删除是人的决定（`theme-seeds/` 就删不得）。
    它只负责让"该清的东西"显形。

    需要 `git` 判定忽略状态；没有 git 时跳过 1/4 两条并提示（2/3 仍执行）。

.EXAMPLE
    pwsh -NoProfile -File tools/audit-temp-artifacts.ps1
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------- 登记表
# 唯一登记处：`artifacts/` 下每个子目录的**写入者**与**能否重建**。
# 新增临时产物类别时先在这里登记，再写代码 —— 否则审计会判红。
$registered = [ordered]@{
    'modules'            = @{ Writer = '模块构建（build/module-package.targets）'; Rebuildable = $true }
    'sdk-packages'       = @{ Writer = 'tools\pack-sdk.ps1'; Rebuildable = $true }
    'docs-site'          = @{ Writer = 'docs\build.ps1 -Mode Build'; Rebuildable = $true }
    'docs-check'         = @{ Writer = 'docs\build.ps1 -Mode Check'; Rebuildable = $true }
    'snapshots'          = @{ Writer = 'tests\HYWDebugAssistant.VisualSnapshots（人眼验收）'; Rebuildable = $true }
    'benchmarks'         = @{ Writer = 'build.ps1 -RunBenchmarks（Config.WithArtifactsPath）'; Rebuildable = $true }
    'release-manifests'  = @{ Writer = 'tools\package-release.ps1 / package-sdk.ps1'; Rebuildable = $true }
    'logs'               = @{ Writer = '构建/测试重定向输出'; Rebuildable = $true }
    'theme-seeds'        = @{ Writer = '手工（sakura 主题变体）'; Rebuildable = $false }
}

# 仓库根允许出现的**忽略项**（其余一律视为垃圾）。
$allowedRootIgnored = @('.vs', '.workbuddy', 'artifacts')

$problems = [System.Collections.Generic.List[string]]::new()

function Add-Problem([string]$message) {
    $problems.Add($message)
}

# ---------------------------------------------------------------- 登记表自检
$artifactsRoot = Join-Path $root 'artifacts'
if (-not (Test-Path -LiteralPath $artifactsRoot -PathType Container)) {
    Write-Host '[OK] 临时产物审计通过：没有 artifacts\（无需检查）' -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------- 2) artifacts 子项已登记
foreach ($entry in (Get-ChildItem -LiteralPath $artifactsRoot -Force | Sort-Object Name)) {
    if (-not $entry.PSIsContainer) {
        # 3) artifacts 根不许放散文件
        Add-Problem "artifacts\$($entry.Name)：artifacts\ 根下不许放散文件（按类别放进对应子目录）"
        continue
    }

    if (-not $registered.Contains($entry.Name)) {
        Add-Problem ("artifacts\$($entry.Name)：未登记的临时产物类别（已登记：{0}）——先登记再说清谁写、能否重建" -f ($registered.Keys -join ' / '))
    }
}

# ---------------------------------------------------------------- git 判定（可降级）
$hasGit = $null -ne (Get-Command git -ErrorAction SilentlyContinue)
$gitUsable = $false
if ($hasGit) {
    & git -C $root rev-parse --is-inside-work-tree 2>$null | Out-Null
    $gitUsable = $LASTEXITCODE -eq 0
}

if (-not $gitUsable) {
    Write-Host '[warn] 当前目录不是 git 工作树或没有 git：跳过"根忽略项"检查（其余两条仍执行）' -ForegroundColor Yellow
}
else {
    # 1) 仓库根只允许三个忽略项
    #    判据是"**这个顶层条目本身**被忽略"，不是"它里面有东西被忽略"：
    #    否则 src/ tests/ docs/ tools/ 会因为各自的 bin/ obj/ 被忽略而误判成垃圾。
    #    所以逐个顶层条目问 `git check-ignore`，而不是从 `status --ignored` 的
    #    深层路径往上归并。
    #    另外也拦**未跟踪**的根条目：一个不再被 .gitignore 覆盖的临时目录
    #    （比如去掉忽略规则后的 BenchmarkDotNet.Artifacts）同样是"垃圾藏在根上"，
    #    只是它现在显形了 —— 显形不等于合格。
    $rootEntries = @(Get-ChildItem -LiteralPath $root -Force | Where-Object { $_.Name -ne '.git' } | Sort-Object Name)
    foreach ($entry in $rootEntries) {
        if ($allowedRootIgnored -contains $entry.Name) { continue }

        $kind = if ($entry.PSIsContainer) { '目录' } else { '文件' }

        & git -C $root check-ignore -q -- $entry.Name 2>$null
        if ($LASTEXITCODE -eq 0) {
            Add-Problem ("仓库根出现不被允许的忽略{0} '{1}'：临时产物只许进 artifacts\（*.log / *.txt / BenchmarkDotNet.Artifacts 之类都属于这一类）" -f $kind, $entry.Name)
            continue
        }

        & git -C $root ls-files --error-unmatch -- $entry.Name 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Add-Problem ("仓库根出现未跟踪的{0} '{1}'：不是源文件就删掉，是临时产物就放进 artifacts\" -f $kind, $entry.Name)
        }
    }
}

# ---------------------------------------------------------------- 4) 不许有第二个 artifacts 目录
# **直接扫文件系统**，不依赖忽略状态：改成根锚定之后 `docs/artifacts` 已经显形，
# 而"显形"正是要检查的对象——藏在忽略规则背后的残留，正是这条规则要防的东西。
# bin/ obj/ 下不扫（那是编译器自己的命名空间，与临时产物根无关）。
$nestedArtifacts = @(
    Get-ChildItem -LiteralPath $root -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -eq 'artifacts' -and
            $_.FullName -ne $artifactsRoot -and
            $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs|\.workbuddy)[\\/]'
        } |
        ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } |
        Sort-Object
)

foreach ($path in $nestedArtifacts) {
    Add-Problem ("{0}：仓库里出现了第二个 artifacts 目录（某个会话在子目录里跑了一次工具的残留）——临时产物只许放在仓库根的 artifacts\" -f $path)
}

# ---------------------------------------------------------------- 结果
if ($problems.Count -gt 0) {
    Write-Host "[FAIL] 临时产物审计：$($problems.Count) 处违规（登记表与规则见 dev/workflow.md「构建产物」）" -ForegroundColor Red
    $problems | Sort-Object -Unique | ForEach-Object { Write-Host "  x $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host '可重建的子目录可直接删除；artifacts\theme-seeds\ 是手工产物，删前先转移。' -ForegroundColor Yellow
    exit 1
}

$present = @(Get-ChildItem -LiteralPath $artifactsRoot -Directory -Force | Select-Object -ExpandProperty Name)
$manual = @($registered.Keys | Where-Object { -not $registered[$_].Rebuildable })
Write-Host ("[OK] 临时产物审计通过：artifacts\ 现有 {0} 个子目录（登记表 {1} 项，全部在册）；无散文件、无第二个临时根。不可重建的只有 {2}。" -f `
    $present.Count, $registered.Count, ($manual -join ' / ')) -ForegroundColor Green
exit 0
