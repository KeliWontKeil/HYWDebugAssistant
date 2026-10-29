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

    /// <summary>网络名长度上限（字符）。</summary>
    public const int MaxNameLength = 64;

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

        throw new InvalidOperationException(CoreText.Get("core.composite.network.exhausted"));
    }

    /// <summary>名字是否合法（非空、不含空白、长度受限）；不合法时给出**枚举原因**。</summary>
    /// <param name="name">候选名字（可空/空白）。</param>
    /// <param name="error">不合法原因（合法时 = <see cref="NetworkNameError.None"/>）。</param>
    /// <remarks>
    /// 原因用枚举而不是文案：文案要进文本表、可换语言，**不能当控制流或协议**。
    /// 调用方按枚举取文案（表：<c>core.network.name.*</c>）。
    /// </remarks>
    public static bool TryValidateName(string? name, out NetworkNameError error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = NetworkNameError.Empty;
            return false;
        }

        if (name.Length > MaxNameLength)
        {
            error = NetworkNameError.TooLong;
            return false;
        }

        if (name.Any(char.IsWhiteSpace))
        {
            error = NetworkNameError.ContainsWhitespace;
            return false;
        }

        error = NetworkNameError.None;
        return true;
    }
}

/// <summary>网络名校验失败的原因（枚举，**不是文案**）。</summary>
public enum NetworkNameError
{
    /// <summary>合法。</summary>
    None,

    /// <summary>空 / 全空白。</summary>
    Empty,

    /// <summary>超过长度上限。</summary>
    TooLong,

    /// <summary>含空白字符。</summary>
    ContainsWhitespace,
}

/// <summary>网络名校验文案的文本表键（表属 Core）。</summary>
public static class NetworkNameText
{
    /// <summary>空 / 全空白。</summary>
    public const string Empty = "core.network.name.empty";

    /// <summary>超过长度上限（<c>{0}</c> = <see cref="NetworkNaming.MaxNameLength"/>）。</summary>
    public const string TooLong = "core.network.name.tooLong";

    /// <summary>含空白字符。</summary>
    public const string ContainsWhitespace = "core.network.name.containsWhitespace";
}
