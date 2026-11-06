#Requires -Version 7.0
<#
.SYNOPSIS
    构建与验证门禁：文档站检查 / 颜色 / 资源与文本 / 注释布局 / 版本纪律审计 → 还原 → 构建 → 模块包与资源校验 → 依赖许可审计 → 测试。

.DESCRIPTION
    0) 清除被污染的环境变量 Platform（否则 MSBuild 会把它当作目标平台，报 MSB4126）
    1) 文档站结构检查（docs/build.ps1 -Mode Check：严格构建 + 导航完整性 + 生成内容自检），
       以及颜色口径审计（docs/tools/audit-colors.ps1）、资源与文本审计（tools/audit-resources.ps1）、
       注释布局数值审计（tools/audit-layout-comments.ps1）与版本纪律审计（tools/audit-versions.ps1）
       —— 前四者同受 -SkipDocs 控制，版本审计始终执行（它不只看文档）
    2) dotnet restore / build
    3) 校验内置模块包归拢（应用输出 modules\ + artifacts\modules\）
    4) 校验输出资源包（tools/verify-resources.ps1）+ 依赖许可审计（tools/audit-licenses.ps1，
       白名单：MIT/Apache-2.0/BSD 等宽松许可）
    4c) 模块作者包：pack-sdk.ps1 生成四个包 → verify-sdk-packages.ps1 校验 dll / xml / 依赖组
       （-SkipPack 可跳过；发布前不得跳过）
    5) dotnet test（默认排除压力/性能类别与截图工具；-IncludePressure / -IncludeSnapshots 按需打开）

.EXAMPLE
    ./build.ps1                 # 完整门禁
    ./build.ps1 -SkipTests      # 只做文档站检查 / 颜色 / 资源审计 + 构建 + 模块包/资源校验 + 许可审计
    ./build.ps1 -SkipPack       # 迭代时跳过作者包打包（约 10–20 s）
    ./build.ps1 -IncludePressure # 数据面 / 外部接入 / 串口改动时，连压力与性能用例一起跑
    ./build.ps1 -IncludeSnapshots # 改 UI 时连截图工具一起跑（产出 PNG 供人眼，不是判据）
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipTests,
    [switch]$SkipAudit,
    # 跳过文档站检查与文档派生审计（仅当本次改动确实不涉及 docs/ 时使用）
    [switch]$SkipDocs,
    # 跳过模块作者包打包与校验（迭代时用；发布/交付前不得跳过）
    [switch]$SkipPack,
    # Gate B 数值实测：跑 BenchmarkDotNet 基准（Release）。
    # 不进常规门禁（耗时且数值随机器波动）；数值只记录、不设阈值。
    [switch]$RunBenchmarks,
    # 压力/性能用例（Category=Pressure / Perf）默认不进门禁：数据面 / 外部接入 / 串口改动时按需打开。
    [switch]$IncludePressure,
    # 渲染截图（VisualSnapshots 是**截图工具**，不是判据）默认不进门禁：改 UI 时按需打开。
    [switch]$IncludeSnapshots,
    # VS 的 Avalonia XAML 预览器（设计器宿主）会加载并锁定输出 DLL，导致构建失败；
    # 加此开关可在构建前自动停止它（VS 需要时会自行重启预览器）。
    [switch]$StopDesignerHost
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

# ---- 0) 检测 Avalonia 设计器宿主（VS 预览器）是否占用本工程输出 ----
$designerHosts = @()
try {
    $designerHosts = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like '*Avalonia.Designer.HostApp*' -and $_.CommandLine -like "*$root*" }
}
catch { }

if ($designerHosts) {
    if ($StopDesignerHost) {
        foreach ($p in $designerHosts) {
            Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
            Write-Host "[env] 已停止 Avalonia 设计器宿主 PID $($p.ProcessId)（VS 会按需重启预览器）" -ForegroundColor Yellow
        }
        Start-Sleep -Milliseconds 800
    }
    else {
        Write-Host "[warn] 检测到 Avalonia 设计器宿主（VS XAML 预览器）占用本工程输出，可能导致 MSB3021/MSB3027：" -ForegroundColor Yellow
        $designerHosts | ForEach-Object { Write-Host ("         PID {0}" -f $_.ProcessId) -ForegroundColor Yellow }
        Write-Host "         可改用： ./build.ps1 -StopDesignerHost" -ForegroundColor Yellow
    }
}

# ---- 1) 环境清理 ----
foreach ($name in @('Platform', 'Platform(x86)')) {
    if (Test-Path "Env:$name") {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        Write-Host "[env] 已清除被污染的 $name 环境变量" -ForegroundColor Yellow
    }
}

$sln = Join-Path $root 'HYWDebugAssistant.slnx'
if (-not (Test-Path $sln)) { throw "未找到解决方案：$sln" }

