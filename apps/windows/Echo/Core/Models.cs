using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core;

// One model for every source (Audius, Jamendo, uploads, local files). JSON keys match the web `slim()` shape.
public sealed class Track
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("src")] public string Src { get; set; } = "";
    [JsonPropertyName("rawId")] public string? RawId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("artistId")] public string ArtistId { get; set; } = "";
    [JsonPropertyName("cover")] public string? Cover { get; set; }
    [JsonPropertyName("coverSm")] public string? CoverSm { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("genre")] public string? Genre { get; set; }
    [JsonPropertyName("mood")] public string? Mood { get; set; }
    [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
    [JsonPropertyName("bpm")] public double? Bpm { get; set; }
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("releaseDate")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("plays")] public long? Plays { get; set; }
    [JsonPropertyName("downloadable")] public bool Downloadable { get; set; }
    [JsonPropertyName("quality")] public string? Quality { get; set; }
    [JsonPropertyName("stream")] public string? Stream { get; set; }
    [JsonPropertyName("dlOrig")] public string? DlOrig { get; set; }
    [JsonPropertyName("dlMp3")] public string? DlMp3 { get; set; }
    [JsonPropertyName("ext")] public string? Ext { get; set; }
    [JsonPropertyName("instrumental")] public bool? Instrumental { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("link")] public string? Link { get; set; }
    [JsonPropertyName("likedAt")] public long? LikedAt { get; set; }
    [JsonPropertyName("at")] public long? At { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore] public IReadOnlyList<string> TagList => Tags ?? (IReadOnlyList<string>)Array.Empty<string>();
    [JsonIgnore] public string GenreS => Genre ?? "";
    [JsonIgnore] public string MoodS => Mood ?? "";

    public Track Clone()
    {
        var c = (Track)MemberwiseClone();
        c.Tags = Tags?.ToList();
        c.Extra = null;
        return c;
    }
}

