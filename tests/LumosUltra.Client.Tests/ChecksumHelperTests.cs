namespace LumosUltra.Client.Tests;

public class ChecksumHelperTests
{
    [Fact]
    public void Uppercase_md5_of_empty_string_matches_the_well_known_value()
    {
        // The device's md5 parameter is documented (2ktsch/weburn, marcoreps/lumos_RE) as
        // an uppercase hex MD5 of the payload - this pins that exact format.
        Assert.Equal("D41D8CD98F00B204E9800998ECF8427E", ChecksumHelper.UppercaseMd5(""));
    }

    [Fact]
    public void Uppercase_md5_of_known_text_is_correct_and_uppercase()
    {
        Assert.Equal("9E107D9D372BB6826BD81D3542A419D6", ChecksumHelper.UppercaseMd5("The quick brown fox jumps over the lazy dog"));
    }
}
