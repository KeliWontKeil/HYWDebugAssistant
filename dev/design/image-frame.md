# 图片框：控件持久化与 `ModuleImageSurface` 设计

> 图片属于**图片控件的持久化状态**，不属于模块壁纸、模块值或模块整体呈现。壁纸与控件图片共用**同一个实例资源槽**和**同一条资产命名规则**（`assets/<sha256>.<真实扩展名>`），但各有独立的 kind 与 JSON 位置。旧实现的路径/裁切值、`.img` 与兼容映射全部删除；旧组合件不迁移。

## 0. 边界

- **模块壁纸**：模块窗口呈现，保存在 `CompositeInstanceRecord.Wallpaper` / `presentation.wallpaper`。
- **图片框图片**：图片控件的状态，保存在 `CompositeInstanceRecord.ViewState.Image` / `viewState.image`。
- 两者是同一个实例资源存储的两个 **kind**（`Wallpaper` / `ViewImage`），共用 assets 命名与读写规则；但互不读取、互不写入、互不清除。
- 同一份字节 + 同一真实扩展名 ⇒ 同一个 `assets/<sha256>.<ext>`；壁纸和图片可以共享同一个资产文件。
- 图片框模块本身零端口、零角色、零值、零输入、零动作、零数据流。

## 1. 界面与交互

```text
┌────────────────────────────────────┐
│                                    │
│        右键选择图片                 │  ← 空态
│                                    │
└────────────────────────────────────┘
```

- 空态：显示「右键选择图片」；模块壁纸可以正常透出。
- 有图：`UniformToFill` 铺满内容区；有裁切时只绘制裁切区域。
- 选图：框架文件选择 + 统一裁切窗口，裁切比例取**点击时图片控件内容区的宽高比**。
- 加载中：无图时显示「正在加载图片...」；替换时保留上一张直到成功。
- 失败：显示「图片加载失败，右键重新选择」。
- 「清除图片」只清控件图片状态；模块壁纸保持不变。

## 2. 数据模型

```json
"viewState": {
  "image": {
    "asset": "assets/<sha256>.<ext>",
    "crop": { "x": 0.1, "y": 0.2, "w": 0.5, "h": 0.6 }
  }
}
```

- `<ext>` 是**字节的真实图片格式**：`png` / `jpg` / `gif` / `webp` / `bmp`（`jpeg` 归一为 `jpg`）；壁纸固定 `png`。不再有 `.img`。
- `ViewStateSnapshot` 保留可空 `Image`：
  ```csharp
  public sealed record ViewImageStateSnapshot(string AssetRef, ViewImageCrop Crop);
  public readonly record struct ViewImageCrop(double X, double Y, double Width, double Height);
  ```
- `Composite.Assets` 以**包内相对引用**（`assets/<sha256>.<ext>`）为键，值 = 字节；引用即文件名。
- 壁纸与图片共用同一个 `assets/` 与同一条命名规则；同一份字节 + 同一扩展名只落一个文件。
- 图片字节与裁切随组合件包/工作区保存；扁平 `.json` 也在同目录写 `assets/`。
- 旧 `imagePath`/`imageCrop`/`imagePick` 与旧壁纸图片不读取、不迁移、不保留旧字段；旧 `.img` 引用按原 ref 仍可读，新保存不再产生 `.img`。

## 3. 核心服务

```csharp
public enum ModuleInstanceAssetKind { Wallpaper, ViewImage }

public sealed record ModuleInstanceAsset(string Ref, byte[] Bytes, ViewImageCrop Crop, long Revision);

ModuleRuntime.GetAsset(instanceId, kind)
ModuleRuntime.GetAssetRevision(instanceId, kind)
ModuleRuntime.SetAsset(instanceId, kind, bytes, crop, extension)
ModuleRuntime.RestoreAsset(instanceId, kind, reference, bytes, crop)
ModuleRuntime.RemoveAsset(instanceId, kind)
ModuleRuntime.CaptureAssets(kind)

IRuntimeQuery.GetModuleWallpaper(instanceId)
IRuntimeQuery.GetModuleViewImage(instanceId)
IRuntimeQuery.GetModuleViewImageRevision(instanceId)

IRuntimeCommand.SetModuleWallpaper(instanceId, png)
IRuntimeCommand.SetModuleViewImage(instanceId, bytes, crop)
```

