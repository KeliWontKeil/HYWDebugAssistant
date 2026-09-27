# Phase B 收尾：把新分册换进 docs/architecture/（默认 dry-run；-Execute 才真换）
#
# 前置：先跑 archive-docs.ps1 -Execute 把旧分册搬走，再跑本脚本。
# 安全：若目标已存在且内容不同，则**拒绝覆盖**并报告（避免误盖旧文件）。

[CmdletBinding()]
param([switch]$Execute)

$ErrorActionPreference = 'Stop'
$new = 'D:\project\上位机开发\.reorg\new-architecture'
$dst = 'D:\project\上位机开发\docs\architecture'

$expected = @(
    '00-architecture-index.md',
    '01-layering-and-projects.md',
    '02-module-sdk.md',
    '03-data-protocol-and-typing.md',
    '04-runtime-mechanisms.md',
    '05-network-semantics.md',
    '06-ui-shell-and-windows.md',
    '07-overlay-and-visual-baseline.md',
    '08-application-settings.md',
    '09-test-and-gate.md',
    '10-delivery-and-ops.md'
)

Write-Host '== 检查待换入的 11 份分册 ==' -ForegroundColor Green
$missing = @()
foreach ($name in $expected) {
    $p = Join-Path $new $name
    if (Test-Path -LiteralPath $p) {
        $lines = (Get-Content -LiteralPath $p | Measure-Object -Line).Lines
        Write-Host ("  ✓ {0,-42} {1,5} 行" -f $name, $lines) -ForegroundColor Green
    }
    else { Write-Host "  ✗ 缺：$name" -ForegroundColor Red; $missing += $name }
}
if ($missing.Count -gt 0) { Write-Host '缺文件，未换入。' -ForegroundColor Red; exit 2 }

Write-Host '== 检查旧的现行目录是否已清空 ==' -ForegroundColor Green
$left = Get-ChildItem $dst -File -ErrorAction SilentlyContinue
if ($left) {
    Write-Host "  ⚠ docs/architecture 下仍有 $($left.Count) 个文件（应先归档）：" -ForegroundColor Yellow
    $left | Select-Object -First 25 | ForEach-Object { Write-Host "      $($_.Name)" -ForegroundColor DarkYellow }
    if ($Execute) { Write-Host '  已 -Execute 但目标目录非空 ⇒ 中止（避免覆盖）。' -ForegroundColor Red; exit 3 }
}

Write-Host '== 换入 ==' -ForegroundColor Green
foreach ($name in $expected) {
    Write-Host "  [copy] $name → docs/architecture/" -ForegroundColor Cyan
    if ($Execute) { Copy-Item (Join-Path $new $name) -Destination (Join-Path $dst $name) -Force }
}

Write-Host ''
Write-Host '== 换入后自检 ==' -ForegroundColor Green
Write-Host '  下一步跑：.reorg\tools\audit-docs.ps1'
if (-not $Execute) { Write-Host '（dry-run 结束；加 -Execute 才真正换入）' -ForegroundColor Yellow }
