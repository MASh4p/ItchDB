using ApexCharts;
using ItchDB.Web.Services;

namespace ItchDB.Web.Components.Charts;

internal static class ChartTheme
{
    public static readonly string[] Palette = [ItchTheme.Accent, "#5CC8FA", "#4CC38A", "#F5B84A", "#B98CFF", "#FF8FB1", "#7FD1C7"];

    public static ApexChartOptions<T> Options<T>() where T : class => new()
    {
        Chart = new Chart
        {
            Background = "transparent",
            FontFamily = "Segoe UI, Inter, Roboto, sans-serif",
            Toolbar = new Toolbar { Show = false },
            Zoom = new Zoom { Enabled = false },
            Animations = new Animations { Enabled = false },
        },
        Theme = new Theme { Mode = Mode.Dark },
        Grid = new Grid { BorderColor = "rgba(255,255,255,0.08)" },
        Legend = new Legend { Position = LegendPosition.Top, HorizontalAlign = ApexCharts.Align.Left },
        Tooltip = new Tooltip { Theme = Mode.Dark },
    };
}
