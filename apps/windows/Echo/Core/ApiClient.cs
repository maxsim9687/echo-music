using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core;

public sealed class ApiException : Exception
{
    public int Status { get; }
    public ApiException(string message, int status) : base(message) { Status = status; }
}

// HttpClient + persisted cookie jar (auth cookie `sid`), X-Echo header on writes, Russian error text from {"error"}.
public static class Api
{
    public const string Base = "https://music.maxsim.space";
    static readonly CookieContainer Jar = new();
    static readonly Dictionary<string, (DateTime at, string body)> Cache = new();
    static readonly object CacheLock = new();
    public static HttpClient Http { get; private set; } = null!;

    public static void Init()
    {
        LoadCookies();
        var h = new HttpClientHandler
        {
            CookieContainer = Jar,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        Http = new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Echo-Windows/1.0");
    }

    public static Uri Abs(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(path) : new Uri(Base + (path.StartsWith('/') ? path : "/" + path));

    public static void ClearCache() { lock (CacheLock) Cache.Clear(); }

    public static async Task<T> Get<T>(string path, TimeSpan ttl = default, CancellationToken ct = default)
    {
        if (ttl > TimeSpan.Zero)
        {
            lock (CacheLock)
                if (Cache.TryGetValue(path, out var c) && DateTime.UtcNow - c.at < ttl)
                    return JsonSerializer.Deserialize<T>(c.body, Json.Opts)!;
        }
        var body = await Send(HttpMethod.Get, path, null, ct);
        var v = JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(body) ? "{}" : body, Json.Opts)!;
        if (ttl > TimeSpan.Zero) lock (CacheLock) Cache[path] = (DateTime.UtcNow, body);
        return v;
    }

    public static async Task<T> Post<T>(string path, object? body = null, CancellationToken ct = default)
    {
        var s = await Send(HttpMethod.Post, path, body, ct);
        return JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(s) ? "{}" : s, Json.Opts)!;
    }

    public static Task Put(string path, object body) => Send(HttpMethod.Put, path, body, default);
    public static Task Delete(string path) => Send(HttpMethod.Delete, path, null, default);

    public static async Task<string> Send(HttpMethod method, string path, object? body, CancellationToken ct, HttpContent? raw = null, TimeSpan? timeout = null)
    {
        using var req = new HttpRequestMessage(method, Abs(path));
        if (method != HttpMethod.Get) req.Headers.Add("X-Echo", "1");
        if (raw != null) req.Content = raw;
        else if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body, body.GetType(), Json.Opts), Encoding.UTF8, "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        HttpResponseMessage r;
        try { r = await Http.SendAsync(req, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException("Сервер не ответил вовремя", 0); }
        catch (HttpRequestException) { throw new ApiException("Сеть недоступна", 0); }
        using (r)
        {
            string text;
            try { text = await r.Content.ReadAsStringAsync(cts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ApiException("Сервер не ответил вовремя", 0); }
            catch (HttpRequestException) { throw new ApiException("Сеть недоступна", 0); }
            if (r.Headers.Contains("Set-Cookie")) SaveCookies();
            if (!r.IsSuccessStatusCode)
            {
                string? err = null;
                try { err = JsonSerializer.Deserialize<ErrorBody>(text, Json.Opts)?.Error; } catch { }
                throw new ApiException(string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)r.StatusCode}" : err, (int)r.StatusCode);
            }
            return text;
        }
    }

    // Raw-body upload with progress (admin). Returns the parsed JSON response.
    public static async Task<T> Upload<T>(string path, Stream data, long length, string contentType, IProgress<double>? progress, CancellationToken ct = default)
    {
        var content = new ProgressContent(data, length, progress);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Headers.ContentLength = length;
        var s = await Send(HttpMethod.Post, path, null, ct, content, TimeSpan.FromMinutes(30));
        return JsonSerializer.Deserialize<T>(string.IsNullOrWhiteSpace(s) ? "{}" : s, Json.Opts)!;
    }

    sealed class ErrorBody { [JsonPropertyName("error")] public string? Error { get; set; } }

    sealed class ProgressContent : HttpContent
    {
        readonly Stream src; readonly long len; readonly IProgress<double>? p;
        public ProgressContent(Stream s, long l, IProgress<double>? pr) { src = s; len = l; p = pr; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buf = new byte[81920]; long sent = 0; int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await stream.WriteAsync(buf.AsMemory(0, n));
                sent += n;
                p?.Report(len > 0 ? (double)sent / len : 0);
            }
        }
        protected override bool TryComputeLength(out long length) { length = len; return true; }
    }

    // ---- cookie persistence ----
    sealed class CookieDto
    {
        public string Name { get; set; } = ""; public string Value { get; set; } = ""; public string Domain { get; set; } = "";
        public string Path { get; set; } = "/"; public DateTime Expires { get; set; }
        public bool Secure { get; set; }
        public bool HttpOnly { get; set; }
    }

    public static void SaveCookies()
    {
        try
        {
            var list = Jar.GetAllCookies().Cast<Cookie>().Where(c => !c.Expired)
                .Select(c => new CookieDto { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, Expires = c.Expires, Secure = c.Secure, HttpOnly = c.HttpOnly }).ToList();
            Paths.WriteAtomic(Paths.Cookies, JsonSerializer.Serialize(list));
        }
        catch (Exception e) { Logger.Write("cookies save: " + e.Message); }
    }

    static void LoadCookies()
    {
        try
        {
            if (!File.Exists(Paths.Cookies)) return;
            var list = JsonSerializer.Deserialize<List<CookieDto>>(File.ReadAllText(Paths.Cookies)) ?? new();
            foreach (var c in list)
            {
                if (c.Expires != DateTime.MinValue && c.Expires < DateTime.Now) continue;
                var ck = new Cookie(c.Name, c.Value, string.IsNullOrEmpty(c.Path) ? "/" : c.Path, c.Domain) { Secure = c.Secure, HttpOnly = c.HttpOnly };
                if (c.Expires != DateTime.MinValue) ck.Expires = c.Expires;
                Jar.Add(ck);
            }
        }
        catch (Exception e) { Logger.Write("cookies load: " + e.Message); }
    }

    public static void ClearCookies()
    {
        foreach (Cookie c in Jar.GetAllCookies()) c.Expired = true;
        SaveCookies();
    }
}
