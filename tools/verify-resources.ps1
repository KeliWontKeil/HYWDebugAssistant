#Requires -Version 7.0
<#
.SYNOPSIS
  资源包校验（产物侧）：应用输出里的 `resources\` 必须与仓库 `resources\` 完全一致。

.DESCRIPTION
  判据（与 docs/zh/concepts/resources.md 的规则对齐；**任一不满足即 exit 1**）：

    ① **文件集一致**：应用输出（`$(OutDir)`，由 MSBuild 求值，不手抄目录结构）的 `resources\`
       与仓库 `resources\` 的相对路径集合完全相同——输出少文件、多文件都判红
       （多出来的只可能来自手工拷贝或过期构建）；
    ② **内容一致**：同相对路径的文件 SHA256 必须相同——构建的 `StageResources` 若跳过更新、
       或有人直接改输出目录，都会在这里暴露；
    ③ **种子主题包完整**：输出里的每个 `theme.json` 必须同目录有清单里声明的壁纸文件，
       且该文件是 PNG（magic bytes）；
    ④ **产物缺失即失败**：输出目录不存在 / 没有 `resources\` ⇒ 直接失败（提示先构建 App），
       不把"没有可校验对象"当成通过。

.PARAMETER Configuration
  构建配置（决定应用项目的 `$(OutDir)`；默认 Debug）。

.EXAMPLE
  ./tools/verify-resources.ps1
  ./tools/verify-resources.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'app-output.ps1')

$sourceRoot = Join-Path $root 'resources'

# **输出目录只向 MSBuild 要**：手抄 `bin\<配置>\net10.0` 会与 `$(PlatformName)` 的改动脱节，
# 校验一棵构建没写过的树 ⇒ 假通过（详见 tools/app-output.ps1 头注释）。
$appOutput = Get-AppOutputDirectory -Root $root -Configuration $Configuration
$producedRoot = Join-Path $appOutput 'resources'

# 平台名固定前遗留的第二棵树：**不参与校验**（校验已按 MSBuild 真值定位），只提示删除。
$staleTrees = @(Get-StalePlatformOutputTrees -Root $root)
if ($staleTrees.Count -gt 0) {
    $preview = ($staleTrees | Select-Object -First 3 | ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_) }) -join '；'
    Write-Host ("[warn] 发现 {0} 处陈旧产物树（PlatformName 固定前遗留，可整目录删除）：{1}" -f $staleTrees.Count, $preview) -ForegroundColor Yellow
}

$problems = [System.Collections.Generic.List[string]]::new()

function Get-RelativePath([string]$basePath, [string]$fullPath) {
    return [System.IO.Path]::GetRelativePath($basePath, $fullPath).Replace('\', '/')
}

function Get-FileHashMap([string]$directory) {
    $map = @{}
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File | Sort-Object FullName) {
        $relative = Get-RelativePath $directory $file.FullName
        $map[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    return $map
}

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    Write-Host "[FAIL] 仓库 resources\ 不存在：$sourceRoot" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path -LiteralPath $producedRoot -PathType Container)) {
    Write-Host "[FAIL] 输出 resources\ 不存在：$producedRoot" -ForegroundColor Red
    Write-Host "       请先构建 App：./tools/dev.ps1 -Mode Build -Project App（-Configuration $Configuration）" -ForegroundColor Yellow
    exit 1
}

$sourceFiles = Get-FileHashMap $sourceRoot
$producedFiles = Get-FileHashMap $producedRoot

foreach ($relative in ($sourceFiles.Keys | Sort-Object)) {
    if (-not $producedFiles.ContainsKey($relative)) {
        $problems.Add("输出缺少文件：resources/$relative（仓库里有、输出里没有）")
    }
    elseif ($sourceFiles[$relative] -ne $producedFiles[$relative]) {
        $problems.Add("输出文件内容不一致：resources/$relative（SHA256 不同）")
    }
}

foreach ($relative in ($producedFiles.Keys | Sort-Object)) {
    if (-not $sourceFiles.ContainsKey($relative)) {
        $problems.Add("输出多出文件：resources/$relative（仓库里没有，可能是手工拷贝或过期产物）")
    }
}

# ---- ③ 种子主题包完整（清单 + 同目录壁纸，PNG magic bytes）

$themeCount = 0
$activeThemeCount = 0
if ($producedFiles.Count -eq 0) {
    $problems.Add('输出 resources\ 为空（至少应含出厂种子）')
}

foreach ($manifest in (Get-ChildItem -LiteralPath $producedRoot -Recurse -File -Filter theme.json | Sort-Object FullName)) {
    $themeCount++
    $directory = $manifest.Directory.FullName
    $relative = Get-RelativePath $producedRoot $manifest.FullName

    try {
        $theme = Get-Content -LiteralPath $manifest.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        $problems.Add("主题清单无法解析：resources/$relative（$($_.Exception.Message)）")
        continue
    }

    $wallpaper = [string]$theme.wallpaper
    if ($wallpaper.Length -eq 0) {
        $activeThemeCount++
        continue
    }

    $wallpaperPath = Join-Path $directory $wallpaper
    $wallpaperRelative = Get-RelativePath $producedRoot $wallpaperPath
    if (-not (Test-Path -LiteralPath $wallpaperPath -PathType Leaf)) {
        $problems.Add("主题包缺少壁纸：resources/$wallpaperRelative（清单声明了它）")
        continue
    }

    $header = [byte[]](Get-Content -LiteralPath $wallpaperPath -AsByteStream -TotalCount 8)
    $pngMagic = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    $isPng = $header.Length -eq $pngMagic.Length
    for ($i = 0; $isPng -and $i -lt $pngMagic.Length; $i++) {
        if ($header[$i] -ne $pngMagic[$i]) { $isPng = $false }
    }

    if (-not $isPng) {
        $problems.Add("主题壁纸不是 PNG：resources/$wallpaperRelative（magic bytes 不符）")
        continue
    }

    $activeThemeCount++
}

if ($themeCount -eq 0) {
    $problems.Add('输出里没有任何种子主题包（resources\seeds\settings\appearance\themes\<id>\theme.json）')
}

foreach ($problem in $problems) {
    Write-Host ("  x " + $problem) -ForegroundColor Red
}

if ($problems.Count -eq 0) {
    Write-Host ("[OK] 资源包校验通过：{0} 个文件与仓库一致（SHA256）；{1}/{2} 个种子主题包完整。" -f `
        $sourceFiles.Count, $activeThemeCount, $themeCount) -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] 资源包校验：{0} 处不一致（输出 {1} → 仓库 {2}）。" -f $problems.Count, $producedRoot, $sourceRoot) -ForegroundColor Red
exit 1
