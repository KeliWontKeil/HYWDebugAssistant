using HYWDebugAssistant.Hosting.Context;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>运行期外观的启用状态必须可设、可恢复，不能只剩“禁用后永远禁用”。</summary>
public class AppearanceStoreTests
{
    [Fact]
    public void 启用状态应能禁用并恢复()
    {
        var store = new AppearanceStore();

        store.SetEnabled("x", isEnabled: false);
        store.Snapshot().ShouldHaveSingleItem().Enabled.ShouldBe(false);

        store.SetEnabled("x", isEnabled: true);
        store.Snapshot().ShouldHaveSingleItem().Enabled.ShouldBe(true);
    }
}
