#Requires -Version 7.0
<#
.SYNOPSIS
    构建与验证门禁（ADR-0025 §4）：文档机械审计 → 还原 → 构建 → 依赖许可审计 → 测试。

.DESCRIPTION
    0) 清除被污染的环境变量 Platform（否则 MSBuild 会把它当作目标平台，报 MSB4126）
    1) 文档机械审计（docs/tools/audit-docs.ps1，只读）—— 规定就在该脚本头部注释的「判据」清单里
    2) dotnet restore / build
    3) 校验内置模块包归拢（应用输出 modules\ + artifacts\modules\）
    4) 依赖许可审计（tools/audit-licenses.ps1，白名单：MIT/Apache-2.0/BSD 等宽松许可）
    5) dotnet test

.EXAMPLE
    ./build.ps1                 # 完整门禁
    ./build.ps1 -SkipTests      # 只做文档审计 + 构建 + 许可审计
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipTests,
    [switch]$SkipAudit,
    # 跳过文档机械审计（仅当本次改动确实不涉及 docs/ 时使用；判据见 docs/tools/audit-docs.ps1 头部）
    [switch]$SkipDocAudit,
    # Gate B 数值实测（04a §5 清单）：跑 BenchmarkDotNet 基准（Release）。
    # **不进常规门禁**（耗时且数值随机器波动）；数值**只记录、不设阈值**。
    [switch]$RunBenchmarks,
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

# ---- 1) 文档机械审计（只读；判据见该脚本头部注释）----
# 放在最前：文档问题最便宜、且不依赖构建产物；改了 docs/ 却忘了跑审计会在这里被拦下。
if (-not $SkipDocAudit) {
    Write-Host "===== 1/5 文档机械审计 =====" -ForegroundColor Cyan
    & (Join-Path $root 'docs\tools\audit-docs.ps1')
    if ($LASTEXITCODE -ne 0) { throw '文档机械审计未通过（见上方 文件:行号；判据见 docs/tools/audit-docs.ps1 头部注释）' }
}
else {
    Write-Host "[skip] 文档机械审计已跳过（-SkipDocAudit）" -ForegroundColor Yellow
}

# ---- 1b) 颜色口径审计（只读；判据见该脚本头部注释）----
# 与文档审计同一位置、同一理由：**相对色引 token / 绝对色带理由标记**这条口径，
# 靠"改了源码就顺手跑一次"是守不住的；放在构建最前，违规当场拦下。
if (-not $SkipDocAudit) {
    & (Join-Path $root 'docs\tools\audit-colors.ps1')
    if ($LASTEXITCODE -ne 0) { throw '颜色口径审计未通过（见上方 文件:行号；判据见 docs/tools/audit-colors.ps1 头部注释）' }
}

Write-Host "===== 2/5 还原 =====" -ForegroundColor Cyan
dotnet restore $sln
if ($LASTEXITCODE -ne 0) { throw '还原失败' }

Write-Host "===== 3/5 构建（$Configuration）=====" -ForegroundColor Cyan
dotnet build $sln -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

# ---- 4) 校验模块包归拢结果（应用输出 modules\ + artifacts\modules\）----
# 包由**构建自动归拢**（src/Modules/Directory.Build.targets + App 镜像目标）；
# 本步只做一致性校验：让"内置模块开箱可用"这件事在门禁里可验证。
Write-Host "===== 4/5 校验模块包 + 依赖许可审计 =====" -ForegroundColor Cyan
& (Join-Path $root 'tools\verify-module-packages.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw '模块包校验失败' }

if (-not $SkipAudit) {
    & (Join-Path $root 'tools\audit-licenses.ps1') -Root $root
    if ($LASTEXITCODE -ne 0) { throw '依赖许可审计未通过（见上方违规清单）' }
}

if (-not $SkipTests) {
    Write-Host "===== 5/5 测试 =====" -ForegroundColor Cyan

    # 【F 裁定 · 2026-09 · 与 tools/dev.ps1 -Mode Test -AllTests 同一口径】
    # `VisualSnapshots` **必须单独进程跑**：`AvaloniaHeadlessPlatform` 是**进程级**配置，与其它测试项目并发时
    # 真实 Skia 渲染互相破坏 ⇒ **假失败**（实测 `ColorPickerDialogSnapshotTests` 并发下红、单独跑全绿）。
    # 门禁的"通过"必须可信 ⇒ 这里逐个串行跑，快照项目最后单独跑。
    # 项目清单按目录发现（目录名 = 项目名），新增测试项目零改动；`Benchmarks` 不是 xUnit 用例 ⇒ 排除。
    $testProjects = Get-ChildItem (Join-Path $root 'tests') -Directory |
        ForEach-Object { Join-Path $_.FullName "$($_.Name).csproj" } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Where-Object { $_ -notmatch 'Benchmarks' }

    $snapshotProjects = @($testProjects | Where-Object { $_ -match 'VisualSnapshots' })
    $regularProjects = @($testProjects | Where-Object { $_ -notmatch 'VisualSnapshots' })

    $failed = @()
    foreach ($proj in @($regularProjects) + @($snapshotProjects)) {
        $label = [System.IO.Path]::GetFileNameWithoutExtension($proj)
        $isSnapshot = $proj -match 'VisualSnapshots'
        Write-Host "----- $label$(if ($isSnapshot) { '（单独进程：渲染快照）' }) -----" -ForegroundColor DarkCyan

        dotnet test $proj -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { $failed += $label }
    }

    if ($failed.Count -gt 0) { throw "测试失败：$($failed -join '、')" }
}

$testNote = if ($SkipTests) { ' · 测试已跳过' } else { '' }
$docNote = if ($SkipDocAudit) { ' · 文档审计已跳过' } else { '' }
Write-Host ""
Write-Host "✅ 门禁全部通过：文档审计 · 构建成功 · 许可合规$testNote$docNote" -ForegroundColor Green

if ($RunBenchmarks) {
    Write-Host ""
    Write-Host "===== Gate B 数值实测（Release · 不进常规门禁）=====" -ForegroundColor Cyan
    dotnet run -c Release --project (Join-Path $root 'tests\HYWDebugAssistant.Benchmarks\HYWDebugAssistant.Benchmarks.csproj')
    if ($LASTEXITCODE -ne 0) { throw '基准运行失败' }

    Write-Host ""
    Write-Host "数值已输出；按 ADR-0033 回写到主文档 §13.2 与 docs/architecture/04a §5（只记录、不设阈值）" -ForegroundColor Yellow
}
