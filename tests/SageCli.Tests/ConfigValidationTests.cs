using Xunit;
using SageCli.Models;
using SageCli.Validation;
using System.Collections.Generic;

namespace SageCli.Tests;

public class ConfigValidationTests
{
    [Fact]
    public void ValidConfig_Passes()
    {
        var cfg = new RootConfig
        {
            Version = 1,
            Ssh = new SshConfig { User = "user", Port = 22, PrivateKeyPath = "/tmp/id_ed25519", Become = true },
            Hosts = new List<HostConfig>
            {
                new HostConfig { Name = "host1", Address = "192.0.2.10" }
            }
        };

        var res = new RootConfigValidator().Validate(cfg);
        Assert.True(res.IsValid, string.Join("\n", res.Errors));
    }

    [Fact]
    public void InvalidHost_Fails()
    {
        var cfg = new RootConfig
        {
            Version = 1,
            Ssh = new SshConfig { User = "user", Port = 22, PrivateKeyPath = "/tmp/id", Become = true },
            Hosts = new List<HostConfig>
            {
                new HostConfig { Name = "", Address = "not-an-ip" }
            }
        };

        var res = new RootConfigValidator().Validate(cfg);
        Assert.False(res.IsValid);
    }
}
