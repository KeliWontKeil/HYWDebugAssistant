#Requires -Version 7.0
<#
.SYNOPSIS
    产品发布：自包含发布 App → 复制资源/内置模块 → 生成设置基线 → 边界校验 → 写 release.json。

.DESCRIPTION
    产品只包含程序层：可执行文件、运行时依赖、`modules/`、`resources/`、许可与 release.json。
    不包含 SDK、样例、源码、用户数据，也不生成压缩包。

    默认 RID = win-x64（当前承诺目标）。其他 RID 允许尝试发布，但不承诺可编译/可运行，
    脚本会标记 `validation = unverified`，不处理相关平台 bug。

    默认先跑完整门禁 `build.ps1 -Configuration Release`；`-SkipGate` 只用于本地迭代。
    默认要求工作树干净；`-AllowDirty` 会记录 `sourceDirty = true`，产物不能作为发布基准。

.EXAMPLE
    ./tools/package-release.ps1 -OutputDirectory D:\dist\HYWDebugAssistant-0.0.0.13-win-x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$RuntimeIdentifier = 'win-x64',

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipGate,

    [switch]$AllowDirty
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

$rid = $RuntimeIdentifier.Trim().ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($rid)) {
    throw 'RuntimeIdentifier 不能为空'
}

$promised = $rid -eq 'win-x64'
$validation = if ($promised) { 'full' } else { 'unverified' }
if (-not $promised) {
    Write-Host "[release] [warn] RID $rid 未验证：允许发布，但不承诺可编译/可运行，不处理相关 bug" -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 版本与源码状态

$version = (& (Join-Path $PSScriptRoot 'version.ps1') -Value).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "无法从 version.props 取得四段版本：'$version'"
}

$commit = (& git rev-parse HEAD 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
    throw '无法读取 git 提交号；发布必须能追溯到源码提交'
}

$dirty = -not [string]::IsNullOrWhiteSpace((& git status --porcelain 2>$null | Out-String).Trim())
if ($dirty -and -not $AllowDirty) {
    throw '工作树不干净：请先提交，或显式使用 -AllowDirty（产物不能作为发布基准）'
}

if ($dirty) {
    Write-Host "[release] [warn] 工作树不干净：release.json 将记录 sourceDirty=true" -ForegroundColor Yellow
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
    [System.IO.Path]::GetFullPath((Join-Path $root 'templates'))
)