# ---- 1) 文档站检查（严格构建 + 结构；文字质量不靠脚本判断）----
# 放在最前：文档问题最便宜、且不依赖构建产物；结构规则见 dev/workflow.md。
if (-not $SkipDocs) {
    Write-Host "===== 1/6 文档站检查 =====" -ForegroundColor Cyan
    & (Join-Path $root 'docs\build.ps1') -Mode Check
    if ($LASTEXITCODE -ne 0) { throw '文档站检查未通过（见上方输出；结构规则见 dev/workflow.md）' }
}
else {
    Write-Host "[skip] 文档站检查已跳过（-SkipDocs）" -ForegroundColor Yellow
}

# ---- 1b) 颜色口径审计（只读；判据见该脚本头部注释）----
# 相对色引 token / 绝对色带理由标记这条口径，靠"改了源码就顺手跑一次"是守不住的；
# 放在构建最前，违规当场拦下。
if (-not $SkipDocs) {
    & (Join-Path $root 'tools\audit-colors.ps1')
    if ($LASTEXITCODE -ne 0) { throw '颜色口径审计未通过（见上方 文件:行号；判据见 tools/audit-colors.ps1 头部注释）' }
}

# ---- 1c) 资源与文本审计（只读；判据见该脚本头部注释）----
# 出厂种子形状 / 主题清单 / 文本表 / {loc:Tr} 键 / 死键 都是"编译期看不见"的事实，
# 放在构建之前：坏种子与死键不该等到发布打包才发现。与颜色审计同受 -SkipDocs 控制。
if (-not $SkipDocs) {
    & (Join-Path $root 'tools\audit-resources.ps1')
    if ($LASTEXITCODE -ne 0) { throw '资源与文本审计未通过（见上方 文件:行号；判据见 tools/audit-resources.ps1 头部注释）' }
}

# ---- 1d) 源码注释的布局数值审计（只读；判据见该脚本头部注释）----
# 注释里的"当前坐标/尺寸/像素值"会随布局调整过期，成为代码/主题/声明之外的第二份事实源；
# 与资源审计同一位置、同一理由，也同受 -SkipDocs 控制。
if (-not $SkipDocs) {
    & (Join-Path $root 'tools\audit-layout-comments.ps1')
    if ($LASTEXITCODE -ne 0) { throw '注释布局数值审计未通过（见上方 文件:行号；判据见 tools/audit-layout-comments.ps1 头部注释）' }
}

# ---- 1e) 版本纪律审计（只读；判据见 tools/audit-versions.ps1 头部注释）----
# 唯一版本事实源、禁止第二版本轴字段、模块清单两段标识：都是"编译期看不见"的事实，
# 放在构建之前当场拦下。它查的不只是文档，因此不受 -SkipDocs 控制。
Write-Host "===== 1e/6 版本纪律审计 =====" -ForegroundColor Cyan
& (Join-Path $root 'tools\audit-versions.ps1')
if ($LASTEXITCODE -ne 0) { throw '版本纪律审计未通过（见上方 文件:行号；判据见 tools/audit-versions.ps1 头部注释）' }

Write-Host "===== 2/6 还原 =====" -ForegroundColor Cyan
dotnet restore $sln
if ($LASTEXITCODE -ne 0) { throw '还原失败' }

Write-Host "===== 3/6 构建（$Configuration）=====" -ForegroundColor Cyan
dotnet build $sln -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

