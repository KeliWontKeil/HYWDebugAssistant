#Requires -Version 7.0
<#
.SYNOPSIS
  .NET 工具链环境事实的**唯一解析实现**（工具脚本共用）。

.DESCRIPTION
  这些值过去按平台约定手拼（`$env:USERPROFILE\.nuget\packages`、`.exe` 后缀）：
  在非 Windows 上 `USERPROFILE` 为空 ⇒ 路径退化成 `\.nuget\packages`，
  而调用方**不报错**（许可审计退化成 `<nuspec not found>`）⇒ 门禁静默失真。
  改为向 `dotnet` 要答案：它认得 `NUGET_PACKAGES`、跨平台路径与自定义缓存位置。

.NOTES
  调用方：`audit-licenses.ps1` · `pack-sdk.ps1`。
#>

<#
.SYNOPSIS
  取 NuGet 全局包目录（`dotnet nuget locals global-packages --list` 的结果）。

.PARAMETER 无
#>
function Get-NuGetGlobalPackagesDirectory {
    [CmdletBinding()]
    param()

    $raw = & dotnet nuget locals global-packages --list 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "解析 NuGet 全局包目录失败（dotnet nuget locals exit $LASTEXITCODE）：$($raw.Trim())"
    }

    $line = @($raw -split "`r?`n" | Where-Object { $_ -match 'global-packages\s*:' } | Select-Object -First 1)
    if ($line.Count -eq 0) {
        throw "解析 NuGet 全局包目录失败（输出里没有 global-packages 行）：$($raw.Trim())"
    }

    $path = ($line[0] -split ':', 2)[1].Trim()
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "解析 NuGet 全局包目录失败（目录不存在）：$path"
    }

    return $path
}
