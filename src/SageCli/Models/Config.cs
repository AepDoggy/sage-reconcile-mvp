using System.Collections.Generic;

namespace SageCli.Models;

public class RootConfig
{
    public int Version { get; set; } = 1;
    public SshConfig Ssh { get; set; } = new();
    public List<HostConfig> Hosts { get; set; } = new();
}

public class SshConfig
{
    // YAML ssh.user / ssh.port / ssh.private_key_path / ssh.become
    public string User { get; set; } = "ubuntu";   
    public int Port { get; set; } = 22;
    public string PrivateKeyPath { get; set; } = "~/.ssh/id_ed25519";
    public bool Become { get; set; } = true;
}

public class HostConfig
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty; // IP или DNS

    // В YAML: sysctl: { key: value }
    public Dictionary<string, string>? Sysctl { get; set; }

    // В YAML: hosts_entries: { managed_block_name, entries: [...] }
    public HostsFileConfig? HostsEntries { get; set; }

    // В YAML: packages: { present: [...], absent: [...] }
    public PackagesConfig? Packages { get; set; }

    // В YAML: docker_apps: [ { ... }, ... ]
    public List<DockerApp>? DockerApps { get; set; }
}

public class HostsFileConfig
{
    // YAML: hosts_entries.managed_block_name
    public string ManagedBlockName { get; set; } = "sre-managed";
    // YAML: hosts_entries.entries
    public List<string> Entries { get; set; } = new();
}

public class PackagesConfig
{
    public List<string> Present { get; set; } = new();
    public List<string> Absent { get; set; } = new();
}

public class DockerApp
{
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string RestartPolicy { get; set; } = "unless-stopped";
    public Dictionary<string, string>? Env { get; set; }
    public List<string>? Command { get; set; }
    public List<string>? Volumes { get; set; }
    public List<string>? Ports { get; set; }
    public double? Cpus { get; set; }
    public string? Memory { get; set; }
    public List<string>? ExtraHosts { get; set; }
}

