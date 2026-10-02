using FluentFTP;

namespace LumosUltra.Client;

public sealed partial class LumosUltraJobUploadOptions
{
    /// <summary>Where MakeIt itself places a job before arming it. CONFIRMED by direct
    /// observation of a Lumos Ultra's own MakeIt log.</summary>
    public const string DeviceGCodePath = "/mnt/SDCARD/data/printing/gcode.gc";

    /// <summary>Default SSH/FTP credentials documented by marcoreps/lumos_RE. Not
    /// independently verified by this project's authors; not a secret WeCreat gave
    /// anyone - it is published in that public repository's README.</summary>
    public const string DefaultFtpUser = "mbtc";
    public const string DefaultFtpPassword = "mbtc";
}

public sealed partial class LumosUltraClient
{
    /// <summary>
    /// Uploads a job and arms it, following the sequence CONFIRMED by direct observation
    /// of a Lumos Ultra's own MakeIt log: (1) place the file over FTP at
    /// <see cref="LumosUltraJobUploadOptions.DeviceGCodePath"/>, then (2)
    /// <c>POST /process/fileupload_via_ftp</c> with the exact JSON shape this project's
    /// authors watched MakeIt itself send:
    /// <c>{"md5":"&lt;UPPERCASE_MD5&gt;","filepath":"...","action_type":1,"door_status":0,"mopa_status":1}</c>.
    /// This does NOT start the job - call <see cref="StartArmedJobAsync"/> (or press the
    /// physical start button) afterwards. Arming alone does not fire the laser.
    /// CONFIRMED end-to-end on the authors' own Ultra: this method followed by
    /// <see cref="StartArmedJobAsync"/> actually fired the laser and marked real material,
    /// with no MakeIt UI or physical button involved at any step.
    /// </summary>
    /// <param name="gcode">The full G-code text to send.</param>
    /// <param name="ftpHost">FTP host, if different from the HTTP base address's host.</param>
    /// <param name="ftpUser">Defaults to the credential documented by marcoreps/lumos_RE.</param>
    /// <param name="ftpPassword">Defaults to the credential documented by marcoreps/lumos_RE.</param>
    /// <param name="requireDoorConfirmedClosed">
    /// If true (the default), this method first calls <see cref="TryIsDoorClosedAsync"/>
    /// and throws unless it confirms the door is closed - the device's API does not
    /// enforce the interlock itself, so this check exists only in this library. Pass
    /// false only if you have verified safety by some other means for this specific call.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UploadAndArmJobAsync(
        string gcode,
        string? ftpHost = null,
        string ftpUser = LumosUltraJobUploadOptions.DefaultFtpUser,
        string ftpPassword = LumosUltraJobUploadOptions.DefaultFtpPassword,
        bool requireDoorConfirmedClosed = true,
        CancellationToken ct = default)
    {
        if (requireDoorConfirmedClosed)
        {
            var closed = await TryIsDoorClosedAsync(ct).ConfigureAwait(false);
            if (closed != true)
                throw new InvalidOperationException(
                    "Refusing to arm a job: door state could not be confirmed closed " +
                    "(this endpoint's response shape is not fully confirmed - see PROVENANCE.md). " +
                    "Pass requireDoorConfirmedClosed: false only once you have verified safety yourself.");
        }

        var host = ftpHost ?? _http.BaseAddress!.Host;
        var bytes = System.Text.Encoding.UTF8.GetBytes(gcode);

        using (var ftp = new AsyncFtpClient(host, ftpUser, ftpPassword))
        {
            await ftp.Connect(ct).ConfigureAwait(false);
            await ftp.UploadBytes(bytes, LumosUltraJobUploadOptions.DeviceGCodePath, FtpRemoteExists.Overwrite, createRemoteDir: false, token: ct).ConfigureAwait(false);
            await ftp.Disconnect(ct).ConfigureAwait(false);
        }

        var md5 = ChecksumHelper.UppercaseMd5(bytes);
        using var armBody = JsonBody(new
        {
            md5,
            filepath = LumosUltraJobUploadOptions.DeviceGCodePath,
            action_type = 1,
            door_status = 0, // Observed once, in this exact request, with the door closed. INFERRED convention.
            mopa_status = 1, // Observed once. Meaning not independently confirmed.
        });
        using var resp = await _http.PostAsync("/process/fileupload_via_ftp", armBody, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Starts a job previously armed by <see cref="UploadAndArmJobAsync"/>. CONFIRMED
    /// end-to-end on the authors' own Ultra: calling this after arming actually fired the
    /// laser and marked real material, with the job status transitioning through busy and
    /// back to idle/finished exactly as <see cref="GetJobStatusAsync"/> predicts.
    /// <b>A previously armed job will fire if this is called again without re-arming</b> -
    /// marcoreps/lumos_RE's README warns of this explicitly; not independently
    /// re-confirmed by this project's authors.
    /// </summary>
    public async Task StartArmedJobAsync(CancellationToken ct = default)
    {
        using var resp = await _http.PostAsync("/process/start", content: null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }
}
