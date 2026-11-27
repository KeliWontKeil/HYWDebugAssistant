#Requires -Version 7.0
<#
.SYNOPSIS
    把截图工程生成的封面复制成各模块工程根的 `preview.png`。

.DESCRIPTION
    封面是**可选**的模块包资源（包根一个 PNG，清单页拿它当模块卡片图）。它的生效条件只有两条：
      ① 文件叫 `preview.png`，住在**包根**（与 module.json 同级）——不是 `resources/` 下；
      ② 构建时由 `build/module-package.targets` 从**模块工程根目录**归拢进包。

    因此作者侧只需要"把图放到工程根"，**不需要**往 csproj 里写 `<None Update ...>`：
    那类条目只影响"复制到输出目录"，而包归拢读的是工程目录；它也不经 ProjectReference 传递
    ⇒ 靠它把封面带到下游是不可能的（模块的封面只属于模块自己的包）。

    本脚本做的是"把截图结果搬到工程根"这一步：产物在 `artifacts\snapshots\modules\`，
    文件名形如 `<模块>-cover.png`，目标文件一律叫 `preview.png`。

    映射依据是 **module.json 的 id 最后一段**（`hyw/serial` → `serial`），落到 `src\Modules\**\module.json`
    里 id 匹配的那个工程目录。找不到、图缺失、图不是 PNG、图比例不是 9:5，一律**失败退出**（不静默跳过）。

.PARAMETER SnapshotDirectory
    截图产物目录（缺省 `artifacts\snapshots\modules`）。

.PARAMETER Check
    只核对"源图齐不齐、工程能不能对上"，不写文件（改封面流程里的干跑）。

.EXAMPLE
    pwsh -NoProfile -File tools/sync-module-covers.ps1
    pwsh -NoProfile -File tools/sync-module-covers.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$SnapshotDirectory,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SnapshotDirectory)) {
    $SnapshotDirectory = Join-Path $root 'artifacts\snapshots\modules'
}

if (-not (Test-Path -LiteralPath $SnapshotDirectory -PathType Container)) {
    throw "封面截图目录不存在：$SnapshotDirectory（先跑 tools/dev.ps1 -Mode Snapshots）"
}

# 清单页图块是 72×40（9:5）：比例不对的封面会被居中裁切，等于白做。
$expectedWidth = 360
$expectedHeight = 200

# 源码模块：包目录名（= id 最后一段）→ 工程目录
$moduleDirectories = @{}
foreach ($manifestFile in (Get-ChildItem -LiteralPath (Join-Path $root 'src\Modules') -Recurse -Filter module.json -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.id)) {
        throw "模块清单缺少 id：$($manifestFile.FullName)"
    }

    $packageName = ([string]$manifest.id).Split('/')[-1]
    if ($moduleDirectories.ContainsKey($packageName)) {
        throw "两个模块的包目录名相同：$packageName（$($manifestFile.FullName)）"
    }

    $moduleDirectories[$packageName] = $manifestFile.Directory.FullName
}

# 截图文件名 → 包目录名：`Serial-cover.png` → `serial`（小写短横线）。
$covers = Get-ChildItem -LiteralPath $SnapshotDirectory -File -Filter '*-cover.png'

if ($covers.Count -eq 0) {
    throw "封面截图目录里没有 *-cover.png：$SnapshotDirectory"
}

$problems = [System.Collections.Generic.List[string]]::new()
$written = 0

foreach ($cover in $covers) {
    $stem = $cover.Name.Substring(0, $cover.Name.Length - '-cover.png'.Length)

    # 截图侧是类型名（`AttitudeDisplay`），包目录是短横线小写（`attitude-display`）。
    $candidate = ($stem -creplace '(?<!^)([A-Z])', '-$1').ToLowerInvariant()

    if (-not $moduleDirectories.ContainsKey($candidate)) {
        $problems.Add("封面 '$($cover.Name)' 找不到对应模块工程（按包目录名 '$candidate' 查找）")
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($cover.FullName)
    $isPng = $bytes.Length -ge 8 -and
        $bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50 -and
        $bytes[2] -eq 0x4E -and $bytes[3] -eq 0x47
    if (-not $isPng) {
        $problems.Add("封面 '$($cover.Name)' 不是 PNG（清单页只认 PNG）")
        continue
    }

    # PNG 尺寸直接读 IHDR（宽高各 4 字节大端，位于签名 + 长度 + 类型之后）。
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    if ($width -ne $expectedWidth -or $height -ne $expectedHeight) {
        $problems.Add(("封面 '{0}' 尺寸 {1}×{2}，应为 {3}×{4}（清单图块的比例）" -f `
                    $cover.Name, $width, $height, $expectedWidth, $expectedHeight))
        continue
    }

    $target = Join-Path $moduleDirectories[$candidate] 'preview.png'
    $relative = [System.IO.Path]::GetRelativePath($root, $target).Replace('\', '/')

    if ($Check) {
        Write-Host ("[check] {0} → {1}" -f $cover.Name, $relative)
        continue
    }

    Copy-Item -LiteralPath $cover.FullName -Destination $target -Force
    $written++
    Write-Host ("[cover] {0} → {1}" -f $cover.Name, $relative)
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host "[error] $problem" -ForegroundColor Red }
    throw "封面同步失败：$($problems.Count) 处问题"
}

$missing = @($moduleDirectories.Keys | Where-Object {
        -not (Test-Path -LiteralPath (Join-Path $moduleDirectories[$_] 'preview.png'))
    })

if ($missing.Count -gt 0) {
    Write-Host ("[warn] 这些模块还没有封面（界面显示占位字形）：{0}" -f ($missing -join ', ')) -ForegroundColor Yellow
}

if ($Check) {
    Write-Host '[check] 干跑完成，未写文件' -ForegroundColor Green
}
else {
    Write-Host "[cover] 已更新 $written 张模块封面" -ForegroundColor Green
}
