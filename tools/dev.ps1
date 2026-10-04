#Requires -Version 7.0
<#
.SYNOPSIS
    开发内环入口（分层验证）：单项目编译 / 单类测试 / 单测试项目 / 完整门禁 / 启动应用。
    工作流说明见 docs/implementation/07-dev-inner-loop.md（**唯一落点**）。

.DESCRIPTION
    分层验证（避免"每改一行就跑全量"）：
      T0  Build -Project <名字片段>   只编译该项目（含其下游）        1–3 s
      T1  Test  -Filter <过滤词>      只跑匹配的测试                  3–6 s
      T2  Test                        跑主测试项目（一个增量收尾）    4–8 s
      T3  Gate                       完整门禁（= build.ps1）         25–40 s

    本脚本还会消除两个已知摩擦：
      · 构建前**自动停掉 VS 的 Avalonia 设计器宿主**（否则它锁住输出 DLL，构建会刷 MSB3026 重试）；
      · 清空被污染的 `Platform` 环境变量（否则 MSB4126）。

.EXAMPLE
    .\tools\dev.ps1 -Mode Build -Project Core
    .\tools\dev.ps1 -Mode Test -Filter 端口标签
    .\tools\dev.ps1 -Mode Gate
    .\tools\dev.ps1 -Mode Run
#>
[CmdletBinding()]
param(
    [ValidateSet('Build', 'Test', 'Gate', 'Tree', 'Stop', 'Run')]
    [string]$Mode = 'Build',

    # Build：项目名片段（如 Core / Ui / FrameworkUi / Monitor）或 csproj 相对路径
    [string]$Project,

    # Test：xUnit 过滤词（方法名/类名片段；**只写片段**，脚本自己拼 `FullyQualifiedName~`）
    [string]$Filter,

    # Test：测试项目名片段（默认主测试项目）
    [string]$TestProject = 'HYWDebugAssistant.Tests',

    # Test：跑全部测试项目（不含许可审计）
    [switch]$AllTests,

    # 保持 Avalonia 设计器宿主不动（默认会先停掉它）
    [switch]$KeepDesignerHost,

    # **正在运行的应用会锁住输出 DLL**（构建报 MSB3026/MSB3027）：默认快速失败并提示，加此开关则直接停掉它
    [switch]$StopApp,

    # 应急：应用正在跑又不想关它时，只重编测试项目自身、不重编引用项目（App 输出不受影响 → 不撞文件锁）
    # 前提：被改动的源项目已先用 -Mode Build 编好
    [switch]$SkipAppRebuild,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 【2026-09 缺陷修复】**过滤词不许自带 `FullyQualifiedName~` 前缀** —— 脚本自己会拼一层。
# 曾把 `-Filter "FullyQualifiedName~X"` 原样再拼一次 ⇒ `FullyQualifiedName~FullyQualifiedName~X`
# ⇒ **匹配到 0 个用例，却报「测试通过」**（绿色门禁其实一个用例都没跑，是"假通过"）。
# 这里**就地剥掉**前缀并把过滤词归一化，用户怎么写都不至于静默空跑。
if ($Filter) {
    $Filter = $Filter -replace '^\s*FullyQualifiedName\s*~\s*', ''
    $Filter = $Filter.Trim()
}

function Clear-PollutedEnvironment {
    foreach ($name in @('Platform', 'Platform(x86)')) {
        if (Test-Path "Env:$name") {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
            Write-Host "[env] 已清除被污染的 $name" -ForegroundColor Yellow
        }
    }
}

function Stop-DesignerHost {
    if ($KeepDesignerHost) { return }

    try {
        $hosts = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and $_.CommandLine -like '*Avalonia.Designer.HostApp*' -and $_.CommandLine -like "*$root*" }

        foreach ($process in $hosts) {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
            Write-Host "[env] 已停止 Avalonia 设计器宿主 PID $($process.ProcessId)（VS 会按需重启预览器）" -ForegroundColor Yellow
        }

        if ($hosts) { Start-Sleep -Milliseconds 600 }
    }
    catch {
        # 拿不到进程信息时继续（构建失败会自己报出来）
    }
}

