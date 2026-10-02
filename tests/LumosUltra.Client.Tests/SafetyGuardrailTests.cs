using System.Net;

namespace LumosUltra.Client.Tests;

public class SafetyGuardrailTests
{
    [Fact]
    public async Task Upload_and_arm_refuses_when_door_status_cannot_be_confirmed_closed()
    {
        // The device enforces no interlock at the API layer, so this library's own
        // refusal is the only thing stopping an arm request when door state is unknown.
        // A 404 here stands in for "this endpoint's shape isn't confirmed and parsing
        // failed" - TryIsDoorClosedAsync must return null, not assume closed.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.UploadAndArmJobAsync("G0X0Y0"));

        // And critically: it must refuse before ever attempting the FTP upload.
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("fileupload_via_ftp"));
    }

    [Fact]
    public async Task Send_raw_command_refuses_without_explicit_acknowledgement()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should never reach the network"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendRawCommandAsync("M43S1", iUnderstandTheRisk: false));
    }

    [Fact]
    public async Task Try_is_door_closed_returns_null_rather_than_guessing_on_an_unrecognised_shape()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json("""{"some_field_nobody_has_documented":true}"""));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        Assert.Null(await client.TryIsDoorClosedAsync());
    }

    // Locks in two direct real-hardware observations on the Ultra: status:1 with the lid
    // physically closed, status:0 with it open - the opposite of this method's original
    // guessed convention (0 = closed), caught only once someone read the lid by hand.
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task Try_is_door_closed_matches_confirmed_status_field_convention(int status, bool expectedClosed)
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Json($$"""{"code":0,"result":"ok","status":{{status}}}"""));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        Assert.Equal(expectedClosed, await client.TryIsDoorClosedAsync());
    }
}
