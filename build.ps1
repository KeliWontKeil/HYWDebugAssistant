#Requires -Version 7.0
<#
.SYNOPSIS
    构建与验证门禁（ADR-0025 §4）：还原 → 构建 → 依赖许可审计 → 测试。

.DESCRIPTION
    1) 清除被污染的环境变量 Platform（否则 MSBuild 会把它当作目标平台，报 MSB4126）
    2) dotnet restore / build
    2.5) 归拢内置模块包（应用输出 modules\ + artifacts\modules\）
    3) 依赖许可审计（tools/audit-licenses.ps1，白名单：MIT/Apache-2.0/BSD 等宽松许可）
    4) dotnet test

.EXAMPLE
    ./build.ps1                 # 完整门禁
    ./build.ps1 -SkipTests      # 只构建 + 许可审计
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipTests,
    [switch]$SkipAudit,
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

Write-Host "===== 1/4 还原 =====" -ForegroundColor Cyan
dotnet restore $sln
if ($LASTEXITCODE -ne 0) { throw '还原失败' }

Write-Host "===== 2/4 构建（$Configuration）=====" -ForegroundColor Cyan
dotnet build $sln -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

# ---- 2.5) 校验模块包归拢结果（应用输出 modules\ + artifacts\modules\）----
# 包由**构建自动归拢**（src/Modules/Directory.Build.targets + App 镜像目标）；
# 本步只做一致性校验：让"内置模块开箱可用"这件事在门禁里可验证。
Write-Host "===== 2.5/4 校验模块包 =====" -ForegroundColor Cyan
& (Join-Path $root 'tools\stage-reference-modules.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw '模块包校验失败' }

if (-not $SkipAudit) {
    Write-Host "===== 3/4 依赖许可审计 =====" -ForegroundColor Cyan
    & (Join-Path $root 'tools\audit-licenses.ps1') -Root $root
    if ($LASTEXITCODE -ne 0) { throw '依赖许可审计未通过（见上方违规清单）' }
}

if (-not $SkipTests) {
    Write-Host "===== 4/4 测试 =====" -ForegroundColor Cyan
    dotnet test $sln -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw '测试失败' }
}

$testNote = if ($SkipTests) { ' · 测试已跳过' } else { '' }
Write-Host ""
Write-Host "✅ 门禁全部通过：构建成功 · 许可合规$testNote" -ForegroundColor Green

if ($RunBenchmarks) {
    Write-Host ""
    Write-Host "===== Gate B 数值实测（Release · 不进常规门禁）=====" -ForegroundColor Cyan
    dotnet run -c Release --project (Join-Path $root 'tests\HYWDebugAssistant.Benchmarks\HYWDebugAssistant.Benchmarks.csproj')
    if ($LASTEXITCODE -ne 0) { throw '基准运行失败' }

    Write-Host ""
    Write-Host "数值已输出；按 ADR-0033 回写到主文档 §13.2 与 docs/architecture/04a §5（只记录、不设阈值）" -ForegroundColor Yellow
}