<#
.SYNOPSIS
    判断某项目是否（直接或间接）依赖 `App` 项目 —— 只有依赖它时，"应用正在运行"才可能锁住构建输出。
#>
function Test-ReferencesApp {
    param([string]$ProjectPath)

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) { return $true }   # 解决方案级构建：保守起见按"可能碰 App"处理

    $visited = @{}
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($ProjectPath)

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if ($visited.ContainsKey($current)) { continue }
        $visited[$current] = $true

        if (-not (Test-Path $current)) { continue }
        if ([System.IO.Path]::GetFileName($current) -like 'HYWDebugAssistant.App*') { return $true }

        $directory = Split-Path -Parent $current
        $references = Select-String -Path $current -Pattern 'ProjectReference\s+Include="([^"]+)"' -AllMatches |
            ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value }

        foreach ($reference in $references) {
            $queue.Enqueue([System.IO.Path]::GetFullPath((Join-Path $directory $reference)))
        }
    }

    return $false
}

<#
.SYNOPSIS
    检查"正在运行的应用"是否锁住输出（否则构建会出现 10 次 MSB3026 重试后才失败，白等 10+ 秒）。
.DESCRIPTION
    只在该构建**真的会碰 App 输出**时检查（目标项目不依赖 App 时，应用在跑也不影响）；
    默认**快速失败并给出可操作提示**；加 -StopApp 则直接停掉它（会丢失运行现场：实例/窗口布局）。
#>
function Assert-NoRunningApp {
    param([string]$TargetProject)

    if (-not (Test-ReferencesApp -ProjectPath $TargetProject)) {
        return   # T1/T2 常见情形：目标测试项目不依赖 App → 应用在跑也照样能编译
    }

    if ($SkipAppRebuild) {
        Write-Host "[env] 应用正在运行，但已指定 -SkipAppRebuild：只重编测试项目自身（改动过的项目需已 T0 编译）" -ForegroundColor Yellow
        return
    }

    try {
        $running = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.CommandLine -and $_.CommandLine -like "*$root*" -and
                ($_.Name -like 'HYWDebugAssistant.App*' -or $_.CommandLine -like '*HYWDebugAssistant.App.dll*') -and
                # **排除 VS 的 Avalonia 设计器宿主**（XAML 预览器）：它也要加载 App 程序集，
                # 但由 Stop-DesignerHost 处理；不排除就会误报"应用正在运行"（实测踩过）
                $_.CommandLine -notlike '*Avalonia.Designer.HostApp*'
            }

        if (-not $running) { return }

        if ($StopApp) {
            foreach ($process in $running) {
                Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
                Write-Host "[env] 已停止正在运行的应用 PID $($process.ProcessId)（运行现场已丢失）" -ForegroundColor Yellow
            }

            Start-Sleep -Milliseconds 800
            return
        }

        $ids = ($running | ForEach-Object { $_.ProcessId }) -join ', '
        Write-Host "[env] 检测到**应用正在运行**（PID $ids），它会锁住输出 DLL，构建必然失败（MSB3026 ×10）。" -ForegroundColor Red
        Write-Host "      请先关闭应用；或用 -StopApp 让本脚本停掉它（会丢失运行现场）。" -ForegroundColor Red
        throw '应用正在运行，构建被文件锁阻断'
    }
    catch [System.Management.Automation.RuntimeException] {
        throw
    }
    catch {
        # 查询失败时不阻断（让构建自己报错）
    }
}

