using SageCli.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SageCli.Config;

public static class ConfigLoader
{
    public static RootConfig Load(string path)
    {
        var text = File.ReadAllText(path);

        //  UnderscoredNamingConvention → ключи вида private_key_path, hosts_entries
        // корректно сопоставятся свойствам C# PrivateKeyPath, HostsEntries и т.д.
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var cfg = deserializer.Deserialize<RootConfig>(text) ?? new RootConfig();

        // Нормализуем "~/..." в абсолютный путь (для ssh.private_key_path)
        cfg.Ssh.PrivateKeyPath = ExpandHome(cfg.Ssh.PrivateKeyPath);
        return cfg;
    }

    private static string ExpandHome(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return p;
        if (p.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, p[2..]); // убираем "~/"
        }
        return p;
    }
}

