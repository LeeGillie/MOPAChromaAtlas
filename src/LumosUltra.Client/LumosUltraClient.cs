using System.Net.Http.Json;
using System.Text.Json;
using LumosUltra.Client.Models;

namespace LumosUltra.Client;

/// <summary>
/// Client for a WeCreat Lumos Ultra's local HTTP API. Unofficial and community
/// reverse-engineered - see PROVENANCE.md for exactly which claim traces to which
/// source. The API has no real authentication: the <c>md5</c> parameter some endpoints
/// require is a payload-integrity checksum, not a credential, and the device does not
/// enforce the lid interlock at this layer - see <see cref="StartArmedJobAsync"/>.
/// </summary>
public sealed partial class LumosUltraClient : IDisposable
{
    /// <summary>The address MakeIt itself talks to over a USB RNDIS connection. Different
    /// on Wi-Fi (DHCP-assigned, seen as e.g. 192.168.1.234 in one observed session) - pass
    /// the actual address if not on USB.</summary>
    public const string DefaultUsbBaseAddress = "http://192.168.42.1:8080";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public LumosUltraClient(string baseAddress = DefaultUsbBaseAddress) : this(new HttpClient { BaseAddress = new Uri(baseAddress) }, ownsHttpClient: true) { }

    /// <summary>Use this overload to supply your own configured <see cref="HttpClient"/>
    /// (timeouts, a test handler, etc.). Its <see cref="HttpClient.BaseAddress"/> must
    /// already be set.</summary>
    public LumosUltraClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
        if (_http.BaseAddress is null)
            throw new ArgumentException("The HttpClient must have BaseAddress set.", nameof(httpClient));

        // CONFIRMED necessary against real hardware: the device's embedded web server
        // (Mongoose) does not handle the "Expect: 100-continue" handshake .NET's
        // HttpClient sends by default on the first POST with a body to a given host -
        // it just resets the connection, surfacing as a generic HttpRequestException on
        // any POST that has a body (e.g. StartFramingPreviewAsync). GET requests and
        // POSTs with no body (GetJobStatusAsync, StartArmedJobAsync) are unaffected,
        // which is why this wasn't caught until a body-bearing POST was actually tried.
        _http.DefaultRequestHeaders.ExpectContinue = false;

