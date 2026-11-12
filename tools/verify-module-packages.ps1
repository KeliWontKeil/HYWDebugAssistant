#Requires -Version 7.0
<#
.SYNOPSIS
    校验（或导出）**构建产物中的模块包目录**。

.DESCRIPTION
    模块包由**构建自动归拢**（见 src/Modules/Directory.Build.targets + App 项目的镜像目标）：
      · 模块项目构建  → artifacts\modules\<Configuration>\<包名>\
      · App 项目构建  → <应用输出>\modules\<包名>\（**内置包目录**，启动默认扫描）
    本脚本只做几件事：
      ① 校验两处包目录：manifest 必填项、实现程序集、**不得含宿主提供程序集**；
      ② 校验模块**自有资源包**：清单声明的 `resources/` 目录随包、逐字对账、不越界、不带未声明的资源；
      ③ 校验两处的模块 ID 集合与 src/Modules 下的源码清单一致（按构建配置）；
      ④ 校验宿主输出含 Sdk / Sdk.Ui / Sdk.AccessKit / Access / System.IO.Ports；
      ⑤ -ExportDirectory <目录>：把包复制出去，用于发布打包。
    注：包来源零改动（仍由构建自动归拢），本脚本只校验/导出。

.EXAMPLE
    ./tools/verify-module-packages.ps1
    ./tools/verify-module-packages.ps1 -Configuration Release
    ./tools/verify-module-packages.ps1 -ExportDirectory D:\publish\modules
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # 指定则把包复制到该目录（发布打包用）；不指定则只校验。
    [string]$ExportDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'app-output.ps1')

$packageRoot = Join-Path $root "artifacts\modules\$Configuration"

# 应用输出只向 MSBuild 要（不手抄 `bin\<配置>\net10.0`；理由见 tools/app-output.ps1 头注释）。
$appOutput = Get-AppOutputDirectory -Root $root -Configuration $Configuration
$appModules = Join-Path $appOutput 'modules'

# 源码模块目录（id → 目录）：包内随包文本要与源码逐字对账。
$sourceModuleDirectories = @{}
foreach ($manifestFile in (Get-ChildItem (Join-Path $root 'src\Modules') -Recurse -Filter module.json -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    $manifest = Get-Content $manifestFile.FullName -Raw | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace($manifest.id)) {
        $sourceModuleDirectories[$manifest.id] = $manifestFile.Directory.FullName
    }
}

# 宿主提供的程序集：它们随 App 输出，绝不允许出现在模块包里。
# 作者包（Sdk.Testing）同样不得进包：它只给作者的测试项目用，不是运行时依赖。
$forbiddenHostPatterns = @(
    'HYWDebugAssistant.Sdk*.dll',
    'HYWDebugAssistant.Sdk*.pdb',
    'HYWDebugAssistant.Sdk*.xml',
    'HYWDebugAssistant.Access*.dll',
    'Avalonia*',
    'System.IO.Ports*',
    'SkiaSharp*',
    'HarfBuzzSharp*',
    'MicroCom.Runtime*',
    '*.deps.json'
)

function Get-ForbiddenHostPattern {
    param([string]$FileName)

    foreach ($pattern in $forbiddenHostPatterns) {
        if ($FileName -like $pattern) { return $pattern }
    }

    return $null
}

function Get-ExpectedModuleIds {
    $manifests = Get-ChildItem (Join-Path $root 'src\Modules') -Recurse -Filter module.json -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

    if (-not $manifests) { throw "未找到源码模块清单（src\Modules\**\module.json）" }

    $ids = [System.Collections.Generic.List[string]]::new()
    foreach ($manifestFile in $manifests) {
        $manifest = Get-Content $manifestFile.FullName -Raw | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($manifest.id)) {
            throw "源码清单缺少 id：$($manifestFile.FullName)"
        }

        if ($ids.Contains($manifest.id)) {
            throw "源码模块 ID 重复：$($manifest.id)（$($manifestFile.FullName)）"
        }

        $ids.Add($manifest.id)
    }

    return [string[]]$ids
}

