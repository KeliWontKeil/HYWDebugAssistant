#Requires -Version 7.0
<#
.SYNOPSIS
    Release 导出：构建 Release → 校验模块包 → 复制 App 输出（只含程序层）→ 生成出厂设置基线 →
    断言发布边界 → 写 release.json。

.DESCRIPTION
    不引入 RID / self-contained 新形态，不做安装器：只把**现有 Release 构建产物**整理成可分发目录。
    发布包只含**程序层**：程序文件 + `modules/`（内置包）+ `resources/`（出厂种子 + 文本表）。
    步骤：
      1. 清理 PLATFORM，`dotnet build HYWDebugAssistant.slnx -c Release`；
      2. `tools/verify-module-packages.ps1 -Configuration Release`（门禁）；
      3. 复制应用输出（`$(OutDir)`，由 MSBuild 求值，不手抄目录结构）到干净目标目录，**排除**用户目录
         （settings/ workspace/ logs/ composites/ framework/ modules/external/）；
      4. 复核目标 modules 内无宿主程序集、模块 ID 与源码一致；
      5. 用导出目录里的应用入口 `--emit-default-settings` 生成
         `resources/seeds/settings/settings.json`（默认值唯一来源是代码，发布时才落盘）；
      6. 断言发布边界：禁止项缺席、种子主题包在位、整个导出目录里 wallpaper.png 只有一份；
      7. 写 `release.json`（产品四段版本、resources[] 全树哈希、8 个模块 ID/文件 hash）。
    安全护栏：目标目录必须**在仓库外**且为空/不存在；脚本不会清理仓库内路径。

.EXAMPLE
    ./tools/package-release.ps1 -OutputDirectory D:\publish\hyw-release
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot 'app-output.ps1')

foreach ($name in @('Platform', 'Platform(x86)')) {
    if (Test-Path "Env:$name") {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        Write-Host "[env] 已清除被污染的 $name" -ForegroundColor Yellow
    }
}

# ---------------------------------------------------------------- 安全护栏

$target = [System.IO.Path]::GetFullPath($OutputDirectory)
$repo = [System.IO.Path]::GetFullPath($root)
$separator = [System.IO.Path]::DirectorySeparatorChar

