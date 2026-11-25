#Requires -Version 7.0
<#
.SYNOPSIS
  对外发布包形态的**唯一判据来源**（发布脚本与校验脚本共用；只给数据，不做动作）。

.DESCRIPTION
  「Windows 产品目录里哪些文件不该有」过去写在三处：`package-release.ps1` 的删除清单、
  `verify-release.ps1` 的黑名单、`dev/workflow.md` 的文字枚举。三份必然漂移——
  删除端加了文件、校验端没加 ⇒ 校验永远绿；校验端加了、删除端没加 ⇒ 发布永远失败。
  改这里之后：删的人与验的人读同一份数组，文档只指路不抄清单。

  只导出两类事实：
    · `Get-ReleaseCullList`     —— Windows 目标要从 publish 结果里**删掉**的文件名；
    · `Get-ReleaseCullGuard`    —— 删掉之后**必须还在**的护栏文件（反向清单）。
  以及一条判据：
    · `Test-ReleaseAssemblyClosure` —— 从渲染链根出发的程序集引用闭包有没有洞
      （`Get-ReleaseRendererRoots` 给起点）。裁剪清单靠人列举，必然漏；闭包是算出来的。
  非 Windows 目标目前不做裁剪，三组都返回空 / 跳过。

  **消费方**：`package-release.ps1`（执行删除）· `verify-release.ps1`（校验残留/缺项）。
#>

<#
.SYNOPSIS
  取 Windows 产品目录要删除的文件名（含通配符）。

.PARAMETER Rid
  运行时标识符；只有 `win-*` 有裁剪规则。

.PARAMETER Category
  可选：只取某一类（`symbols` / `diagnostics` / `foreign-platform` / `designer`）。
  省略 = 全部。
#>
function Get-ReleaseCullList {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Rid,

        [string]$Category
    )

    if ($Rid -notlike 'win-*') {
        return @()
    }

    $rules = [ordered]@{
        # 运行时自带的诊断组件：崩溃转储与调试符号读取，正常使用用不到。
        # 去掉后崩溃不再产出 dump —— 产品目录本就不带调试手段，取舍是"少 8 MB 体积"。
        'diagnostics'       = @(
            'createdump.exe'
            'mscordaccore.dll'
            'mscordaccore_*'
            'mscordbi.dll'
            'Microsoft.DiaSymReader.Native.amd64.dll'
            'msquic.dll'
            'System.Net.Quic.dll'
        )

        # Windows 目标用不到的其它平台后端。
        # **不要按"这个文件属于哪个平台"判断可删**：`Avalonia.Skia` 在程序集层面无条件引用
        # `Avalonia.Metal`（IMetalDevice / IMetalPlatformSurface 等），而 Skia 是 Windows 下的
        # 实际渲染器 —— 删掉 Metal 后 Avalonia 静默降级，窗口照建、标题正常、内容全黑，
        # 进程退出码仍是 0、stderr 为空。因此 Metal 必须随产品发布（15 KB）。
        # 这条判据由 `Test-ReleaseAssemblyClosure` 在发布与校验两侧自动兜住。
        # 留下的这几个是可以删的：它们的引用方同样会被删，或本就不在渲染链上。
        'foreign-platform'  = @(
            'Avalonia.X11.dll'
            'Avalonia.FreeDesktop.dll'
            'Avalonia.FreeDesktop.AtSpi.dll'
            'Avalonia.Native.dll'
            'Tmds.DBus.Protocol.dll'
        )

        # 设计器支持只在 VS 的 XAML 预览器里用；产品运行期不需要。
        'designer'          = @('Avalonia.DesignerSupport.dll')
    }

    if ([string]::IsNullOrWhiteSpace($Category)) {
        return @($rules.Values | ForEach-Object { $_ })
    }

    if (-not $rules.Contains($Category)) {
        throw "未知的裁剪类别 '$Category'（可用：$($rules.Keys -join ' / ')）"
    }

    return @($rules[$Category])
}

<#
.SYNOPSIS
  取「裁剪之后必须仍然存在」的文件（反向护栏）：删过头会让产品起不来。

