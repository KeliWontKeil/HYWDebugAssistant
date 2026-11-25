# 模块目录

随产品提供的模块。每个模块一册，写清它做什么、界面怎么用、状态句是什么意思。

| 模块 | 标识 | 说明 |
|---|---|---|
| [三轴姿态仪](attitude-display.md) | `hyw/attitude-display` | 把波形帧的三个通道显示为三轴姿态 |
| [指令发送器](command-sender.md) | `hyw/command-sender` | 按列表分组发送文本/HEX |
| [数据显示器](data-monitor.md) | `hyw/data-monitor` | 以特定格式(文本/十六进制)显示接收的字节流 |
| [指针仪表盘](gauge-display.md) | `hyw/gauge-display` | 把波形帧的单个通道显示为指针仪表盘 |
| [HEX 发送器](hex-sender.md) | `hyw/hex-sender` | 以文本形式发送十六进制字符串 |
| [HEX-字符串转换器](hex-string-converter.md) | `hyw/hex-string-converter` | HEX与文本字符串转换小工具 |
| [图片显示器](image-frame.md) | `hyw/image-frame` | 在工作区或模块组合展示本地图片或GIF动图 |
| [文本->波形转换器](raw-to-waveform.md) | `hyw/raw-to-waveform` | 将文本流解析为波形帧 |
| [水平进度条](scale-bar-horizontal.md) | `hyw/scale-bar-horizontal` | 把波形帧的单个通道显示为带刻度的水平进度条 |
| [垂直进度条](scale-bar-vertical.md) | `hyw/scale-bar-vertical` | 把波形帧的单个通道显示为带刻度的垂直进度条 |
| [串口](serial.md) | `hyw/serial` | 将一路串口接入上位机 |
| [TCP 客户端](tcp-client.md) | `hyw/tcp-client` | 将一个TCP客户端接入上位机，以连接到远端TCP服务器进行数据交换 |
| [TCP 服务端](tcp-server.md) | `hyw/tcp-server` | 将一个TCP服务端接入上位机，以启动一个基础的TCP服务器并进行数据交换 |
| [文本发送器](text-sender.md) | `hyw/text-sender` | 发送字符串 |
| [UDP](udp.md) | `hyw/udp` | 将一个UDP端口接入上位机，以与其他UDP端口进行数据交换 |
| [波形显示器](waveform-display.md) | `hyw/waveform-display` | 接收波形帧并绘制曲线图 |
| [波形发生器](waveform-generator.md) | `hyw/waveform-generator` | 多组多通道波形发生器 |
| [波形->文本转换器](waveform-to-raw.md) | `hyw/waveform-to-raw` | 将波形帧编码为文本流 |

## 相关

- [模块管理](../manage-modules.md)：加入现场、导入外部包、移除
- [模块机制](../../concepts/modules.md)：模块怎么被加载与实例化
- [校验与错误行为](../../module-dev/validation.md)：字段红框、状态句与按钮禁用的统一口径