function Test-DeclaredResourcePath {
    param([object[]]$Declared, [string]$RelativePath)

    $normalized = $RelativePath -replace '\\', '/'
    foreach ($entry in $Declared) {
        $prefix = ([string]$entry) -replace '\\', '/'
        if ($normalized -eq $prefix -or $normalized.StartsWith($prefix + '/', [StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

function Test-ModulePackages {
    param([string]$Directory)

    if (-not (Test-Path $Directory)) {
        throw "模块包目录不存在：$Directory（请先运行 ./build.ps1 -Configuration $Configuration）"
    }

    $packages = @(Get-ChildItem $Directory -Directory)
    if ($packages.Count -eq 0) { throw "模块包目录为空：$Directory" }

    $ids = [System.Collections.Generic.List[string]]::new()
    $rows = [System.Collections.Generic.List[object]]::new()

    foreach ($package in $packages) {
        $manifestPath = Join-Path $package.FullName 'module.json'
        if (-not (Test-Path $manifestPath)) {
            throw "包目录缺少模块清单：$($package.FullName)"
        }

        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($manifest.id)) {
            throw "包清单缺少 id：$manifestPath"
        }

        if ([string]::IsNullOrWhiteSpace($manifest.entryAssembly)) {
            throw "包清单缺少 entryAssembly：$manifestPath"
        }

        if ($manifest.id -notmatch '^[a-z0-9-]+/[a-z0-9-]+$') {
            throw "包清单的 id 必须是两段（作者/模块名）：$manifestPath → $($manifest.id)"
        }

        $entryAssembly = Join-Path $package.FullName $manifest.entryAssembly
        if (-not (Test-Path $entryAssembly)) {
            throw "包目录缺少实现程序集：$($package.FullName) → $($manifest.entryAssembly)"
        }

        $files = @(Get-ChildItem $package.FullName -Recurse -File)
        foreach ($file in $files) {
            $pattern = Get-ForbiddenHostPattern -FileName $file.Name
            if ($pattern) {
                throw "模块包含宿主提供程序集（模式 '$pattern'）：$($file.FullName)"
            }
        }

        if ($ids.Contains($manifest.id)) {
            throw "模块 ID 重复：$($manifest.id)（$($package.FullName)）"
        }

        # **模块自有资源包**：源码侧 `resources/` 全量随包、与清单声明的目录一致、内容逐字对账；
        # 包里也不得出现"没声明的资源"（每个模块只带自己的包）。
        if ($sourceModuleDirectories.ContainsKey($manifest.id)) {
            $sourceModule = $sourceModuleDirectories[$manifest.id]
            $declared = @($manifest.resources | Where-Object { $null -ne $_ })

            foreach ($declaredPath in $declared) {
                $value = [string]$declaredPath
                if ([string]::IsNullOrWhiteSpace($value) -or
                    [System.IO.Path]::IsPathRooted($value) -or
                    $value -match '(^|[\\/])\.\.([\\/]|$)') {
                    throw "模块清单的 resources 必须是相对路径：'$value'（$manifestPath）"
                }

                $packagedDirectory = Join-Path $package.FullName ($value -replace '[\\/]', '\')
                if (-not (Test-Path -LiteralPath $packagedDirectory -PathType Container)) {
                    throw "模块包缺少清单声明的资源目录：$value（包 $($package.FullName)）"
                }
            }

            $sourceResources = Join-Path $sourceModule 'resources'
            if (Test-Path -LiteralPath $sourceResources -PathType Container) {
                foreach ($sourceFile in (Get-ChildItem -LiteralPath $sourceResources -Recurse -File | Sort-Object FullName)) {
                    $relativeText = [System.IO.Path]::GetRelativePath($sourceModule, $sourceFile.FullName).Replace('\', '/')

                    if (-not (Test-DeclaredResourcePath -Declared $declared -RelativePath $relativeText)) {
                        throw "模块资源没有在清单里声明：$relativeText（$manifestPath）"
                    }

                    $packagedFile = Join-Path $package.FullName $relativeText.Replace('/', '\')
                    if (-not (Test-Path -LiteralPath $packagedFile -PathType Leaf)) {
                        throw "模块包缺少资源文件：$relativeText（包 $($package.FullName)）"
                    }

                    $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
                    $packagedHash = (Get-FileHash -LiteralPath $packagedFile -Algorithm SHA256).Hash
                    if ($sourceHash -ne $packagedHash) {
                        throw "模块资源与源码不一致：$relativeText（包 $($package.FullName)）"
                    }
                }
            }

            $packagedResources = Join-Path $package.FullName 'resources'
            if (Test-Path -LiteralPath $packagedResources -PathType Container) {
                foreach ($packagedFile in (Get-ChildItem -LiteralPath $packagedResources -Recurse -File)) {
                    $relativeText = [System.IO.Path]::GetRelativePath($package.FullName, $packagedFile.FullName).Replace('\', '/')
                    if (-not (Test-DeclaredResourcePath -Declared $declared -RelativePath $relativeText)) {
                        throw "模块包含有未声明的资源：$relativeText（包 $($package.FullName)）"
                    }
                }
            }

            # **端口声明源**（views/*.axaml）：与源码逐字对账 —— Hosting 从它解析端口，
            # 旧副本会让"源码改了、包里还是旧端口"。
            $sourceViews = Join-Path $sourceModule 'Views'
            $packagedViews = Join-Path $package.FullName 'views'
            if (Test-Path -LiteralPath $sourceViews -PathType Container) {
                foreach ($sourceFile in (Get-ChildItem -LiteralPath $sourceViews -Recurse -File -Filter *.axaml | Sort-Object FullName)) {
                    $relativeText = [System.IO.Path]::GetRelativePath($sourceViews, $sourceFile.FullName).Replace('\', '/')
                    $packagedFile = Join-Path $package.FullName ('views\' + $relativeText.Replace('/', '\'))
                    if (-not (Test-Path -LiteralPath $packagedFile -PathType Leaf)) {
                        throw "模块包缺少端口声明源：views/$relativeText（包 $($package.FullName)）"
                    }

                    $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
                    $packagedHash = (Get-FileHash -LiteralPath $packagedFile -Algorithm SHA256).Hash
                    if ($sourceHash -ne $packagedHash) {
                        throw "端口声明源与源码不一致：views/$relativeText（包 $($package.FullName)）"
                    }
                }
            }
        }

        $ids.Add($manifest.id)
        $totalKb = [math]::Round((($files | Measure-Object Length -Sum).Sum / 1KB), 1)
        $rows.Add([pscustomobject]@{
            Package = $package.Name
            Id = $manifest.id
            Files = $files.Count
            SizeKB = $totalKb
        })
    }

    foreach ($row in $rows) {
        Write-Host ("[verify]   {0,-24} {1,5} 个文件 {2,8:N1} KB  {3}" -f `
            $row.Package, $row.Files, $row.SizeKB, $row.Id)
    }

    Write-Host "[verify] $($packages.Count) 个模块包通过：$Directory" -ForegroundColor Green
    return [pscustomobject]@{
        Ids = [string[]]$ids
        Count = $packages.Count
    }
}

function Assert-ModuleIdSet {
    param(
        [string]$Label,
        [string[]]$Actual,
        [string[]]$Expected
    )

    $missing = @($Expected | Where-Object { $Actual -notcontains $_ })
    $extra = @($Actual | Where-Object { $Expected -notcontains $_ })

    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        $message = ("{0} 的模块 ID 集合与源码清单不一致。" +
            " 缺少：{1}；多余：{2}") -f $Label, ($missing -join ', '), ($extra -join ', ')
        throw $message
    }
}

function Test-HostOutput {
    param([string]$Directory)

    if (-not (Test-Path $Directory)) {
        throw "宿主输出目录不存在：$Directory（请先构建 App 项目）"
    }

    $required = @(
        'HYWDebugAssistant.Sdk.dll',
        'HYWDebugAssistant.Sdk.Ui.dll',
        'HYWDebugAssistant.Sdk.AccessKit.dll',
        'HYWDebugAssistant.Access.dll',
        'System.IO.Ports.dll'
    )

    $missing = @($required | Where-Object { -not (Test-Path (Join-Path $Directory $_)) })
    if ($missing.Count -gt 0) {
        throw "宿主输出缺少外部接入所需程序集：$($missing -join ', ')（$Directory）"
    }

    Write-Host "[verify] 宿主输出齐全：$($required -join ', ')" -ForegroundColor Green
}

$expectedIds = Get-ExpectedModuleIds

Write-Host "[verify] 包根（按配置）：$packageRoot"
$built = Test-ModulePackages -Directory $packageRoot
$mirrored = Test-ModulePackages -Directory $appModules

if ($built.Count -ne $expectedIds.Count) {
    throw "构建产出的包数量（$($built.Count)）与源码模块数（$($expectedIds.Count)）不一致"
}

Assert-ModuleIdSet -Label 'artifacts\modules\<Configuration>' -Actual @($built.Ids) -Expected $expectedIds
Assert-ModuleIdSet -Label 'src\App\bin\<Configuration>\net10.0\modules' -Actual @($mirrored.Ids) -Expected $expectedIds

Test-HostOutput -Directory $appOutput

if ($ExportDirectory) {
    # 【2026-09 安全护栏 · `decisions.md` 7.55 §F】导出**会先清空目标目录** ⇒ 必须挡住"指到程序目录/用户数据"
    # 这类位置（否则一句话就把 `settings.json`、`modules\`、`data\` 全删了）。
    $target = [System.IO.Path]::GetFullPath($ExportDirectory)
    $forbiddenTargets = @(
        [System.IO.Path]::GetFullPath($root),
        [System.IO.Path]::GetFullPath((Join-Path $root 'src\App\bin')),
        [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts')),
        [System.IO.Path]::GetFullPath((Join-Path $root 'src'))
    )

    foreach ($bad in $forbiddenTargets) {
        if ($target -eq $bad -or $target.StartsWith($bad + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "拒绝导出：目标目录在仓库/构建产物内部（$target）——请指定仓库之外的**新目录**（导出会先清空目标目录）"
        }
    }

    # 目标已有内容时要求它是"空的或看起来就是模块包目录"，避免误删用户的其它数据
    if ((Test-Path $target) -and (Get-ChildItem $target -Force | Where-Object { $_.Name -ne 'module.json' })) {
        $looksLikePackages = (Get-ChildItem $target -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'module.json') }).Count -gt 0
        if (-not $looksLikePackages) {
            throw "拒绝导出：目标目录非空且不像模块包目录（$target）——导出会先清空它，请先自行确认"
        }
    }

    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $packageRoot '*') $target -Recurse -Force
    Write-Host "[export] $($built.Count) 个模块包 → $target" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "模块包来源：$packageRoot" -ForegroundColor Cyan
Write-Host "内置包目录（应用启动默认扫描）：$appModules" -ForegroundColor Cyan
Write-Host "应用内：工具栏「模块清单」→ 选「可用模块」→「加入现场」；「重扫来源」可发现新增的包（改过代码的包需重启应用）。"
