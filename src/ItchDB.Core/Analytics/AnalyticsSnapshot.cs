using System.Collections.Concurrent;
using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;

namespace ItchDB.Core.Analytics;

/// <summary>
/// A loaded dataset with memoized reports per filter. Pages take one snapshot per render,
/// so every number on a page comes from the same data even while a reload is running.
/// </summary>
public sealed class AnalyticsSnapshot(AnalyticsDataset data, AnalyticsParams p, DateTime now, UiLanguage language = UiLanguage.Ru)
{
    /// <summary>Tags with fewer known games are not ranked (too noisy for the opportunity map).</summary>
    public const int MinTagMembers = 25;

    private readonly ConcurrentDictionary<string, Lazy<object>> _cache = new();

    public static readonly AnalyticsSnapshot Empty = new(AnalyticsDataset.Empty, new AnalyticsParams(), DateTime.UtcNow);

    public AnalyticsDataset Data { get; } = data;
    public AnalyticsParams Params { get; } = p;
    public DateTime Now { get; } = now;
    public UiLanguage Language { get; } = language;
    public bool IsEmpty => Data.IsEmpty;

    public T Cached<T>(string key, Func<T> compute) where T : class
    {
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<object>(() => compute(), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return (T)lazy.Value;
        }
        catch
        {
            _cache.TryRemove(key, out _);
            throw;
        }
    }

    public IReadOnlyList<GameRow> Games(AnalyticsFilter f) =>
        f.ShowNsfw ? Data.Games : Cached($"games|{f.Key}", () => Data.Games.Where(f.Includes).ToList());

    public SegmentAnalyzer Analyzer(AnalyticsFilter f) => Cached($"analyzer|{f.Key}", () => new SegmentAnalyzer(Data, f, Params, Now));

    public SegmentStats All(AnalyticsFilter f) => Cached($"all|{f.Key}", () => Analyzer(f).Compute(Segments.AllOf(Language), Games(f)));

    public List<SegmentStats> Genres(AnalyticsFilter f) => Cached($"genres|{f.Key}", () => Analyzer(f).ComputeMany(Segments.Genres(Data, Language)));

    public List<SegmentStats> Tags(AnalyticsFilter f) => Cached($"tags|{f.Key}", () => Analyzer(f).ComputeMany(
        Segments.OfKind(Data, TagKind.Tag, MinTagMembers, language: Language).Where(s => f.ShowNsfw || !ItchCatalog.NsfwTags.Contains(s.Key))));

    public List<SegmentStats> Engines(AnalyticsFilter f) => Cached($"engines|{f.Key}", () => Analyzer(f).ComputeMany(
        Segments.OfKind(Data, TagKind.Engine, MinTagMembers, language: Language)));

    public List<SegmentStats> Formats(AnalyticsFilter f) => Cached($"formats|{f.Key}", () => Analyzer(f).ComputeMany(
        Segments.Formats(Data, MinTagMembers, Language)));

    public List<SegmentStats> Languages(AnalyticsFilter f) => Cached($"languages|{f.Key}", () => Analyzer(f).ComputeMany(
        Segments.OfKind(Data, TagKind.Language, MinTagMembers, language: Language), rank: false));

    /// <summary>Stats of any segment by key: a ranked one when available, otherwise computed for the tag with that slug.</summary>
    public SegmentStats? Segment(AnalyticsFilter f, string key)
    {
        if (key == Segments.All.Key) return All(f);
        var ranked = Genres(f).Concat(Tags(f)).Concat(Engines(f)).Concat(Formats(f)).FirstOrDefault(s => s.Key == key);
        if (ranked is not null) return ranked;
        return Data.Tag(key) is { } t ? Cached($"segment|{key}|{f.Key}", () => Analyzer(f).Compute(Segments.FromTag(t, Segments.DisplayName(t, Language)))) : null;
    }

    public DriversReport Drivers(AnalyticsFilter f) => Cached($"drivers|{f.Key}", () => Analytics.Drivers.Analyze(Data, f, Params, Now, language: Language));

    public List<TagComboResult> Combos(AnalyticsFilter f, int? withTag = null) =>
        Cached($"combos|{withTag}|{f.Key}", () => TagCombos.Compute(Data, f, Params, onlyWithTag: withTag));

    public List<YearPoint> Years(AnalyticsFilter f) => Cached($"years|{f.Key}", () => Trends.ByYear(Data, f, Params));

    public List<Insight> Insights(AnalyticsFilter f) => Cached($"insights|{f.Key}", () => InsightGenerator.Build(new InsightInput
    {
        Data = Data,
        Filter = f,
        Params = Params,
        All = All(f),
        Genres = Genres(f),
        Tags = Tags(f),
        Formats = Formats(f),
        Drivers = Drivers(f),
        Combos = Combos(f),
        TrendCohorts = Analyzer(f).TrendCohorts,
        Language = Language,
    }));

    /// <summary>Stats of every segment a game belongs to, used for the game card.</summary>
    public List<SegmentStats> SegmentsOf(AnalyticsFilter f, GameRow g)
    {
        var list = new List<SegmentStats>();
        foreach (var id in g.TagIds)
            if (Data.Tags.TryGetValue(id, out var t) && Segment(f, t.Slug) is { } s) list.Add(s);
        return list;
    }
}
