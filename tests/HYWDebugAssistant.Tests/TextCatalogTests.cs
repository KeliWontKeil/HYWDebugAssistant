using System.Globalization;
using System.Reflection;
using System.Text;
using HYWDebugAssistant.Infrastructure.Storage;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 文本表机制的机制级用例：根解析、表文件合并、坏表降级、回退链、缺失标记、`Apply` 不动 `CurrentCulture`。
/// </summary>
/// <remarks>
/// <b>为什么这些要测</b>：它们全是"看不见"的机制 —— 表是磁盘上的数据文件、语言是进程级 UI 文化，
/// 出错时的表现只有两种：界面上露出 <c>⟦key⟧</c>，或数值/日期格式被顺手改掉。两者都不会报错。
/// 用例表由 <see cref="TextCatalogTestRoot"/> 造在临时目录里（布局与出厂一致）。
/// </remarks>
[Collection(nameof(TextCatalogTests))]
public class TextCatalogTests : IDisposable
{
    private static readonly Assembly Owner = typeof(TextCatalogTests).Assembly;

    private readonly TextCatalogTestRoot _tables = TextCatalogTestRoot.CreateWithProbeTables();

    /// <inheritdoc />
    public void Dispose()
    {
        _tables.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 回退链_精确语言与语言中立与中文简体()
    {
        // ① 精确：本语言的表
        TextCatalog.Apply(TextLanguages.ZhHans);
        TextCatalog.Get(Owner, "probe.base.label").ShouldBe(_tables.Entries["probe.base.label"]);

        // ② 语言中立：`zh-Hans` 里没有、`zh` 表里有
        TextCatalog.Apply("zh");
        TextCatalog.Get(Owner, "probe.neutral.label").ShouldBe(_tables.Entries["probe.neutral.label"]);

        // ③ 缺表：没有这门语言的表 ⇒ 回落中文简体（不是空白，也不是英文）
        TextCatalog.Apply("fr-FR");
        CultureInfo.CurrentUICulture.Name.ShouldBe(TextLanguages.ZhHans);
        TextCatalog.Get(Owner, "probe.base.label").ShouldBe(_tables.Entries["probe.base.label"]);
    }

    [Fact]
    public void 缺失键_应返回可见缺失标记()
    {
        TextCatalog.Apply(TextLanguages.ZhHans);

        TextCatalog.Get(Owner, "probe.absent.message").ShouldBe(TextCatalog.MissingValue("probe.absent.message"));
        TextCatalog.TryGet(Owner, "probe.absent.message", out _).ShouldBeFalse();
    }

    [Fact]
    public void 基表与分片_应按文件名序合并()
    {
        TextCatalog.Apply(TextLanguages.ZhHans);

        TextCatalog.Get(Owner, "probe.base.label").ShouldBe(_tables.Entries["probe.base.label"]);
        TextCatalog.Get(Owner, "probe.shard.label").ShouldBe(_tables.Entries["probe.shard.label"], "分片表里的键要合并进来");
    }

    /// <summary>
    /// **默认根 == `Path.Combine(StoragePaths.ResourcesRoot, "i18n")`**（契约 §2.5 的唯一来源）。
    /// </summary>
    /// <remarks>
    /// `Sdk` 与 `Infrastructure` 零依赖、不能共享常量 ⇒ 两处字面量必须被一条用例钉在一起：
    /// 在 `<BaseDirectory>/resources/i18n/<拥有者>/` 放一张表，默认解析必须命中；目录名若有一边写错，这里就红。
    /// </remarks>
    [Fact]
    public void 默认根_应与StoragePaths的文本包根一致()
    {
        StoragePaths.TextPacksDirectoryName.ShouldBe("i18n", "文本包子目录名是审计登记在案的字面量");
        StoragePaths.TextPacksRoot.ShouldBe(
            Path.Combine(StoragePaths.ResourcesRoot, "i18n"),
            "TextPacksRoot 必须由 ResourcesRoot + i18n 组成");

        var packsRoot = StoragePaths.TextPacksRoot;   // = <BaseDirectory>/resources/i18n
        var ownerDirectory = Path.Combine(packsRoot, TextCatalogTestRoot.OwnerName);
        var tablePath = Path.Combine(ownerDirectory, "zh-Hans.json");
        var createdPacksRoot = !Directory.Exists(packsRoot);

        try
        {
            _ = Directory.CreateDirectory(ownerDirectory);
            File.WriteAllText(tablePath, """
                {
                  "language": "zh-Hans",
                  "entries": {
                    "probe.defaultRoot.label": "默认根表"
                  }
                }
                """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            TextCatalog.UseRoot(null);   // 清掉显式根，让默认解析（BaseDirectory/resources/i18n）生效
            TextCatalog.Apply(TextLanguages.ZhHans);

            var defaultRootText = "默认根表";
            TextCatalog.Get(Owner, "probe.defaultRoot.label")
                .ShouldBe(defaultRootText, "输出目录下的 resources/i18n 必须被默认解析命中（与 StoragePaths 同值）");
        }
        finally
        {
            TextCatalog.UseRoot(null);

            try
            {
                if (createdPacksRoot && Directory.Exists(packsRoot))
                {
                    Directory.Delete(packsRoot, recursive: true);
                }
                else
                {
                    if (File.Exists(tablePath)) { File.Delete(tablePath); }
                    if (Directory.Exists(ownerDirectory)
                        && !Directory.EnumerateFileSystemEntries(ownerDirectory).Any())
                    {
                        Directory.Delete(ownerDirectory);
                    }
                }

                // 本用例可能把 <输出>/resources 建成空壳：删掉，别让后续用例的默认根改道
                if (Directory.Exists(StoragePaths.ResourcesRoot)
                    && !Directory.EnumerateFileSystemEntries(StoragePaths.ResourcesRoot).Any())
                {
                    Directory.Delete(StoragePaths.ResourcesRoot);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// **两套根不串**：框架拥有者只查框架根（`<BaseDirectory>/resources/i18n` / 显式根 / 仓库根），
    /// 程序集旁那份 `<程序集目录>/i18n/<拥有者>/` 不再算数（那是模块的落点，且必须住在 `resources/` 下）。
    /// </summary>
    /// <remarks>
    /// 用测试自身的程序集（框架拥有者：旁边没有 `module.json`），在它旁边造一份旧式 `i18n/<程序集简名>/`；
    /// 显式根指向一个**没有该拥有者目录**的临时根 ⇒ 唯一的候选就是程序集旁那份，它**不该**被命 中。
    /// 用完即删。
    /// </remarks>
    [Fact]
    public void 程序集旁的i18n_框架拥有者不得命中()
    {
        var ownerName = TextCatalogTestRoot.OwnerName;
        var assemblyDirectory = Path.GetDirectoryName(typeof(TextCatalogTests).Assembly.Location);
        assemblyDirectory.ShouldNotBeNullOrWhiteSpace("测试程序集应有磁盘位置（否则这条边界不可测）");

        var legacyRoot = Path.Combine(assemblyDirectory!, "i18n");
        var packageRoot = Path.Combine(legacyRoot, ownerName);
        var tablePath = Path.Combine(packageRoot, "zh-Hans.json");

        try
        {
            _ = Directory.CreateDirectory(packageRoot);
            File.WriteAllText(tablePath, """
                {
                  "language": "zh-Hans",
                  "entries": {
                    "probe.package.label": "程序集旁的表"
                  }
                }
                """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            using var emptyRoot = TextCatalogTestRoot.CreateEmpty();   // 第一级没有该拥有者目录
            TextCatalog.UseRoot(emptyRoot.Root);
            TextCatalog.Apply(TextLanguages.ZhHans);

            TextCatalog.Get(Owner, "probe.package.label")
                .ShouldBe(
                    TextCatalog.MissingValue("probe.package.label"),
                    "框架拥有者只查框架根：旧式 `<程序集目录>/i18n/<拥有者>/` 不再命中");
        }
        finally
        {
            TextCatalog.UseRoot(null);

            try
            {
                if (File.Exists(tablePath)) { File.Delete(tablePath); }
                if (Directory.Exists(packageRoot)
                    && !Directory.EnumerateFileSystemEntries(packageRoot).Any())
                {
                    Directory.Delete(packageRoot);
                }

                if (Directory.Exists(legacyRoot)
                    && !Directory.EnumerateFileSystemEntries(legacyRoot).Any())
                {
                    Directory.Delete(legacyRoot);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// **整张表目录缺失 ⇒ 每个拥有者只报一次**（契约 §2.4）；缺键不报。
    /// </summary>
    [Fact]
    public void 缺表目录_每个拥有者只报一次()
    {
        using var empty = TextCatalogTestRoot.CreateEmpty();   // 有根、没有拥有者目录

        var diagnostics = new List<string>();
        TextCatalog.Diagnostics = diagnostics.Add;

        try
        {
            TextCatalog.UseRoot(empty.Root);
            TextCatalog.Apply(TextLanguages.ZhHans);

            TextCatalog.Get(Owner, "probe.absent.first").ShouldBe(TextCatalog.MissingValue("probe.absent.first"));
            TextCatalog.Get(Owner, "probe.absent.second").ShouldBe(TextCatalog.MissingValue("probe.absent.second"));
            TextCatalog.Get(Owner, "probe.absent.third").ShouldBe(TextCatalog.MissingValue("probe.absent.third"));
        }
        finally
        {
            TextCatalog.Diagnostics = null;
        }

        diagnostics.Count.ShouldBe(1, "整张表目录缺失按拥有者去重：查多少次键都只报一条");
        diagnostics[0].ShouldContain(TextCatalogTestRoot.OwnerName, Case.Insensitive);
        diagnostics[0].ShouldContain(empty.OwnerDirectory, Case.Insensitive);
    }

    [Fact]
    public void 坏文件_应跳过该文件并报诊断()
    {
        _tables.WriteTable("zh-Hans.broken.json", """{ "entries": """);
        _tables.Use();   // 同一根里多了文件：重设根以清掉按旧文件集算出的缓存

        var diagnostics = new List<string>();
        TextCatalog.Diagnostics = diagnostics.Add;

        try
        {
            TextCatalog.Apply(TextLanguages.ZhHans);

            TextCatalog.Get(Owner, "probe.base.label").ShouldBe(_tables.Entries["probe.base.label"], "坏分片不能拖垮同目录的基表");
        }
        finally
        {
            TextCatalog.Diagnostics = null;
        }

        diagnostics.ShouldContain(
            message => message.Contains("zh-Hans.broken.json", StringComparison.Ordinal),
            "坏文件必须留一条可定位的诊断（组合根会把它转成 Issue）");
    }

    [Fact]
    public void 分片重复键_先者胜_并整份跳过该分片()
    {
        _tables.WriteTable("zh-Hans.zz.json", """
            {
              "language": "zh-Hans",
              "entries": {
                "probe.duplicate.label": "分片后到",
                "probe.shardExtra.label": "分片独有"
              }
            }
            """);
        _tables.Use();

        var diagnostics = new List<string>();
        TextCatalog.Diagnostics = diagnostics.Add;

        try
        {
            TextCatalog.Apply(TextLanguages.ZhHans);

            TextCatalog.Get(Owner, "probe.duplicate.label").ShouldBe(_tables.Entries["probe.duplicate.label"], "重复键取先者：基础表排在最前");
            TextCatalog.TryGet(Owner, "probe.shardExtra.label", out _)
                .ShouldBeFalse("分片出现重复键 ⇒ 整份跳过（契约 §2.4：跳过该文件），不只丢那一个键");
        }
        finally
        {
            TextCatalog.Diagnostics = null;
        }

        diagnostics.ShouldContain(message => message.Contains("zh-Hans.zz.json", StringComparison.Ordinal));
        diagnostics.ShouldContain(message => message.Contains("probe.duplicate.label", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_不得改动CurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            TextCatalog.Apply(TextLanguages.ZhHans);

            CultureInfo.CurrentCulture.Name.ShouldBe("en-US", "界面语言只切 UI 文化，数值/日期格式不动");
            CultureInfo.CurrentUICulture.Name.ShouldBe(TextLanguages.ZhHans);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
        }
    }

    [Fact]
    public void 语言切换_应触发事件且同语言重复应用不触发()
    {
        var events = 0;

        void Handler(object? sender, EventArgs args) => events++;

        TextCatalog.Apply("zh");   // 先落到一个确定不同的语言，事件计数才有确定的起点
        TextCatalog.LanguageChanged += Handler;

        try
        {
            TextCatalog.Apply(TextLanguages.ZhHans);
            TextCatalog.Apply(TextLanguages.ZhHans);
        }
        finally
        {
            TextCatalog.LanguageChanged -= Handler;
        }

        events.ShouldBe(1, "语言真的变了才发事件；同一个语言重复应用是幂等的");
    }

    /// <summary>
    /// **产品程序集的表必须真的能被解析**（表是"编译通过却带病出货"的高发区）。
    /// </summary>
    /// <remarks>只断言"键解析得到、且不是缺失标记"，不复制表里的文案（那是表自己的事）。</remarks>
    [Fact]
    public void 表_产品程序集里的界面键应可解析()
    {
        TextCatalog.UseRoot(null);   // 回到默认根：仓库 resources/i18n（或应用输出里的那份）
        TextCatalog.Apply(TextLanguages.ZhHans);

        var frameworkUi = typeof(HYWDebugAssistant.FrameworkUi.ViewModels.SettingsPanelViewModel).Assembly;
        var ui = typeof(HYWDebugAssistant.Ui.Hotkeys.HotkeyCatalog).Assembly;

        TextCatalog.TryGet(frameworkUi, "settings.general.language.label", out var settingsLabel).ShouldBeTrue();
        settingsLabel.ShouldNotBeNullOrWhiteSpace();
        TextCatalog.IsMissing(settingsLabel).ShouldBeFalse();

        TextCatalog.TryGet(ui, "hotkey.action.openCatalog.label", out var hotkeyLabel).ShouldBeTrue();
        hotkeyLabel.ShouldNotBeNullOrWhiteSpace();
        TextCatalog.IsMissing(hotkeyLabel).ShouldBeFalse();
    }

}
[CollectionDefinition(nameof(TextCatalogTests), DisableParallelization = true)]
public sealed class TextCatalogSerial
{
}
