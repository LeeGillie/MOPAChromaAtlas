namespace LumosUltra.Client.Tests;

public class JsonBodyTests
{
    [Fact]
    public async Task Framing_preview_request_declares_content_length_not_chunked_encoding()
    {
        // CONFIRMED against real hardware: a request built with JsonContent.Create failed
        // with "the response ended prematurely" - the device's embedded web server does
        // not handle chunked request bodies. The identical JSON sent as a fully-buffered
        // StringContent (which always knows its length upfront) succeeded. This pins that
        // StartFramingPreviewAsync's request can never regress back to chunked encoding.
        //
        // The request's Content is disposed as soon as the library method returns, so it
        // must be inspected from inside the handler callback, not after awaiting the call.
        long? contentLength = null;
        bool? chunked = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            contentLength = req.Content!.Headers.ContentLength;
            chunked = req.Headers.TransferEncodingChunked;
            return FakeHttpMessageHandler.Json("""{"code":0,"result":"ok","status":0}""");
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        await client.StartFramingPreviewAsync("/mnt/SDCARD/data/framing/gcode.gc", "07E1DCA0E35E6D781AE17E87D94E3049");

        Assert.NotNull(contentLength);
        Assert.NotEqual(true, chunked);
    }

    [Fact]
    public async Task Framing_preview_request_body_matches_the_shape_observed_on_real_hardware()
    {
        string? body = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return FakeHttpMessageHandler.Json("""{"code":0,"result":"ok","status":0}""");
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        await client.StartFramingPreviewAsync("/mnt/SDCARD/data/framing/gcode.gc", "07E1DCA0E35E6D781AE17E87D94E3049");

        Assert.Equal(
            """{"framing_file":"/mnt/SDCARD/data/framing/gcode.gc","md5":"07E1DCA0E35E6D781AE17E87D94E3049"}""",
            body);
    }
}
