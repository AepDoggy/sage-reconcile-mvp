// src/SageCli/Ansible/AnsibleRunner.cs
using System.Diagnostics;

namespace SageCli.Ansible;

public static class AnsibleRunner
{
    public static int Run(string workDir, string? limit = null, bool check = true, bool askBecomePass = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ansible-playbook",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("inventory.ini");
        if (!string.IsNullOrWhiteSpace(limit)) { psi.ArgumentList.Add("-l"); psi.ArgumentList.Add(limit!); }
        psi.ArgumentList.Add("site.yml");
        if (check) { psi.ArgumentList.Add("--check"); psi.ArgumentList.Add("--diff"); }
        if (askBecomePass) { psi.ArgumentList.Add("-K"); } // спросит sudo пароль

        var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode;
    }
}

