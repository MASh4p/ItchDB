namespace ItchDB.Core.Domain;

public class Jam
{
    public int Id { get; set; }
    public long? ItchJamId { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Hosts { get; set; }
    public string? CoverUrl { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public DateTime? VotingEndAt { get; set; }
    public int? Joined { get; set; }
    public int? Submissions { get; set; }
    public int? RatingsCount { get; set; }
    public bool IsRanked { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? PageFetchedAt { get; set; }
    public DateTime? EntriesFetchedAt { get; set; }

    public List<JamEntry> Entries { get; set; } = [];
}

public class JamEntry
{
    public int JamId { get; set; }
    public long GameId { get; set; }
    public int? JamRatingCount { get; set; }
    public double? Coolness { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public long? AuthorId { get; set; }
    public string? AuthorName { get; set; }

    public Jam? Jam { get; set; }
}
