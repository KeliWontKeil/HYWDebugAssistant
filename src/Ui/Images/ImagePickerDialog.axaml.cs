using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Ui.Images;

/// <summary>全局图片裁切对话框：固定目标比例、默认居中、可缩放/平移。</summary>
public partial class ImagePickerDialog : Window
{
    private readonly double _aspect;

    /// <summary>设计器用。</summary>
    public ImagePickerDialog()
        : this(null, 16d / 9d)
    {
    }

    /// <summary>创建对话框。</summary>
    /// <param name="source">源图。</param>
    /// <param name="aspect">目标宽高比。</param>
    public ImagePickerDialog(Bitmap? source, double aspect)
    {
        InitializeComponent();

        _aspect = aspect > 0 && double.IsFinite(aspect) ? aspect : 1;
        CropView.Source = source;
        CropView.AspectRatio = _aspect;

        var width = _aspect >= 1 ? 420d : 420d * _aspect;
        var height = _aspect >= 1 ? 420d / _aspect : 420d;
        CropView.Width = Math.Max(160, width);
        CropView.Height = Math.Max(120, height);

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
        ConfirmButton.Click += (_, _) => Close(CropView.CapturePng());
    }
}
