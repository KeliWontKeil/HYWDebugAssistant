namespace HYWDebugAssistant.Core.Composition;

/// <summary>
/// 网络命名：自动命名 <c>net-N</c> + 冲突处理（主文档 §6.3）。
/// </summary>
/// <remarks>
/// 名字默认弱化显示（UI），命名网络用于"标签化"多链路；匿名网络（直连）无名字。
/// 自动命名取当前未占用的最小序号，保证稳定且可读。
/// </remarks>
public static class NetworkNaming
{
    /// <summary>自动命名前缀。</summary>
    public const string AutoPrefix = "net-";

    /// <summary>生成一个未被占用的自动名字。</summary>
    /// <param name="isTaken">判定名字是否已被占用。</param>
    public static string NextAutoName(Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(isTaken);

        for (var index = 1; index < int.MaxValue; index++)
        {
            var candidate = AutoPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("无法生成可用的网络名（序号耗尽）");
    }

    /// <summary>名字是否合法（非空、不含空白、长度受限）。</summary>
    public static bool IsValidName(string? name, out string? reason)
    {
        reason = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            reason = "网络名不能为空";
            return false;
        }

        if (name.Length > 64)
        {
            reason = "网络名过长（上限 64 字符）";
            return false;
        }

        if (name.Any(char.IsWhiteSpace))
        {
            reason = "网络名不能包含空白字符";
            return false;
        }

        return true;
    }
}
