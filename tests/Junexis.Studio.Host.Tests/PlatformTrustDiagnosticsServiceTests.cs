using Junexis.Studio.Abstractions;
using Junexis.Studio.Host;
using Xunit;

namespace Junexis.Studio.Host.Tests;

public class PlatformTrustDiagnosticsServiceTests
{
    [Fact]
    public void RedactSensitiveDetails_RedactsFilePathsAndSecrets()
    {
        const string rawDetail = @"Failed loading key from C:\Users\operator\.junexis\private-key.pem with secret=12345678 and sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var redacted = PlatformTrustDiagnosticsService.RedactSensitiveDetails(rawDetail);

        Assert.DoesNotContain(@"C:\Users\", redacted);
        Assert.DoesNotContain("secret=12345678", redacted);
        Assert.Contains("[REDACTED_PATH]", redacted);
        Assert.Contains("[REDACTED_SECRET]", redacted);
        Assert.Contains("sha256:[REDACTED_KEY_ID]", redacted);
    }
}