        // TRIAL FIX, not yet independently confirmed: after fixing the above, a
        // body-bearing POST failed differently - HttpIOException "The response ended
        // prematurely" - consistent with the same embedded server not handling HTTP
        // keep-alive/persistent connections reliably. Forcing a fresh connection per
        // request is the standard mitigation for exactly this failure signature against
        // minimal embedded HTTP servers. If this doesn't resolve it, the cause is
        // something else and this should be reverted rather than left as cargo-cult code.
        _http.DefaultRequestHeaders.ConnectionClose = true;
    }

    public void Dispose() { if (_ownsHttpClient) _http.Dispose(); }

    // ---------------------------------------------------------------- device

    /// <summary>CONFIRMED (observed directly in a Lumos Ultra's own MakeIt log).</summary>
    public async Task<DeviceInfo?> GetDeviceInfoAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<DeviceInfo>("/device/info", ct).ConfigureAwait(false);

    /// <summary>CONFIRMED endpoint name (marcoreps/lumos_RE); returns true if the device
    /// answered at all. Response body shape is not confirmed.</summary>
    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("/device/heartbeat", ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    // ---------------------------------------------------------------- job / process

    /// <summary>
    /// CONFIRMED by direct observation on a Lumos Ultra: MakeIt polls this with method
    /// POST (not GET) roughly every 5 seconds while idle, receiving e.g.
    /// <c>{"code":0,"result":"ok","status":4}</c>. <see cref="JobStatus.Idle"/> (4) is the
    /// state seen while a job sits armed, waiting for the physical start button;
    /// <see cref="JobStatus.Preview"/> (6) was seen during a framing/boundary pass.
    /// </summary>
    public async Task<JobStatus> GetJobStatusAsync(CancellationToken ct = default)
    {
        using var resp = await _http.PostAsync("/process/status", content: null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("status", out var s) && s.TryGetInt32(out var code)
            ? code.ToJobStatus() : JobStatus.Unknown;
    }

    /// <summary>
    /// Read of <c>GET /process/door_status</c>. Response shape CONFIRMED on the Ultra:
    /// <c>{"code":0,"result":"ok","status":N}</c>, with <c>status</c> observed as 1 with
    /// the lid physically closed and 0 with it open (two direct real-hardware
    /// observations, one per state) - the opposite of this method's original guessed
    /// convention. Still returns null rather than guessing further if the shape doesn't
    /// match, so callers - including <see cref="StartArmedJobAsync"/> - treat an
    /// unrecognised shape as "not confirmed closed," not as "closed."
    /// </summary>
    public async Task<bool?> TryIsDoorClosedAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("/process/door_status", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct).ConfigureAwait(false);
            foreach (var name in new[] { "door_status", "status", "closed" })
                if (doc.RootElement.TryGetProperty(name, out var v) && v.TryGetInt32(out var n))
                    return n != 0;
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Pause (action=0) or cancel (action=2), per 2ktsch/weburn's documentation of the
    /// same endpoint on WeCreat's earlier Vision 20W. Not yet independently confirmed on
    /// the Ultra.
    /// </summary>
    public Task PauseJobAsync(CancellationToken ct = default) => ProcessControlAsync(action: 0, ct);

    /// <summary>See <see cref="PauseJobAsync"/> - cancel returns the machine to 0,0,0 per weburn.</summary>
    public Task CancelJobAsync(CancellationToken ct = default) => ProcessControlAsync(action: 2, ct);

    private async Task ProcessControlAsync(int action, CancellationToken ct)
    {
        using var resp = await _http.PostAsync($"/process/control?action={action}", content: null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// CONFIRMED at the protocol/state level: this is what MakeIt itself calls before an
    /// engrave, and this library's own call was independently exercised end-to-end
    /// against real hardware (the device's <c>status</c> correctly moved to
    /// <see cref="Models.JobStatus.Preview"/> and back to <see cref="Models.JobStatus.Idle"/>
    /// after <see cref="StopFramingPreviewAsync"/>). <b>NOT CONFIRMED: that it actually
    /// traces/draws the job's boundary.</b> The Chinese log label MakeIt itself uses
    /// (走边框, "trace border") implies a drawn outline, but repeated real-hardware calls
    /// produced at most isolated static dots and, on a later call, no new visible
    /// indicator at all - never an actual traced rectangle. Do not rely on this for
    /// positioning verification until that's characterised further. Call
    /// <see cref="StopFramingPreviewAsync"/> when done.
    /// </summary>
    public async Task StartFramingPreviewAsync(string framingFilePath, string uppercaseMd5, CancellationToken ct = default)
    {
        using var body = JsonBody(new { framing_file = framingFilePath, md5 = uppercaseMd5 });
        using var resp = await _http.PostAsync("/lumos/dev/framing_preview_start", body, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>See <see cref="StartFramingPreviewAsync"/>. CONFIRMED end-to-end against
    /// real hardware, including the device's status correctly returning to
    /// <see cref="Models.JobStatus.Idle"/> afterwards.</summary>
    public async Task StopFramingPreviewAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("/lumos/dev/framing_preview_stop", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    // ---------------------------------------------------------------- camera

    /// <summary>
    /// CONFIRMED end-to-end on a real Lumos Ultra: returns a JPEG, 4032x3024, matching
    /// what 2ktsch/weburn reported for the earlier Vision 20W. CONFIRMED: the raw JPEG is
    /// physically rotated relative to an upright view of the bed - rotate the returned
    /// image 90 degrees clockwise to match what a human standing at the machine sees (the
    /// support card's text reads normally and the exhaust grille lands at the bottom
    /// edge). MakeIt's own UI most likely applies this same correction before display;
    /// this method intentionally returns the raw bytes unmodified so callers can choose
    /// how to handle it.
    /// </summary>
    public async Task<byte[]> TakePhotoAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync("/camera/take_photo", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- raw escape hatch

    /// <summary>
    /// For any endpoint this client doesn't wrap yet (see README for the full known-but-
    /// unwrapped list: /device/set_config, /device/light/*, /device/material/*,
    /// /camera/measure_distance, /camera/get_calibration, /lumos/camera/*, and more).
    /// Returns the raw response body as text.
    /// </summary>
    public async Task<string> GetRawAsync(string pathAndQuery, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(pathAndQuery, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>As <see cref="GetRawAsync"/>, for POST with a raw JSON body.</summary>
    public async Task<string> PostRawJsonAsync(string pathAndQuery, string jsonBody, CancellationToken ct = default)
    {
        using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(pathAndQuery, content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// <b>Sends raw G-code/M-code text for immediate execution, bypassing every guardrail
    /// MakeIt's own UI provides.</b> 2ktsch/weburn documents that commands sent this way
    /// "run regardless of the lid position" on the earlier Vision 20W - i.e. no interlock
    /// is enforced at this layer. This library will not soften that: you are asking a
    /// Class 4 laser to do exactly what the text says. Confirm <paramref name="iUnderstandTheRisk"/>
    /// explicitly; there is no safer default to fall back to here.
    /// </summary>
    public async Task<string> SendRawCommandAsync(string gcode, bool iUnderstandTheRisk, CancellationToken ct = default)
    {
        if (!iUnderstandTheRisk)
            throw new InvalidOperationException(
                "Refusing to send a raw command: this endpoint does not honour the lid interlock. " +
                "Pass iUnderstandTheRisk: true only after you have verified this specific command is safe.");
        var md5 = ChecksumHelper.UppercaseMd5(gcode);
        using var content = new StringContent(gcode, System.Text.Encoding.UTF8, "text/plain");
        using var resp = await _http.PostAsync($"/test/cmd/mcu?md5={md5}", content, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A JSON request body guaranteed to send an upfront <c>Content-Length</c> rather than
    /// <c>Transfer-Encoding: chunked</c>. CONFIRMED necessary: <see cref="JsonContent"/>
    /// does not always provide a computable length ahead of time, and the device's
    /// embedded web server does not handle chunked request bodies - a request built with
    /// <c>JsonContent.Create</c> failed with "the response ended prematurely" on
    /// real hardware, while the identical JSON sent as a plain buffered string (which is
    /// what a fully-buffered <see cref="StringContent"/> always does) succeeded.
    /// </summary>
    internal static StringContent JsonBody<T>(T value) =>
        new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
}
