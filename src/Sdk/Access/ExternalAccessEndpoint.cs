namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// 一条接入的**结构化端点**：地址 + 可选端口（只搬运事实，不拼界面文案）。
/// </summary>
/// <remarks>
/// 适配件只给出"连的是哪里 / 绑的是哪里"这两个事实；怎么措辞由模块自己的文本表决定。
/// 没有端口的接入（如串口）端口为空，拼文本时只出现地址。
/// </remarks>
/// <param name="Address">地址文本（主机名 / IP / 端口名等；由适配件原样给出）。</param>
/// <param name="Port">端口；没有端口语义的接入为 <see langword="null"/>。</param>
public readonly record struct ExternalAccessEndpoint(string Address, int? Port = null);
