# 草案校验器：按 .dsh/drafts/view.schema.draft.json 检查 .dsh/drafts/view-declaration-examples/*.view.json
# 用途：证明"四模块的界面能被新声明面完整表达、且声明本身可机械校验"。
# 只读（不改任何文件）。这是**草案**校验器，不是框架读取器；落地时口径以 decisions.md 为准。

[CmdletBinding()]
param(
    [string]$SchemaPath = (Join-Path $PSScriptRoot 'view.schema.draft.json'),
    [string]$ExamplesDirectory = (Join-Path $PSScriptRoot 'view-declaration-examples')
)

$ErrorActionPreference = 'Stop'

$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json
$commonKeys = @($schema.commonProperties.PSObject.Properties.Name | Where-Object { $_ -ne 'doc' })
$containerKinds = @($schema.containers.PSObject.Properties.Name | Where-Object { $_ -ne 'doc' })
$atomKinds = @($schema.atoms.PSObject.Properties.Name | Where-Object { $_ -ne 'doc' })
$sourceKinds = @($schema.document.sources.item.kind.values)
$axisStrategies = @($schema.axes.strategies.PSObject.Properties.Name)

$script:failures = 0
$script:warnings = 0
$script:declaredCommands = @()

function Fail([string]$file, [string]$message) {
    $script:failures++
    Write-Host "  [FAIL] $message"
}

function Warn([string]$file, [string]$message) {
    $script:warnings++
    Write-Host "  [warn] $message"
}

function Test-Axis($axis, [string]$where, [string]$axisName, [bool]$mustBePixels) {
    if ($null -eq $axis) {
        # 省略轴 = 用 schema 的 defaultGeometry（C-14 建议 B）；只有 absolute 容器里必须显式给 pixels。
        if ($mustBePixels) { Fail $where "absolute 容器里的 $axisName 必须显式给 pixels（没有可用的默认）" }
        return
    }
    $strategies = @($axis.PSObject.Properties.Name | Where-Object { $_ -in $axisStrategies })
    if ($strategies.Count -ne 1) {
        Fail $where "$axisName 必须只给一条尺寸口径（adaptive / fill / pixels），当前 $(($strategies | Sort-Object) -join '/' )"
        return
    }
    foreach ($key in @($axis.PSObject.Properties.Name)) {
        if ($key -notin $axisStrategies -and $key -notin @('min', 'max')) { Fail $where "$axisName 出现未登记的键 '$key'" }
    }
    if ($mustBePixels -and $strategies[0] -ne 'pixels') {
        Fail $where "absolute 容器里的 $axisName 只能是 pixels，当前是 $($strategies[0])"
    }
    if ($strategies[0] -eq 'pixels' -and [double]$axis.pixels -lt 1) {
        Fail $where "$axisName.pixels 必须 ≥ 1，当前 $($axis.pixels)"
    }
    if ($null -ne $axis.min -and $null -ne $axis.max -and [double]$axis.min -gt [double]$axis.max) {
        Fail $where "$axisName 的 min > max 不成立"
    }
}