public sealed class Playlist
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("desc")] public string? Desc { get; set; }
    [JsonPropertyName("tracks")] public List<Track> Tracks { get; set; } = new();
    [JsonPropertyName("at")] public long At { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

// Taste profile `P` (same keys as web recs.js; extra keys from other clients survive a round trip).
public sealed class Profile
{
    [JsonPropertyName("g")] public Dictionary<string, double> G { get; set; } = new();
    [JsonPropertyName("m")] public Dictionary<string, double> M { get; set; } = new();
    [JsonPropertyName("a")] public Dictionary<string, double> A { get; set; } = new();
    [JsonPropertyName("t")] public Dictionary<string, double> T { get; set; } = new();
    [JsonPropertyName("k")] public Dictionary<string, double> K { get; set; } = new();
    [JsonPropertyName("an")] public Dictionary<string, string> An { get; set; } = new();
    [JsonPropertyName("fav")] public Dictionary<string, string> Fav { get; set; } = new();
    [JsonPropertyName("hist")] public List<Track> Hist { get; set; } = new();
    [JsonPropertyName("skip")] public Dictionary<string, double> Skip { get; set; } = new();
    [JsonPropertyName("ban")] public Dictionary<string, double> Ban { get; set; } = new();
    [JsonPropertyName("banA")] public Dictionary<string, double> BanA { get; set; } = new();
    [JsonPropertyName("banN")] public Dictionary<string, double> BanN { get; set; } = new();
    [JsonPropertyName("likes")] public Dictionary<string, Track> Likes { get; set; } = new();
    [JsonPropertyName("playlists")] public List<Playlist> Playlists { get; set; } = new();
    [JsonPropertyName("decayAt")] public long DecayAt { get; set; }
    [JsonPropertyName("updated")] public long Updated { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    // JSON may contain nulls for missing maps: make sure nothing is null after loading.
    public Profile Fix()
    {
        G ??= new(); M ??= new(); A ??= new(); T ??= new(); K ??= new(); An ??= new(); Fav ??= new();
        Hist ??= new(); Skip ??= new(); Ban ??= new(); BanA ??= new(); BanN ??= new(); Likes ??= new(); Playlists ??= new();
        Hist.RemoveAll(h => h == null || string.IsNullOrEmpty(h.Id));
        foreach (var k in Likes.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) Likes.Remove(k);
        Playlists.RemoveAll(p => p == null);
        foreach (var p in Playlists) p.Tracks ??= new();
        return this;
    }
}

public sealed class WaveSettings
{
    [JsonPropertyName("novelty")] public double Novelty { get; set; } = 50;
    [JsonPropertyName("popularity")] public double Popularity { get; set; } = 50;
    [JsonPropertyName("tempo")] public string Tempo { get; set; } = "any";
    [JsonPropertyName("vocals")] public string Vocals { get; set; } = "any";
    [JsonPropertyName("onlyDl")] public bool OnlyDl { get; set; }
    public WaveSettings Copy() => (WaveSettings)MemberwiseClone();
}

public sealed class User
{
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
}

public sealed class MeResponse
{
    [JsonPropertyName("user")] public User? User { get; set; }
    [JsonPropertyName("jamendo")] public bool Jamendo { get; set; }
}

public sealed class ProfileResponse
{
    [JsonPropertyName("data")] public Profile? Data { get; set; }
}

public sealed class ChartRef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public sealed class ChartList
{
    [JsonPropertyName("charts")] public List<ChartRef> Charts { get; set; } = new();
}

public sealed class ChartItem
{
    [JsonPropertyName("pos")] public int Pos { get; set; }
    [JsonPropertyName("change")] public string? Change { get; set; }
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("with")] public List<string>? With { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("spotify")] public string? Spotify { get; set; }
    [JsonPropertyName("streams")] public long Streams { get; set; }
}

public sealed class Chart
{
    [JsonPropertyName("country")] public string Country { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("items")] public List<ChartItem> Items { get; set; } = new();
}

public sealed class AiPlan
{
    [JsonPropertyName("title")] public string Title { get; set; } = "Подборка";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("queries")] public List<string> Queries { get; set; } = new();
    [JsonPropertyName("genres")] public List<string> Genres { get; set; } = new();
    [JsonPropertyName("moods")] public List<string> Moods { get; set; } = new();
    [JsonPropertyName("bpm_min")] public double? BpmMin { get; set; }
    [JsonPropertyName("bpm_max")] public double? BpmMax { get; set; }
    [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
}

public sealed class AiResponse
{
    [JsonPropertyName("plan")] public AiPlan? Plan { get; set; }
}

// ---- raw source shapes ----
public sealed class AuUser
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class AuTrack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("genre")] public string? Genre { get; set; }
    [JsonPropertyName("mood")] public string? Mood { get; set; }
    [JsonPropertyName("tags")] public string? Tags { get; set; }
    [JsonPropertyName("bpm")] public double? Bpm { get; set; }
    [JsonPropertyName("musical_key")] public string? MusicalKey { get; set; }
    [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("play_count")] public long PlayCount { get; set; }
    [JsonPropertyName("is_downloadable")] public bool IsDownloadable { get; set; }
    [JsonPropertyName("is_streamable")] public bool? IsStreamable { get; set; }
    [JsonPropertyName("is_stream_gated")] public bool IsStreamGated { get; set; }
    [JsonPropertyName("permalink")] public string? Permalink { get; set; }
    [JsonPropertyName("user")] public AuUser? User { get; set; }
    [JsonPropertyName("artwork")] public bool Artwork { get; set; }
}

public sealed class AuList
{
    [JsonPropertyName("data")] public List<AuTrack>? Data { get; set; }
}

public sealed class JaTags
{
    [JsonPropertyName("genres")] public List<string>? Genres { get; set; }
    [JsonPropertyName("vartags")] public List<string>? Vartags { get; set; }
}

public sealed class JaMusicInfo
{
    [JsonPropertyName("vocalinstrumental")] public string? VocalInstrumental { get; set; }
    [JsonPropertyName("tags")] public JaTags? Tags { get; set; }
}

public sealed class JaTrack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("artist_name")] public string? ArtistName { get; set; }
    [JsonPropertyName("artist_id")] public string? ArtistId { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("releasedate")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("musicinfo")] public JaMusicInfo? MusicInfo { get; set; }
    [JsonPropertyName("audiodownload_allowed")] public bool AudioDownloadAllowed { get; set; }
    [JsonPropertyName("audiodownload")] public string? AudioDownload { get; set; }
    [JsonPropertyName("audio")] public string? Audio { get; set; }
    [JsonPropertyName("shareurl")] public string? ShareUrl { get; set; }
}

public sealed class JaList
{
    [JsonPropertyName("results")] public List<JaTrack>? Results { get; set; }
}

public sealed class Upload
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("artist")] public string? Artist { get; set; }
    [JsonPropertyName("genre")] public string? Genre { get; set; }
    [JsonPropertyName("mood")] public string? Mood { get; set; }
    [JsonPropertyName("bpm")] public double? Bpm { get; set; }
    [JsonPropertyName("ext")] public string? Ext { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("cover")] public bool Cover { get; set; }
}

public sealed class UploadList
{
    [JsonPropertyName("data")] public List<Upload>? Data { get; set; }
}

public sealed class IdResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}
