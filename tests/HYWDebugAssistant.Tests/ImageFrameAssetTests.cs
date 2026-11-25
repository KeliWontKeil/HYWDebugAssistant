using Avalonia;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Ui.ModuleViews;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 图片显示器的控件图片状态与模块壁纸的真实隔离/持久化测试。
/// 使用真实 RuntimeServices + ModuleViewImageStore，不经过假存储。
/// </summary>
public class ImageFrameAssetTests
{
    // 只用于格式识别与往返比对的最小文件头。
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static readonly byte[] Jpeg =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    private static readonly byte[] Gif =
        [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00];

    [Fact]
    public void 写控件图片_不改变模块壁纸()
    {
        using var runtime = new ModuleRuntime();
        CoreRuntimeTests.AddModule(runtime, "img", "sender", typeof(CoreRuntimeTests.SenderModule));
        var services = new RuntimeServices(runtime);
        var wallpaper = Png;
        var image = Jpeg;

        services.SetModuleWallpaper("img", wallpaper).Succeeded.ShouldBeTrue();
        var store = new ModuleViewImageStore("img", services, services);

        store.Write(image, new Rect(0.1, 0.2, 0.5, 0.6)).ShouldBeTrue();

        services.GetModuleWallpaper("img")!.Png.SequenceEqual(wallpaper).ShouldBeTrue();
        var stored = services.GetModuleViewImage("img");
        stored.ShouldNotBeNull();
        stored!.Bytes.SequenceEqual(image).ShouldBeTrue();
        stored.Crop.ShouldBe(new ViewImageCrop(0.1, 0.2, 0.5, 0.6));
        store.Read()!.Value.Bytes.SequenceEqual(image).ShouldBeTrue();
    }

    [Fact]
    public void 清除壁纸_应使实例视图的壁纸版本归零()
    {
        using var runtime = new ModuleRuntime();
        CoreRuntimeTests.AddModule(runtime, "img", "sender", typeof(CoreRuntimeTests.SenderModule));
        var services = new RuntimeServices(runtime);

        services.SetModuleWallpaper("img", Png).Succeeded.ShouldBeTrue();
        var withWallpaper = services.GetInstances().Single(instance => instance.InstanceId == "img");
        withWallpaper.WallpaperRevision.ShouldBeGreaterThan(0);

        services.SetModuleWallpaper("img", null).Succeeded.ShouldBeTrue();

        var withoutWallpaper = services.GetInstances().Single(instance => instance.InstanceId == "img");
        withoutWallpaper.WallpaperRevision.ShouldBe(0);
    }

    [Fact]
    public void 保存组合件包_控件图片与模块壁纸都恢复()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-image-view-" + Guid.NewGuid().ToString("N"));

