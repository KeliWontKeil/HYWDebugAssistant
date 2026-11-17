namespace HYWDebugAssistant.Modules.TextSender.Views;

/// <summary>**文本发送器视图的键**（XAML 只经 <c>{x:Static}</c> 引用）。</summary>
public static class TextSenderViewKeys
{
    public const string Text1 = TextSenderModule.Text1;
    public const string Text2 = TextSenderModule.Text2;
    public const string Text3 = TextSenderModule.Text3;
    public const string Text4 = TextSenderModule.Text4;
    public const string Text5 = TextSenderModule.Text5;
    public const string Continuous = TextSenderModule.ContinuousSource;
    public const string Interval = TextSenderModule.IntervalSource;
    public const string NewlineCr = TextSenderModule.NewlineCrSource;
    public const string NewlineLf = TextSenderModule.NewlineLfSource;
    public const string SubmitKey = TextSenderModule.SubmitKeySource;

    /// <summary>回车写入哪种换行（由 CR/LF 勾选派生）。</summary>
    public const string EnterNewline = TextSenderModule.EnterNewlineSource;
    public const string Sent = TextSenderModule.SentSource;
    public const string Status = TextSenderModule.StatusSource;
    public const string Send = TextSenderModule.SendCommand;
    public const string PagesStateKey = TextSenderModule.PagesStateKey;
}
