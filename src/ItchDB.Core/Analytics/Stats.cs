namespace ItchDB.Core.Analytics;

/// <summary>Small, allocation-light statistics helpers.</summary>
public static class Stats
{
    /// <summary>Quantile with linear interpolation between order statistics (the common "type 7" definition).</summary>
    public static double Quantile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0) return double.NaN;
        if (sorted.Count == 1) return sorted[0];
        var pos = Math.Clamp(q, 0, 1) * (sorted.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(lo + 1, sorted.Count - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    public static double Median(IReadOnlyList<double> sorted) => Quantile(sorted, 0.5);

    public static double[] Sorted(IEnumerable<double> values)
    {
        var a = values.ToArray();
        Array.Sort(a);
        return a;
    }

    /// <summary>Ratings of games with at least <paramref name="minRatings"/> ratings, sorted.</summary>
    public static double[] QualifiedRatings(IEnumerable<GameRow> games, int minRatings) =>
        Sorted(games.Where(g => g.Ratings >= minRatings).Select(g => (double)g.Ratings));

    /// <summary>Wilson score interval for a binomial proportion.</summary>
    public static (double Low, double High) Wilson(int successes, int n, double z = 1.96)
    {
        if (n <= 0) return (0, 0);
        var p = (double)successes / n;
        var z2 = z * z;
        var denom = 1 + z2 / n;
        var center = (p + z2 / (2.0 * n)) / denom;
        var half = z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / denom;
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>Shrinks a segment value toward the global value: adj = (n·seg + k·all) / (n + k).</summary>
    public static double Shrink(double segment, int n, double global, double k)
    {
        if (double.IsNaN(segment)) return global;
        if (n + k <= 0) return segment;
        return (n * segment + k * global) / (n + k);
    }

    /// <summary>IMDb-style weighted rating: WR = v/(v+m)·R + m/(v+m)·C.</summary>
    public static double WeightedRating(int votes, double average, int minVotes, double globalMean)
    {
        if (votes <= 0) return globalMean;
        return (double)votes / (votes + minVotes) * average + (double)minVotes / (votes + minVotes) * globalMean;
    }

    /// <summary>Average ranks (1-based) with ties sharing the mean rank.</summary>
    public static double[] Ranks(IReadOnlyList<double> values)
    {
        var n = values.Count;
        var idx = Enumerable.Range(0, n).ToArray();
        Array.Sort(idx, (a, b) => values[a].CompareTo(values[b]));
        var ranks = new double[n];
        for (var i = 0; i < n;)
        {
            var j = i;
            while (j + 1 < n && values[idx[j + 1]].Equals(values[idx[i]])) j++;
            var r = (i + j) / 2.0 + 1;
            for (var k = i; k <= j; k++) ranks[idx[k]] = r;
            i = j + 1;
        }
        return ranks;
    }

    public static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var n = Math.Min(x.Count, y.Count);
        if (n < 3) return double.NaN;
        double mx = 0, my = 0;
        for (var i = 0; i < n; i++)
        {
            mx += x[i];
            my += y[i];
        }
        mx /= n;
        my /= n;
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = x[i] - mx;
            var dy = y[i] - my;
            sxy += dx * dy;
            sxx += dx * dx;
            syy += dy * dy;
        }
        return sxx <= 0 || syy <= 0 ? double.NaN : sxy / Math.Sqrt(sxx * syy);
    }

    public static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y) => Pearson(Ranks(x), Ranks(y));

    /// <summary>Share of values strictly below x plus half of the ties, in [0, 1].</summary>
    public static double PercentRank(IReadOnlyList<double> sorted, double x)
    {
        if (sorted.Count == 0 || double.IsNaN(x)) return 0.5;
        var below = LowerBound(sorted, x);
        var upTo = UpperBound(sorted, x);
        return (below + (upTo - below) / 2.0) / sorted.Count;
    }

    private static int LowerBound(IReadOnlyList<double> a, double x)
    {
        int lo = 0, hi = a.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (a[mid] < x) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(IReadOnlyList<double> a, double x)
    {
        int lo = 0, hi = a.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (a[mid] <= x) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>95% CI of a ratio of two proportions (risk ratio) via the log method.</summary>
    public static (double Ratio, double Low, double High)? RiskRatio(int x1, int n1, int x2, int n2)
    {
        if (x1 <= 0 || x2 <= 0 || n1 <= 0 || n2 <= 0) return null;
        var p1 = (double)x1 / n1;
        var p2 = (double)x2 / n2;
        var rr = p1 / p2;
        var se = Math.Sqrt(1.0 / x1 - 1.0 / n1 + 1.0 / x2 - 1.0 / n2);
        return (rr, Math.Exp(Math.Log(rr) - 1.96 * se), Math.Exp(Math.Log(rr) + 1.96 * se));
    }

    /// <summary>Two-sided p-value of a standard normal statistic.</summary>
    public static double NormalPValue(double z) => 2 * (1 - MathNet.Numerics.Distributions.Normal.CDF(0, 1, Math.Abs(z)));
}
