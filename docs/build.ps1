#Requires -Version 7.0
<#
.SYNOPSIS
    文档站：结构检查 / 构建 / 本地预览（MkDocs + Material for MkDocs）。

.DESCRIPTION
    -Check  严格构建到 artifacts\docs-check，并做结构检查（导航完整、目录归属、生成产物），不产出发布站
    -Build  构建到 artifacts\docs-site：中文在根，英文在 /en/
    -Serve  构建后用本地静态服务器预览

    结构检查只回答"页面在不在、有没有登记、产物有没有生成"，不判断文字质量。
    工具链依赖见本目录 requirements.txt（Python 3.11+，pip install -r）。

.EXAMPLE
    .\docs\build.ps1 -Mode Check
    .\docs\build.ps1 -Mode Build -Languages all
    .\docs\build.ps1 -Mode Serve
#>
[CmdletBinding()]
param(
    [ValidateSet('Check', 'Build', 'Serve')]
    [string]$Mode = 'Check',

    [ValidateSet('zh', 'all')]
    [string]$Languages = 'zh',

    [int]$Port = 8000
)

$ErrorActionPreference = 'Stop'

# 本脚本住在 docs\ 下：文档工程根 = 脚本目录，仓库根 = 上一级。
$projectRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $projectRoot

$python = Get-Command python -ErrorAction SilentlyContinue
$mkdocs = Get-Command mkdocs -ErrorAction SilentlyContinue
if (-not $python) {
    throw '未找到 python。先安装 Python 3.11+，再执行：python -m pip install -r docs\requirements.txt'
}

if (-not $mkdocs) {
    throw '未找到 mkdocs。先执行：python -m pip install -r docs\requirements.txt'
}

$requirements = Join-Path $projectRoot 'requirements.txt'
$pinnedLine = Get-Content -LiteralPath $requirements | Where-Object { $_ -match '^mkdocs==' } | Select-Object -First 1
if ($pinnedLine) {
    $pinned = ($pinnedLine -split '==')[1].Trim()
    $versionText = & mkdocs --version
    if ($versionText -match 'version\s+(\S+)') {
        $installed = $Matches[1].TrimEnd(',')
        if ($installed -ne $pinned) {
            Write-Warning "mkdocs 版本与 requirements.txt 不一致：安装 $installed，要求 $pinned"
        }
    }
}

$buildAll = $Languages -eq 'all'
$checkRoot = Join-Path $repoRoot 'artifacts\docs-check'
$siteRoot = Join-Path $repoRoot 'artifacts\docs-site'
$zhConfig = Join-Path $projectRoot 'mkdocs.yml'
$enConfig = Join-Path $projectRoot 'mkdocs.en.yml'
$checkScript = Join-Path $projectRoot 'tools\check.py'

function Invoke-SiteBuild([string]$config, [string]$siteDir) {
    if (Test-Path -LiteralPath $siteDir) {
        Remove-Item -LiteralPath $siteDir -Recurse -Force
    }

    & mkdocs build -f $config --strict -d $siteDir
    if ($LASTEXITCODE -ne 0) {
        throw "文档构建失败：$config"
    }
}

function Invoke-StructureCheck([string]$config, [string]$siteDir, [string]$lang, [string]$parityConfig) {
    $arguments = @($checkScript, '--config', $config, '--site', $siteDir, '--lang', $lang)
    if ($parityConfig) {
        $arguments += @('--parity-config', $parityConfig)
    }

    & python @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "文档结构检查失败：$config"
    }
}

function Invoke-BuildAll([string]$targetRoot) {
    Invoke-SiteBuild $zhConfig $targetRoot
    if ($buildAll) {
        Invoke-SiteBuild $enConfig (Join-Path $targetRoot 'en')
    }
}

switch ($Mode) {
    'Check' {
        if (Test-Path -LiteralPath $checkRoot) {
            Remove-Item -LiteralPath $checkRoot -Recurse -Force
        }

        Invoke-SiteBuild $zhConfig (Join-Path $checkRoot 'zh')
        Invoke-StructureCheck $zhConfig (Join-Path $checkRoot 'zh') 'zh' $(if ($buildAll) { $enConfig } else { $null })

        if ($buildAll) {
            Invoke-SiteBuild $enConfig (Join-Path $checkRoot 'en')
            Invoke-StructureCheck $enConfig (Join-Path $checkRoot 'en') 'en' $null
        }

        Write-Host '[docs] 结构检查通过' -ForegroundColor Green
    }
    'Build' {
        Invoke-BuildAll $siteRoot
        Invoke-StructureCheck $zhConfig $siteRoot 'zh' $(if ($buildAll) { $enConfig } else { $null })
        Write-Host "[docs] 构建完成：$siteRoot" -ForegroundColor Green
    }
    'Serve' {
        Invoke-BuildAll $siteRoot
        Write-Host "[docs] 预览：http://localhost:$Port/（Ctrl+C 停止）" -ForegroundColor Green
        & python -m http.server $Port --directory $siteRoot
    }
}
