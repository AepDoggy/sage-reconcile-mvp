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

    public void WriteHostVars(HostConfig host)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(YamlDotNet.Serialization.DefaultValuesHandling.OmitNull)
            .Build();

        var vars = new Dictionary<string, object?>();
        if (host.Sysctl is { Count: > 0 }) vars["sysctls"] = host.Sysctl;

        if (host.HostsEntries != null)
        {
            vars["hosts_block_name"] = host.HostsEntries.ManagedBlockName;
            vars["hosts_entries"] = host.HostsEntries.Entries;
        }

        if (host.Packages != null)
        {
            if (host.Packages.Present.Count > 0) vars["packages_present"] = host.Packages.Present;
            if (host.Packages.Absent.Count > 0) vars["packages_absent"] = host.Packages.Absent;
        }

        if (host.DockerApps is { Count: > 0 }) vars["docker_apps"] = host.DockerApps;

        var yml = serializer.Serialize(vars);
        var path = Path.Combine(WorkDir, "host_vars", $"{host.Name}.yml");
        File.WriteAllText(path, yml);
    }
}

