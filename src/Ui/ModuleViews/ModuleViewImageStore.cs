using Avalonia;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ui;

namespace HYWDebugAssistant.Ui.ModuleViews;

/// <summary>
/// 控件图片状态适配器：读写当前实例 <c>viewState</c> 下的控件图片，不接触模块壁纸。
/// </summary>
internal sealed class ModuleViewImageStore : IModuleViewImageStore
{
    private readonly string _instanceId;
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;

    public ModuleViewImageStore(string instanceId, IRuntimeQuery query, IRuntimeCommand command)
    {
        _instanceId = instanceId;
        _query = query;
        _command = command;
    }

    /// <inheritdoc />
    public long Revision => _query.GetModuleViewImageRevision(_instanceId);

    /// <inheritdoc />
    public ModuleViewImage? Read()
    {
        if (_query.GetModuleViewImage(_instanceId) is not { } image)
        {
            return null;
        }

        return new ModuleViewImage(image.Bytes, ToRect(image.Crop));
    }

    /// <inheritdoc />
    public bool Write(byte[]? bytes, Rect? crop) =>
        _command.SetModuleViewImage(_instanceId, bytes, ToCrop(crop)).Succeeded;

    private static Rect? ToRect(ViewImageCrop crop)
    {
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            return null;
        }

        return crop.X == 0 && crop.Y == 0 && crop.Width == 1 && crop.Height == 1
            ? null
            : new Rect(crop.X, crop.Y, crop.Width, crop.Height);
    }

    private static ViewImageCrop ToCrop(Rect? crop)
    {
        if (crop is not { Width: > 0, Height: > 0 } rect)
        {
            return ViewImageCrop.Full;
        }

        var x = Math.Clamp(rect.X, 0, 1);
        var y = Math.Clamp(rect.Y, 0, 1);
        return new ViewImageCrop(
            x,
            y,
            Math.Clamp(rect.Width, 0, 1 - x),
            Math.Clamp(rect.Height, 0, 1 - y));
    }
}
