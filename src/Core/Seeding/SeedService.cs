using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Seeding;

/// <summary>
/// **出厂种子投放器**：把只读的 `resources\seeds\`（与目标目录**同构**）合并进程序目录。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>单文件</b>：目标文件不存在才复制 —— 设置文件（`settings\settings.json`）用它；
///     **不需要记录**：文件存在即证明；</item>
///   <item><b>叶子目录 = 一个投放单元</b>（不含子目录的目录）：目标目录不存在才**整目录**复制，
///     并把相对路径（如 `settings/appearance/themes/sakura`）交给调用方记进 `seeded`；</item>
///   <item><b>已经在 `seeded` 里的单元不再投放</b>：用户删除后由记录阻止复活（记录在设置里，本类不写设置文件）；</item>
///   <item><b>只做整单元投放</b>：目标已存在时**一个字都不改**（用户可能已经改过它）；</item>
///   <item><b>失败不阻断启动</b>：读不到 / 写不进 / 校验不过 ⇒ 一条 **Warning**，**不记 `seeded`**（下次启动重试）；
///     本次刚建出来的半成品目录会被撤掉（否则"目标已存在"会让重试永远不再发生）；</item>
///   <item><b>单元校验</b>（可选钩子）：投放器只负责"搬"，不知道"主题"；调用方可用
///     <c>validateUnit</c> 对一个刚投放的单元做业务校验（返回原因 ⇒ 视同失败）。</item>
/// </list>
/// </remarks>
public sealed class SeedService
{
    private readonly IIssueSink _issues;
    private readonly Func<string, string?>? _validateUnit;

    /// <summary>创建投放器。</summary>
    /// <param name="programRoot">投放目标根（生产 = 程序目录 `StoragePaths.AppRoot`）。</param>
    /// <param name="seedsRoot">出厂种子根（生产 = `StoragePaths.SeedsRoot`；不存在 = 没有出厂种子）。</param>
    /// <param name="issues">Issue 上报口（缺省丢弃）。</param>
    /// <param name="validateUnit">
    /// 可选：校验一个**刚投放**的单元（入参 = 相对程序目录的单元路径；返回 <see langword="null"/> = 可用，
    /// 否则返回原因）。校验不过 ⇒ 撤掉该次投放并视同失败。
    /// </param>
    public SeedService(
        string programRoot,
        string seedsRoot,
        IIssueSink? issues = null,
        Func<string, string?>? validateUnit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedsRoot);

