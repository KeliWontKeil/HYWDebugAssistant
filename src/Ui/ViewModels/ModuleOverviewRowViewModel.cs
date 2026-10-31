using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// **交互页"窗口管理"列表的一行**：模块身份 + 当前显示状态 + 与模块窗口按钮簇同一组操作。
/// </summary>
/// <remarks>
/// 行对象按 <see cref="InstanceId"/> 复用（刷新只改属性，不重建对象），否则悬停/展开状态会在
/// 每次刷新时丢失。命令在构造时绑定控制器方法一次，之后不再替换。
/// </remarks>
public sealed partial class ModuleOverviewRowViewModel : ViewModelBase
{
    private readonly Action<string> _show;
    private readonly Action<string> _hide;
    private readonly Action<string> _raise;
    private readonly Action<string> _recall;
    private readonly Action<string> _toggleLock;
    private readonly Action<string> _close;

    /// <summary>创建一行。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="show">显示窗口。</param>
    /// <param name="hide">隐藏窗口。</param>
    /// <param name="raise">显示并置于应用内最前。</param>
    /// <param name="recall">把屏幕外窗口钳回最近屏幕并抬前。</param>
    /// <param name="toggleLock">切换锁定。</param>
    /// <param name="close">关闭模块（移除实例）。</param>
    public ModuleOverviewRowViewModel(
        string instanceId,
        Action<string> show,
        Action<string> hide,
        Action<string> raise,
        Action<string> recall,
        Action<string> toggleLock,
        Action<string> close)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(hide);
        ArgumentNullException.ThrowIfNull(raise);
        ArgumentNullException.ThrowIfNull(recall);
        ArgumentNullException.ThrowIfNull(toggleLock);
        ArgumentNullException.ThrowIfNull(close);

        InstanceId = instanceId;
        _show = show;
        _hide = hide;
        _raise = raise;
        _recall = recall;
        _toggleLock = toggleLock;
        _close = close;

        ShowCommand = new RelayCommand(() => _show(InstanceId));
        HideCommand = new RelayCommand(() => _hide(InstanceId));
        RaiseCommand = new RelayCommand(() => _raise(InstanceId));
        RecallCommand = new RelayCommand(() => _recall(InstanceId));
        ToggleLockCommand = new RelayCommand(() => _toggleLock(InstanceId));
        CloseCommand = new RelayCommand(() => _close(InstanceId));
    }

    /// <summary>实例 ID（行身份；不直接展示给用户）。</summary>
    public string InstanceId { get; }

    /// <summary>模块展示名。</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>状态文本（运行/停用/隐藏/屏幕外/占位…）。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>状态语义（颜色 + 形状双重编码）。</summary>
    [ObservableProperty]
    public partial StateVisual State { get; set; } = StateVisual.Active;

    /// <summary>视图是否开启。</summary>
    [ObservableProperty]
    public partial bool IsViewVisible { get; set; } = true;

    /// <summary>是否锁定。</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    /// <summary>是否占位实例。</summary>
    [ObservableProperty]
    public partial bool IsPlaceholder { get; set; }

    /// <summary>窗口是否落在任一屏幕可见范围内。</summary>
    [ObservableProperty]
    public partial bool IsOffScreen { get; set; }

    /// <summary>是否已有窗口（隐藏/占位时为 false）。</summary>
    [ObservableProperty]
    public partial bool HasWindow { get; set; }

    /// <summary>显示窗口。</summary>
    public IRelayCommand ShowCommand { get; }

    /// <summary>隐藏窗口。</summary>
    public IRelayCommand HideCommand { get; }

    /// <summary>显示并置于应用内最前。</summary>
    public IRelayCommand RaiseCommand { get; }

    /// <summary>把屏幕外窗口钳回最近屏幕并抬前。</summary>
    public IRelayCommand RecallCommand { get; }

    /// <summary>切换锁定。</summary>
    public IRelayCommand ToggleLockCommand { get; }

    /// <summary>关闭模块（移除实例）。</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>用最新的事实刷新本行（不重建对象）。</summary>
    /// <param name="title">展示名。</param>
    /// <param name="statusText">状态文本。</param>
    /// <param name="state">状态语义。</param>
    /// <param name="isViewVisible">视图是否开启。</param>
    /// <param name="isLocked">是否锁定。</param>
    /// <param name="isPlaceholder">是否占位。</param>
    /// <param name="isOffScreen">是否在屏幕外。</param>
    /// <param name="hasWindow">是否已有窗口。</param>
    public void Update(
        string title,
        string statusText,
        StateVisual state,
        bool isViewVisible,
        bool isLocked,
        bool isPlaceholder,
        bool isOffScreen,
        bool hasWindow)
    {
        Title = title;
        StatusText = statusText;
        State = state;
        IsViewVisible = isViewVisible;
        IsLocked = isLocked;
        IsPlaceholder = isPlaceholder;
        IsOffScreen = isOffScreen;
        HasWindow = hasWindow;
    }
}
