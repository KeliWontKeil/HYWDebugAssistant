using HYWDebugAssistant.Infrastructure.Storage;
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Resources;

/// <summary>
/// 资源布局的机制级用例：类别名、默认根、显式根、拥有者目录、按类别的缓存代号。
/// </summary>
/// <remarks>
/// 这些是文本与图标**共用**的规则；它们出错时界面只会"少一段文案 / 少一个图标"，
/// 所以规则本身要被钉住，而不是靠某个界面用例间接覆盖。
/// </remarks>
[Collection(nameof(TextCatalogTests))]
public sealed class ResourceLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "hyw-resource-layout-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        ResourceLayout.UseRoot(ResourceKinds.Icons, null);
        TextCatalog.UseRoot(null);

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 类别名_应与文本包根一致()
    {
        ResourceKinds.Text.ShouldBe("i18n");
        ResourceKinds.Icons.ShouldBe("icons");

        StoragePaths.TextPacksDirectoryName.ShouldBe(
            ResourceKinds.Text,
            "文本包目录名在 Infrastructure 与 Sdk 两处出现，必须同值");
    }

    [Fact]
    public void 默认根_应指向仓库resources下的类别目录()
    {
        TextCatalog.UseRoot(null);
        ResourceLayout.UseRoot(ResourceKinds.Icons, null);

        var textRoot = ResourceLayout.Root(ResourceKinds.Text);
        textRoot.ShouldNotBeNull();
        textRoot!.ShouldEndWith(Path.Combine(ResourceLayout.ResourcesDirectoryName, ResourceKinds.Text), Case.Insensitive);

        var iconRoot = ResourceLayout.Root(ResourceKinds.Icons);
        iconRoot.ShouldNotBeNull();
        iconRoot!.ShouldEndWith(Path.Combine(ResourceLayout.ResourcesDirectoryName, ResourceKinds.Icons), Case.Insensitive);
    }

    [Fact]
    public void UseRoot_应覆盖根_并按类别各自递增代号()
    {
        var textGeneration = ResourceLayout.Generation(ResourceKinds.Text);
        var iconGeneration = ResourceLayout.Generation(ResourceKinds.Icons);

        var textRoot = Path.Combine(_root, ResourceKinds.Text);
        _ = Directory.CreateDirectory(textRoot);

        ResourceLayout.UseRoot(ResourceKinds.Text, textRoot);

        var resolved = ResourceLayout.Root(ResourceKinds.Text);
        resolved.ShouldNotBeNull();
        Path.GetFullPath(resolved!).ShouldBe(Path.GetFullPath(textRoot));

        ResourceLayout.Generation(ResourceKinds.Text).ShouldBe(textGeneration + 1);
        ResourceLayout.Generation(ResourceKinds.Icons).ShouldBe(
            iconGeneration,
            "给文本类别换根不得动图标类别的缓存代号（两类表各自失效）");
    }

    [Fact]
    public void 拥有者目录_框架根命中时优先()
    {
        var owner = typeof(ResourceLayoutTests).Assembly;
        var ownerName = owner.GetName().Name!;

        var iconsRoot = Path.Combine(_root, ResourceKinds.Icons);
        var ownerDirectory = Path.Combine(iconsRoot, ownerName);
        _ = Directory.CreateDirectory(ownerDirectory);

        ResourceLayout.UseRoot(ResourceKinds.Icons, iconsRoot);

        var resolved = ResourceLayout.Directory(ResourceKinds.Icons, owner);
        resolved.ShouldNotBeNull();
        Path.GetFullPath(resolved!).ShouldBe(Path.GetFullPath(ownerDirectory));
    }
}
