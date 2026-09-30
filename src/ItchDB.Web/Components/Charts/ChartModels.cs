using ItchDB.Core.Analytics;
using ItchDB.Web.Services;

namespace ItchDB.Web.Components.Charts;

public sealed record ChartPoint(string Label, double Value);

public enum ChartKind
{
    Bar,
    Line,
    Area,
}

public sealed record ChartSeries(string Name, IReadOnlyList<ChartPoint> Points, ChartKind Kind = ChartKind.Line, bool SecondAxis = false, string? Color = null);

public static class ChartData
{
    /// <summary>ApexCharts cannot draw NaN or infinity; such points become 0.</summary>
    public static decimal Safe(double v) => double.IsFinite(v) ? (decimal)Math.Round(v, 4) : 0m;

    public static string Key(IEnumerable<ChartSeries> series) =>
        string.Join(";", series.Select(s => s.Name + ":" + string.Join(",", s.Points.Select(p => p.Label + "=" + p.Value.ToString("R")))));

    /// <summary>Publication-year cohorts: games on itch.io (where the year filter exists), games in the database and their median ratings.</summary>
    public static List<ChartSeries> Years(IReadOnlyList<YearPoint> years)
    {
        var series = new List<ChartSeries>();
        if (years.Any(y => y.TotalItch is not null))
            series.Add(new("Игр на itch.io", years.Select(y => new ChartPoint(y.Year.ToString(), y.TotalItch ?? 0)).ToList(), ChartKind.Bar, Color: "#3A3F58"));
        series.Add(new("В базе", years.Select(y => new ChartPoint(y.Year.ToString(), y.Known)).ToList(), ChartKind.Bar, Color: "#2F6F8F"));
        series.Add(new("Медиана оценок", years.Select(y => new ChartPoint(y.Year.ToString(), double.IsNaN(y.Median) ? 0 : y.Median)).ToList(),
            ChartKind.Line, SecondAxis: true, Color: ItchTheme.Accent));
        return series;
    }
}
