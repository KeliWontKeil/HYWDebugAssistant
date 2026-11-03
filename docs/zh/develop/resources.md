# 资源与文本

模块自有资源放在包内 `resources/`，按类别分目录：

```
resources/
  i18n/<程序集简名>/<culture>.json
  icons/<程序集简名>/icons.json + *.svg
  <自定义 kind>/...
```

`module.json` 的 `resources` 要登记实际存在的目录（框架加载时校验，目录缺失即拒绝加载）。

## 文本表

- 位置：`resources/i18n/<程序集简名>/<culture>.json`，内容是"键 → 文案"的 JSON 对象；
- 可选分片：`<culture>.<area>.json`，同一目录里基础表排在最前，其余按文件名序合并；
- 回退链：精确 culture → `zh` → `zh-Hans` → 可见缺失标记 `⟦key⟧`；
- 缺表与缺键都**不返回空白**：界面会露出缺的是哪个键。

代码里取文案：

```csharp
private static string Tr(string key) => TextCatalog.Get(typeof(MyModule).Assembly, key);
```

XAML 里用 `{loc:Tr key}`；模块视图的键与显示源名一样，只写一次（常量 + `{x:Static}`）。

## 图标

- 位置：`resources/icons/<程序集简名>/icons.json` + 同目录 `.svg`；
- `icons.json` 是"键 → 相对 `.svg` 路径"的映射；
- SVG 只支持子集：`viewBox` + `<path d>` + `fill-rule`；`transform` / `<use>` / 脚本 / 外部引用会被跳过并报诊断；
- 界面里用 `Icon` 控件：`IconKey` + `Owner`（模块程序集）。

## 自定义资源

框架不解释自定义类别的内容，模块用上下文自己读：

```csharp
var text = context.Resources.Read("templates", "default.txt");
```

读取是**只读、相对、不越界**的：只能读本模块目录下 `resources/` 里的文件；读不到返回 `null`，不抛异常。

## 校验

- 本仓库：`tools/verify-module-packages.ps1` 校验包内文件集合、清单声明、宿主程序集排除；
- 外部工程：构建规则负责归拢；装载失败看[被拒原因](../reference/rejections.md)。

## 相关

- [界面](ui.md)
- [资源与文本（机制）](../concepts/resources-and-text.md)
