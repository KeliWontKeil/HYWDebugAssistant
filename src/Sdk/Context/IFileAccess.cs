namespace HYWDebugAssistant.Sdk.Context;

/// <summary>要打开哪种系统选择器。</summary>
public enum FilePickMode
{
    /// <summary>选一个已有文件（打开）。</summary>
    Open,

    /// <summary>选一个要写入的位置（另存为）。</summary>
    Save,

    /// <summary>选一个目录。</summary>
    Folder,
}

/// <summary>
/// **一次文件选择请求**（模块 → 框架）。
/// </summary>
/// <param name="SourceName">
/// 结果投递到哪个输入源名 —— 用户选定后，框架按它调 <c>SubmitInput</c>，
/// 于是结果与"用户自己敲进去一个路径"走**同一条通道**（模块不必学第二种收件方式）。
/// </param>
/// <param name="Mode">打开 / 另存为 / 选目录。</param>
/// <param name="Filter">
/// 过滤串，形如 <c>文本文件 (*.txt)|*.txt</c>；<see langword="null"/> = 不过滤。
/// **原样透传**：翻译成系统选择器认识的形状是宿主的事。
/// </param>
/// <param name="SuggestedFileName">另存为的预填文件名（可空）。</param>
public sealed record FilePickRequest(
    string SourceName,
    FilePickMode Mode,
    string? Filter = null,
    string? SuggestedFileName = null);

/// <summary>
/// **文件选择的请求面**：模块说"帮我弹一次系统选择器"，用户选定后框架把**路径**投回输入通道。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是能力，不是界面元素</b>：文件访问是 IO 能力，它的正确归属是
/// <see cref="ModuleCapability.File"/> 这条声明（"文件读写，限制在用户选择的路径内"）。
/// 把它做成一个按钮型控件，等于让声明面去描述"宿主对话怎么配"（过滤串 / 预填名 / 三种模式），
/// 而那不是界面事实；而且那个控件会把路径写进它自己的输入源，造成"同一个值有两个存放处"。
/// </para>
/// <para>
/// <b>线程</b>：模块在**自己的线程**上调用，本方法**立即返回**；宿主在 UI 刷新节拍里取走执行
/// —— 与 <see cref="IViewDisplayAccess"/> 同一条纪律（框架不跨线程碰 UI）。
/// </para>
/// <para>
/// <b>结果只有一条路</b>：选定的路径经 <c>SourceName</c> 投回输入通道。
/// 不另开"模块收结果"的通道：收件箱只有出、没有入，"再开一条"等于把输入契约复制第二份。
/// </para>
/// <para>
/// <b>没有对话框的文件读写不经过这里</b>：模块拿到路径后自己用 <c>System.IO</c> 读写
/// （异常就地反馈）。本接口只负责"让用户指一个路径"，不替模块做 IO。
/// </para>
/// </remarks>
public interface IFileAccess
{
    /// <summary>
    /// 请求弹一次系统选择器（模块线程调用，立即返回）。
    /// </summary>
    /// <param name="request">请求内容。</param>
    /// <remarks>
    /// 同一 <see cref="FilePickRequest.SourceName"/> 上的**未完成请求会被后一次替换**：
    /// 用户连点两次只该有一个结果，多弹几个对话框是明显的错。
    /// </remarks>
    void Request(FilePickRequest request);
}
