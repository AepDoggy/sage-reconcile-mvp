using Xunit;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SageCli.Models;
using SageCli.Ansible;

namespace SageCli.Tests;

public class AnsibleGeneratorTests
{
    private static string FindRepoRoot()
    {
        // идём вверх от bin/Debug к корню репозитория
        var dir = AppContext.BaseDirectory;
        while (dir != "/" && dir != Path.GetPathRoot(dir))
        {
            var probe = Path.Combine(dir, "ansible_templates", "site.yml");
            if (File.Exists(probe)) return dir;
            dir = Directory.GetParent(dir)!.FullName;
        }
        throw new DirectoryNotFoundException("repo root not found (no ansible_templates/site.yml)");
    }

    [Fact]
    public void Generates_Inventory_And_HostVars_And_SiteYml()
    {
        var repoRoot = FindRepoRoot();
        var workBase = Directory.CreateTempSubdirectory().FullName;

        var defaults = new DefaultsConfig
        {
            Sysctl = new Dictionary<string, string> { ["vm.swappiness"] = "10" },
            HostsEntries = new HostsFileConfig { ManagedBlockName = "sre-managed", Entries = new List<string> { "10.0.0.2 db" } }
        };

        var cfg = new RootConfig
        {
            Version = 1,
            Ssh = new SshConfig { User = "user", Port = 22, PrivateKeyPath = "/tmp/id", Become = true },
            Hosts = new List<HostConfig> {
                new HostConfig { Name = "host1", Address = "192.0.2.12" }
            }
        };

        var gen = new AnsibleGenerator(workBase, defaults);
        gen.WriteSiteYml(Path.Combine(repoRoot, "ansible_templates", "site.yml"));
        gen.WriteInventory(cfg);
        foreach (var h in cfg.Hosts) gen.WriteHostVars(h);

        // найдём run-YYYY... каталог
        var runDir = Directory.GetDirectories(workBase, "run-*").Single();
        var inventory = Path.Combine(runDir, "inventory.ini");
        var site = Path.Combine(runDir, "site.yml");
        var hostVars = Path.Combine(runDir, "host_vars", "host1.yml");

        Assert.True(File.Exists(inventory), "inventory.ini not found");
        Assert.True(File.Exists(site), "site.yml not found");
        Assert.True(File.Exists(hostVars), "host_vars/host1.yml not found");

        var invText = File.ReadAllText(inventory);
        Assert.Contains("192.0.2.12", invText);

        var hvText = File.ReadAllText(hostVars);
        Assert.Contains("sysctls:", hvText);
        Assert.Contains("hosts_block_name:", hvText);
    }
}
