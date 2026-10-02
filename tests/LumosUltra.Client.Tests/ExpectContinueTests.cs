namespace LumosUltra.Client.Tests;

public class ExpectContinueTests
{
    [Fact]
    public void Constructing_a_client_disables_expect_continue()
    {
        // CONFIRMED against real hardware: the device's embedded web server (Mongoose)
        // resets the connection on the "Expect: 100-continue" handshake .NET sends by
        // default on the first POST with a body to a host - surfacing as a generic
        // HttpRequestException on any body-bearing POST (e.g. StartFramingPreviewAsync),
        // while GET and empty-body POST calls worked fine. Pinning this so it can't
        // silently regress.
        var http = new HttpClient { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        Assert.False(http.DefaultRequestHeaders.ExpectContinue);
    }

    [Fact]
    public void Constructing_a_client_forces_a_fresh_connection_per_request()
    {
        // Trial fix for a second protocol-level failure against the same embedded server
        // ("HttpIOException: The response ended prematurely") seen after fixing
        // ExpectContinue above - not yet independently confirmed as the actual cause.
        // If this test starts failing because the fix was reverted, that's expected;
        // update it alongside whatever replaces the fix.
        var http = new HttpClient { BaseAddress = new Uri("http://192.168.42.1:8080") };
        using var client = new LumosUltraClient(http, ownsHttpClient: true);

        Assert.True(http.DefaultRequestHeaders.ConnectionClose);
    }
}
