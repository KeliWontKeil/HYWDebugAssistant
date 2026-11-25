#Requires -Version 7.0
<#
.SYNOPSIS
    文档站：结构检查 / 构建 / 本地预览（MkDocs + Material for MkDocs）。

.DESCRIPTION
    -Check  严格构建到 artifacts\docs-check，并做结构检查（导航完整、目录归属），不产出发布站
    -Build  构建到 artifacts\docs-site：中文在根，英文在 /en/
    -Serve  用 mkdocs 内置服务器本地预览（热重载）

    结构检查只回答"页面在不在、有没有登记"，不判断文字质量。
    页面内容全部手写：仓库里没有生成文档正文的脚本，导航与正文都是源文件。

    工具链：mkdocs 是一个 Python 程序（依赖见本目录 requirements.txt）。
    它只在本机作为**预览工具**使用，不是项目的持续开发工具；项目工具链是
    .NET / PowerShell / Visual Studio。

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

$mkdocs = Get-Command mkdocs -ErrorAction SilentlyContinue
if (-not $mkdocs) {
    throw '未找到 mkdocs。先安装 Python 3.11+，再执行：python -m pip install -r docs\requirements.txt'
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

# mkdocs.yml 用 INHERIT 复用共用配置，docs_dir 写在各自配置里。
$allowedDirs = @('start', 'guides', 'concepts', 'module-dev', 'project', 'about')

function Get-DocsDir([string]$config) {
    foreach ($line in (Get-Content -LiteralPath $config -Encoding utf8)) {
        if ($line -match '^\s*docs_dir:\s*(\S+)\s*$') {
            return [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Matches[1]))
        }
    }
    throw "配置里没有 docs_dir：$config"
}

