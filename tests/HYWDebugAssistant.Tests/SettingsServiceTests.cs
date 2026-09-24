using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T4（M1-7）：应用级设置——加载容错、校验、即时生效、原子写、**未知字段片段保留**。
/// </summary>
public class SettingsServiceTests
{
    [Fact]
    public void 加载_文件不存在时应使用默认设置且不报错()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);

        service.Load().ShouldBe(SettingsLoadOutcome.DefaultsCreated);
        service.Current.InputQueueCapacity.ShouldBe(1024);
        service.Current.StopTimeoutSeconds.ShouldBe(5);
        runtime.Issues.Count.ShouldBe(0, "首次运行不是异常");
    }

    [Fact]
    public void 加载_文件损坏时应使用默认设置并报Issue但不阻断()
    {
        using var scope = new SettingsScope();
        File.WriteAllText(scope.SettingsPath, "{ this is not json ");

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);

        service.Load().ShouldBe(SettingsLoadOutcome.DefaultsAfterFailure);
        service.Current.InputQueueCapacity.ShouldBe(1024);
        runtime.Issues.Count.ShouldBe(1);
        runtime.Issues.Recent(1)[0].Issue.Category.ShouldBe(Sdk.Reporting.IssueCategory.ConfigurationError);
    }

    [Fact]
    public void 更新_合法设置应即时生效并原子写盘()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var updated = service.Current.Clone();
        updated.InputQueueCapacity = 256;
        updated.StopTimeoutSeconds = 2.5;
        updated.AlignAutoNetworkEnabled = false;

        var result = service.Update(updated);

        result.Succeeded.ShouldBeTrue(result.Failures.Count > 0 ? result.Failures[0] : null);

        // 即时生效：承载参数与建网开关已更新
        runtime.Carriers.Options.QueueCapacity.ShouldBe(256);
        runtime.Carriers.Options.StopTimeout.ShouldBe(TimeSpan.FromSeconds(2.5));
        runtime.AlignAutoNetwork.ShouldBeFalse();

        // 已落盘
        File.Exists(scope.SettingsPath).ShouldBeTrue();
        var text = File.ReadAllText(scope.SettingsPath);
        text.ShouldContain("\"inputQueueCapacity\": 256");
        text.ShouldContain("\"stopTimeoutSeconds\": 2.5");
    }

    [Fact]
    public void 更新_吸附距离应落盘并往返()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        service.Current.SnapDistance.ShouldBe(4, "默认 4（ADR-0042 D6）");

        var updated = service.Current.Clone();
        updated.SnapDistance = 6;
        service.Update(updated).Succeeded.ShouldBeTrue();

        var text = File.ReadAllText(scope.SettingsPath);
        text.ShouldContain("\"snapDistance\": 6");

        var reloaded = new SettingsService(runtime, scope.SettingsPath);
        reloaded.Load();
        reloaded.Current.SnapDistance.ShouldBe(6, "重新加载应保留用户设置");
    }

    [Fact]
    public void 更新_吸附距离超出范围应被拒绝()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var invalid = service.Current.Clone();
        invalid.SnapDistance = 20;

        var result = service.Update(invalid);

        result.Succeeded.ShouldBeFalse();
        result.Failures[0].ShouldContain("吸附距离");
        service.Current.SnapDistance.ShouldBe(4, "被拒绝的更新不得改变当前值");
    }

    [Fact]
    public void 更新_非法值应被拒绝且不改变当前设置()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        var invalid = service.Current.Clone();
        invalid.InputQueueCapacity = 0;

        var result = service.Update(invalid);

        result.Succeeded.ShouldBeFalse();
        result.Failures[0].ShouldContain("输入队列容量");
        service.Current.InputQueueCapacity.ShouldBe(1024, "被拒绝的更新不得改变当前值");
    }

    [Fact]
    public void 往返_应保留未识别字段()
    {
        using var scope = new SettingsScope();
        // 预置一个含**未知字段**的文件（模拟更高版本写下的设置）
        File.WriteAllText(scope.SettingsPath, """
        {
          "schemaVersion": 1,
          "inputQueueCapacity": 512,
          "futureOption": "由更高版本写入",
          "nested": { "a": 1, "b": [1, 2, 3] }
        }
        """);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load().ShouldBe(SettingsLoadOutcome.Loaded);
        service.Current.InputQueueCapacity.ShouldBe(512);

        // 更新一个已知字段
        var updated = service.Current.Clone();
        updated.UiRefreshRateHz = 30;
        service.Update(updated).Succeeded.ShouldBeTrue();

        // 未知字段原样保留（AC-4 同策略）
        var root = JsonNode.Parse(File.ReadAllText(scope.SettingsPath))!.AsObject();
        root["futureOption"]!.GetValue<string>().ShouldBe("由更高版本写入");
        root["nested"]!["b"]!.AsArray().Count.ShouldBe(3);
        root["uiRefreshRateHz"]!.GetValue<int>().ShouldBe(30);
        root["inputQueueCapacity"]!.GetValue<int>().ShouldBe(512, "未修改的已知字段保持原值");
    }

    [Fact]
    public void 往返_损坏文件不应被覆盖()
    {
        using var scope = new SettingsScope();
        var broken = "{ broken content";
        File.WriteAllText(scope.SettingsPath, broken);

        using var runtime = new ModuleRuntime();
        var service = new SettingsService(runtime, scope.SettingsPath);
        service.Load();

        // 只加载、不更新 → 原文件保持不变（保护用户数据）
        File.ReadAllText(scope.SettingsPath).ShouldBe(broken);
    }

    [Fact]
    public void 服务_应可经公开面读写设置()
    {
        using var scope = new SettingsScope();
        using var runtime = new ModuleRuntime();
        var settings = new SettingsService(runtime, scope.SettingsPath);
        settings.Load();

        var services = new RuntimeServices(runtime, settings);

        var view = services.GetSettings();
        view.UiRefreshRateHz.ShouldBe(60);

        view.UiRefreshRateHz = 120;
        services.UpdateSettings(view).Succeeded.ShouldBeTrue();
        services.GetSettings().UiRefreshRateHz.ShouldBe(120);

        // 返回的是副本：改它不影响生效值
        var copy = services.GetSettings();
        copy.UiRefreshRateHz = 1;
        services.GetSettings().UiRefreshRateHz.ShouldBe(120);
    }

    /// <summary>测试用临时目录（设置文件路径）。</summary>
    private sealed class SettingsScope : IDisposable
    {
        public SettingsScope()
        {
            Directory = Path.Combine(Path.GetTempPath(), "hyw-settings-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            SettingsPath = Path.Combine(Directory, "settings.json");
        }

        public string Directory { get; }

        public string SettingsPath { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }
}
