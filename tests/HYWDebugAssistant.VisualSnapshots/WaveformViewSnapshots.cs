using Avalonia.Controls;
using HYWDebugAssistant.Modules.WaveformDisplay.Views;
using HYWDebugAssistant.Modules.WaveformGenerator.Views;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Geometry;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>波形发生器/显示器视图的截图（含合成数据；没有断言）。</summary>
[Collection("visual-snapshots")]
public class WaveformViewSnapshots
{
    private static readonly IModuleColorPicker ColorPicker = new SnapshotColorPicker();

    private static readonly string[] GeneratorFormControls =
    [
        "GroupNameInput", "GroupIdInput", "FrequencyInput", "PeriodInput",
        "ChannelNameInput", "ChannelEnabledToggle", "WaveformType",
        "PhaseInput", "AmplitudeInput", "DutyInput",
        "CarrierFrequencyInput", "ModulationIndexInput", "CarrierPhaseInput",
    ];

    [Fact]
    public async Task 生成快照_波形发生器() =>
        await SaveAsync(
            new WaveformGeneratorViewFactory(),
            new GeneratorSnapshotContext(),
            1002,
            368,
            "modules/waveform-generator.png");

    [Fact]
    public async Task 生成快照_波形发生器_组属性() =>
        await SaveAsync(
            new WaveformGeneratorViewFactory(),
            new GeneratorSnapshotContext(groupSelected: true),
            1002,
            368,
            "modules/waveform-generator-group.png");

    [Fact]
    public async Task 生成快照_波形发生器_SPWM() =>
        await SaveAsync(
            new WaveformGeneratorViewFactory(),
            new GeneratorSnapshotContext(kind: "spwm"),
            1002,
            368,
            "modules/waveform-generator-spwm.png");

    [Fact]
    public async Task 生成快照_波形发生器_方波() =>
        await SaveAsync(
            new WaveformGeneratorViewFactory(),
            new GeneratorSnapshotContext(kind: "square"),
            1002,
            368,
            "modules/waveform-generator-square.png");

    [Fact]
    public async Task 生成快照_波形发生器_无刷新预览() =>
        await SavePreviewAsync(
            new WaveformGeneratorViewFactory(),
            new GeneratorSnapshotContext(),
            1002,
            368,
            "modules/waveform-generator-preview.png");

    [Fact]
    public async Task 生成快照_波形显示器_无刷新预览() =>
        await SavePreviewAsync(
            new WaveformDisplayViewFactory(),
            new DisplaySnapshotContext(),
            1002,
            588,
            "modules/waveform-display-preview.png");

    [Fact]
    public async Task 发生器_无刷新时参数控件应完整可见()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new WaveformGeneratorViewFactory().Create(new GeneratorSnapshotContext());
                var host = new ModuleViewHost(new GeneratorSnapshotContext(), view);
                var window = new Window
                {
                    Width = 960,
                    Height = 320,
                    Content = new Border
                    {
                        Margin = ModuleSurfaceMetrics.ContentMargin,
                        Child = host,
                    },
                };
                window.Show();
                SnapshotTool.Pump();

                foreach (var name in GeneratorFormControls)
                {
                    var control = view.FindControl<Control>(name);
                    Assert.NotNull(control);
                    Assert.True(control!.IsVisible, name + " 在无刷新预览中不可见");
                }

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 发生器_参数行应随波形隐藏()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var sine = VisibilityFor("sine");
                Assert.True(sine.Base);
                Assert.True(sine.Phase);
                Assert.False(sine.Duty);
                Assert.False(sine.Carrier);

                var square = VisibilityFor("square");
                Assert.True(square.Base);
                Assert.True(square.Duty);
                Assert.False(square.Carrier);

                var spwm = VisibilityFor("spwm");
                Assert.True(spwm.Base);
                Assert.False(spwm.Duty);
                Assert.True(spwm.Carrier);

                var noise = VisibilityFor("noise");
                Assert.True(noise.Base);
                Assert.False(noise.Phase);