if ($target -eq $repo -or
    $target.StartsWith($repo + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "拒绝输出：目标目录在仓库内部（$target）——请指定仓库之外的**新目录**"
}

$forbidden = @(
    [System.IO.Path]::GetFullPath((Join-Path $root 'src\App\bin')),
    [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts')),
    [System.IO.Path]::GetFullPath((Join-Path $root 'src')),
    [System.IO.Path]::GetFullPath((Join-Path $root 'tests')),
    [System.IO.Path]::GetFullPath((Join-Path $root 'tools')),
    [System.IO.Path]::GetFullPath((Join-Path $root 'samples'))
)

foreach ($bad in $forbidden) {
    if ($target -eq $bad -or $target.StartsWith($bad + $separator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝输出：目标目录在仓库/构建产物内部（$target）"
    }
}

if ((Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)) {
    throw "拒绝输出：目标目录非空（$target）——请指定空目录或不存在的路径"
}

Write-Host "[release] 目标目录：$target"
Write-Host "[release] 构建 Release：HYWDebugAssistant.slnx" -ForegroundColor Cyan
dotnet build 'HYWDebugAssistant.slnx' -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { throw "Release 构建失败" }

Write-Host "[release] 校验模块包（Release）" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'verify-module-packages.ps1') -Configuration $Configuration

# 应用输出只向 MSBuild 要（不手抄 `bin\<配置>\net10.0`；理由见 tools/app-output.ps1 头注释）。
$appOutput = Get-AppOutputDirectory -Root $root -Configuration $Configuration
if (-not (Test-Path -LiteralPath $appOutput)) {
    throw "宿主输出目录不存在：$appOutput"
}

# ---------------------------------------------------------------- 复制 + 复核

if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

# **只导出程序层**：用户目录是运行期产生的现场，不是产品内容，一律不进发布。
$excludedTopLevel = @('settings', 'workspace', 'logs', 'composites', 'framework')

foreach ($item in (Get-ChildItem -LiteralPath $appOutput -Force)) {
    if ($excludedTopLevel -contains $item.Name) {
        continue
    }

    if ($item.Name -eq 'modules' -and $item.PSIsContainer) {
        # 内置包目录：external/ 是用户导入的包，同样不进发布。
        $modulesTarget = Join-Path $target 'modules'
        New-Item -ItemType Directory -Force -Path $modulesTarget | Out-Null

        foreach ($moduleItem in (Get-ChildItem -LiteralPath $item.FullName -Force)) {
            if ($moduleItem.Name -eq 'external') {
                continue
            }

            Copy-Item -LiteralPath $moduleItem.FullName -Destination $modulesTarget -Recurse -Force
        }

        continue
    }

    Copy-Item -LiteralPath $item.FullName -Destination $target -Recurse -Force
}

$releaseModules = Join-Path $target 'modules'
if (-not (Test-Path -LiteralPath $releaseModules)) {
    throw "导出目录缺少 modules/：$releaseModules"
}

$hostPatterns = @(
    'HYWDebugAssistant.Sdk.dll',
    'HYWDebugAssistant.Sdk.pdb',
    'HYWDebugAssistant.Sdk.Ui.*',
    'HYWDebugAssistant.Sdk.AccessKit.*',
    'HYWDebugAssistant.Access.*',
    'Avalonia*',
    'System.IO.Ports*',
    'SkiaSharp*',
    'HarfBuzzSharp*',
    'MicroCom.Runtime*',
    '*.deps.json'
)

$offenders = [System.Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem $releaseModules -Recurse -File) {
    foreach ($pattern in $hostPatterns) {
        if ($file.Name -like $pattern) {
            $offenders.Add($file.FullName)
            break
        }
    }
}

if ($offenders.Count -gt 0) {
    $preview = ($offenders | Select-Object -First 10) -join ', '
    throw "导出 modules/ 含宿主提供程序集：$preview"
}

$sourceManifests = Get-ChildItem (Join-Path $root 'src\Modules') -Recurse -Filter module.json -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$expectedIds = [System.Collections.Generic.List[string]]::new()
foreach ($manifestFile in $sourceManifests) {
    $id = (Get-Content $manifestFile.FullName -Raw | ConvertFrom-Json).id
    if ($expectedIds.Contains($id)) { throw "源码模块 ID 重复：$id" }
    $expectedIds.Add($id)
}

$packages = @(Get-ChildItem $releaseModules -Directory)
if ($packages.Count -ne $expectedIds.Count) {
    throw "导出 modules/ 包数量（$($packages.Count)）与源码模块数（$($expectedIds.Count)）不一致"
}

$moduleRecords = [System.Collections.Generic.List[object]]::new()
$actualIds = [System.Collections.Generic.List[string]]::new()

foreach ($package in ($packages | Sort-Object Name)) {
    $manifestPath = Join-Path $package.FullName 'module.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "导出包缺少 module.json：$($package.FullName)"
    }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.id)) { throw "导出包缺少 id：$manifestPath" }
    if ([string]::IsNullOrWhiteSpace($manifest.entryAssembly)) { throw "导出包缺少 entryAssembly：$manifestPath" }

    if ($actualIds.Contains($manifest.id)) { throw "导出模块 ID 重复：$($manifest.id)" }
    $actualIds.Add($manifest.id)

    $entryPath = Join-Path $package.FullName $manifest.entryAssembly
    if (-not (Test-Path -LiteralPath $entryPath)) { throw "导出包缺少实现程序集：$entryPath" }

    $files = @(Get-ChildItem $package.FullName -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{
            path = [System.IO.Path]::GetRelativePath($package.FullName, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })

    $moduleRecords.Add([pscustomobject]@{
        id = $manifest.id
        package = $package.Name
        entryAssembly = $manifest.entryAssembly
        entryAssemblySha256 = (Get-FileHash -LiteralPath $entryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        files = $files
    })
}

$missing = @($expectedIds | Where-Object { $actualIds -notcontains $_ })
$extra = @($actualIds | Where-Object { $expectedIds -notcontains $_ })
if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
    throw "导出模块 ID 集合与源码不一致。缺少：$($missing -join ', ')；多余：$($extra -join ', ')"
}

# ---------------------------------------------------------------- 出厂设置基线 + 发布边界

# 默认设置的唯一来源是代码：发布时才用导出目录里的程序生成基线（不在仓库里手工维护第二份）。
Write-Host "[release] 生成出厂设置基线（--emit-default-settings）" -ForegroundColor Cyan
$settingsSeed = Join-Path $target 'resources\seeds\settings\settings.json'
$seedArgument = '"' + $settingsSeed + '"'

# 入口形态随平台：Windows 用 apphost（.exe），其他平台用 `dotnet <dll>`（没有 `.exe` 后缀的 apphost）。
# 应用是 WinExe：脚本里用 `&` 调用不会等它退出 ⇒ 必须 Start-Process -Wait，否则文件还没写完就检查。
if ($IsWindows) {
    $appExe = Join-Path $target 'HYWDebugAssistant.App.exe'
    if (-not (Test-Path -LiteralPath $appExe)) { throw "导出目录缺少应用入口：$appExe" }

    $emit = Start-Process -FilePath $appExe `
        -ArgumentList @('--emit-default-settings', $seedArgument) `
        -Wait -PassThru -WindowStyle Hidden
}
else {
    $appDll = Join-Path $target 'HYWDebugAssistant.App.dll'
    if (-not (Test-Path -LiteralPath $appDll)) { throw "导出目录缺少应用入口：$appDll" }

    $emit = Start-Process -FilePath 'dotnet' `
        -ArgumentList @(('"' + $appDll + '"'), '--emit-default-settings', $seedArgument) `
        -Wait -PassThru
}
if ($emit.ExitCode -ne 0) {
    throw "生成出厂设置基线失败（--emit-default-settings 退出码 $($emit.ExitCode)）"
}

