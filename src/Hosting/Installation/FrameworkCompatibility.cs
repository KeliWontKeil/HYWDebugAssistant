namespace HYWDebugAssistant.Hosting.Installation;

/// <summary>
/// 框架兼容范围校验：模块声明其兼容的框架版本区间，加载时判定是否满足（ADR-0020/0028）。
/// </summary>
/// <remarks>
/// <para><b>当前框架版本</b>：<see cref="CurrentFrameworkVersion"/>（对应顶层基线 v1.0 的能力面）。</para>
/// <para>支持的区间语法（M1 最小集，AND 语义，空格分隔）：<c>&gt;=1.0.0 &lt;2.0.0</c>；
/// 运算符：<c>&gt;=</c> <c>&gt;</c> <c>&lt;=</c> <c>&lt;</c> <c>=</c>；空范围 = 不限制。</para>
/// <para>不满足 → **拒绝注册**并给出理由（**不静默降级**）。</para>
/// </remarks>
public static class FrameworkCompatibility
{
    /// <summary>当前框架版本（模块据此声明兼容范围）。</summary>
    public const string CurrentFrameworkVersion = "1.0.0";

    /// <summary>判定版本是否落在区间内。</summary>
    /// <param name="range">区间表达式（空 = 不限制）。</param>
    /// <param name="version">待判定版本。</param>
    /// <param name="error">解析/判定失败原因。</param>
    public static bool IsSatisfied(string? range, string version, out string? error)
    {
        error = null;

        if (!TryParseVersion(version, out var candidate))
        {
            error = $"框架版本号非法：'{version}'";
            return false;
        }

        if (string.IsNullOrWhiteSpace(range))
        {
            return true; // 未声明 = 不限制
        }

        foreach (var clause in range.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseClause(clause, out var op, out var bound))
            {
                error = $"兼容范围语法非法：'{clause}'（支持 >=、>、<=、<、=，如 >=1.0.0 <2.0.0）";
                return false;
            }

            var comparison = Compare(candidate, bound);
            var satisfied = op switch
            {
                ">=" => comparison >= 0,
                ">" => comparison > 0,
                "<=" => comparison <= 0,
                "<" => comparison < 0,
                "=" => comparison == 0,
                _ => false,
            };

            if (!satisfied)
            {
                error = $"框架版本 {version} 不满足兼容范围 '{range}'";
                return false;
            }
        }

        return true;
    }

    private static bool TryParseClause(string clause, out string op, out (int Major, int Minor, int Patch) bound)
    {
        op = string.Empty;
        bound = default;

        foreach (var candidateOp in (string[])[">=", "<=", ">", "<", "="])
        {
            if (clause.StartsWith(candidateOp, StringComparison.Ordinal))
            {
                op = candidateOp;
                return TryParseVersion(clause[candidateOp.Length..], out bound);
            }
        }

        // 无运算符 → 视为精确匹配
        op = "=";
        return TryParseVersion(clause, out bound);
    }

    private static bool TryParseVersion(string text, out (int Major, int Minor, int Patch) version)
    {
        version = default;

        var parts = text.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var patch))
        {
            return false;
        }

        version = (major, minor, patch);
        return true;
    }

    private static int Compare((int Major, int Minor, int Patch) left, (int Major, int Minor, int Patch) right)
    {
        var result = left.Major.CompareTo(right.Major);
        if (result != 0)
        {
            return result;
        }

        result = left.Minor.CompareTo(right.Minor);
        return result != 0 ? result : left.Patch.CompareTo(right.Patch);
    }
}