foreach ($bad in $forbidden) {
    if ($target -eq $bad -or $target.StartsWith($bad + $separator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝输出：目标目录在仓库/构建产物内部（$target）"
    }
}

if ((Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)) {
    throw "拒绝输出：目标目录非空（$target）——请指定空目录或不存在的路径"
}

$parent = Split-Path -Parent $target
if ([string]::IsNullOrWhiteSpace($parent)) {
    throw "无法解析输出目录的父目录：$target"
}

New-Item -ItemType Directory -Force -Path $parent | Out-Null
$temp = Join-Path $parent ('.' + (Split-Path -Leaf $target) + '.publishing-' + [guid]::NewGuid().ToString('N').Substring(0, 8))

Write-Host "[release] 目标目录：$target"
Write-Host "[release] RID：$rid（validation=$validation）"

try {
    # ------------------------------------------------------------ 门禁
    if (-not $SkipGate) {
        Write-Host '[release] 完整门禁：build.ps1 -Configuration Release' -ForegroundColor Cyan
        & (Join-Path $root 'build.ps1') -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw '门禁未通过' }
    }
    else {
        Write-Host '[release] [warn] 已跳过门禁（-SkipGate）' -ForegroundColor Yellow
    }

    # ------------------------------------------------------------ 自包含发布
    Write-Host "[release] dotnet publish（$rid，self-contained）" -ForegroundColor Cyan
    $appProject = Join-Path $root 'src\App\HYWDebugAssistant.App.csproj'
    dotnet publish $appProject -c $Configuration -r $rid --self-contained true -o $temp -nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

    if (-not (Test-Path -LiteralPath $temp)) { throw "发布目录不存在：$temp" }

    # 开发符号与 XML 文档不属于产品；保留 .deps.json / .runtimeconfig.json / 原生库。
    # 模块包里的 pdb 也一并去掉（运行时不需要）。
    # 逐个删而不是管道接 Remove-Item：管道形式在部分壳层里拿不到路径操作数。
    $devArtifacts = @(Get-ChildItem -LiteralPath $temp -Recurse -File -Include '*.pdb', '*.xml' -ErrorAction SilentlyContinue)
    foreach ($devArtifact in $devArtifacts) {
        Remove-Item -LiteralPath $devArtifact.FullName -Force -ErrorAction SilentlyContinue
    }

    # ------------------------------------------------------------ 自包含检查
    $isWindowsTarget = $rid -like 'win-*'
    $runtimeFiles = if ($isWindowsTarget) {
        @('coreclr.dll', 'hostfxr.dll')
    }
    elseif ($rid -like 'osx-*') {
        @('libcoreclr.dylib', 'libhostfxr.dylib')
    }
    else {
        @('libcoreclr.so', 'libhostfxr.so')
    }

    $missingRuntime = @($runtimeFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $temp $_)) })
    if ($missingRuntime.Count -gt 0) {
        $message = "发布目录缺少自包含运行时文件：$($missingRuntime -join ', ')"
        if ($promised) { throw $message }
        Write-Host "[release] [warn] $message（该 RID 未验证）" -ForegroundColor Yellow
    }

    # ------------------------------------------------------------ 设置基线
    $seedDirectory = Join-Path $temp 'resources\seeds\settings'
    New-Item -ItemType Directory -Force -Path $seedDirectory | Out-Null
    $seedPath = Join-Path $seedDirectory 'settings.json'

    $hostRunnable = $IsWindows -and $rid -eq 'win-x64'
    if ($hostRunnable) {
        $entryName = if ($IsWindows) { 'HYWDebugAssistant.App.exe' } else { 'HYWDebugAssistant.App' }
        $entry = Join-Path $temp $entryName
        if (-not (Test-Path -LiteralPath $entry)) { throw "发布目录缺少应用入口：$entry" }

        $seedArgument = '"' + $seedPath + '"'
        $emit = Start-Process -FilePath $entry `
            -ArgumentList @('--emit-default-settings', $seedArgument) `
            -Wait -PassThru -WindowStyle Hidden
        if ($emit.ExitCode -ne 0) {
            throw "生成出厂设置基线失败（--emit-default-settings 退出码 $($emit.ExitCode)）"
        }
        $seedSource = 'target'
    }
    else {
        # 非宿主 RID：用宿主可运行的 Release App 生成种子；产物本身不保证能在此机器启动。
        $hostApp = Join-Path $root 'src\App\bin\Release\net10.0\HYWDebugAssistant.App.exe'
        if (-not (Test-Path -LiteralPath $hostApp)) {
            Write-Host '[release] 构建宿主可运行的 App 以生成设置基线' -ForegroundColor Cyan
            dotnet build (Join-Path $root 'src\App\HYWDebugAssistant.App.csproj') -c $Configuration -nologo
            if ($LASTEXITCODE -ne 0) { throw '宿主 App 构建失败（无法生成设置基线）' }
        }
        if (-not (Test-Path -LiteralPath $hostApp)) {
            throw "找不到宿主可运行的 App.exe：$hostApp；非宿主 RID 需要先在 Windows 上构建宿主 App 才能生成设置基线"
        }

        $seedArgument = '"' + $seedPath + '"'
        $emit = Start-Process -FilePath $hostApp `
            -ArgumentList @('--emit-default-settings', $seedArgument) `
            -Wait -PassThru -WindowStyle Hidden
        if ($emit.ExitCode -ne 0) {
            throw "宿主 App 生成设置基线失败（退出码 $($emit.ExitCode)）"
        }
        $seedSource = 'host'
        Write-Host '[release] [warn] 种子由宿主 App 生成；非宿主 RID 的启动未验证' -ForegroundColor Yellow
    }

    if (-not (Test-Path -LiteralPath $seedPath)) {
        throw "生成出厂设置基线后仍找不到文件：$seedPath"
    }

    # ------------------------------------------------------------ 许可与说明
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination (Join-Path $temp 'LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $temp 'THIRD_PARTY_NOTICES.md') -Force

    $entryName = if ($isWindowsTarget) { 'HYWDebugAssistant.App.exe' } else { 'HYWDebugAssistant.App' }
    $readme = @"
何译位调试助手 $version（$rid）

启动：双击 $entryName（或从终端运行）。
本目录是自包含产品：不需要另装 .NET 运行时；不包含安装器，也不包含压缩包。
运行中的产品就是主应用，也是模块宿主；模块包导入这个进程。
数据目录（首次运行时按需建立）：settings\、workspace\、composites\、logs\。
modules\ 是内置模块包；resources\ 是出厂文本、图标与种子。
模块开发工具链是独立发布的 SDK 目录，不在本目录中。

release.json 记录版本、源码提交与产物哈希。
"@
    Set-Content -LiteralPath (Join-Path $temp 'README.txt') -Value $readme -Encoding utf8 -NoNewline

    # ------------------------------------------------------------ 边界校验
    $forbiddenEntries = @(
        'sdk',
        'samples',
        'settings',
        'workspace',
        'logs',
        'composites',
        'framework',
        (Join-Path 'modules' 'external')
    )

    foreach ($entry in $forbiddenEntries) {
        $path = Join-Path $temp $entry
        if (Test-Path -LiteralPath $path) {
            throw "发布边界违规：导出目录里出现禁止项 '$entry'（$path）"
        }
    }

    # 模块包内的 views/*.axaml 是运行期资源，不是待编译源码；其余 .cs/.axaml/工程文件一律拒绝。
    $sourceFiles = Get-ChildItem -LiteralPath $temp -Recurse -File
    $sourceHits = @($sourceFiles | Where-Object {
            $_.Extension -in @('.cs', '.csproj', '.sln', '.slnx') -or
            ($_.Extension -eq '.axaml' -and $_.FullName -notmatch '[\\/]modules[\\/][^\\/]+[\\/]views[\\/][^\\/]+\.axaml$')
        })
    if ($sourceHits.Count -gt 0) {
        throw "发布目录包含源码文件：$($sourceHits[0].FullName)"
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

    $releaseModules = Join-Path $temp 'modules'
    if (-not (Test-Path -LiteralPath $releaseModules)) { throw "发布目录缺少 modules/：$releaseModules" }

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
        throw "发布 modules/ 含宿主提供程序集：$(($offenders | Select-Object -First 10) -join ', ')"
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
        throw "发布 modules/ 包数量（$($packages.Count)）与源码模块数（$($expectedIds.Count)）不一致"
    }

    $moduleRecords = [System.Collections.Generic.List[object]]::new()
    $actualIds = [System.Collections.Generic.List[string]]::new()

    foreach ($package in ($packages | Sort-Object Name)) {
        $manifestPath = Join-Path $package.FullName 'module.json'
        if (-not (Test-Path -LiteralPath $manifestPath)) {
            throw "发布包缺少 module.json：$($package.FullName)"
        }

        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($manifest.id)) { throw "发布包缺少 id：$manifestPath" }
        if ([string]::IsNullOrWhiteSpace($manifest.entryAssembly)) { throw "发布包缺少 entryAssembly：$manifestPath" }

        if ($actualIds.Contains($manifest.id)) { throw "发布模块 ID 重复：$($manifest.id)" }
        $actualIds.Add($manifest.id)

        $entryPath = Join-Path $package.FullName $manifest.entryAssembly
        if (-not (Test-Path -LiteralPath $entryPath)) { throw "发布包缺少实现程序集：$entryPath" }

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
        throw "发布模块 ID 集合与源码不一致。缺少：$($missing -join ', ')；多余：$($extra -join ', ')"
    }

    $resourcesRoot = Join-Path $temp 'resources'
    if (-not (Test-Path -LiteralPath $resourcesRoot)) { throw "发布目录缺少 resources/：$resourcesRoot" }

    $requiredResource = Join-Path $resourcesRoot 'i18n\HYWDebugAssistant.FrameworkUi\zh-Hans.json'
    if (-not (Test-Path -LiteralPath $requiredResource)) {
        throw "发布目录缺少框架文本表：$requiredResource"
    }

    $seedThemeRoot = Join-Path $resourcesRoot 'seeds\settings\appearance\themes'
    $seedThemes = @(Get-ChildItem -LiteralPath $seedThemeRoot -Directory -ErrorAction SilentlyContinue)
    if ($seedThemes.Count -eq 0) { throw "发布目录里没有任何种子主题包：$seedThemeRoot" }

    $resourceRecords = [System.Collections.Generic.List[object]]::new()
    foreach ($file in (Get-ChildItem $resourcesRoot -Recurse -File | Sort-Object FullName)) {
        $resourceRecords.Add([pscustomobject]@{
            path = [System.IO.Path]::GetRelativePath($resourcesRoot, $file.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    # ------------------------------------------------------------ release.json
    $appRecords = [System.Collections.Generic.List[object]]::new()
    foreach ($file in (Get-ChildItem $temp -Recurse -File | Sort-Object FullName)) {
        $relative = [System.IO.Path]::GetRelativePath($temp, $file.FullName).Replace('\', '/')
        if ($relative.StartsWith('modules/') -or $relative.StartsWith('resources/') -or $relative -eq 'release.json') {
            continue
        }

        $appRecords.Add([pscustomobject]@{
            path = $relative
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    $release = [pscustomobject]@{
        kind = 'product'
        version = [string]$version
        sourceCommit = [string]$commit
        sourceDirty = [bool]$dirty
        rid = $rid
        selfContained = $true
        promised = [bool]$promised
        validation = $validation
        seedSource = $seedSource
        app = $appRecords
        modules = $moduleRecords
        resources = $resourceRecords
    }

    $releaseJson = $release | ConvertTo-Json -Depth 10
    Set-Content -LiteralPath (Join-Path $temp 'release.json') -Value $releaseJson -Encoding utf8

    $written = Get-Content (Join-Path $temp 'release.json') -Raw | ConvertFrom-Json
    if ([string]$written.version -ne $version) { throw "release.json 的 version 不正确：$($written.version)" }
    if ([string]$written.rid -ne $rid) { throw "release.json 的 rid 不正确：$($written.rid)" }
    if (@($written.modules).Count -ne $expectedIds.Count) {
        throw "release.json 的模块数量不正确：$($written.modules).Count"
    }

    # ------------------------------------------------------------ 落位
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    Move-Item -LiteralPath $temp -Destination $target
    Write-Host "[release] 产品发布完成：$target" -ForegroundColor Green
    Write-Host "[release] version=$version rid=$rid modules=$($moduleRecords.Count) resources=$($resourceRecords.Count) app=$($appRecords.Count)" -ForegroundColor Green
}
catch {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
}
