# 值

值（value）是模块视图里**用户能看见并修改的一项数据**。它没有“配置值 / 输入值”的分类：配置字段、文本内容、开关、下拉都只是值；控件、持久化、提交与错误都由值的契约表达。

## 值声明

在 `OnRegistered` 里声明：

```csharp
context.Declaration.DeclareValue(new ValueDeclaration("header")
{
    Editor = ValueEditor.Text,          // Text / Number / Boolean / Enum
    Persistent = true,                  // 随组合件/工作区保存
    ResetWithModule = true,             // 参与模块窗口的“还原”
});
```

配置字段不需要再次声明：框架把每个配置字段也当作同名的值。视图按名字绑定即可，模块按名字读取当前值。配置类型仍需在 `OnRegistered` 读一次（`Configuration.Read<T>()`）以登记 Schema 与默认值；值本身由 `Values` 读取。

`Editor` 只决定控件默认选哪种编辑器；`Persistent` 与 `ResetWithModule` 是正交属性，不是值的种类。一次性的动作输入（文件选择结果、结构动作）仍用 `DeclareInput` / 命令，不属于值。

## 一个值只能有一个编辑器

同一个值名在视图 XAML 里**只能出现在一个输入控件上**（\ModuleTextSurface\ 的 \Value\、\ModuleToggle\/\ModuleCheckBox\、\ModuleSelect\/\ModuleSegmentedControl\、\ModuleConfigField\ 都算）。重复会出现两个初值、包加载时被拒绝（\DuplicateValue\），整个模块都装不上——把同一项设置放进工具栏和设置面板两处就是这种写法。

## 视图绑定

控件只写**值名**，不写配置字段名、不直接读写视图状态：

```xml
<ui:ModuleTextSurface Mode="Input"
                      Value="{x:Static local:MyModule.HeaderValue}"
                      Text="ab"
                      CommitMode="OnChange" />

<ui:ModuleToggle Value="{x:Static local:MyModule.EnabledValue}" />

<ui:ModuleSelect Value="{x:Static local:MyModule.ProtocolValue}"
                 SelectedKey="TextFrame">
  <ui:ModuleSelect.PreviewOptions>
    <ui:ModuleSelectOption Key="TextFrame" Label="{loc:Tr choice.textFrame}" />
    <ui:ModuleSelectOption Key="FireWater" Label="{loc:Tr choice.fireWater}" />
  </ui:ModuleSelect.PreviewOptions>
</ui:ModuleSelect>
```

- `CommitMode="OnChange"`：每次修改都交值；适合开关、下拉、内容文本。
- `CommitMode="OnExplicit"`：回车、失焦或显式动作交值；适合普通设置文本、数字。
- `OnChange` 已经逐键提交，失焦不重复。
- `Text` / `IsChecked` / `SelectedKey` 是预览/实例化默认值，不是第二套运行期逻辑；运行期显示以值状态为准。

## 模块读取

```csharp
var header = context.Values.ReadText(MyModule.HeaderValue) ?? string.Empty;
```

- 读到的就是当前有效值：持久化值优先；否则是框架在激活前从视图 XAML 播种的初值（配置字段则用配置类型默认值）。
- 模块不再用 `ViewState.GetInputText` 读控件文本；也不再按“配置/输入”分别处理。
- 需要规范化或拒绝时：

```csharp
context.Values.AcceptText(MyModule.HeaderValue, normalized);
context.Values.Report(MyModule.HeaderValue, new ValueNotice(
    ValueNoticeSeverity.Warning, "帧头不能为空"));
```

`Report(null)` 收起结论。控件只负责把结论画出来，不判断什么算错。

## 初值与持久化

- **默认值只写在 XAML**：控件的 `Text` / `IsChecked` / `SelectedKey` / `InitialValue` 就是用户实例化一个新模块后看到的值。包加载时读取这些初值，模块激活前播种；没有第二个 `ValueDeclaration.DefaultValue`。
- **持久化值**：`Persistent = true` 时随组合件/工作区保存；加载时覆盖 XAML 初值；未持久化的值只在本次会话内保留。
- **配置字段**：配置类型属性初始化器是 Schema 默认值；配置文件里的值覆盖它。配置字段的默认值不来自控件 XAML。
- **还原**：`ResetWithModule = true` 的值在模块“还原尺寸 + 配置”时回到 XAML 初值；文本内容等 `ResetWithModule = false` 的值保留。

## 持久化边界

- **值只保存用户设置/输入**：开关、刻度、频率、名称等用户能编辑的内容，才由 `Persistent = true` 持久化；瞬时视口、当前选中项、历史样本不是值。
- **数据帧派生的对象不持久化**：数据里发现的组/通道、它们的运行期历史/范围/显隐/删除标记，重启后应由下一帧重建，不能写进配置。否则会出现“配置里存在、当前没有对应数据”的幽灵对象。
- **用户创建的领域结构可以持久化**：例如波形发生器的组/通道是用户创建的领域对象，用模块配置保存；视图输入框只是当前选中对象的编辑镜像，不要给每个输入框再存一份会漂移的第二事实。
- 同一事实只选一个持久化归属：要么在值里，要么在模块配置里；不要把同一项同时写两处。

## 控件责任

控件只做三件事：

1. 读 `IModuleViewContext.ReadValue(name)` 并画出来；
2. 按 `CommitMode` 调 `CommitValue(name, text)`；
3. 把 `ValueState.Notice` / 只读 / 可用状态画出来。

控件**不**读配置、不读写视图状态、不调用 `SubmitInput` / `UpdateField`、不解析、不校验、不生成错误句子。值的后端由框架决定，替换存储实现不需要改控件。

## 旧输入源

`DeclareInput` 仍然存在，但只用于**动作/事件**：文件选择结果、图像结果、结构动作等“一次事件”，不是可编辑的值。可编辑的值请用 `DeclareValue` + `Value` 绑定。
