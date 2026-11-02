#Requires -Version 7.0
<#
.SYNOPSIS
  应用输出目录的**唯一解析实现**（工具脚本共用；不猜路径、不抄目录结构）。

.DESCRIPTION
  `src\App\bin\<配置>\net10.0` 这条路径依赖 `$(OutDir)`，而 `$(OutDir)` 会被
  `$(PlatformName)` / 环境变量 `PLATFORM` / VS 的解决方案平台三者之一改写（实测能改出
  `bin\C:\...\` 这种非法路径）。脚本里再手抄一遍目录结构，就等于**第二份事实源**：
  校验脚本会去看一棵"构建根本没写过的树"，于是一边报通过、一边界面还是旧文案。

  因此这里统一向 MSBuild 要 **`OutDir` 的计算结果**（`-getProperty:OutDir`，纯求值、不构建），
  只把结果拼成绝对路径。解析失败 / 结果为空 ⇒ 直接抛错（**"目标缺失即失败"**，
  不允许"没有可校验对象"被当成通过）。

.NOTES
  调用方：`verify-resources.ps1` · `verify-module-packages.ps1` · `package-release.ps1` · `dev.ps1`。
#>

$script:AppProjectRelative = 'src\App\HYWDebugAssistant.App.csproj'

<#
.SYNOPSIS
  取应用输出目录的绝对路径（含 TFM 子目录，与构建写入的位置一致）。

.PARAMETER Root
  仓库根目录。

.PARAMETER Configuration
  构建配置（Debug / Release）。
#>
function Get-AppOutputDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$Configuration
    )

    $project = Join-Path $Root $script:AppProjectRelative
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "未找到应用项目：$project"
    }

    $raw = & dotnet msbuild $project -getProperty:OutDir -p:Configuration=$Configuration 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "解析应用输出目录失败（dotnet msbuild exit $LASTEXITCODE）：$($raw.Trim())"
    }

    # 输出形态随请求的属性个数而变：单个属性 = 裸值一行；多个属性 = JSON 对象。
    # 两种都收（不依赖某个具体 MSBuild 版本的输出习惯）。
    $text = $raw.Trim()
    $outDir = $null

    if ($text.StartsWith('{')) {
        try {
            $outDir = ($text | ConvertFrom-Json).Properties.OutDir
        }
        catch {
            $outDir = $null
        }
    }

    if ([string]::IsNullOrWhiteSpace($outDir)) {
        $outDir = @($text -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -Last 1)[0].Trim()
    }

    if ([string]::IsNullOrWhiteSpace($outDir)) {
        throw "解析应用输出目录失败：OutDir 为空（配置 $Configuration）"
    }

    $projectDirectory = Split-Path -Parent $project
    return [System.IO.Path]::GetFullPath((Join-Path $projectDirectory $outDir))
}

<#
.SYNOPSIS
  找出**陈旧的双产物树**（`bin\Any CPU\` / `obj\Any CPU\` 这类由平台名带空格产生的目录）。

.DESCRIPTION
  `PlatformName` 已固定为 `AnyCPU`（Directory.Build.props），新构建不会再产出这些目录；
  但旧会话留下的树不会被自动删除，且**门禁只看得到标准树** ⇒ 只提示，不判负。
#>
function Get-StalePlatformOutputTrees {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    return @(
        Get-ChildItem -LiteralPath (Join-Path $Root 'src') -Directory -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like '* *' -and ($_.Parent.Name -in @('bin', 'obj')) } |
            Select-Object -ExpandProperty FullName
    )
}
