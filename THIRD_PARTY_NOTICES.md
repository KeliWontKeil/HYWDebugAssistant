# 第三方组件声明

本产品（何译位调试助手）包含下列第三方组件。各组件的版权归属与许可条件见对应小节，
许可全文见文末。产品自身的许可见 `LICENSE.txt`。

## 图标素材

产品界面与模块包中的 SVG 图标来自以下三个开源图标集，均已按原样使用。

### Fluent UI System Icons

- 版权：Copyright (c) 2020 Microsoft Corporation
- 许可：MIT
- 来源：<https://github.com/microsoft/fluentui-system-icons>
- 用途：波形生成、波形显示、波形转原始数据三个模块的图标。
- 修改：未改动图形；仅重命名文件并登记键名映射。

### Fluent UI MDL2 Icons

- 版权：Copyright (c) Microsoft Corporation
- 许可：MIT
- 来源：<https://github.com/microsoft/fluentui>
- 用途：主窗口活动栏、模块窗口按钮簇、模块清单的分类与占位图标，以及终端显示模块的设置与关闭按钮。
- 修改：未改动图形；仅重命名文件并登记键名映射。

### Lucide

- 版权：Copyright (c) Lucide Contributors
- 许可：ISC
- 来源：<https://lucide.dev>
- 用途：模块内的刷新、清空、删除等线条图标。
- 修改：未改动图形；仅重命名文件并登记键名映射。

## 程序库

产品分发的程序集及其许可：

| 组件 | 版权 | 许可 |
|---|---|---|
| .NET 运行时（`System.*`、`Microsoft.CSharp`、`mscorlib`、宿主与 JIT 等） | Copyright (c) .NET Foundation and Contributors | MIT |
| Avalonia 及 `Avalonia.*` 系列（约 20 个程序集，含 Win32、Skia、HarfBuzz、Vulkan 后端） | Copyright 2013-2026 © The AvaloniaUI Project | MIT |
| ANGLE（`av_libglesv2.dll`） | Copyright 2018 The ANGLE Project Authors | BSD-3-Clause |
| SkiaSharp 与 HarfBuzzSharp（含 `libSkiaSharp.dll`、`libHarfBuzzSharp.dll`） | © Microsoft Corporation | MIT |
| CommunityToolkit.Mvvm | (c) .NET Foundation and Contributors | MIT |
| Microsoft.Extensions.DependencyInjection 及其抽象包 | (c) .NET Foundation and Contributors | MIT |
| MicroCom.Runtime | Copyright 2021 © Nikita Tsukanov | MIT |
| System.IO.Ports | © Microsoft Corporation | MIT |

模块包只携带模块自身程序集与资源，不携带上述框架程序集。
平台专属组件按目标平台取舍：Windows 发布不含 `Tmds.DBus.Protocol` 等其它平台后端。

## MIT 许可

下列条款适用于上文标注 MIT 的组件；各组件各自的版权行为其小节开头所列。

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## ISC 许可

适用于 Lucide。

```text
Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## BSD 3-Clause 许可

适用于 ANGLE。

```text
Copyright 2018 The ANGLE Project Authors.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

    Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

    Redistributions in binary form must reproduce the above
    copyright notice, this list of conditions and the following
    disclaimer in the documentation and/or other materials provided
    with the distribution.

    Neither the name of TransGaming Inc., Google Inc., 3DLabs Inc.
    Ltd., nor the names of their contributors may be used to endorse
    or promote products derived from this software without specific
    prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```
