using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Tests;

public class DataProtectionTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    // Plaid access tokens are only as durable as the key ring that encrypted
    // them, so DataProtection:KeysPath must actually be where keys go (#57).
    [Fact]
    public void KeysPath_PersistsKeysThere()
    {
        var keysPath = Path.Combine(Path.GetTempPath(), $"pf-keys-{Guid.NewGuid():N}");
        try
        {
            using var configured = factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DataProtection:KeysPath"] = keysPath
                    })));

            var protector = configured.Services.GetRequiredService<PlaidTokenProtector>();
            var token = protector.Protect("access-sandbox-123");

            Assert.Equal("access-sandbox-123", protector.Unprotect(token));
            Assert.NotEmpty(Directory.GetFiles(keysPath, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(keysPath)) Directory.Delete(keysPath, recursive: true);
        }
    }
}