                var groupSelected = VisibilityFor("sine", groupSelected: true);
                Assert.False(groupSelected.Parameters);
            },
            CancellationToken.None);
    }

    private static (bool Parameters, bool Base, bool Phase, bool Duty, bool Carrier) VisibilityFor(
        string kind,
        bool groupSelected = false)
    {
        var context = new GeneratorSnapshotContext(kind, groupSelected);
        var view = new WaveformGeneratorViewFactory().Create(context);
        var host = new ModuleViewHost(context, view);
        var window = new Window
        {
            Width = 960,
            Height = 320,
            Content = new Border
            {
                Margin = ModuleSurfaceMetrics.ContentMargin,
                Child = host,
            },
        };

        try
        {
            window.Show();
            SnapshotTool.Pump();
            host.Refresh();
            SnapshotTool.Pump();
            return (
                view.FindControl<Control>("ParameterRows")?.IsVisible ?? false,
                view.FindControl<Control>("BaseParameterRow")?.IsVisible ?? false,
                view.FindControl<Control>("PhaseCell")?.IsVisible ?? false,
                view.FindControl<Control>("DutyParameterRow")?.IsVisible ?? false,
                view.FindControl<Control>("CarrierParameterRow")?.IsVisible ?? false);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task 生成快照_波形显示器() =>
        await SaveAsync(
            new WaveformDisplayViewFactory(),
            new DisplaySnapshotContext(),
            1002,
            588,
            "modules/waveform-display.png");

    [Fact]
    public async Task 生成快照_波形显示器_无数据() =>
        await SaveAsync(
            new WaveformDisplayViewFactory(),
            new DisplaySnapshotContext(empty: true),
            1002,
            588,
            "modules/waveform-display-empty.png");

    [Fact]
    public async Task 波形显示器_初始尺寸应镜像给设计预览属性()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new WaveformDisplayViewFactory().Create(new DisplaySnapshotContext());
                Assert.Equal(960, Design.GetWidth(view));
                Assert.Equal(540, Design.GetHeight(view));
            },
            CancellationToken.None);
    }

    private static async Task SavePreviewAsync(
        IModuleViewFactory factory,
        IModuleViewContext context,
        double width,
        double height,
        string path)
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var previousPreviewHost = ModuleView.IsPreviewHost;
                ModuleView.IsPreviewHost = true;

                try
                {
                    var view = factory.Create(context);
                    var host = new ModuleViewHost(context, view);
                    var window = new Window
                    {
                        Width = width,
                        Height = height,
                        Content = new Border
                        {
                            Margin = ModuleSurfaceMetrics.ContentMargin,
                            Child = host,
                        },
                    };

                    window.Show();
                    SnapshotTool.Pump();
                    // 故意不调用 host.Refresh()：模拟 Avalonia 预览器。
                    SnapshotTool.Save(window, path);
                    window.Close();
                }
                finally
                {
                    ModuleView.IsPreviewHost = previousPreviewHost;
                }
            },
            CancellationToken.None);
    }

    private static async Task SaveAsync(
        IModuleViewFactory factory,
        IModuleViewContext context,
        double width,
        double height,
        string path)
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = factory.Create(context);
                var host = new ModuleViewHost(context, view);
                var window = new Window
                {
                    Width = width,
                    Height = height,
                    Content = new Border
                    {
                        Margin = ModuleSurfaceMetrics.ContentMargin,
                        Child = host,
                    },
                };

                window.Show();
                SnapshotTool.Pump();
                host.Refresh();
                SnapshotTool.Pump();
                SnapshotTool.Save(window, path);
                window.Close();
            },
            CancellationToken.None);
    }

    private abstract class BaseSnapshotContext : IModuleViewContext
    {
        public string InstanceId => "waveform-snapshot";

        public virtual IModuleColorPicker? ColorPicker => null;

        public string MultilineSubmitKey => "CtrlEnter";

        public virtual double? ReadLatest(string source) => source switch
        {
            "latestIndex" => 199,
            "rate" => 1000,
            _ => 0,
        };

        public virtual IReadOnlyList<double> ReadSeries(string source) => [];

        public virtual SeriesWindowSnapshot? ReadSeriesWindow(string source) => null;

        public virtual TextBufferSnapshot ReadText(string source) => new(["就绪"], 0, 20000, 1);

        public virtual long ReadTextRevision(string source) => 1;

        public bool SubmitInput(string source, string text) => true;

        public CommandOutcome InvokeCommand(string command) => CommandOutcome.Ok;

        public ConfigFieldView? ReadField(string fieldName) => null;

        public CommandOutcome UpdateField(string fieldName, string value) => CommandOutcome.Ok;

        public ViewAppearance Appearance(string source) => ViewAppearance.None;

        public string? ReadInputText(string source) => null;

        public void WriteInputText(string source, string text)
        {
        }

        public int? SelectedPage(string stateKey) => null;

        public void SetSelectedPage(string stateKey, int page)
        {
        }
    }

    private sealed class GeneratorSnapshotContext : BaseSnapshotContext
    {
        private readonly string _snapshot;
        private readonly string _kind;
        private readonly bool _groupSelected;

        public GeneratorSnapshotContext(string kind = "sine", bool groupSelected = false)
        {
            _kind = kind;
            _groupSelected = groupSelected;
            var activeChannel = groupSelected ? -1 : 0;
            _snapshot =
                "{\"revision\":1,\"structureRevision\":1,\"groups\":["
                + "{\"index\":0,\"id\":1,\"name\":\"G1\",\"enabled\":true,\"frequency\":10,\"rate\":1000,"
                + "\"channels\":["
                + "{\"index\":0,\"name\":\"CH1\",\"kind\":\""
                + kind
                + "\",\"enabled\":true,\"phase\":0,\"offset\":0,\"amplitude\":1,"
                + "\"resolution\":100,\"duty\":0.5,\"carrierFrequency\":100,\"modulationIndex\":0.8,\"carrierPhase\":0},"
                + "{\"index\":1,\"name\":\"CH2\",\"kind\":\"square\",\"enabled\":true,"
                + "\"phase\":0,\"offset\":0,\"amplitude\":0.5,\"resolution\":50,\"duty\":0.5,"
                + "\"carrierFrequency\":100,\"modulationIndex\":0.8,\"carrierPhase\":0}]},"
                + "{\"index\":1,\"id\":2,\"name\":\"G2\",\"enabled\":false,\"frequency\":5,\"rate\":500,"
                + "\"channels\":["
                + "{\"index\":0,\"name\":\"CH1\",\"kind\":\"triangle\",\"enabled\":false,"
                + "\"phase\":0,\"offset\":0,\"amplitude\":1,\"resolution\":100,\"duty\":0.5,"
                + "\"carrierFrequency\":100,\"modulationIndex\":0.8,\"carrierPhase\":0}]}],"
                + "\"activeGroupIndex\":0,\"activeChannelIndex\":" + activeChannel.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"oneShotPeriods\":5,\"estimatedFrames\":500,\"estimatedBytes\":140000,"
                + "\"totalEstimatedFrames\":500,\"totalEstimatedBytes\":140000,\"dropped\":0,"
                + "\"sending\":false,\"continuous\":false}";
        }

        public override IModuleColorPicker? ColorPicker => WaveformViewSnapshots.ColorPicker;

        public override SeriesWindowSnapshot? ReadSeriesWindow(string source)
        {
            if (source != WaveformGeneratorViewKeys.Preview)
            {
                return null;
            }

            if (_groupSelected)
            {
                return new SeriesWindowSnapshot(
                    1,
                    0,
                    2,
                    2,
                    0,
                    -1.2,
                    1.2,
                    true,
                    [
                        new SeriesWindowChannel(0, "CH1", null, BuildValues("sine", 200)),
                        new SeriesWindowChannel(1, "CH2", null, BuildValues("square", 200)),
                    ]);
            }

            return new SeriesWindowSnapshot(
                1,
                0,
                2,
                2,
                0,
                -1.2,
                1.2,
                true,
                [new SeriesWindowChannel(0, "CH1", null, BuildValues(_kind, 200))]);
        }

        public override TextBufferSnapshot ReadText(string source) => source switch
        {
            WaveformGeneratorViewKeys.Snapshot => new([_snapshot], 0, 20000, 1),
            WaveformGeneratorViewKeys.Status => new(["就绪"], 0, 20000, 1),
            _ => base.ReadText(source),
        };

        private static float[] BuildValues(string kind, int count)
        {
            var values = new float[count];
            for (var index = 0; index < count; index++)
            {
                var time = index / 1000d;
                if (kind == "spwm")
                {
                    var reference = 0.8 * Math.Sin(2 * Math.PI * 10 * time);
                    var fraction = (100 * time) - Math.Floor(100 * time);
                    var carrier = (2 * Math.Abs((2 * fraction) - 1)) - 1;
                    values[index] = reference >= carrier ? 1f : -1f;
                }
                else if (kind == "square")
                {
                    values[index] = Math.Sin(index * 2 * Math.PI / 100) >= 0 ? 0.5f : -0.5f;
                }
                else
                {
                    values[index] = (float)Math.Sin(index * 2 * Math.PI / 100);
                }
            }

            return values;
        }
    }

    private sealed class DisplaySnapshotContext : BaseSnapshotContext
    {
        private readonly bool _empty;

        public DisplaySnapshotContext(bool empty = false) => _empty = empty;

        private const string Meta =
            "{\"revision\":1,\"structureRevision\":1,\"activeGroupId\":1,\"activeChannelIndex\":0,\"groups\":[{\"id\":1,\"name\":\"组 1\",\"channels\":2,\"dataCount\":200,\"declaredRate\":1000,\"actualRate\":998,\"lost\":0,\"received\":200,\"bytes\":1600,\"active\":true,\"yAuto\":true,\"yMin\":-1,\"yMax\":1,\"yFitChannel\":-1},{\"id\":2,\"name\":\"组 2\",\"channels\":1,\"dataCount\":100,\"declaredRate\":500,\"actualRate\":500,\"lost\":2,\"received\":100,\"bytes\":400,\"active\":false,\"yAuto\":true,\"yMin\":-1,\"yMax\":1,\"yFitChannel\":-1}],\"channels\":[{\"index\":0,\"name\":\"CH1\",\"color\":null,\"visible\":true},{\"index\":1,\"name\":\"CH2\",\"color\":null,\"visible\":true}],\"settings\":{\"xAxisMode\":\"index\",\"xTick\":0,\"yTick\":0,\"follow\":true,\"followRange\":256,\"cursors\":true,\"keepInactive\":true,\"historyLimit\":0},\"badFrames\":0,\"historyLimited\":false}";

        private const string EmptyMeta =
            "{\"revision\":1,\"structureRevision\":0,\"activeGroupId\":0,\"activeChannelIndex\":0,\"groups\":[],\"channels\":[],\"settings\":{\"xAxisMode\":\"index\",\"xTick\":0,\"yTick\":0,\"follow\":true,\"followRange\":256,\"cursors\":true,\"keepInactive\":true,\"historyLimit\":0},\"badFrames\":0,\"historyLimited\":false}";

        public override IModuleColorPicker? ColorPicker => WaveformViewSnapshots.ColorPicker;

        public override double? ReadLatest(string source) => source switch
        {
            WaveformDisplayViewKeys.LatestIndex => _empty ? 0 : 199,
            WaveformDisplayViewKeys.Rate => _empty ? 0 : 1000,
            WaveformDisplayViewKeys.HistoryUsed => _empty ? 0 : 100,
            WaveformDisplayViewKeys.YAuto => 1,
            WaveformDisplayViewKeys.Follow => 1,
            WaveformDisplayViewKeys.Cursors => 1,
            _ => base.ReadLatest(source),
        };

        public override SeriesWindowSnapshot? ReadSeriesWindow(string source)
        {
            if (source != WaveformDisplayViewKeys.Series)
            {
                return null;
            }

            if (_empty)
            {
                return new SeriesWindowSnapshot(1, 0, 255, 0, 0, -1, 1, true, []);
            }

            const int start = 40;
            const int end = 160;
            var count = end - start + 1;
            var first = new float[count];
            var second = new float[count];
            for (var index = 0; index < count; index++)
            {
                var sample = start + index;
                first[index] = (float)Math.Sin(sample * 2 * Math.PI / 100);
                second[index] = (float)Math.Sin((sample * 2 * Math.PI / 100) + Math.PI);
            }

            return new SeriesWindowSnapshot(
                1,
                start,
                end,
                199,
                1000,
                -1.2,
                1.2,
                true,
                [
                    new SeriesWindowChannel(0, "CH1", null, first),
                    new SeriesWindowChannel(1, "CH2", null, second),
                ]);
        }

        public override TextBufferSnapshot ReadText(string source) => source switch
        {
            WaveformDisplayViewKeys.Meta => new([_empty ? EmptyMeta : Meta], 0, 20000, 1),
            WaveformDisplayViewKeys.Status => new(["接收中"], 0, 20000, 1),
            WaveformDisplayViewKeys.FollowRange => new(["256"], 0, 20000, 1),
            WaveformDisplayViewKeys.YMin => new(["-1"], 0, 20000, 1),
            WaveformDisplayViewKeys.YMax => new(["1"], 0, 20000, 1),
            WaveformDisplayViewKeys.XTick => new(["0"], 0, 20000, 1),
            WaveformDisplayViewKeys.YTick => new(["0"], 0, 20000, 1),
            WaveformDisplayViewKeys.ImageRequest => new([string.Empty], 0, 20000, 1),
            _ => base.ReadText(source),
        };
    }

    private sealed class SnapshotColorPicker : IModuleColorPicker
    {
        public IReadOnlyList<uint> SuggestedColors { get; } =
        [
            0xFF2563EB,
            0xFF0891B2,
            0xFF16A34A,
            0xFFEA580C,
            0xFFDC2626,
            0xFF7C3AED,
            0xFFDB2777,
            0xFF52525B,
        ];

        public Task<ModuleColorPickResult> PickAsync(
            Window? owner,
            uint? initialArgb,
            string? title,
            string? resetText) =>
            Task.FromResult(new ModuleColorPickResult(false, initialArgb ?? 0, false));
    }
}