if (-not (Test-Path -LiteralPath $settingsSeed)) { throw "生成出厂设置基线后仍找不到文件：$settingsSeed" }

# 发布边界：禁止项缺席。settings/ 与设置基线（resources/seeds/settings/settings.json）是两回事：
# 前者是用户数据根，后者是出厂种子，只有后者允许出现在导出目录里。
$forbiddenEntries = @(
    'settings',
    'workspace',
    'logs',
    'composites',
    'framework',
    (Join-Path 'modules' 'external')
)

foreach ($entry in $forbiddenEntries) {
    $path = Join-Path $target $entry
    if (Test-Path -LiteralPath $path) {
        throw "发布边界违规：导出目录里出现禁止项 '$entry'（$path）"
    }
}

# 出厂种子主题包必须在位，且整个导出目录里壁纸只有一份。
# 主题包**按目录发现**（不写死某套主题名：目录名就是 id，清单里的 wallpaper 才是文件名）。
$seedThemeRoot = Join-Path $target 'resources\seeds\settings\appearance\themes'
$seedThemes = @(Get-ChildItem -LiteralPath $seedThemeRoot -Directory -ErrorAction SilentlyContinue)
if ($seedThemes.Count -eq 0) { throw "导出目录里没有任何种子主题包：$seedThemeRoot" }

foreach ($themeDirectory in $seedThemes) {
    $manifestPath = Join-Path $themeDirectory.FullName 'theme.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "出厂种子主题包不完整：缺少 $manifestPath"
    }

    $wallpaperName = [string](Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json).wallpaper
    if ($wallpaperName.Length -gt 0) {
        $wallpaperPath = Join-Path $themeDirectory.FullName $wallpaperName
        if (-not (Test-Path -LiteralPath $wallpaperPath -PathType Leaf)) {
            throw "出厂种子主题包不完整：缺少 $wallpaperPath（清单声明了它）"
        }
    }
}

$wallpapers = @(Get-ChildItem -LiteralPath $target -Recurse -File -Filter wallpaper.png)
if ($wallpapers.Count -ne 1) {
    $preview = ($wallpapers | Select-Object -First 5 | ForEach-Object { $_.FullName }) -join '; '
    throw "导出目录里 wallpaper.png 必须只有一份（实际 $($wallpapers.Count) 份：$preview）"
}

Write-Host "[release] 发布边界通过：用户目录缺席；种子主题包在位；壁纸唯一" -ForegroundColor Green

# ---------------------------------------------------------------- release.json

$productVersion = (& (Join-Path $PSScriptRoot 'version.ps1') -Value).Trim()
if ($productVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "无法从 version.props 取得四段版本：'$productVersion'"
}

# 出厂资源清单：`resources/` **全树**（相对路径 + SHA256）。`seeds/` 是投放源、`i18n/` 是就地读的
# 文本表，两者一起纳入同一张清单；发布校验按这份清单逐文件核对。
$resourcesRoot = Join-Path $target 'resources'
if (-not (Test-Path -LiteralPath $resourcesRoot)) { throw "导出目录缺少出厂资源根：$resourcesRoot" }

$frameworkTable = Join-Path $resourcesRoot 'i18n\HYWDebugAssistant.FrameworkUi\zh-Hans.json'
if (-not (Test-Path -LiteralPath $frameworkTable -PathType Leaf)) {
    throw "导出目录缺少框架文本表：$frameworkTable（文本包没有被 StageResources 带进发布）"
}

$resourceRecords = [System.Collections.Generic.List[object]]::new()
foreach ($file in (Get-ChildItem -LiteralPath $resourcesRoot -Recurse -File | Sort-Object FullName)) {
    $resourceRecords.Add([pscustomobject]@{
        path = [System.IO.Path]::GetRelativePath($resourcesRoot, $file.FullName).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

if ($resourceRecords.Count -eq 0) { throw '导出目录的出厂资源为空（至少应有设置基线与文本表）' }

$release = [pscustomobject]@{
    version = [string]$productVersion
    configuration = $Configuration
    resources = $resourceRecords
    modules = $moduleRecords
}

$releaseJson = $release | ConvertTo-Json -Depth 8
Set-Content -LiteralPath (Join-Path $target 'release.json') -Value $releaseJson -Encoding utf8

# 读回校验，避免写出不可解析的 JSON
$written = Get-Content (Join-Path $target 'release.json') -Raw | ConvertFrom-Json
if ($written.modules.Count -ne $expectedIds.Count) {
    throw "release.json 的模块数量不正确：$($written.modules.Count)"
}

if ([string]$written.version -ne $productVersion) {
    throw "release.json 的 version 不正确：$($written.version)"
}

if (@($written.resources).Count -ne $resourceRecords.Count) {
    throw "release.json 的 resources 数量不正确：$(@($written.resources).Count)"
}

Write-Host "[release] release.json：version=$($written.version) resources=$(@($written.resources).Count) modules=$($written.modules.Count)" -ForegroundColor Green
Write-Host "[release] 导出完成：$target" -ForegroundColor Green