        ProgramRoot = Path.GetFullPath(programRoot);
        SeedsRoot = Path.GetFullPath(seedsRoot);
        _issues = issues ?? NullIssueSink.Instance;
        _validateUnit = validateUnit;
    }

    /// <summary>投放目标根（生产 = 程序目录）。</summary>
    public string ProgramRoot { get; }

    /// <summary>出厂种子根（只读）。</summary>
    public string SeedsRoot { get; }

    /// <summary>
    /// 投放**单文件**种子（叶子目录里的文件不属于"单文件"，它们随单元整体复制）。
    /// </summary>
    /// <returns>本次真正复制的文件（相对程序目录的路径）；没有种子根 ⇒ 空表。</returns>
    public IReadOnlyList<string> SeedFiles()
    {
        var copied = new List<string>();

        if (!Directory.Exists(SeedsRoot))
        {
            // 没有出厂种子是合法状态（开发目录、删掉 resources\ 的安装）：静默跳过。
            return copied;
        }

        foreach (var (source, relative) in EnumerateSeedFiles(SeedsRoot, string.Empty))
        {
            var target = TargetPathOf(relative);
            if (File.Exists(target))
            {
                continue;   // 单文件：目标存在即视为已投放（不覆盖用户内容）
            }

            try
            {
                CreateParentDirectory(target);
                File.Copy(source, target, overwrite: false);
                copied.Add(relative);
            }
            catch (Exception ex) when (IsIoFailure(ex))
            {
                // 半成品文件会让"目标存在 ⇒ 不重投"永远成立 ⇒ 撤掉它，下次启动才会真的重试。
                DeleteFileIfExists(target);
                Report(CoreText.Get("core.seed.fileCopyFailed", relative, ex.Message));
            }
        }

        return copied;
    }

    /// <summary>
    /// 投放**叶子目录单元**（不含子目录的目录）。
    /// </summary>
    /// <param name="alreadySeeded">
    /// 已投放过的单元（相对程序目录；<see langword="null"/> = 无记录）。
    /// </param>
    /// <returns>本次真正投放的单元（相对程序目录的路径）。</returns>
    public IReadOnlyList<string> SeedUnits(IReadOnlyCollection<string>? alreadySeeded = null)
    {
        var seeded = new List<string>();

        if (!Directory.Exists(SeedsRoot))
        {
            return seeded;
        }

        var known = NormalizeUnits(alreadySeeded);

        foreach (var unit in EnumerateLeafUnits(SeedsRoot, string.Empty))
        {
            if (unit.Length == 0 || known.Contains(unit))
            {
                continue;   // 已记录 ⇒ 用户删了也不复活
            }

            var target = TargetPathOf(unit);
            if (Directory.Exists(target))
            {
                continue;   // 已存在 ⇒ 整单元跳过（不改写用户内容）
            }

            if (File.Exists(target))
            {
                Report(CoreText.Get("core.seed.unitTargetOccupied", unit));
                continue;
            }

            var source = SourcePathOf(unit);
            try
            {
                CopyDirectory(source, target);
            }
            catch (Exception ex) when (IsIoFailure(ex))
            {
                DeleteUnitDirectory(target);
                Report(CoreText.Get("core.seed.unitCopyFailed", unit, ex.Message));
                continue;
            }

            if (_validateUnit?.Invoke(unit) is { } reason)
            {
                // 本次刚建出来 >> 可以撤掉：不记 seeded 且目标不存在，下次启动才会真的重试。
                DeleteUnitDirectory(target);
                Report(CoreText.Get("core.seed.unitInvalid", unit, reason));
                continue;
            }

            seeded.Add(unit);
        }

        return seeded;
    }

    // ---------------------------------------------------------------- 枚举（种子树）

    /// <summary>枚举**非叶子目录里的文件**（叶子目录里的文件随单元整体复制，不在这里逐个复制）。</summary>
    private IEnumerable<(string Source, string Relative)> EnumerateSeedFiles(string directory, string relative)
    {
        var (subdirectories, files) = ReadEntries(directory, relative);
        if (subdirectories is null || files is null)
        {
            yield break;
        }

        if (subdirectories.Length == 0)
        {
            yield break;   // 叶子目录 = 投放单元
        }

        foreach (var file in files.OrderBy(path => path, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            yield return (file, relative.Length == 0 ? name : relative + "/" + name);
        }

        foreach (var subdirectory in subdirectories.OrderBy(path => path, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(subdirectory);
            var child = relative.Length == 0 ? name : relative + "/" + name;

            foreach (var item in EnumerateSeedFiles(subdirectory, child))
            {
                yield return item;
            }
        }
    }

    /// <summary>枚举**叶子目录单元**（深度优先、按名字序，保证投放顺序稳定）。</summary>
    private IEnumerable<string> EnumerateLeafUnits(string directory, string relative)
    {
        var (subdirectories, _) = ReadEntries(directory, relative);
        if (subdirectories is null)
        {
            yield break;
        }

        if (subdirectories.Length == 0)
        {
            if (relative.Length > 0)
            {
                yield return relative;
            }

            yield break;
        }

        foreach (var subdirectory in subdirectories.OrderBy(path => path, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(subdirectory);
            var child = relative.Length == 0 ? name : relative + "/" + name;

            foreach (var unit in EnumerateLeafUnits(subdirectory, child))
            {
                yield return unit;
            }
        }
    }

    /// <summary>读一个种子目录下的子目录与文件（读不到 ⇒ 记一条 Warning 并给 null）。</summary>
    private (string[]? Subdirectories, string[]? Files) ReadEntries(string directory, string relative)
    {
        try
        {
            return (Directory.GetDirectories(directory), Directory.GetFiles(directory));
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            Report(CoreText.Get("core.seed.enumerateFailed", Display(relative), ex.Message));
            return (null, null);
        }
    }

    // ---------------------------------------------------------------- 复制 / 清理

    private static void CopyDirectory(string source, string target)
    {
        _ = Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var subdirectory in Directory.GetDirectories(source))
        {
            CopyDirectory(subdirectory, Path.Combine(target, Path.GetFileName(subdirectory)));
        }
    }

    private static void CreateParentDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }
    }

    private static void DeleteUnitDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            // 清理失败不影响结论：下次启动会因"目标已存在"而跳过（值得知道，但不再追加一条 Issue）。
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            // 同上：清理失败不影响结论。
        }
    }

    // ---------------------------------------------------------------- 路径 / 判定

    private string TargetPathOf(string relative) =>
        Path.Combine(ProgramRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private string SourcePathOf(string relative) =>
        Path.Combine(SeedsRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static HashSet<string> NormalizeUnits(IReadOnlyCollection<string>? units)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (units is null)
        {
            return normalized;
        }

        foreach (var unit in units)
        {
            if (string.IsNullOrWhiteSpace(unit))
            {
                continue;
            }

            var value = unit.Trim().Replace('\\', '/').Trim('/');
            if (value.Length > 0)
            {
                _ = normalized.Add(value);
            }
        }

        return normalized;
    }

    private static bool IsIoFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static string Display(string relative) => relative.Length == 0 ? "." : relative;

    private void Report(string message) =>
        _issues.Report("seed", new IssueReport(
            IssueCategory.ConfigurationError,
            IssueSeverity.Warning,
            message));
}
