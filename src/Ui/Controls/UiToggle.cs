using Avalonia.Controls.Primitives;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **宿主界面用的滑块开关**（设置页 / 交互页等非模块视图）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>派生自 <see cref="ToggleButton"/>：点击 / 空格 / 回车切换、`IsChecked` 双向绑定都沿用框架行为；</item>
///   <item>视觉是轨道 + 圆钮（`PART_Track` / `PART_Knob`），由 `Ui/Styles/Controls.axaml` 的
///     `ControlTheme` 提供；**不是 Fluent CheckBox**，因此检查态填充走 `AccentPrimary`，不会出现"灰色勾选框"；</item>
///   <item>模块视图仍用自己的 `ModuleToggle`（`Sdk.Ui`）；两者共用同一组 switch token，观感一致。</item>
/// </list>
/// </remarks>
public sealed class UiToggle : ToggleButton
{
}
