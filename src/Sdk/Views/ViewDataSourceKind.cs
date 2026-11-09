namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// 可暴露数据源的**类型**（名称 + 类型）——决定订阅缓冲形态与呈现方式。
/// </summary>
/// <remarks>
/// 与元素类型的分工：**数据源类型**描述「模块往里写的是什么」，**元素类型**描述「用户看到什么」
/// （按元素类型判定交接形态：A1/A2/A7 = 最新值槽，A3/A4/A5/A6/A8 = 环形缓冲）。
/// 声明面里这个值写在 `sources[].kind`；省略时由框架 schema 给出缺省。
/// </remarks>
public enum ViewDataSourceKind
{
    /// <summary>单值 / 离散状态（数值）。</summary>
    Value = 0,

    /// <summary>序列（曲线 / 波形）。</summary>
    Sequence = 1,

    /// <summary>行集（表格）。</summary>
    RowSet = 2,

    /// <summary>字节块（十六进制视图）。</summary>
    ByteBlock = 3,

    /// <summary>文本（日志）。</summary>
    Text = 4,

    /// <summary>帧（图像 / 位图）。</summary>
    Frame = 5,

    /// <summary>
    /// 序列窗口快照（不可变对象：一次提供某范围的完整序列）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Sequence"/> 的分工：<see cref="Sequence"/> 是有界环形缓冲（适合滚动窗口）；
    /// 本类由模块直接发布一整个不可变窗口，适合“查看任意选定范围的全部点”，没有环形容量与 UI 降采样上限。
    /// </remarks>
    SeriesWindow = 6,
}
