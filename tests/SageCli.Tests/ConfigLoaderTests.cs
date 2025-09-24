using Xunit;
using System.IO;
using SageCli.Config;

namespace SageCli.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Tilde_Is_Expanded_In_PrivateKeyPath()
    {
        var yaml = """
        version: 1
        ssh:
          user: user
          port: 22
          private_key_path: ~/.ssh/id_ed25519
          become: true
        hosts:
          - name: h1
            address: 192.0.2.11
        """;

        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, yaml);

        var cfg = ConfigLoader.Load(tmp);

        Assert.StartsWith(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            cfg.Ssh.PrivateKeyPath
        );
    }
}