function Walk($node, [string]$path, [bool]$inGrid, [bool]$inAbsolute, [string]$file, $sourceNames) {
    if ($null -eq $node) { Fail $file "$path 是空节点"; return }

    $kind = $node.kind
    if (-not $kind) { Fail $file "$path 没有 kind"; return }

    $isContainer = $kind -in $containerKinds
    $isAtom = $kind -in $atomKinds
    if (-not ($isContainer -or $isAtom)) { Fail $file "$path 的 kind '$kind' 不在目录里"; return }

    $allowed = @($commonKeys)
    if ($isContainer) {
        $allowed += @($schema.containers.$kind.PSObject.Properties.Name | Where-Object { $_ -ne 'doc' })
    }
    else {
        $atom = $schema.atoms.$kind
        if ($atom.rendered -eq $false) { Warn $file "$path 声明了 rendered:false 的控件 '$kind'（框架尚未渲染 ⇒ 界面会缺这一块；允许声明、不拒绝加载 —— C-2 用户裁决 m04188）" }
        $allowed += @($atom.properties.PSObject.Properties.Name | Where-Object { $_ -ne 'doc' })
        $roleKeys = @($atom.roleKeys)
        $present = @($roleKeys | Where-Object { $null -ne $node.$_ })
        if ($present.Count -ne 1) {
            Fail $file "$path（$kind）必须且只能给一个绑定键（$($roleKeys -join '/')），当前 $(($present -join '/'))"
        }
        else {
            $value = $node.($present[0])
            if ($present[0] -eq 'command') {
                # 命令不在 sources[] 里：声明即入口（C-9），处理器由模块代码按名注册。
                # "声明了命令但模块没注册处理器"只能由宿主在加载时对账，声明文件本身校验不到。
                if ($value -notin $script:declaredCommands) { $script:declaredCommands += $value }
            }
            elseif ($value -notin $sourceNames) { Fail $file "$path 的 $($present[0]) = '$value' 在 sources[] 里不存在" }
        }
        $wrongRoles = @(($roleKeys + @('data', 'input', 'command')) | Sort-Object -Unique | Where-Object { $_ -notin $roleKeys -and $null -ne $node.$_ })
        foreach ($w in $wrongRoles) { Fail $file "$path（$kind）不允许绑定键 '$w'" }
    }

    foreach ($key in @($node.PSObject.Properties.Name)) {
        if ($key -notin $allowed) { Fail $file "$path 出现未登记的键 '$key'" }
    }

    Test-Axis $node.width $file "$path.width" $inAbsolute
    Test-Axis $node.height $file "$path.height" $inAbsolute

    if ($inGrid) {
        $hasCell = ($null -ne $node.column) -and ($null -ne $node.row)
        $hasAuto = ($node.auto -eq $true) -or ($null -ne $node.column) -or ($null -ne $node.row)
        if (-not $hasCell -and -not ($node.auto -eq $true)) {
            Fail $file "$path 在 grid 里没给位置（要么 column+row，要么 auto:true）"
        }
        if ($hasCell -and $node.auto -eq $true) { Fail $file "$path 同时写了 column/row 与 auto:true" }
    }
    elseif ($null -ne $node.column -or $null -ne $node.row -or $node.auto -eq $true) {
        Warn $file "$path 不在 grid 容器里却写了 column/row/auto（该条忽略）"
    }

    if (-not $isContainer) { return }

    $children = @($node.children)
    if ($children.Count -eq 0) { Fail $file "$path（$kind）必须有子节点" }

    $childGrid = ($kind -eq 'container' -and $node.layout -eq 'grid')
    $childAbsolute = ($kind -eq 'container' -and $node.layout -eq 'absolute')
    if ($null -eq $node.layout) { $childGrid = $false; $childAbsolute = $false }

    if ($kind -ne 'container' -and ($null -ne $node.columns -or $null -ne $node.rows)) {
        Warn $file "$path（$kind）声明了 columns/rows 但不是 grid 容器（该条忽略）"
    }
    if ($childGrid) {
        $autos = @($children | Where-Object { $_.auto -eq $true }).Count
        $explicits = @($children | Where-Object { $null -ne $_.column -or $null -ne $_.row }).Count
        if ($autos -gt 0 -and $explicits -gt 0) { Fail $file "$path 里混用了显式 column/row 与 auto:true" }
    }

    for ($i = 0; $i -lt $children.Count; $i++) {
        Walk $children[$i] "$path/$i" $childGrid $childAbsolute $file $sourceNames
    }
}

$files = Get-ChildItem -Path $ExamplesDirectory -Filter '*.view.json' | Sort-Object Name
if ($files.Count -eq 0) { Write-Host "没有找到示例声明文件" -ForegroundColor Yellow; exit 2 }

