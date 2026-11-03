#Requires -Version 7.0
<#
.SYNOPSIS
    校验模块作者包产物：数量、内容（dll + xml / props + targets）、依赖组、无多余文件。

.DESCRIPTION
    判据只看**产物**，不看源码（改源码不会让本脚本通过变绿）：
      · 恰好五个包，名字与版本来自 `version.props`；
      · 运行时契约包（`lib`）：含 `lib/net10.0/<包名>.dll` 与 `<包名>.xml`
        —— 模块作者拿不到框架源码，包内 XML 文档就是 API 的唯一语义来源；
      · 打包规则包（`build`）：含 `build/<包名>.props` 与 `build/<包名>.targets`；
      · 依赖组：`Sdk` 无依赖；`Sdk.Ui` → `Sdk` + `Avalonia`；`Sdk.AccessKit` → `Sdk`；
        `Sdk.Testing` → `Sdk`；`Module.Build` 无依赖（作者包，不随宿主发布、不进模块包）；
      · 包内不得出现上述内容与元数据之外的文件。

.EXAMPLE
    ./tools/verify-sdk-packages.ps1

.EXAMPLE
    ./tools/verify-sdk-packages.ps1 -Directory D:\temp\packages
#>
[CmdletBinding()]
param(
    # 包目录（缺省 = 仓库内 artifacts\sdk-packages）。
    [string]$Directory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Directory) {
    $Directory = Join-Path $root 'artifacts\sdk-packages'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

$version = (& (Join-Path $PSScriptRoot 'version.ps1') -Value).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "无法从 version.props 取得四段版本（得到 '$version'）"
}

$packagesProps = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
if ($packagesProps -notmatch '<PackageVersion Include="Avalonia" Version="([^"]+)"') {
    throw 'Directory.Packages.props 里没有 Avalonia 的版本条目'
}
$avaloniaVersion = $Matches[1]

# 包名 → 期望内容（Kind：lib = 运行时契约，build = 打包规则）与依赖 id。
$expectations = [ordered]@{
    'HYWDebugAssistant.Sdk'           = @{ Kind = 'lib';   Deps = @() }
    'HYWDebugAssistant.Sdk.Ui'        = @{ Kind = 'lib';   Deps = @('HYWDebugAssistant.Sdk', 'Avalonia') }
    'HYWDebugAssistant.Sdk.AccessKit' = @{ Kind = 'lib';   Deps = @('HYWDebugAssistant.Sdk') }
    'HYWDebugAssistant.Sdk.Testing'   = @{ Kind = 'lib';   Deps = @('HYWDebugAssistant.Sdk') }
    'HYWDebugAssistant.Module.Build'  = @{ Kind = 'build'; Deps = @() }
}

if (-not (Test-Path -LiteralPath $Directory)) {
    throw "包目录不存在：$Directory（先运行 tools/pack-sdk.ps1）"
}

$produced = @(Get-ChildItem -LiteralPath $Directory -File)
$expectedNames = @($expectations.Keys | ForEach-Object { "$_.$version.nupkg" })

$missing = @($expectedNames | Where-Object { $name = $_; -not ($produced | Where-Object { $_.Name -eq $name }) })
if ($missing.Count -gt 0) {
    throw "缺少包：$($missing -join '，')（目录：$Directory）"
}

$extra = @($produced | Where-Object { $_.Name -notin $expectedNames })
if ($extra.Count -gt 0) {
    throw "只允许五个包，发现额外文件：$($extra.Name -join '，')（目录：$Directory）"
}

$errors = @()

foreach ($id in $expectations.Keys) {
    $spec = $expectations[$id]
    $packagePath = Join-Path $Directory "$id.$version.nupkg"
    $zip = [System.IO.Compression.ZipFile]::OpenRead($packagePath)

    try {
        $entries = @($zip.Entries)

        if ($spec.Kind -eq 'lib') {
            foreach ($suffix in @('.dll', '.xml')) {
                $wanted = "lib/net10.0/$id$suffix"
                $entry = $entries | Where-Object { $_.FullName -eq $wanted }
                if (-not $entry) {
                    $errors += "$id：缺少 $wanted"
                }
                elseif ($suffix -eq '.xml' -and $entry.Length -lt 1024) {
                    $errors += "$id：$wanted 过小（$($entry.Length) B），可能没有真的生成文档"
                }
            }
        }
        else {
            foreach ($suffix in @('.props', '.targets')) {
                $wanted = "build/$id$suffix"
                if (-not ($entries | Where-Object { $_.FullName -eq $wanted })) {
                    $errors += "$id：缺少 $wanted"
                }
            }
        }

        foreach ($entry in $entries) {
            $isMetadata = $entry.FullName -like '_rels/*' `
                -or $entry.FullName -eq '[Content_Types].xml' `
                -or $entry.Name -like '*.nuspec' `
                -or $entry.FullName -like 'package/services/metadata/*'
            $isContent = if ($spec.Kind -eq 'lib') {
                $entry.FullName -like 'lib/net10.0/*'
            }
            else {
                $entry.FullName -like 'build/*'
            }

            if (-not ($isMetadata -or $isContent)) {
                $errors += "$id：包内出现不允许的文件 $($entry.FullName)"
            }
        }

        $nuspecEntry = $entries | Where-Object { $_.Name -like '*.nuspec' }
        if (-not $nuspecEntry) {
            $errors += "$id：缺少 nuspec"
            continue
        }

        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $dependencyIds = @(
            $nuspec.package.metadata.dependencies.group.dependency |
                ForEach-Object { $_.id } |
                Where-Object { $_ }
        )
        $wantedDependencies = @($spec.Deps)

        foreach ($wanted in $wantedDependencies) {
            if ($dependencyIds -notcontains $wanted) {
                $errors += "$id：依赖组缺少 $wanted（实际：$($dependencyIds -join '，')）"
            }
        }

        foreach ($actual in $dependencyIds) {
            if ($wantedDependencies -notcontains $actual) {
                $errors += "$id：依赖组出现未预期的 $actual"
            }
        }

        foreach ($dependency in @($nuspec.package.metadata.dependencies.group.dependency)) {
            if (-not $dependency) { continue }
            $expectedVersion = if ($dependency.id -eq 'Avalonia') { $avaloniaVersion } else { $version }
            if ($dependency.version -ne $expectedVersion) {
                $errors += "$id：依赖 $($dependency.id) 的版本是 $($dependency.version)，应为 $expectedVersion"
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "  x $_" -ForegroundColor Red }
    throw "SDK 包产物校验未通过（$($errors.Count) 处）"
}

Write-Host "[verify] $($expectations.Count) 个包通过（$version）：内容与依赖组正确" -ForegroundColor Green