# 导航项就是配置 nav 段里的每一个 *.md 路径；出现其它形态说明写法不对。
function Get-NavPages([string]$config) {
    $pages = [System.Collections.Generic.List[string]]::new()
    $inNav = $false
    foreach ($line in (Get-Content -LiteralPath $config -Encoding utf8)) {
        if ($line -match '^nav:\s*$') { $inNav = $true; continue }
        if ($inNav -and $line -match '^\S') { break }   # 下一个顶层键
        if (-not $inNav -or $line -match '^\s*#') { continue }
        if ($line -match '([^\s:]+\.md)\s*$') {
            $pages.Add($Matches[1].Replace('\', '/'))
        }
    }
    return $pages
}

function Get-TopLevelNavCount([string]$config) {
    $count = 0
    $inNav = $false
    foreach ($line in (Get-Content -LiteralPath $config -Encoding utf8)) {
        if ($line -match '^nav:\s*$') { $inNav = $true; continue }
        if ($inNav -and $line -match '^\S') { break }
        if ($inNav -and $line -match '^\s{2}-\s') { $count++ }
    }
    return $count
}

function Test-Structure([string]$config, [string]$lang, [string]$parityConfig) {
    $problems = [System.Collections.Generic.List[string]]::new()
    $docsDir = Get-DocsDir $config

    $navPages = Get-NavPages $config
    if ($navPages.Count -eq 0) {
        $problems.Add('配置里没有 nav 段，或 nav 里没有页面')
    }

    foreach ($page in $navPages) {
        if (-not (Test-Path -LiteralPath (Join-Path $docsDir $page) -PathType Leaf)) {
            $problems.Add("nav 指向不存在的页面：$page")
        }
    }

    $onDisk = @(Get-ChildItem -LiteralPath $docsDir -Recurse -File -Filter '*.md' | ForEach-Object {
            $_.FullName.Substring($docsDir.Length + 1).Replace('\', '/')
        })
    foreach ($page in ($onDisk | Sort-Object)) {
        if ($navPages -notcontains $page) {
            $problems.Add("页面没有登记到 nav：$page")
        }
    }

    foreach ($directory in (Get-ChildItem -LiteralPath $docsDir -Directory)) {
        if ($allowedDirs -notcontains $directory.Name) {
            $problems.Add("docs 目录下出现未登记的目录：$($directory.Name)")
        }
        if (-not (Test-Path -LiteralPath (Join-Path $directory.FullName 'index.md') -PathType Leaf)) {
            $problems.Add("目录缺少首页 index.md：$($directory.Name)")
        }
    }

    # 中文站顶层导航固定为 7 项（首页 + 六层）。
    if ($lang -eq 'zh') {
        $topLevel = Get-TopLevelNavCount $config
        if ($topLevel -ne 7) {
            $problems.Add("导航顶层必须是 7 项（首页 + 6 层），当前 $topLevel 项")
        }
    }

    if ($parityConfig) {
        $otherDir = Get-DocsDir $parityConfig
        if (-not (Test-Path -LiteralPath $otherDir -PathType Container)) {
            $problems.Add("对照语言目录不存在：$otherDir")
        }
        else {
            $otherPages = @(Get-ChildItem -LiteralPath $otherDir -Recurse -File -Filter '*.md' | ForEach-Object {
                    $_.FullName.Substring($otherDir.Length + 1).Replace('\', '/')
                })
            foreach ($page in ($otherPages | Sort-Object)) {
                if ($onDisk -notcontains $page) {
                    $problems.Add("对应语言页没有匹配的中文源页：$page")
                }
            }
        }
    }

    foreach ($problem in $problems) {
        Write-Host "[FAIL] $problem" -ForegroundColor Red
    }
    if ($problems.Count -gt 0) {
        throw "文档结构检查失败：$config"
    }

    Write-Host "[OK] 结构检查通过：$($onDisk.Count) 页，$($navPages.Count) 个导航项"
}

function Test-SiteOutput([string]$siteDir, [string]$lang) {
    if (-not (Test-Path -LiteralPath (Join-Path $siteDir 'index.html') -PathType Leaf)) {
        throw "构建产物缺少 index.html：$siteDir（$lang）"
    }

    $search = Join-Path $siteDir 'search\search_index.json'
    if (-not (Test-Path -LiteralPath $search -PathType Leaf)) {
        throw "构建产物缺少搜索索引：$search（$lang）"
    }
    $data = Get-Content -LiteralPath $search -Raw -Encoding utf8 | ConvertFrom-Json
    if (-not $data.docs) {
        throw "搜索索引为空（$lang）"
    }
}

function Invoke-SiteBuild([string]$config, [string]$siteDir) {
    if (Test-Path -LiteralPath $siteDir) {
        Remove-Item -LiteralPath $siteDir -Recurse -Force
    }

    & mkdocs build -f $config --strict -d $siteDir
    if ($LASTEXITCODE -ne 0) {
        throw "文档构建失败：$config"
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
        Test-Structure $zhConfig 'zh' $(if ($buildAll) { $enConfig } else { $null })
        Test-SiteOutput (Join-Path $checkRoot 'zh') 'zh'

        if ($buildAll) {
            Invoke-SiteBuild $enConfig (Join-Path $checkRoot 'en')
            Test-Structure $enConfig 'en' $null
            Test-SiteOutput (Join-Path $checkRoot 'en') 'en'
        }

        Write-Host '[docs] 结构检查通过' -ForegroundColor Green
    }
    'Build' {
        Invoke-BuildAll $siteRoot
        Test-Structure $zhConfig 'zh' $(if ($buildAll) { $enConfig } else { $null })
        Test-SiteOutput $siteRoot 'zh'
        if ($buildAll) {
            Test-Structure $enConfig 'en' $null
            Test-SiteOutput (Join-Path $siteRoot 'en') 'en'
        }
        Write-Host "[docs] 构建完成：$siteRoot" -ForegroundColor Green
    }
    'Serve' {
        Write-Host "[docs] 预览：http://localhost:$Port/（Ctrl+C 停止）" -ForegroundColor Green
        & mkdocs serve -f $zhConfig --dev-addr "127.0.0.1:$Port"
    }
}
