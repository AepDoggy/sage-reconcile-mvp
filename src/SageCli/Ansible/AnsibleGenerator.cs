using SageCli.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SageCli.Ansible;

public class AnsibleGenerator
{
    public string WorkDir { get; }

    public AnsibleGenerator(string baseDir)
    {
        WorkDir = Path.Combine(baseDir, $"run-{DateTime.UtcNow:yyyyMMddHHmmss}");
        Directory.CreateDirectory(WorkDir);
        Directory.CreateDirectory(Path.Combine(WorkDir, "host_vars"));
    }

    public void WriteSiteYml(string templatePath)
    {
        var dest = Path.Combine(WorkDir, "site.yml");
        File.Copy(templatePath, dest, overwrite: true);
    }

    public void WriteInventory(RootConfig cfg)
    {
        var lines = new List<string> { "[all]" };
        foreach (var h in cfg.Hosts)
        {
            lines.Add(
                $"{h.Name} ansible_host={h.Address} " +
                $"ansible_user={cfg.Ssh.User} ansible_port={cfg.Ssh.Port} " +
                $"ansible_ssh_private_key_file={cfg.Ssh.PrivateKeyPath}"
            );
        }
        File.WriteAllLines(Path.Combine(WorkDir, "inventory.ini"), lines);
    }

        public void WriteHostVars(HostConfig host, DefaultsConfig? d = null)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(YamlDotNet.Serialization.DefaultValuesHandling.OmitNull)
            .Build();

        var vars = new Dictionary<string, object?>();

        var sysctls = host.Sysctl ?? d?.Sysctl;
        if (sysctls is { Count: > 0 }) vars["sysctls"] = sysctls;

        var hostsEntries = host.HostsEntries ?? d?.HostsEntries;
        if (hostsEntries != null)
        {
            vars["hosts_block_name"] = hostsEntries.ManagedBlockName;
            vars["hosts_entries"] = hostsEntries.Entries;
        }

        var pkgs = host.Packages ?? d?.Packages;
        if (pkgs != null)
        {
            if (pkgs.Present.Count > 0) vars["packages_present"] = pkgs.Present;
            if (pkgs.Absent.Count > 0) vars["packages_absent"] = pkgs.Absent;
        }

        var dockerApps = host.DockerApps ?? d?.DockerApps;
        if (dockerApps is { Count: > 0 }) vars["docker_apps"] = dockerApps;

        var yml = serializer.Serialize(vars);
        var path = Path.Combine(WorkDir, "host_vars", $"{host.Name}.yml");
        File.WriteAllText(path, yml);
    }
}