function Resolve-ProjectPath {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return $null }

    if ($Name -like '*.csproj') {
        $direct = Join-Path $root $Name
        if (Test-Path $direct) { return $direct }
        throw "未找到项目：$Name"
    }

    $matches = Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Recurse -Filter *.csproj |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.BaseName -like "*$Name*" }

    if ($matches.Count -eq 0) { throw "未找到匹配的项目：$Name（可用 -Mode Tree 查看项目清单）" }

    # 消歧：优先"恰好等于 HYWDebugAssistant.<Name>"，其次"以 .<Name> 结尾且名字最短"（Ui 优于 FrameworkUi）
    $exact = $matches | Where-Object { $_.BaseName -eq "HYWDebugAssistant.$Name" }
    if ($exact.Count -eq 1) { return $exact[0].FullName }

    if ($matches.Count -gt 1) {
        $ordered = $matches | Sort-Object { $_.BaseName.Length }, { $_.BaseName }
        Write-Host "[dev] 名称匹配到多个项目，取最贴近的：$($ordered[0].BaseName)" -ForegroundColor Yellow
        return $ordered[0].FullName
    }

    return $matches[0].FullName
}

Clear-PollutedEnvironment

switch ($Mode) {
    'Tree' {
        Write-Host "解决方案中的项目：" -ForegroundColor Cyan
        Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Recurse -Filter *.csproj |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
            ForEach-Object { "  {0,-52} {1}" -f $_.BaseName, $_.FullName.Replace("$root\", '') }
        break
    }

    'Stop' {
        Stop-DesignerHost
        Write-Host "[dev] 完成" -ForegroundColor Green
        break
    }

    'Build' {
        Stop-DesignerHost
        $target = Resolve-ProjectPath -Name $Project
        Assert-NoRunningApp -TargetProject $target
        if ($target) {
            Write-Host "===== T0 编译：$([System.IO.Path]::GetFileName($target)) =====" -ForegroundColor Cyan
            dotnet build $target -c $Configuration --nologo -v q
        }
        else {
            Write-Host "===== T0 编译：整个解决方案 =====" -ForegroundColor Cyan
            dotnet build (Join-Path $root 'HYWDebugAssistant.slnx') -c $Configuration --nologo -v q
        }

        if ($LASTEXITCODE -ne 0) { throw '编译失败' }
        Write-Host "[dev] 编译通过" -ForegroundColor Green
        break
    }

    'Test' {
        Stop-DesignerHost

        $output = $null

        if ($AllTests) {
            Write-Host "===== T2 测试：全部测试项目（不含许可审计）=====" -ForegroundColor Cyan

            # 【F 裁定 · 2026-09】`VisualSnapshots` **必须单独进程跑**：`AvaloniaHeadlessPlatform` 是**进程级**配置，
            # 与其它测试项目并发时真实 Skia 渲染互相破坏 ⇒ **假失败**
            # （实测：`ColorPickerDialogSnapshotTests` 在 -AllTests 下红、单独跑 4/4 全绿）。
            # 故：先跑其余测试项目，**最后串行单独**跑它。
            # 项目清单**按目录发现**（目录名 = 项目名），新增测试项目零改动；`Benchmarks` 不是 xUnit 用例 ⇒ 排除。
            $testProjects = Get-ChildItem (Join-Path $root 'tests') -Directory |
                ForEach-Object { Join-Path $_.FullName "$($_.Name).csproj" } |
                Where-Object { Test-Path -LiteralPath $_ } |
                Where-Object { $_ -notmatch 'Benchmarks' }

            $snapshotProjects = @($testProjects | Where-Object { $_ -match 'VisualSnapshots' })
            $regularProjects = @($testProjects | Where-Object { $_ -notmatch 'VisualSnapshots' })

            $failed = @()
            # 逐个串行：常规项目先跑，快照项目**最后单独**跑（顺序即隔离，避免并发破坏渲染会话）
            foreach ($proj in @($regularProjects) + @($snapshotProjects)) {
                $label = [System.IO.Path]::GetFileNameWithoutExtension($proj)
                $isSnapshot = $proj -match 'VisualSnapshots'
                Write-Host "----- $label$(if ($isSnapshot) { '（单独进程：渲染快照）' }) -----" -ForegroundColor DarkCyan

                $projOut = dotnet test $proj -c $Configuration --nologo -v q 2>&1
                $projOut | Select-Object -Last 4 | ForEach-Object { Write-Host $_ }

                if ($LASTEXITCODE -ne 0) { $failed += $label }
            }

            if ($failed.Count -gt 0) { throw "测试失败：$($failed -join '、')" }
            Write-Host "[dev] 测试通过（含快照项目单独跑）" -ForegroundColor Green
            break
        }
        else {
            $testTarget = Resolve-ProjectPath -Name $TestProject
            Assert-NoRunningApp -TargetProject $testTarget
            Write-Host "===== $(if ($Filter) { 'T1' } else { 'T2' }) 测试：$([System.IO.Path]::GetFileName($testTarget))$(if ($Filter) { "（过滤：$Filter）" }) =====" -ForegroundColor Cyan

            # 应用在跑时用 -SkipAppRebuild：不重编引用项目（否则会写 App 输出 → 撞文件锁）。
            # 注意：这里**逐个分支写全命令**，不用数组拼接——PowerShell 的数组在表达式里会被拆成字符（P14 同类坑）。
            $output = if ($SkipAppRebuild) {
                if ($Filter) {
                    dotnet test $testTarget -c $Configuration --nologo -v q -p:BuildProjectReferences=false --filter "FullyQualifiedName~$Filter" 2>&1
                }
                else {
                    dotnet test $testTarget -c $Configuration --nologo -v q -p:BuildProjectReferences=false 2>&1
                }
            }
            else {
                if ($Filter) {
                    dotnet test $testTarget -c $Configuration --nologo -v q --filter "FullyQualifiedName~$Filter" 2>&1
                }
                else {
                    dotnet test $testTarget -c $Configuration --nologo -v q 2>&1
                }
            }
        }

        $output | Select-Object -Last 12 | ForEach-Object { Write-Host $_ }

        # 「过滤词没匹配到任何测试」**必须显式区分**，否则会变成"跑了 0 个用例的绿灯"：
        #   ① 旧 xUnit/VSTest 措辞（没有测试与…匹配）；
        #   ② **.NET 10 MTP 措辞**「总共 N 个测试文件与指定模式相匹配」——
        #      只说了"文件匹配上了"，**没说用例跑了几条**；若整段输出里**没有**用例汇总行，就是零用例空跑。
        # 实测踩过：`-Filter "FullyQualifiedName~X"` 被再拼一层前缀 ⇒ 0 用例 + 「测试通过」。
        $noMatch = $output | Where-Object { $_ -match '没有测试与|No test matches|no test matches' }
        $summary = $output | Where-Object { $_ -match '已通过!|失败!|通过:\s*\d+|Passed!|Failed!' }

        if ($noMatch -or -not $summary) {
            Write-Host '[dev] 过滤词未匹配到任何测试（换个过滤词，或去掉 -Filter 跑整个测试项目）' -ForegroundColor Yellow
            throw '没有用例被执行：过滤词未匹配（拒绝把"零用例"当作通过）'
        }

        if ($LASTEXITCODE -ne 0) { throw '测试失败' }
        Write-Host "[dev] 测试通过" -ForegroundColor Green
        break
    }

    'Gate' {
        Assert-NoRunningApp
        Write-Host "===== T3 完整门禁（还原 + 构建 + 模块包校验 + 许可审计 + 全部测试）=====" -ForegroundColor Cyan
        & (Join-Path $root 'build.ps1') -Configuration $Configuration -StopDesignerHost
        if ($LASTEXITCODE -ne 0) { throw '门禁未通过' }
        break
    }

    'Run' {
        Stop-DesignerHost
        Assert-NoRunningApp
        Write-Host "===== 构建并启动应用（手工验证）=====" -ForegroundColor Cyan
        dotnet build (Join-Path $root 'src\App\HYWDebugAssistant.App.csproj') -c $Configuration --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw '构建失败' }

        $exe = Join-Path $root "src\App\bin\$Configuration\net10.0\HYWDebugAssistant.App.exe"
        Write-Host "[dev] 启动：$exe" -ForegroundColor Green
        Start-Process -FilePath $exe
        break
    }

    default { break }
}
