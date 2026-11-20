namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **自绘控件的排版框声明**：实现它的控件会出现在预览器的虚线范围框里。
/// </summary>
/// <remarks>
/// 预览范围框只框"布局有意义、又没有可见轮廓"的东西（标签、勾选框、无背景容器）。
/// 自绘控件（仪表盘、刻度条、姿态）自己画内容、没有边框元素，框架无法从类型上判断
/// "它的边界就是它的绘制范围" —— 所以由控件**自己声明**，`ModuleView` 只认这一个接口，
/// 不写任何具体控件类型。
/// </remarks>
public interface IModulePreviewBoundsTarget
{
}