        try
        {
            var wallpaper = Png;
            var image = Jpeg;
            var crop = new ViewImageCrop(0.1, 0.2, 0.5, 0.6);

            using (var runtime = new ModuleRuntime())
            {
                CoreRuntimeTests.AddModule(runtime, "img", "sender", typeof(CoreRuntimeTests.SenderModule));
                var services = new RuntimeServices(runtime);
                services.SetModuleWallpaper("img", wallpaper).Succeeded.ShouldBeTrue();
                new ModuleViewImageStore("img", services, services).Write(image, new Rect(0.1, 0.2, 0.5, 0.6))
                    .ShouldBeTrue();
                services.SaveComposite(directory, "image").Succeeded.ShouldBeTrue();
            }

            var assets = Path.Combine(directory, "assets");
            Directory.GetFiles(assets, "*.png").ShouldHaveSingleItem();
            Directory.GetFiles(assets, "*.jpg").ShouldHaveSingleItem();
            Directory.GetFiles(assets, "*.img").ShouldBeEmpty();

            using (var runtime = new ModuleRuntime())
            {
                var services = new RuntimeServices(runtime);
                services.Registry.TryRegister(
                    CoreRuntimeTests.TestDefinition("sender", typeof(CoreRuntimeTests.SenderModule)),
                    out _).ShouldBeTrue();

                services.LoadComposite(directory).Succeeded.ShouldBeTrue();

                services.GetModuleWallpaper("img")!.Png.SequenceEqual(wallpaper).ShouldBeTrue();
                var restored = services.GetModuleViewImage("img");
                restored.ShouldNotBeNull();
                restored!.Bytes.SequenceEqual(image).ShouldBeTrue();
                restored.Crop.ShouldBe(crop);

                var recaptured = runtime.CaptureComposite("recheck", null, out _);
                var record = recaptured.Instances.Single();
                record.Wallpaper.ShouldEndWith(".png");
                record.ViewState!.Image!.AssetRef.ShouldEndWith(".jpg");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 保存扁平json_控件图片写真实格式并恢复()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-image-flat-" + Guid.NewGuid().ToString("N"));
        var document = Path.Combine(directory, "image.json");

        try
        {
            _ = Directory.CreateDirectory(directory);
            var wallpaper = Png;
            var image = Jpeg;

            using (var runtime = new ModuleRuntime())
            {
                CoreRuntimeTests.AddModule(runtime, "img", "sender", typeof(CoreRuntimeTests.SenderModule));
                var services = new RuntimeServices(runtime);
                services.SetModuleWallpaper("img", wallpaper).Succeeded.ShouldBeTrue();
                new ModuleViewImageStore("img", services, services).Write(image, null).ShouldBeTrue();
                services.SaveComposite(document, "image").Succeeded.ShouldBeTrue();
            }

            var assets = Path.Combine(directory, "assets");
            Directory.GetFiles(assets, "*.png").ShouldHaveSingleItem();
            Directory.GetFiles(assets, "*.jpg").ShouldHaveSingleItem();
            Directory.GetFiles(assets, "*.img").ShouldBeEmpty();

            using (var runtime = new ModuleRuntime())
            {
                var services = new RuntimeServices(runtime);
                services.Registry.TryRegister(
                    CoreRuntimeTests.TestDefinition("sender", typeof(CoreRuntimeTests.SenderModule)),
                    out _).ShouldBeTrue();

                services.LoadComposite(document).Succeeded.ShouldBeTrue();
                services.GetModuleWallpaper("img")!.Png.SequenceEqual(wallpaper).ShouldBeTrue();
                services.GetModuleViewImage("img")!.Bytes.SequenceEqual(image).ShouldBeTrue();
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 同一份字节作壁纸和图片_只写一个资产文件并都恢复()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-image-shared-" + Guid.NewGuid().ToString("N"));

        try
        {
            using (var runtime = new ModuleRuntime())
            {
                CoreRuntimeTests.AddModule(runtime, "img", "sender", typeof(CoreRuntimeTests.SenderModule));
                var services = new RuntimeServices(runtime);
                services.SetModuleWallpaper("img", Png).Succeeded.ShouldBeTrue();
                new ModuleViewImageStore("img", services, services).Write(Png, null).ShouldBeTrue();
                services.SaveComposite(directory, "image").Succeeded.ShouldBeTrue();
            }

            var assets = Path.Combine(directory, "assets");
            Directory.GetFiles(assets, "*", SearchOption.TopDirectoryOnly).ShouldHaveSingleItem();

            using (var runtime = new ModuleRuntime())
            {
                var services = new RuntimeServices(runtime);
                services.Registry.TryRegister(
                    CoreRuntimeTests.TestDefinition("sender", typeof(CoreRuntimeTests.SenderModule)),
                    out _).ShouldBeTrue();

                services.LoadComposite(directory).Succeeded.ShouldBeTrue();
                services.GetModuleWallpaper("img")!.Png.SequenceEqual(Png).ShouldBeTrue();
                services.GetModuleViewImage("img")!.Bytes.SequenceEqual(Png).ShouldBeTrue();
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void 更换图片_不改变壁纸_保存重开仍各自恢复()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-image-replace-" + Guid.NewGuid().ToString("N"));

        try
        {
            var wallpaper = Png;

            using (var runtime = new ModuleRuntime())
            {
                CoreRuntimeTests.AddModule(runtime, "img", "sender", typeof(CoreRuntimeTests.SenderModule));
                var services = new RuntimeServices(runtime);
                services.SetModuleWallpaper("img", wallpaper).Succeeded.ShouldBeTrue();
                var store = new ModuleViewImageStore("img", services, services);

                store.Write(Jpeg, null).ShouldBeTrue();
                store.Write(Gif, null).ShouldBeTrue();

                services.GetModuleWallpaper("img")!.Png.SequenceEqual(wallpaper).ShouldBeTrue(
                    "更换图片不应改变模块壁纸");
                services.GetModuleViewImage("img")!.Bytes.SequenceEqual(Gif).ShouldBeTrue();
                services.SaveComposite(directory, "image").Succeeded.ShouldBeTrue();
            }

            Directory.GetFiles(Path.Combine(directory, "assets"), "*.gif").ShouldHaveSingleItem();

            using (var runtime = new ModuleRuntime())
            {
                var services = new RuntimeServices(runtime);
                services.Registry.TryRegister(
                    CoreRuntimeTests.TestDefinition("sender", typeof(CoreRuntimeTests.SenderModule)),
                    out _).ShouldBeTrue();

                services.LoadComposite(directory).Succeeded.ShouldBeTrue();
                services.GetModuleWallpaper("img")!.Png.SequenceEqual(wallpaper).ShouldBeTrue();
                services.GetModuleViewImage("img")!.Bytes.SequenceEqual(Gif).ShouldBeTrue();
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
