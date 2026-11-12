# 资源与文本

模块自有资源放在包内 `resources/`，按类别分目录：

```
resources/
  i18n/<程序集简名>/<culture>.json
  icons/<程序集简名>/icons.json + *.svg
  <自定义 kind>/...
```

`module.json` 的 `resources` 要登记实际存在的目录（框架加载时校验，目录缺失即拒绝加载）。`i18n` 与 `icons` 的拥有者是**程序集简名**；框架只读本模块包内的这两棵树，不会回退到主应用资源。

## 文本表

位置：`resources/i18n/<程序集简名>/<culture>.json`。文件是 JSON 对象，文案放在 `entries` 下：

```json
{
  "language": "zh-Hans",
  "entries": {
    "module.title": "示例模块",
    "view.count": "累计值",
    "state.ready": "就绪"
  }
}
```

| 规则 | 说明 |
|---|---|
| 结构 | 根对象里必须有 `entries` 对象，值为字符串；`language` 只是元数据，读取器不校验 |
| 分片 | 可加 `<culture>.<area>.json`（例如 `zh-Hans.settings.json`）；基础表排最前，其余按文件名序合并，**先者胜** |
| 重复键 | 同一文件内重复 ⇒ 该文件整份跳过；分片与已有键重复 ⇒ 该分片整份跳过；都会报一条诊断 |
| 回退链 | 精确 culture → `zh` → `zh-Hans` → 可见缺失标记 `⟦key⟧` |
| 缺表/缺键 | 都不返回空白：缺表每个拥有者报一次，缺键在界面露出键名 |
| 体积 | 单文件上限 4 MB；超限整份跳过 |
| 键名 | 按界面区域分段，如 `view.reset`、`state.ready`、`config.serial.portName`；同一区域聚在一起 |

代码里取文案：

```csharp
private static string Tr(string key) => TextCatalog.Get(typeof(MyModule).Assembly, key);
```

带占位符时用 `TextCatalog.Get(assembly, key, args)`，模板写 `{0}`、`{1}`；格式化用 `CurrentCulture`（数字随用户区域设置，界面语言随 UI 文化）。

XAML 里用 `{loc:Tr key}`。属主按 `avares://` 的 host 解析（即模块程序集），所以模块 XAML 只写键即可命中本模块的表。

配置字段的标题、说明、分组与枚举选项也走同一张表：在配置类型上用 `TitleKey` / `DescriptionKey` / `GroupKey` / `[ConfigurationChoice(TitleKey = …)]` 指向键。**声明的外部标准接入**的参数类型在框架侧，但表单文案的属主是声明该接入的模块，因此模块需要自备这些键：

| 标准 | 需要的键前缀 |
|---|---|
| `serial` | `config.serial.*`（`portName`、`port.description`、`baudRate`、`dataBits`、`stopBits`、`parity`） |
| `tcp-client` | `config.tcpClient.*`（`address`、`address.description`、`port`、`timeout`、`noDelay`、`noDelay.description`） |
| `tcp-server` | `config.tcpServer.*`（`address`、`address.description`、`port`、`maxConnections`、`noDelay`、`noDelay.description`） |
| `udp` | `config.udp.*`（`localAddress`、`localAddress.description`、`localPort`、`targetAddress`、`targetAddress.description`、`targetPort`、`broadcast`、`broadcast.description`） |

生产模块的文本表可对照参考（例如框架内置的发送器/串口模块）。

## 图标

位置：`resources/icons/<程序集简名>/icons.json` + 同目录 `.svg`。JSON 同样是 `entries` 对象：

```json
{
  "entries": {
    "icon.refresh": "refresh.svg"
  }
}
```

- 值是相对 `.svg` 的路径；缺键/缺表时界面显示键名，并报一条诊断，不显示空白。
- SVG 只支持子集：`viewBox` + `<path d>` + `fill-rule`；`transform`、`<use>`、脚本、外部引用会被跳过并报诊断。
- 同一文件里所有 path 的有效填充都是 `none`（自身或祖先继承）⇒ 线条图标：`Icon` 只描边，线宽由控件统一（不读 SVG 的 `stroke-width`）；有任一 path 带填充色 ⇒ 填充图标（填充 + 细描边）。
- 界面里用 `Icon` 控件：`IconKey` + `Owner`（模块程序集）。控件默认 16×16，具体尺寸由视图给。

```xml
<ui:Icon IconKey="{x:Static local:MyViewIcons.Refresh}" Owner="{x:Static local:MyModule.IconOwner}" />
```

## 自定义资源

框架不解释自定义类别的内容，模块用上下文自己读：

```csharp
if (context.Resources.TryOpen("templates", "default.txt", out var stream))
{
    using (stream) { /* 只读流 */ }
}

var bytes = context.Resources.ReadAllBytes("dictionaries", "terms.bin");
```

- 读取是**只读、相对、不越界**的：根是模块包内 `resources/<kind>/`，绝对路径、`..`、盘符一律拒绝，返回 `false` 不抛。
- `i18n` 与 `icons` 由框架读取，不要用 `IResourceAccess` 去读它们。
- 自定义目录不需要登记到 `module.json` 的 `resources`（那条只约束框架读取的目录），但打包规则会把整个 `resources/` 收进包。

## 预览与测试为什么需要复制

工程里的 `Content` 项（见[项目与包布局](project-layout.md)）把 `resources/` 复制到构建输出。预览器与行为测试按“程序集旁”的 `resources/` 读取；只打包不复制会让它们读不到表，界面显示键名。

## 校验

- 本仓库：`tools/verify-module-packages.ps1` 校验包内文件集合、清单声明、宿主程序集排除；
- 外部工程：构建规则负责归拢；装载失败看[被拒原因](publishing.md)。
- 文本缺键不会导致加载失败，但发布前应在预览器与产品里各看一遍，避免界面露出 `⟦key⟧`。

## 相关

- [界面](ui.md)
- [项目与包布局](project-layout.md)
- [资源与文本（机制）](../concepts/resources.md)