foreach ($file in $files) {
    Write-Host "== $($file.Name)"
    try {
        $doc = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json
    }
    catch {
        [void](Fail $file.Name "JSON 解析失败：$($_.Exception.Message)")
        continue
    }

    $topAllowed = @('version', 'frame', 'sources', 'views')
    foreach ($key in @($doc.PSObject.Properties.Name)) {
        if ($key -notin $topAllowed) { Fail $file.Name "顶层出现未登记的键 '$key'" }
    }

    if ($doc.version -ne $schema.document.version.const) { Fail $file.Name "version 必须是 $($schema.document.version.const)，当前 $($doc.version)" }

    # ---- 框架级段（m04188：极少量、非解释型）----
    $frame = $doc.frame
    if ($null -eq $frame) { Fail $file.Name 'frame 段缺失（框架级：窗口尺寸与网络标签默认位置）' }
    else {
        foreach ($key in @($frame.PSObject.Properties.Name)) {
            if ($key -notin @('window', 'ports', 'theme')) { Fail $file.Name "frame 出现未登记的键 '$key'" }
        }
        if ($null -ne $frame.theme) { Warn $file.Name 'frame.theme 是预留键（Q3：外观只走全局主题），本轮不应出现' }

        $window = $frame.window
        if ($null -eq $window) { Warn $file.Name 'frame.window 缺失 ⇒ 窗口走框架缺省 320×220' }
        else {
            $winAllowed = @($schema.window.PSObject.Properties.Name | Where-Object { $_ -ne 'doc' })
            foreach ($key in @($window.PSObject.Properties.Name)) {
                if ($key -notin $winAllowed) { Fail $file.Name "frame.window 出现未登记的键 '$key'" }
                elseif ([double]$window.$key -lt 1) { Fail $file.Name "frame.window.$key 必须 ≥ 1，当前 $($window.$key)" }
            }
            if ($null -ne $window.minWidth -and $null -ne $window.maxWidth -and [double]$window.minWidth -gt [double]$window.maxWidth) { Fail $file.Name 'frame.window 的 minWidth > maxWidth 不成立' }
            if ($null -ne $window.minHeight -and $null -ne $window.maxHeight -and [double]$window.minHeight -gt [double]$window.maxHeight) { Fail $file.Name 'frame.window 的 minHeight > maxHeight 不成立' }
        }

        $portNames = @()
        foreach ($port in @($frame.ports | Where-Object { $null -ne $_ })) {
            if (-not $port.name) { Fail $file.Name 'frame.ports[] 有一项没有 name'; continue }
            if ($port.name -in $portNames) { Fail $file.Name "frame.ports[] 的口名 '$($port.name)' 重复" }
            $portNames += $port.name
            foreach ($key in @($port.PSObject.Properties.Name)) {
                if ($key -notin @('name', 'edge', 'offset')) { Fail $file.Name "frame.ports[] 的 '$($port.name)' 出现未登记的键 '$key'（标记几何与可拖动性属框架固定项）" }
            }
            if ($null -ne $port.edge -and $port.edge -notin @('auto', 'left', 'right', 'top', 'bottom')) { Fail $file.Name "frame.ports[] 的 '$($port.name)' edge = '$($port.edge)' 不认识" }
            if ($null -ne $port.offset -and ([double]$port.offset -lt 0 -or [double]$port.offset -gt 1)) { Fail $file.Name "frame.ports[] 的 '$($port.name)' offset = $($port.offset) 不在 0..1" }
        }
    }

    $sourceNames = @()
    foreach ($source in @($doc.sources)) {
        if (-not $source.name) { Fail $file.Name 'sources[] 有一项没有 name'; continue }
        if ($source.name -in $sourceNames) { Fail $file.Name "sources[] 的名字 '$($source.name)' 重复" }
        $sourceNames += $source.name
        foreach ($key in @($source.PSObject.Properties.Name)) {
            if ($key -notin @('name', 'kind', 'valueType', 'unit')) { Fail $file.Name "sources[] 的 '$($source.name)' 出现未登记的键 '$key'" }
        }
        if ($null -ne $source.kind -and $source.kind -notin $sourceKinds) { Fail $file.Name "sources[] 的 '$($source.name)' kind = '$($source.kind)' 不认识" }
    }

    $views = @($doc.views)
    if ($views.Count -eq 0) { Fail $file.Name 'views[] 为空' }
    for ($i = 0; $i -lt $views.Count; $i++) {
        foreach ($key in @($views[$i].PSObject.Properties.Name)) {
            if ($key -notin @('title', 'root')) { Fail $file.Name "views[$i] 出现未登记的键 '$key'" }
        }
        if ($i -gt 0) { Warn $file.Name "views[$i] 的条目按 schema 会被忽略（框架只渲染第 0 个视图）" }
        Walk $views[$i].root "views[$i].root" $false $false $file.Name $sourceNames
    }

    $used = @()
    $text = Get-Content -Raw -Path $file.FullName
    foreach ($name in $sourceNames) {
        if ($text -match ('"(data|input|command)"\s*:\s*"' + [regex]::Escape($name) + '"')) { $used += $name }
    }
    $unused = @($sourceNames | Where-Object { $_ -notin $used })
    if ($unused.Count -gt 0) { Warn $file.Name "sources[] 里未被引用的条目：$($unused -join ', ')" }
}

Write-Host ''
Write-Host "命中硬伤 $script:failures 处，警告 $script:warnings 处。"
if ($script:failures -gt 0) { exit 1 }
exit 0
