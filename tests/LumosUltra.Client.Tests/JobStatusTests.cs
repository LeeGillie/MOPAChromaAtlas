using LumosUltra.Client.Models;

namespace LumosUltra.Client.Tests;

public class JobStatusTests
{
    private static LumosUltraClient ClientReturning(string jsonBody)
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(jsonBody));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.168.42.1:8080") };
        return new LumosUltraClient(http, ownsHttpClient: true);
    }

    [Fact]
    public async Task Status_4_parses_as_idle()
    {
        // The exact response shape observed directly on a real Lumos Ultra while a job
        // sat armed, waiting for the physical start button.
        using var client = ClientReturning("""{"code":0,"result":"ok","status":4}""");
        Assert.Equal(JobStatus.Idle, await client.GetJobStatusAsync());
    }

    [Fact]
    public async Task Status_6_parses_as_preview()
    {
        // Observed directly during a framing/boundary preview pass.
        using var client = ClientReturning("""{"code":0,"result":"ok","status":6}""");
        Assert.Equal(JobStatus.Preview, await client.GetJobStatusAsync());
    }

    [Fact]
    public async Task Unrecognised_status_code_is_unknown_not_a_guess()
    {
        using var client = ClientReturning("""{"code":0,"result":"ok","status":99}""");
        Assert.Equal(JobStatus.Unknown, await client.GetJobStatusAsync());
    }

    [Fact]
    public async Task Get_job_status_uses_post_not_get()
    {
        // CONFIRMED from the device's own request log: MakeIt polls /process/status with
        // POST, not GET - easy to get backwards since every other status-like endpoint
        // in this API is GET.
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("""{"code":0,"result":"ok","status":4}"""));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        await client.GetJobStatusAsync();

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/process/status", handler.Requests[0].RequestUri!.AbsolutePath);
    }
}
