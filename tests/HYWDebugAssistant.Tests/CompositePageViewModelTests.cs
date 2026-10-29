using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 组合页：选择集镜像、保存范围、部分网络提示。纯视图模型判据，不起窗口。
/// </summary>
public class CompositePageViewModelTests
{
    [Fact]
    public void 默认全选_取消一项后保存应只写选中范围()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "r1", "receiver", typeof(CoreRuntimeTests.ReceiverModule));
        CoreRuntimeTests.SetLabel(runtime, "s1", CoreRuntimeTests.SenderModule.OutPort, "bus");
        CoreRuntimeTests.SetLabel(runtime, "r1", CoreRuntimeTests.ReceiverModule.InPort, "bus");

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();
        var page = new CompositePageViewModel(services, services, windows);

        page.Activate();

        page.TotalCount.ShouldBe(2);
        page.SelectedCount.ShouldBe(2, "组合页首次进入默认全选");
        page.HasPartialNetworks.ShouldBeFalse();

        var generator = page.Rows.Single(row => row.InstanceId == "s1");
        page.RemoveModuleCommand.Execute(generator.InstanceId);

        page.SelectedCount.ShouldBe(1);
        page.Rows.ShouldNotContain(row => row.InstanceId == "s1");
        page.HasPartialNetworks.ShouldBeTrue("网络另一端仍在选择集中 ⇒ 网络成员未选全");

        var directory = Path.Combine(Path.GetTempPath(), "hyw-composite-page-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "subset.json");
            page.Save(path);

            page.StatusIsOk.ShouldBeTrue(page.StatusMessage);
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            root["instances"]!.AsArray().Count.ShouldBe(1, "只保存勾选范围");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void 全选现场与清空应同步选择集和已选列表()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
        CoreRuntimeTests.AddModule(runtime, "s2", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();
        var page = new CompositePageViewModel(services, services, windows);
        page.Activate();

        page.ClearSelectionCommand.Execute(null);
        page.SelectedCount.ShouldBe(0);
        page.Rows.ShouldBeEmpty();
        page.CanSave.ShouldBeFalse();

        page.SelectAllLiveCommand.Execute(null);
        page.SelectedCount.ShouldBe(2);
        page.Rows.Count.ShouldBe(2);

        page.RemoveModuleCommand.Execute("s1");
        page.SelectedCount.ShouldBe(1);
        page.Rows.ShouldContain(row => row.InstanceId == "s2");
        page.Rows.ShouldNotContain(row => row.InstanceId == "s1");
    }

    [Fact]
    public void 重命名选中组合_应刷新并选中新文件()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();
        var page = new CompositePageViewModel(services, services, windows);
        page.Activate();

        var oldPath = Path.Combine(
            services.GetStorageLocations().CompositesRoot,
            "rename-old-" + Guid.NewGuid().ToString("N") + ".json");
        var newStem = "rename-new-" + Guid.NewGuid().ToString("N");
        var newPath = Path.Combine(services.GetStorageLocations().CompositesRoot, newStem + ".json");
        try
        {
            page.CompositeName = Path.GetFileNameWithoutExtension(oldPath);
            page.Save(oldPath);
            page.SelectedCompositeFile.ShouldNotBeNull();

            page.RenameSelectedComposite(newStem);

            page.StatusIsOk.ShouldBeTrue(page.StatusMessage);
            File.Exists(oldPath).ShouldBeFalse();
            File.Exists(newPath).ShouldBeTrue();
            page.SelectedCompositeFile.ShouldNotBeNull().Path.ShouldBe(newPath);
        }
        finally
        {
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }

            if (File.Exists(newPath))
            {
                File.Delete(newPath);
            }
        }
    }

    [Fact]
    public void 删除选中组合_应删除文件并清空选中()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();
        var page = new CompositePageViewModel(services, services, windows);
        page.Activate();

        var path = Path.Combine(
            services.GetStorageLocations().CompositesRoot,
            "delete-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            page.CompositeName = Path.GetFileNameWithoutExtension(path);
            page.Save(path);
            page.StatusIsOk.ShouldBeTrue(page.StatusMessage);
            page.SelectedCompositeFile.ShouldNotBeNull();
            page.SelectedCompositeFile!.Path.ShouldBe(path);

            page.DeleteSelectedComposite();

            page.StatusIsOk.ShouldBeTrue(page.StatusMessage);
            File.Exists(path).ShouldBeFalse();
            page.SelectedCompositeFile.ShouldBeNull();
            page.CompositeFiles.ShouldNotContain(file => file.Path == path);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void 可用组合列表_应自动扫描_搜索过滤_导入后选中()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();
        var page = new CompositePageViewModel(services, services, windows);
        page.Activate();

        var directory = Path.Combine(Path.GetTempPath(), "hyw-composite-import-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        string? target = null;
        try
        {
            var source = Path.Combine(directory, "page-import.json");
            page.CompositeName = "page-import";
            page.Save(source);
            page.StatusIsOk.ShouldBeTrue(page.StatusMessage);

            page.ImportComposite(source);
            target = page.SelectedCompositeFile?.Path;
            target.ShouldNotBeNull();
            Path.GetDirectoryName(target!).ShouldBe(Path.GetFullPath(services.GetStorageLocations().CompositesRoot));
            page.CompositeFiles.ShouldContain(file => file.Path == target);

            page.CompositeSearchText = "page-import";
            page.CompositeFiles.ShouldNotBeEmpty();
            page.CompositeSearchText = "no-such-composite";
            page.CompositeFiles.ShouldBeEmpty();
            page.CompositeFilesEmptyText.ShouldBe("没有匹配的组合");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }

            if (target is { Length: > 0 } && File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    [Fact]
    public void 默认保存路径_应清洗非法字符并落在默认目录()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        var windows = new ModuleWindowsViewModel(services, services);
        var page = new CompositePageViewModel(services, services, windows);

        page.CompositeName = "a/b:c";
        var path = page.BuildDefaultSavePath();

        Path.GetDirectoryName(path).ShouldBe(services.GetStorageLocations().CompositesRoot);
        Path.GetFileName(path).ShouldBe("a_b_c");

        page.CompositeName = "  ";
        Path.GetFileName(page.BuildDefaultSavePath()).ShouldStartWith("组合-");
        Path.GetExtension(page.BuildDefaultSavePath()).ShouldBeEmpty();
    }

    [Fact]
    public void 保存与加载成功后组合名应同步为文件名()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);
        CoreRuntimeTests.AddModule(runtime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));

        var windows = new ModuleWindowsViewModel(services, services);
        windows.Refresh();
        var page = new CompositePageViewModel(services, services, windows);
        page.Activate();

        var directory = Path.Combine(Path.GetTempPath(), "hyw-composite-page-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "named.json");
            page.Save(path);
            page.CompositeName.ShouldBe("named");

            using var secondRuntime = new ModuleRuntime();
            var secondServices = new RuntimeServices(secondRuntime);
            CoreRuntimeTests.AddModule(secondRuntime, "s1", "sender", typeof(CoreRuntimeTests.SenderModule));
            var secondWindows = new ModuleWindowsViewModel(secondServices, secondServices);
            secondWindows.Refresh();
            var secondPage = new CompositePageViewModel(secondServices, secondServices, secondWindows);

            secondPage.Load(path);
            secondPage.CompositeName.ShouldBe("named");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
