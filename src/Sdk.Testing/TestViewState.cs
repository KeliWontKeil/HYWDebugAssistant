using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Sdk.Testing;

/// <summary>
/// **模块级测试用的视图状态**（T6 契约测试套件）〔2026-09 · `decisions.md` 7.28〕。
/// </summary>
/// <remarks>
/// 模块作者在自己的测试里验证"模块读视图状态"这条路径：**可写**（测试预置值）**+ 可读**（模块读同一份）。
/// 与框架侧的区别只在"谁来写"——框架里写方是 UI（切页 / 编辑 / 追加），测试里写方是用例本身。
/// </remarks>
public sealed class TestViewState : IViewStateReader
{
    private readonly Dictionary<string, int> _selectedPages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _inputTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _displayLines = new(StringComparer.Ordinal);

    /// <summary>预置"某容器当前选中页"。</summary>
    /// <param name="stateKey">容器的 <c>ViewElement.StateKey</c>。</param>
    /// <param name="page">选中页序号（0 基）。</param>
    public void SetSelectedPage(string stateKey, int page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateKey);
        _selectedPages[stateKey] = page;
    }

    /// <summary>预置"某输入源当前文本"。</summary>
    /// <param name="sourceName">输入源名。</param>
    /// <param name="text">当前文本（含"未提交"语义 —— 就是这个值）。</param>
    public void SetInputText(string sourceName, string? text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (text is null)
        {
            _ = _inputTexts.Remove(sourceName);
        }
        else
        {
            _inputTexts[sourceName] = text;
        }
    }

    /// <summary>预置"某显示源保留的文本"（整体替换）。</summary>
    /// <param name="sourceName">显示源名。</param>
    /// <param name="lines">文本行。</param>
    public void SetDisplayLines(string sourceName, params string[] lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(lines);
        _displayLines[sourceName] = [.. lines];
    }

    /// <summary>
    /// **追加文本到某显示源**〔2026-09 用户裁定 · WinForms 模型〕：与框架侧的拼装规则**逐字一致**
    /// （"追加数据应直接追加在后面，不做任何标记"）。
    /// </summary>
    /// <param name="sourceName">显示源名。</param>
    /// <param name="text">文本（原样）。</param>
    /// <param name="lineBreak">这一段之后是否由**通道**换行。</param>
    public void AppendDisplay(string sourceName, string text, bool lineBreak)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(text);

        if (!_displayLines.TryGetValue(sourceName, out var lines))
        {
            lines = [];
            _displayLines[sourceName] = lines;
        }

        if (lineBreak || lines.Count == 0)
        {
            lines.Add(text);
            return;
        }

        lines[^1] += text;
    }

    /// <summary>清空某显示源（模块的「清空」在测试替身里的落点）。</summary>
    /// <param name="sourceName">显示源名。</param>
    public void ClearDisplay(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        _ = _displayLines.Remove(sourceName);
    }

    /// <inheritdoc />
    public int? GetSelectedPage(string stateKey) =>
        stateKey is not null && _selectedPages.TryGetValue(stateKey, out var page) ? page : null;

    /// <inheritdoc />
    public string? GetInputText(string sourceName) =>
        sourceName is not null && _inputTexts.TryGetValue(sourceName, out var text) ? text : null;

    /// <inheritdoc />
    public IReadOnlyList<string> GetDisplayLines(string sourceName) =>
        sourceName is not null && _displayLines.TryGetValue(sourceName, out var lines) ? [.. lines] : [];
}
