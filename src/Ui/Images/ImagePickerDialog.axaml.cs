using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Ui.Images;

/// <summary>全局图片裁切对话框：固定目标比例、默认居中、可缩放/平移。</summary>
public partial class ImagePickerDialog : Window
{
    private readonly double _aspect;
    private readonly bool _selectionOnly;

    /// <summary>设计器用。</summary>
    public ImagePickerDialog()
        : this(null, 16d / 9d)
    {
    }

    /// <summary>创建对话框。</summary>
    /// <param name="source">源图。</param>
    /// <param name="aspect">目标宽高比。</param>
    /// <param name="selectionOnly">true = 返回源图裁切矩形；false = 返回裁切 PNG。</param>
    public ImagePickerDialog(Bitmap? source, double aspect, bool selectionOnly = false)
    {
        InitializeComponent();

        _aspect = aspect > 0 && double.IsFinite(aspect) ? aspect : 1;
        _selectionOnly = selectionOnly;
        CropView.Source = source;
        CropView.AspectRatio = _aspect;

        // 裁切框自身必须与目标比例一致；先按 420 的基准算，再用最小边修正，
        // 修正时保持宽高比，避免"选出来了却比模块更扁/更高"。
        var width = _aspect >= 1 ? 420d : 420d * _aspect;
        var height = _aspect >= 1 ? 420d / _aspect : 420d;
        if (width < 160)
        {
            width = 160;
            height = width / _aspect;
        }
        else if (height < 120)
        {
            height = 120;
            width = height * _aspect;
        }

        CropView.Width = width;
        CropView.Height = height;

        HintText.Text = TextCatalog.Get(typeof(ImagePickerDialog).Assembly, "picker.hint", _aspect.ToString("0.###", CultureInfo.CurrentCulture));
        ZoomSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                CropView.SetZoom(ZoomSlider.Value);
                ZoomText.Text = $"{ZoomSlider.Value * 100:0}%";
            }
        };

        ResetButton.Click += (_, _) =>
        {
            ZoomSlider.Value = 1;
            CropView.Reset();
        };
        CancelButton.Click += (_, _) => Close(null);
        ConfirmButton.Click += (_, _) =>
        {
            object? result = _selectionOnly
                ? CropView.CaptureSelection()
                : CropView.CapturePng();
            Close(result);
        };
    }
}
