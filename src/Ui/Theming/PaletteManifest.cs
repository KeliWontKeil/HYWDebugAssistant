using System.Text.Json;

using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Ui.Theming;

/// <summary>**色板契约里的一支色**（一行）。</summary>
/// <param name="Key">token 名（与色板 XAML 的 <c>x:Key</c> 逐字相同）。</param>
/// <param name="Title">面板上的中文友好名（空 ⇒ 回落到 <paramref name="Key"/>）。</param>
/// <param name="Group">面板里的分组 id。</param>
/// <param name="Purpose">用途（一行；面板的悬停提示与文档共用它）。</param>
/// <param name="Roles">这一支承担的**角色词**（可为空；声明面与运行期共用一份词表）。</param>
/// <param name="Derive">派生规则（<see langword="null"/> = 不派生）。</param>
/// <param name="Surface">是否属于可受“控件表面不透明度”影响的背景/表面 token。</param>
public sealed record PaletteToken(
    string Key,
    string Title,
    string Group,
    string Purpose,
    IReadOnlyList<string> Roles,
    PaletteDeriveRule? Derive,
    bool Surface = false)
{
    /// <summary>它是不是"跟随别的支"（是 ⇒ 面板显示"跟随 &lt;源&gt;"，用户仍可显式改它）。</summary>
    public bool IsDerived => Derive is { Mode: not PaletteDeriveMode.None };
}

/// <summary>色板契约里的一个分组（<paramref name="Title"/> = 面板上的标题）。</summary>
/// <param name="Id">分组 id。</param>
/// <param name="Title">显示名。</param>
/// <param name="Expanded">面板里默认是否展开。</param>
public sealed record PaletteGroup(string Id, string Title, bool Expanded);

/// <summary>
/// **色板契约**（`Styles/palette.json`，随程序集嵌入）—— "有哪几支、谁跟随谁、谁承担哪个角色"的唯一登记处。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>三个消费者</b>：① 设置面板按它生成「主题色板」的行；② <c>tools/audit-colors.ps1</c>
///     校验它与色板 XAML 一一对应、**派生规则与内置值一致**、角色词与词表一一对应；
///     ③ 主题应用器按它解析（覆盖 → 派生 → 内置）—— 于是应用器**不认识任何具体 token 名**；</item>
///   <item><b>它不含色值</b>：值只住在色板 XAML 里 —— 值写两处必然漂，而"有哪几支、谁跟随谁"写两处由审计守住；</item>
///   <item><b>解析失败要响</b>：契约是随程序集出货的资源，读不到 / 解析不了 / 规则种类不认识，
///     都是**构建产物坏了**（不是"运行时缺个配置"），因此这里**抛**而不是回落空表 ——
///     空表会让面板静默变成空白、让应用器静默不派生。</item>
/// </list>
/// </remarks>
public static class PaletteManifest
{
    /// <summary>清单的逻辑资源名（在 `Ui.csproj` 里显式指定，不靠"猜资源名的拼法"）。</summary>
    public const string ResourceName = "HYWDebugAssistant.Ui.Styles.palette.json";

    /// <summary>全部 token（按契约里的顺序）。</summary>
    public static IReadOnlyList<PaletteToken> Tokens { get; }

    /// <summary>全部分组（按契约里的顺序 = 面板上的顺序）。</summary>
    public static IReadOnlyList<PaletteGroup> Groups { get; }

    /// <summary>按分组切好的 token（面板直接用它建分区）。</summary>
    public static IReadOnlyList<(PaletteGroup Group, IReadOnlyList<PaletteToken> Tokens)> Sections { get; }

    /// <summary>全部角色词（<c>角色词 → token</c> 的键集合；与 `ViewInkRoles` 双向一一对应，由审计钉住）。</summary>
    public static IReadOnlyList<string> Roles { get; }

    /// <summary>角色词 → token 名（**唯一一张映射表**：声明面解析与运行期角色 setter 都走它）。</summary>
    private static readonly Dictionary<string, string> RoleKeys = new(StringComparer.Ordinal);

    static PaletteManifest()
    {
        using var stream = typeof(PaletteManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                TextCatalog.Get(
                    typeof(PaletteManifest).Assembly,
                    "ui.palette.resourceMissing",
                    ResourceName));

        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var groups = new List<PaletteGroup>();
        foreach (var item in root.GetProperty("groups").EnumerateArray())
        {
            groups.Add(new PaletteGroup(Text(item, "id"), Text(item, "title"), Flag(item, "expanded")));
        }

        var tokens = new List<PaletteToken>();
        var roles = new List<string>();

        foreach (var item in root.GetProperty("tokens").EnumerateArray())
        {
            var key = Text(item, "key");
            var roleList = new List<string>();

            if (item.TryGetProperty("roles", out var rolesNode) && rolesNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in rolesNode.EnumerateArray())
                {
                    var word = role.GetString() ?? string.Empty;

                    if (word.Length > 0)
                    {
                        roleList.Add(word);
                        roles.Add(word);
                        RoleKeys[word] = key;   // 一个词只承担一支（重复由审计拦）
                    }
                }
            }

            tokens.Add(new PaletteToken(
                key,
                Text(item, "title") is { Length: > 0 } title ? title : key,
                Text(item, "group"),
                Text(item, "purpose"),
                roleList,
                Derive(item),
                Flag(item, "surface")));
        }

        Groups = groups;
        Tokens = tokens;
        Roles = roles;

        var sections = new List<(PaletteGroup, IReadOnlyList<PaletteToken>)>();
        foreach (var group in groups)
        {
            var rows = tokens.Where(t => string.Equals(t.Group, group.Id, StringComparison.Ordinal)).ToList();

            if (rows.Count > 0)
            {
                sections.Add((group, rows));
            }
        }

        Sections = sections;
    }

    /// <summary>按 token 名取一行（没有 ⇒ <see langword="null"/>）。</summary>
    /// <param name="key">token 名。</param>
    public static PaletteToken? Find(string key) =>
        Tokens.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    /// <summary>取一个角色词对应的 token 名（没登记 ⇒ <see langword="false"/>）。</summary>
    /// <param name="role">角色词。</param>
    /// <param name="key">token 名。</param>
    public static bool TryRoleKey(string? role, out string key)
    {
        key = string.Empty;

        return !string.IsNullOrEmpty(role) && RoleKeys.TryGetValue(role, out key!);
    }

    private static PaletteDeriveRule? Derive(JsonElement token)
    {
        if (!token.TryGetProperty("derive", out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var source = Text(node, "from");
        var mode = Text(node, "mode") switch
        {
            "tint" => PaletteDeriveMode.Tint,
            "darken" => PaletteDeriveMode.Darken,
            "shift" => PaletteDeriveMode.Shift,
            "none" => PaletteDeriveMode.None,
            var other => throw new InvalidOperationException(
                TextCatalog.Get(
                    typeof(PaletteManifest).Assembly,
                    "ui.palette.derivationUnknown",
                    Text(token, "key"),
                    other)),
        };

        var amount = node.TryGetProperty("amount", out var amountNode) && amountNode.TryGetDouble(out var parsed)
            ? parsed
            : 0;

        if (source.Length == 0)
        {
            throw new InvalidOperationException(TextCatalog.Get(
                typeof(PaletteManifest).Assembly,
                "ui.palette.derivationMissingFrom",
                Text(token, "key")));
        }

        return new PaletteDeriveRule(mode, source, amount);
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