# ---- 4) 校验模块包归拢结果（应用输出 modules\ + artifacts\modules\）----
# 包由构建自动归拢（src/Modules/Directory.Build.targets + App 镜像目标）；
# 本步只做一致性校验：让"内置模块开箱可用"这件事在门禁里可验证。
Write-Host "===== 4/6 校验模块包 + 资源包 + 依赖许可审计 =====" -ForegroundColor Cyan
& (Join-Path $root 'tools\verify-module-packages.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw '模块包校验失败' }

# 输出 resources\ 必须与仓库 resources\ 逐字节一致：构建的 StageResources 若跳过更新、
# 或有人直接改输出目录，都会在这里暴露（源侧审计不覆盖产物）。
& (Join-Path $root 'tools\verify-resources.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw '资源包校验失败（输出 resources\ 与仓库不一致）' }

if (-not $SkipAudit) {
    & (Join-Path $root 'tools\audit-licenses.ps1') -Root $root
    if ($LASTEXITCODE -ne 0) { throw '依赖许可审计未通过（见上方违规清单）' }
}

# ---- 4c) 模块作者包产物：打包 → 校验（作者拿不到源码，包内 XML 文档是 API 语义来源）----
# 单独一步的理由：包产物是"外部作者能不能开发"的前提，不能只靠手工跑脚本；
# 迭代时可 -SkipPack 跳过（约 10–20 s）。
if (-not $SkipPack) {
    Write-Host "===== 4c/6 模块作者包（SDK）=====" -ForegroundColor Cyan
    & (Join-Path $root 'tools\pack-sdk.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'SDK 打包失败' }

    & (Join-Path $root 'tools\verify-sdk-packages.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'SDK 包产物校验失败（见上方 x 行）' }
}
else {
    Write-Host "[skip] 模块作者包打包已跳过（-SkipPack）" -ForegroundColor Yellow
}

if (-not $SkipTests) {
    Write-Host "===== 5/6 测试 =====" -ForegroundColor Cyan

    # 项目清单按目录发现（目录名 = 项目名），新增测试项目零改动；Benchmarks 不是 xUnit 用例 ⇒ 排除。
    # `VisualSnapshots` 是**截图工具**（渲染 PNG 给人眼看，不是判据）：默认不进门禁；
    # 无头渲染平台是进程级配置，真要跑时**最后单独**跑（顺序即隔离）。
    $discovered = Get-ChildItem (Join-Path $root 'tests') -Directory |
        ForEach-Object { Join-Path $_.FullName "$($_.Name).csproj" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Where-Object { $_ -notmatch 'Benchmarks' }

    $regularProjects = @($discovered | Where-Object { $_ -notmatch 'VisualSnapshots' })
    $snapshotProjects = if ($IncludeSnapshots) { @($discovered | Where-Object { $_ -match 'VisualSnapshots' }) } else { @() }

    # 压力/性能用例默认不进门禁：数据面 / 外部接入 / 串口改动时用 -IncludePressure 打开。
    $categoryFilter = if ($IncludePressure) { $null } else { 'Category!=Pressure&Category!=Perf' }

    $failed = @()
    $testClock = [System.Diagnostics.Stopwatch]::StartNew()

    foreach ($proj in @($regularProjects) + @($snapshotProjects)) {
        $label = [System.IO.Path]::GetFileNameWithoutExtension($proj)
        $isSnapshot = $proj -match 'VisualSnapshots'
        Write-Host "----- $label$(if ($isSnapshot) { '（单独进程：截图工具）' }) -----" -ForegroundColor DarkCyan

        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $output = if ($categoryFilter) {
            dotnet test $proj -c $Configuration --no-build --filter $categoryFilter 2>&1
        }
        else {
            dotnet test $proj -c $Configuration --no-build 2>&1
        }
        $clock.Stop()

        $output | Select-Object -Last 4 | ForEach-Object { Write-Host $_ }
        Write-Host ("        {0}：{1:N1}s" -f $label, $clock.Elapsed.TotalSeconds) -ForegroundColor DarkGray

        # 加过滤词后最危险的失败模式是"零用例绿色"：没有汇总行、或明说没有匹配，都按失败处理。
        $summary = @($output | Where-Object { $_ -match '已通过!|失败!|通过:\s*\d+|Passed!|Failed!' })
        $emptyRun = @($output | Where-Object { $_ -match '没有测试与|No test matches|总计:\s*0\b|Total:\s*0\b' })
        if ($emptyRun.Count -gt 0 -or $summary.Count -eq 0) {
            throw "$label：没有用例被执行（过滤词未匹配 ⇒ 拒绝把零用例当作通过）"
        }

        if ($LASTEXITCODE -ne 0) { $failed += $label }
    }

    $testClock.Stop()
    Write-Host ("测试小计：{0:N1}s" -f $testClock.Elapsed.TotalSeconds) -ForegroundColor Cyan

    if ($failed.Count -gt 0) { throw "测试失败：$($failed -join '、')" }
}

$testNote = if ($SkipTests) { ' · 测试已跳过' } else { '' }
$docNote = if ($SkipDocs) { ' · 文档检查已跳过' } else { '' }
$packNote = if ($SkipPack) { ' · 作者包已跳过' } else { '' }
Write-Host ""
Write-Host "✅ 门禁全部通过：文档站 · 颜色 · 资源与文本 · 注释布局 · 版本纪律 · 构建成功 · 模块包与资源校验 · 作者包 · 许可合规$testNote$docNote$packNote" -ForegroundColor Green

if ($RunBenchmarks) {
    Write-Host ""
    Write-Host "===== Gate B 数值实测（Release · 不进常规门禁）=====" -ForegroundColor Cyan
    dotnet run -c Release --project (Join-Path $root 'tests\HYWDebugAssistant.Benchmarks\HYWDebugAssistant.Benchmarks.csproj')
    if ($LASTEXITCODE -ne 0) { throw '基准运行失败' }

    Write-Host ""
    Write-Host "数值已输出；只记录、不设阈值。" -ForegroundColor Yellow
}