.DESCRIPTION
  `Avalonia.Vulkan` 是硬约束：Win32GlManager 初始化时**无条件**加载它，
  缺了直接抛 FileNotFoundException 且窗口不创建（实测）。把它登记在这里，
  是为了让"手滑把 Vulkan 加进删除清单"在发布与校验两侧都立刻失败。
#>
function Get-ReleaseCullGuard {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Rid
    )

    if ($Rid -notlike 'win-*') {
        return @()
    }

    return @('Avalonia.Vulkan.dll')
}

<#
.SYNOPSIS
  取渲染链的根程序集名（装配闭包检查的起点）。

.DESCRIPTION
  起点选"平台渲染后端 + 实际渲染器"：`Avalonia.Win32` 提供窗口与 GPU 表面，
  `Avalonia.Skia` 提供绘制。两者在程序集层面引用谁，那个程序集就必须在产物里 ——
  "只在别的平台才加载"是运行期行为，不能拿来判断能否删除。

  非 Windows 目标暂无承诺，返回空（`Test-ReleaseAssemblyClosure` 随之跳过）。
#>
function Get-ReleaseRendererRoots {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Rid
    )

    if ($Rid -notlike 'win-*') {
        return @()
    }

    return @('Avalonia.Win32', 'Avalonia.Skia')
}

<#
.SYNOPSIS
  检查程序集引用是否闭合：从渲染链根出发，被引用到的程序集必须都在目录里。

.DESCRIPTION
  裁剪的真正判据不是"这个文件属于哪个平台"，而是**引用闭包有没有洞**。
  删掉一个被渲染链间接引用的程序集，Avalonia 会静默降级：窗口建得出来、标题正常、
  内容全黑，进程退出码 0、stderr 为空 —— 没有任何自动信号。所以这条检查必须存在，
  且必须在发布时（`package-release.ps1`）与校验时（`verify-release.ps1`）各跑一遍。

  实现：用 `System.Reflection.Metadata` 的 `PEReader` 读每个 DLL 的 `AssemblyReferences`，
  从 `-Roots` 出发沿 `Avalonia.*` 边做闭包。只跟 Avalonia 的边——框架分片（`System.*`）
  不是裁剪对象，把它们算进来只会产生噪音。

  返回**引用了但目录里没有**的程序集名（去重、排序；空数组 = 通过）。
#>
function Test-ReleaseAssemblyClosure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [string[]]$Roots
    )

    $directoryPath = (Resolve-Path -LiteralPath $Directory -ErrorAction Stop).Path

    $present = @{}
    foreach ($file in (Get-ChildItem -LiteralPath $directoryPath -Filter '*.dll' -File)) {
        $present[$file.BaseName.ToLowerInvariant()] = $true
    }

    $visited = @{}
    $unresolved = [System.Collections.Generic.List[string]]::new()
    $queue = [System.Collections.Generic.Queue[string]]::new()
    foreach ($root in @($Roots)) {
        if (-not [string]::IsNullOrWhiteSpace($root)) { $queue.Enqueue($root) }
    }

    while ($queue.Count -gt 0) {
        $name = $queue.Dequeue()
        $key = $name.ToLowerInvariant()
        if ($visited.ContainsKey($key)) { continue }
        $visited[$key] = $true

        $file = Join-Path $directoryPath "$name.dll"
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            $unresolved.Add($name)
            continue
        }

        $stream = $null
        $reader = $null
        try {
            $stream = [System.IO.File]::OpenRead($file)
            $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
            $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
            foreach ($handle in $metadata.AssemblyReferences) {
                $reference = $metadata.GetString($metadata.GetAssemblyReference($handle).Name)
                if ($reference -like 'Avalonia.*') { $queue.Enqueue($reference) }
            }
        }
        catch {
            # 读不出元数据的 DLL（原生库同名之类）不参与闭包，只跳过它自己。
        }
        finally {
            if ($reader) { $reader.Dispose() }
            if ($stream) { $stream.Dispose() }
        }
    }

    return @($unresolved | Where-Object { -not $present.ContainsKey($_.ToLowerInvariant()) } | Sort-Object -Unique)
}