- `ModuleRuntime` 只有一个 `_assets` 字典（键 = 实例 + kind）、一把锁、一个资产版本；壁纸与图片不再各有一套存储。
- `WallpaperVersion` 只在壁纸变更时递增；**清除壁纸也递增**（实例视图缓存键据此失效）。
- 捕获：`CaptureInstance`/`CaptureComposite` 把控件图片写入 `viewState.image`、壁纸写入 `presentation.wallpaper`，字节放入 `Composite.Assets[ref]`。
- 加载：`CreateLoadedInstance` 在 `RestoreViewState` 后 `RestoreViewImage`；`RestoreViewState` 交给 `ViewStateStore` 的快照会剥掉 `Image`（图片由 `RestoreViewImage` 单独恢复）。
- `CompositeService.LoadAssets` 按引用读取 `record.Wallpaper` 与 `record.ViewState.Image.AssetRef`；`WriteAssets` 按引用原样写文件。扩展名不再参与定位。
- 移除实例时删除两个 kind。

## 4. 控件服务

```csharp
public readonly record struct ModuleViewImage(byte[] Bytes, Rect? Crop);

public interface IModuleViewImageStore
{
    long Revision { get; }
    ModuleViewImage? Read();
    bool Write(byte[]? bytes, Rect? crop);
}
```

- `ModuleViewImageStore` 正常路径只调用 `GetModuleViewImage*`/`SetModuleViewImage`；没有任何壁纸调用。
- `ModuleViewContext.ViewImageStore` 是唯一入口；设计期/快照为 null。
- `ImageFrameView` 直接读写该服务：
  - `Refresh` 按版本读取图片和裁切；
  - 选图成功后写入字节和归一化裁切；
  - 清除直接写 null；
  - 不再有模块值、JSON、路径回退或迁移。
- `ImageFrameModule` 只保留视图声明，删除全部值和输入。

## 5. `ModuleImageSurface`

```csharp
public sealed class ModuleImageSurface : Control, IModulePreviewBoundsTarget
{
    public byte[]? ImageBytes { get; set; }
    public Rect? Crop { get; set; }
    public Stretch Stretch { get; set; } = Stretch.Uniform;
    public string EmptyHint { get; set; }
    public string LoadingHint { get; set; }
    public string ErrorHint { get; set; }
    public IBrush? HintBrush { get; set; }
    public double HintFontSize { get; set; } = 12;
}
```

- 只从 `ImageBytes` 解码；不读文件、不读模块值、不实现 `IModuleViewElement`。
- 静态图一个 `Bitmap`；GIF 多帧 `WriteableBitmap`，解码在后台线程，动画由 `RequestAnimationFrame` 驱动。
- 裁切只改绘制源矩形，不重新解码。
- 窗口缩放只重算目标矩形。
- 尺寸上限沿用 32 MB / 单边 20,000 / 总像素 3200 万 / GIF 512 帧。

## 6. 验证

- 真实 `RuntimeServices` + `ModuleViewImageStore`：
  - 写控件图片不改变模块壁纸；
  - 清壁纸不清图片；
  - 清图片不清壁纸；
  - 组合件包/扁平 JSON 往返恢复图片、裁切与壁纸；
  - 同一份字节作壁纸和图片只落一个资产文件；
  - 已有壁纸时替换图片，壁纸不变；保存重开后各自恢复；
  - 清除壁纸后实例视图的 `WallpaperRevision` 归零。
- 导入：带图片的组合件包导入后，图片资产被复制并可加载；扁平 JSON 导入复制同目录 `assets/`。
- 真实视图回归：设置图片和不同壁纸后刷新，清壁纸图片仍在，清图片才变空态；空态空白处右键可弹出菜单。
- 模块契约：零值、零输入、零端口、零动作。
- 截图：空态、铺满、裁切、GIF、控件图片状态。
- 系统文件选择/裁切窗口仍由人工验收。
