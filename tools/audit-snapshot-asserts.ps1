#Requires -Version 7.0
<#
.SYNOPSIS
  截图工程「零断言」审计（源侧）：`tests/HYWDebugAssistant.VisualSnapshots/**/*.cs` 不得出现断言。

.DESCRIPTION
  判据（dev/constraints.md「测试面」与 dev/workflow.md「测试分层与规则」）：

    · 截图工程是**工具**，不是测试：它只渲染 PNG 供人眼，任何断言都不会进入自动化门禁；
    · 命中（任一条即违规）：`Assert.`、`.Should`、`ShouldBe` / `ShouldNotBe` / `ShouldThrow`；
    · 扫描范围：该工程下的 `.cs`，跳过 `obj/` 与 `bin/`；
    · 违规一律打印 `文件:行号: 原文` 并 `exit 1`；目标 0 命中。

  为什么：截图工程里一旦堆起"看起来在测什么"的断言，就会被当成回归网保留，但门禁从不执行它——
  既不拦 bug，又让人误以为有覆盖。依赖渲染语义的读数先写成探针，定位完删除；真要转正为断言，
  搬到主测试项目，并同时从截图工程删掉。

.EXAMPLE
  pwsh -NoProfile -File tools/audit-snapshot-asserts.ps1
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$snapshotRoot = Join-Path $root 'tests/HYWDebugAssistant.VisualSnapshots'

if (-not (Test-Path -LiteralPath $snapshotRoot -PathType Container)) {
    Write-Host "[FAIL] 找不到截图工程：$snapshotRoot" -ForegroundColor Red
    exit 1
}

$assertion = [regex]::new(
    'Assert\.|\.Should\b|ShouldBe|ShouldNotBe|ShouldThrow',
    [System.Text.RegularExpressions.RegexOptions]::None)

$files = @(
    Get-ChildItem -LiteralPath $snapshotRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
        Sort-Object FullName)

$problems = 0

foreach ($file in $files) {
    $lines = Get-Content -LiteralPath $file.FullName -Encoding utf8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if (-not $assertion.IsMatch($line)) { continue }

        $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
        Write-Host ("  x {0}:{1}  {2}" -f $relative, ($i + 1), $line.Trim()) -ForegroundColor Red
        $problems++
    }
}

Write-Host ""
Write-Host ("截图工程零断言审计：扫描 {0} 个 .cs · {1} 处断言" -f $files.Count, $problems)

if ($problems -gt 0) {
    Write-Host "[FAIL] 截图工程只出 PNG，不写断言（判据见本脚本头部；要保留的机制不变量请搬进主测试项目）" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] 截图工程不含断言" -ForegroundColor Green
exit 0
