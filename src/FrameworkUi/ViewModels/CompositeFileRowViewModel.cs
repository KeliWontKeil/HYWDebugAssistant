using Avalonia.Media;
using Avalonia.Media.Imaging;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Images;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>“可用组合”列表的一行（名称 + 一行状态标注）。</summary>
public sealed class CompositeFileRowViewModel
{
    /// <summary>由组合文件检查结果创建。</summary>
    /// <param name="file">文件元数据 + 只读检查结果。</param>
    public CompositeFileRowViewModel(CompositeFileView file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Path = file.Path;
        DisplayName = file.DisplayName is { Length: > 0 } displayName
            ? displayName
            : System.IO.Path.GetFileNameWithoutExtension(file.FileName);

        var missing = file.MissingModules ?? [];
        HasError = file.ReadError is { Length: > 0 };
        HasMissing = !HasError && missing.Count > 0;

        Annotation = HasError
            ? file.ReadError!
            : HasMissing
                ? TextCatalog.Get(typeof(CompositeFileRowViewModel).Assembly, "composite.row.missing", missing.Count)
                : TextCatalog.Get(typeof(CompositeFileRowViewModel).Assembly, "composite.row.moduleCount", file.ModuleCount);

        var missingText = missing.Count > 0
            ? TextCatalog.Get(typeof(CompositeFileRowViewModel).Assembly, "composite.row.missingList", string.Join("、", missing))
            : string.Empty;
        TooltipText = $"{DisplayName}\n{file.LastWriteTime:yyyy-MM-dd HH:mm}\n{file.Path}{missingText}";
        PreviewImage = PreviewImageStore.Load(file.PreviewImagePath);
    }

    /// <summary>完整路径。</summary>
    public string Path { get; }

    /// <summary>显示名（文件主名）。</summary>
    public string DisplayName { get; }

    /// <summary>第二行标注（N 个模块 / 缺少 N 个模块 / 无法读取原因）。</summary>
    public string Annotation { get; }

    /// <summary>是否有缺失模块（警示色）。</summary>
    public bool HasMissing { get; }

    /// <summary>是否无法读取（错误色）。</summary>
    public bool HasError { get; }

    /// <summary>完整路径、保存时间与缺失标识（ToolTip）。</summary>
    public string TooltipText { get; }

    /// <summary>预览图（无封面 = null，界面显示占位字形）。</summary>
    public Bitmap? PreviewImage { get; }

    /// <summary>居中裁切的预览画刷（无图 ⇒ null）。</summary>
    public ImageBrush? PreviewBrush => WallpaperImageCache.Preview(PreviewImage);

    /// <summary>是否有封面。</summary>
    public bool HasPreview => PreviewImage is not null;
}
