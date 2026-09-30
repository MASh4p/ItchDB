using System.Collections.Concurrent;
using ItchDB.Core.Catalog;
using ItchDB.Core.Domain;
using ItchDB.Infrastructure.Data;
using ItchDB.Infrastructure.Parsing;
using Microsoft.EntityFrameworkCore;

namespace ItchDB.Infrastructure.Crawling;

/// <summary>How the cards of a browse page relate to the list they came from.</summary>
public sealed record CardContext(IReadOnlyList<string> MembershipSlugs, bool HasRatings, int? Year, bool Nsfw, bool Featured = false)
{
    public static readonly CardContext Plain = new([], false, null, false);
}

public sealed record IngestResult(int Inserted, int Updated);

/// <summary>Writes parsed itch.io data into the database (upserts, memberships, daily snapshots).</summary>
public sealed class CatalogIngestor(IDbContextFactory<ItchDbContext> dbFactory)
{
    private readonly ConcurrentDictionary<string, int> _tagIds = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tagLock = new(1, 1);

    public async Task<IngestResult> IngestCardsAsync(IReadOnlyList<ParsedGameCard> cards, CardContext ctx, CancellationToken ct)
    {
        if (cards.Count == 0) return new IngestResult(0, 0);
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var genreSlugs = cards.Select(c => ItchCatalog.GenreFromName(c.GenreName)).Where(g => g is not null)
            .Select(g => new LinkRef(g!.Slug, g.EnglishName));
        var tagIds = await EnsureTagsAsync(ctx.MembershipSlugs.Select(s => new LinkRef(s, "")).Concat(genreSlugs), ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = cards.Select(c => c.Id).Distinct().ToList();
        var games = await db.Games.Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        await UpsertDevelopersAsync(db, cards.Select(c => (c.AuthorId, c.AuthorName, c.AuthorUrl)), now, ct);

        int inserted = 0, updated = 0;
        var memberships = new List<(long GameId, int TagId)>();
        foreach (var card in cards.DistinctBy(c => c.Id))
        {
            if (!games.TryGetValue(card.Id, out var g))
            {
                g = new Game { Id = card.Id, FirstSeenAt = now };
                db.Games.Add(g);
                games[card.Id] = g;
                inserted++;
            }
            else updated++;

            g.Title = card.Title.Length > 0 ? card.Title : g.Title;
            g.Url = card.Url;
            g.ShortText = card.ShortText ?? g.ShortText;
            g.CoverUrl = card.CoverUrl ?? g.CoverUrl;
            g.CoverIsGif = card.CoverIsGif;
            g.DeveloperId = card.AuthorId ?? g.DeveloperId;
            g.AuthorName = card.AuthorName ?? g.AuthorName;
            g.LastSeenAt = now;
            g.IsRemoved = false;

            var genre = ItchCatalog.GenreFromName(card.GenreName);
            if (genre is not null)
            {
                g.Genre = genre.Slug;
                memberships.Add((g.Id, tagIds[genre.Slug]));
            }

            if (card.PriceCents is { } cents)
            {
                g.PriceCents = cents;
                g.Currency = card.Currency ?? g.Currency;
                g.PriceModel = cents > 0 ? PriceModel.Paid : g.PriceModel == PriceModel.NameYourPrice ? PriceModel.NameYourPrice : PriceModel.Free;
                g.SalePercent = card.SalePercent;
            }
            else
            {
                g.PriceCents = 0;
                g.SalePercent = null;
                if (g.PriceModel != PriceModel.NameYourPrice) g.PriceModel = PriceModel.Free;
            }

            if (card.Platforms != Platforms.None) g.Platforms = card.Platforms | (g.Platforms & Platforms.IOS);

            if (ctx.HasRatings)
            {
                g.RatingCount = card.RatingCount ?? 0;
                g.RatingAvg = card.RatingCount > 0 ? card.RatingAvg : null;
                g.RatingsCheckedAt = now;
            }
            if (ctx.Year is { } year) g.PublishedYear = year;
            if (ctx.Nsfw) g.IsNsfw = true;
            if (ctx.Featured)
            {
                g.IsFeatured = true;
                g.FeaturedAt ??= now;
            }

            foreach (var slug in ctx.MembershipSlugs) memberships.Add((g.Id, tagIds[slug]));
        }

        await AddMembershipsAsync(db, memberships, TagSource.Listing, ct);
        if (ctx.HasRatings) await UpsertSnapshotsAsync(db, games.Values.Where(g => ids.Contains(g.Id)), today, ct);
        await db.SaveChangesAsync(ct);
        return new IngestResult(inserted, updated);
    }

    public async Task IngestGameDetailsAsync(GamePageData data, string url, CancellationToken ct)
    {
        if (data.Id is not { } id) return;
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var facets = data.AllFacets.DistinctBy(f => f.Slug).ToList();
        var tagIds = await EnsureTagsAsync(facets, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var g = await db.Games.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null)
        {
            g = new Game { Id = id, FirstSeenAt = now, Url = url };
            db.Games.Add(g);
        }

        if (data.Title.Length > 0) g.Title = data.Title;
        g.Url = string.IsNullOrEmpty(g.Url) ? url : g.Url;
        g.CoverUrl ??= data.CoverUrl;
        g.ShortText ??= data.ShortText;
        if (data.Platforms != Platforms.None) g.Platforms = data.Platforms;
        if (data.Status != GameStatus.Unknown) g.Status = data.Status;
        g.PublishedAt = data.PublishedAt ?? g.PublishedAt;
        g.UpdatedAt = data.UpdatedAt ?? g.UpdatedAt;
        g.ReleaseDate = data.ReleaseDate ?? g.ReleaseDate;
        var dated = g.PublishedAt ?? g.ReleaseDate;
        if (dated is not null && g.PublishedYear is null) g.PublishedYear = dated.Value.Year;

        g.RatingCount = data.RatingCount ?? 0;
        g.RatingAvg = data.RatingCount > 0 ? data.RatingAvg : null;
        g.RatingsCheckedAt = now;
        g.CommentsCount = data.CommentsCount ?? g.CommentsCount;

        if (data.PriceModel != PriceModel.Unknown)
        {
            g.PriceModel = data.PriceModel;
            g.PriceCents = data.PriceCents;
            g.Currency = data.Currency ?? g.Currency;
            g.SalePercent = data.ActualPriceCents is { } actual && data.PriceCents is > 0 && actual < data.PriceCents
                ? (int)Math.Round(100.0 * (data.PriceCents.Value - actual) / data.PriceCents.Value)
                : null;
        }
        if (data.Ai != AiUsage.Unknown) g.Ai = data.Ai;
        if (data.IsNsfw) g.IsNsfw = true;
        if (data.IsFeatured)
        {
            g.IsFeatured = true;
            g.FeaturedAt ??= now;
        }
        g.ScreenshotsCount = data.ScreenshotsCount;
        if (data.JamSlugs.Count > 0) g.IsJamEntry = true;
        var primaryGenre = data.Genres.FirstOrDefault(x => ItchCatalog.IsGenreSlug(x.Slug));
        if (g.Genre is null && primaryGenre is not null) g.Genre = primaryGenre.Slug;

        if (data.UploaderUserId is { } uid && g.DeveloperId is null)
        {
            var author = data.Authors.FirstOrDefault();
            await UpsertDevelopersAsync(db, [(uid, author.Name, author.Url)], now, ct);
            g.DeveloperId = uid;
            g.AuthorName ??= author.Name;
        }

        g.DetailsFetchedAt = now;
        g.DetailsFailures = 0;
        g.IsRemoved = false;
        g.LastSeenAt = now;

        // Page tags replace older page tags; list memberships stay because lists are the ground truth for filters.
        var wanted = facets.Select(f => tagIds[f.Slug]).ToHashSet();
        var existing = await db.GameTags.Where(t => t.GameId == id).ToListAsync(ct);
        foreach (var t in existing)
        {
            if (wanted.Contains(t.TagId)) t.Source |= TagSource.GamePage;
            else if (t.Source == TagSource.GamePage) db.GameTags.Remove(t);
            else t.Source &= ~TagSource.GamePage;
        }
        foreach (var tagId in wanted.Except(existing.Select(e => e.TagId)))
            db.GameTags.Add(new GameTag { GameId = id, TagId = tagId, Source = TagSource.GamePage });

        await UpsertSnapshotsAsync(db, [g], today, ct);
        await LinkJamsAsync(db, id, data.JamSlugs, now, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkDetailsFailedAsync(long gameId, bool removed, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var g = await db.Games.FirstOrDefaultAsync(x => x.Id == gameId, ct);
        if (g is null) return;
        g.DetailsFailures++;
        g.DetailsFetchedAt = DateTime.UtcNow;
        if (removed) g.IsRemoved = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task SavePositionsAsync(DateOnly day, string listKey, int firstPosition, IReadOnlyList<long> gameIds, CancellationToken ct)
    {
        if (gameIds.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var last = firstPosition + gameIds.Count - 1;
        await db.TagPositions
            .Where(p => p.Day == day && p.ListKey == listKey && p.Position >= firstPosition && p.Position <= last)
            .ExecuteDeleteAsync(ct);
        for (var i = 0; i < gameIds.Count; i++)
            db.TagPositions.Add(new TagPositionSnapshot { Day = day, ListKey = listKey, Position = firstPosition + i, GameId = gameIds[i] });

        if (listKey == "popular")
        {
            var ids = gameIds.ToList();
            var games = await db.Games.Where(g => ids.Contains(g.Id)).ToListAsync(ct);
            foreach (var g in games) g.PopularRank = firstPosition + ids.IndexOf(g.Id);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> HasPositionsAsync(DateOnly day, string listKey, int position, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TagPositions.AnyAsync(p => p.Day == day && p.ListKey == listKey && p.Position == position, ct);
    }

    public async Task SaveListingTotalAsync(DateOnly day, string filterKey, int results, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.ListingTotals.FirstOrDefaultAsync(x => x.Day == day && x.FilterKey == filterKey, ct);
        if (row is null) db.ListingTotals.Add(new ListingTotalSnapshot { Day = day, FilterKey = filterKey, Results = results });
        else row.Results = results;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ApplyRssAsync(IReadOnlyList<RssGameItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var urls = items.Select(i => i.Url).Distinct().ToList();
        var games = await db.Games.Where(g => urls.Contains(g.Url)).ToListAsync(ct);
        var byUrl = games.GroupBy(g => g.Url).ToDictionary(x => x.Key, x => x.First());
        var matched = 0;
        foreach (var item in items)
        {
            if (!byUrl.TryGetValue(item.Url, out var g)) continue;
            matched++;
            var published = item.PubDate ?? item.CreateDate;
            if (published is not null && g.PublishedAt is null) g.PublishedAt = published;
            if (item.UpdateDate is { } upd && (g.UpdatedAt is null || upd > g.UpdatedAt)) g.UpdatedAt = upd;
            if (g.PublishedYear is null && (g.PublishedAt ?? published) is { } p) g.PublishedYear = p.Year;
            if (g.Genre is null && ItchCatalog.GenreFromName(item.GenreName) is { } genre) g.Genre = genre.Slug;
            g.Platforms |= item.Platforms;
        }
        await db.SaveChangesAsync(ct);
        return matched;
    }

    public async Task IngestTagCatalogAsync(TagCatalogPage page, DateOnly day, CancellationToken ct)
    {
        var tagIds = await EnsureTagsAsync(page.Entries.Select(e => new LinkRef(e.Slug, e.Name)).Concat(page.AllTags), ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = tagIds.Values.ToList();
        var tags = await db.Tags.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Slug, ct);
        foreach (var named in page.AllTags)
            if (tags.TryGetValue(named.Slug, out var t) && named.Name.Length > 0) t.Name = named.Name;

        var entryIds = page.Entries.Select(e => tagIds[e.Slug]).ToList();
        var snapshots = await db.TagCatalogSnapshots.Where(s => s.Day == day && entryIds.Contains(s.TagId)).ToDictionaryAsync(s => s.TagId, ct);
        foreach (var e in page.Entries)
        {
            var t = tags[e.Slug];
            if (e.Name.Length > 0) t.Name = e.Name;
            t.ProjectsCount = e.ProjectsCount ?? t.ProjectsCount;
            t.ThisWeekCount = e.ThisWeekCount ?? t.ThisWeekCount;
            t.Description = e.Description ?? t.Description;
            t.RelatedTags = e.RelatedSlugs.Count > 0 ? string.Join(' ', e.RelatedSlugs) : t.RelatedTags;
            t.CatalogSeenAt = DateTime.UtcNow;
            if (e.ProjectsCount is null) continue;
            if (!snapshots.TryGetValue(t.Id, out var s))
                db.TagCatalogSnapshots.Add(new TagCatalogSnapshot { Day = day, TagId = t.Id, ProjectsCount = e.ProjectsCount.Value, ThisWeekCount = e.ThisWeekCount ?? 0 });
            else
            {
                s.ProjectsCount = e.ProjectsCount.Value;
                s.ThisWeekCount = e.ThisWeekCount ?? 0;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task IngestProfileAsync(long developerId, ProfileData profile, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dev = await db.Developers.FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev is null) return;
        dev.Followers = profile.Followers ?? dev.Followers;
        dev.Following = profile.Following ?? dev.Following;
        dev.RegisteredAt = profile.RegisteredAt ?? dev.RegisteredAt;
        dev.ProfileFetchedAt = now;
        dev.ProfileFailures = 0;
        if (profile.Followers is { } followers)
        {
            var snap = await db.DeveloperSnapshots.FirstOrDefaultAsync(s => s.DeveloperId == developerId && s.Day == today, ct);
            if (snap is null) db.DeveloperSnapshots.Add(new DeveloperSnapshot { DeveloperId = developerId, Day = today, Followers = followers });
            else snap.Followers = followers;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkProfileFailedAsync(long developerId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dev = await db.Developers.FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev is null) return;
        dev.ProfileFailures++;
        dev.ProfileFetchedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task IngestJamCellsAsync(IReadOnlyList<JamCell> cells, CancellationToken ct)
    {
        if (cells.Count == 0) return;
        var now = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var slugs = cells.Select(c => c.Slug).Distinct().ToList();
        var jams = await db.Jams.Where(j => slugs.Contains(j.Slug)).ToDictionaryAsync(j => j.Slug, ct);
        foreach (var c in cells.DistinctBy(c => c.Slug))
        {
            if (!jams.TryGetValue(c.Slug, out var j))
            {
                j = new Jam { Slug = c.Slug, FirstSeenAt = now };
                db.Jams.Add(j);
                jams[c.Slug] = j;
            }
            j.Title = c.Title.Length > 0 ? c.Title : j.Title;
            j.Hosts = c.Hosts ?? j.Hosts;
            j.CoverUrl = c.CoverUrl ?? j.CoverUrl;
            j.Joined = c.Joined ?? j.Joined;
            j.Submissions = c.Submissions ?? j.Submissions;
            j.IsRanked |= c.IsRanked;
            j.IsFeatured |= c.IsFeatured;
            j.LastSeenAt = now;
            if (c.Date is { } date && j.PageFetchedAt is null)
            {
                switch (c.Phase)
                {
                    case JamPhase.Upcoming: j.StartAt = date; break;
                    case JamPhase.Running: j.EndAt = date; break;
                    case JamPhase.Voting: j.VotingEndAt = date; break;
                    case JamPhase.Ended:
                        if (c.IsRanked) j.VotingEndAt = date;
                        else j.EndAt = date;
                        break;
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task IngestJamPageAsync(int jamId, JamPageData page, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var j = await db.Jams.FirstOrDefaultAsync(x => x.Id == jamId, ct);
        if (j is null) return;
        j.ItchJamId = page.ItchJamId ?? j.ItchJamId;
        j.Title = page.Title ?? j.Title;
        j.Hosts = page.Hosts ?? j.Hosts;
        j.StartAt = page.StartAt ?? j.StartAt;
        j.EndAt = page.EndAt ?? j.EndAt;
        j.VotingEndAt = page.VotingEndAt ?? j.VotingEndAt;
        j.Submissions = page.Entries ?? j.Submissions;
        j.RatingsCount = page.Ratings ?? j.RatingsCount;
        j.Joined = page.Joined ?? j.Joined;
        if (page.VotingEndAt is not null) j.IsRanked = true;
        j.PageFetchedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> IngestJamEntriesAsync(int jamId, IReadOnlyList<JamEntryData> entries, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var chunk in entries.DistinctBy(e => e.GameId).Chunk(400))
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await UpsertDevelopersAsync(db, chunk.Select(e => (e.AuthorId, e.AuthorName, e.AuthorUrl)), now, ct);
            var ids = chunk.Select(e => e.GameId).ToList();
            var games = await db.Games.Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
            var existingEntries = await db.JamEntries.Where(e => e.JamId == jamId && ids.Contains(e.GameId)).ToDictionaryAsync(e => e.GameId, ct);
            foreach (var e in chunk)
            {
                if (!games.TryGetValue(e.GameId, out var g))
                {
                    g = new Game
                    {
                        Id = e.GameId,
                        Title = e.Title,
                        Url = e.Url,
                        CoverUrl = e.CoverUrl,
                        ShortText = e.ShortText,
                        Platforms = e.Platforms,
                        DeveloperId = e.AuthorId,
                        AuthorName = e.AuthorName,
                        FirstSeenAt = now,
                        LastSeenAt = now,
                    };
                    db.Games.Add(g);
                }
                g.IsJamEntry = true;

                if (!existingEntries.TryGetValue(e.GameId, out var entry))
                {
                    entry = new JamEntry { JamId = jamId, GameId = e.GameId };
                    db.JamEntries.Add(entry);
                }
                entry.JamRatingCount = e.RatingCount;
                entry.Coolness = e.Coolness;
                entry.SubmittedAt = e.SubmittedAt;
                entry.Title = e.Title;
                entry.Url = e.Url;
                entry.AuthorId = e.AuthorId;
                entry.AuthorName = e.AuthorName;
            }
            await db.SaveChangesAsync(ct);
        }

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var jam = await db.Jams.FirstOrDefaultAsync(j => j.Id == jamId, ct);
            if (jam is not null)
            {
                jam.EntriesFetchedAt = now;
                await db.SaveChangesAsync(ct);
            }
        }
        return entries.Count;
    }

    public async Task<int> IngestFeaturedAsync(IReadOnlyList<RssGameItem> items, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var urls = items.Select(i => i.Url).Distinct().ToList();
        var known = await db.FeaturedEvents.Where(f => urls.Contains(f.Url)).ToDictionaryAsync(f => f.Url, ct);
        var games = await db.Games.Where(g => urls.Contains(g.Url)).ToListAsync(ct);
        var byUrl = games.GroupBy(g => g.Url).ToDictionary(x => x.Key, x => x.First());
        var added = 0;
        foreach (var item in items.DistinctBy(i => i.Url))
        {
            byUrl.TryGetValue(item.Url, out var g);
            if (!known.TryGetValue(item.Url, out var ev))
            {
                ev = new FeaturedEvent { Url = item.Url, Title = item.Title, FeaturedAt = item.PubDate ?? now, SeenAt = now };
                db.FeaturedEvents.Add(ev);
                added++;
            }
            ev.GameId ??= g?.Id;
            if (g is not null)
            {
                g.IsFeatured = true;
                g.FeaturedAt ??= ev.FeaturedAt;
            }
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task<Dictionary<string, int>> EnsureTagsAsync(IEnumerable<LinkRef> tags, CancellationToken ct)
    {
        var wanted = tags.Where(t => !string.IsNullOrEmpty(t.Slug)).GroupBy(t => t.Slug)
            .Select(g => g.FirstOrDefault(x => x.Name.Length > 0) ?? g.First()).ToList();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var missing = new List<LinkRef>();
        foreach (var t in wanted)
        {
            if (_tagIds.TryGetValue(t.Slug, out var id)) result[t.Slug] = id;
            else missing.Add(t);
        }
        if (missing.Count == 0) return result;

        await _tagLock.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var slugs = missing.Select(m => m.Slug).ToList();
            foreach (var t in await db.Tags.Where(t => slugs.Contains(t.Slug)).ToListAsync(ct))
            {
                _tagIds[t.Slug] = t.Id;
                result[t.Slug] = t.Id;
            }
            var toAdd = missing.Where(m => !result.ContainsKey(m.Slug)).Select(m => new Tag
            {
                Slug = m.Slug,
                Name = m.Name.Length > 0 ? m.Name : ItchCatalog.GenreFromSlug(m.Slug)?.EnglishName ?? ItchCatalog.NameFromSlug(m.Slug),
                Kind = ItchCatalog.KindFromSlug(m.Slug),
            }).ToList();
            if (toAdd.Count > 0)
            {
                db.Tags.AddRange(toAdd);
                await db.SaveChangesAsync(ct);
                foreach (var t in toAdd)
                {
                    _tagIds[t.Slug] = t.Id;
                    result[t.Slug] = t.Id;
                }
            }
        }
        finally
        {
            _tagLock.Release();
        }
        return result;
    }

    private static async Task UpsertDevelopersAsync(ItchDbContext db, IEnumerable<(long? Id, string? Name, string? Url)> authors, DateTime now, CancellationToken ct)
    {
        var list = authors.Where(a => a.Id is not null).DistinctBy(a => a.Id).ToList();
        if (list.Count == 0) return;
        var ids = list.Select(a => a.Id!.Value).ToList();
        var tracked = db.ChangeTracker.Entries<Developer>().Select(e => e.Entity).Where(d => ids.Contains(d.Id)).ToDictionary(d => d.Id);
        var loadIds = ids.Where(i => !tracked.ContainsKey(i)).ToList();
        foreach (var d in await db.Developers.Where(d => loadIds.Contains(d.Id)).ToListAsync(ct)) tracked[d.Id] = d;
        foreach (var (id, name, url) in list)
        {
            if (!tracked.TryGetValue(id!.Value, out var dev))
            {
                dev = new Developer { Id = id.Value, FirstSeenAt = now };
                db.Developers.Add(dev);
                tracked[id.Value] = dev;
            }
            if (!string.IsNullOrWhiteSpace(name)) dev.DisplayName = name;
            if (!string.IsNullOrWhiteSpace(url))
            {
                dev.Url = url;
                dev.Username = ItchCatalog.UsernameFromUrl(url) ?? dev.Username;
            }
        }
    }

    private static async Task AddMembershipsAsync(ItchDbContext db, List<(long GameId, int TagId)> memberships, TagSource source, CancellationToken ct)
    {
        if (memberships.Count == 0) return;
        var distinct = memberships.Distinct().ToList();
        var gameIds = distinct.Select(m => m.GameId).Distinct().ToList();
        var tagIds = distinct.Select(m => m.TagId).Distinct().ToList();
        var existing = await db.GameTags.Where(t => gameIds.Contains(t.GameId) && tagIds.Contains(t.TagId)).ToListAsync(ct);
        var map = existing.ToDictionary(e => (e.GameId, e.TagId));
        foreach (var m in distinct)
        {
            if (map.TryGetValue(m, out var e)) e.Source |= source;
            else db.GameTags.Add(new GameTag { GameId = m.GameId, TagId = m.TagId, Source = source });
        }
    }

    private static async Task UpsertSnapshotsAsync(ItchDbContext db, IEnumerable<Game> games, DateOnly day, CancellationToken ct)
    {
        var list = games.ToList();
        if (list.Count == 0) return;
        var ids = list.Select(g => g.Id).ToList();
        var existing = await db.GameSnapshots.Where(s => s.Day == day && ids.Contains(s.GameId)).ToDictionaryAsync(s => s.GameId, ct);
        foreach (var g in list)
        {
            if (!existing.TryGetValue(g.Id, out var s))
            {
                s = new GameSnapshot { GameId = g.Id, Day = day };
                db.GameSnapshots.Add(s);
            }
            s.RatingCount = g.RatingCount;
            s.RatingAvg = g.RatingAvg;
            s.CommentsCount = g.CommentsCount ?? s.CommentsCount;
        }
    }

    private static async Task LinkJamsAsync(ItchDbContext db, long gameId, IReadOnlyList<string> slugs, DateTime now, CancellationToken ct)
    {
        if (slugs.Count == 0) return;
        var list = slugs.Distinct().ToList();
        var jams = await db.Jams.Where(j => list.Contains(j.Slug)).ToDictionaryAsync(j => j.Slug, ct);
        foreach (var slug in list)
        {
            if (!jams.ContainsKey(slug))
            {
                var jam = new Jam { Slug = slug, Title = slug, FirstSeenAt = now, LastSeenAt = now };
                db.Jams.Add(jam);
                jams[slug] = jam;
            }
        }
        await db.SaveChangesAsync(ct);
        var jamIds = jams.Values.Select(j => j.Id).ToList();
        var linked = await db.JamEntries.Where(e => e.GameId == gameId && jamIds.Contains(e.JamId)).Select(e => e.JamId).ToListAsync(ct);
        foreach (var j in jams.Values.Where(j => !linked.Contains(j.Id)))
            db.JamEntries.Add(new JamEntry { JamId = j.Id, GameId = gameId });
    }
}
