using System.Diagnostics;

namespace SageCli.Ansible;

public static class AnsibleRunner
{
    public static int Run(
        string workDir,
        string? limit = null,
        bool check = true,
        bool askBecomePass = false,
        int? forks = null,
        bool verbose = false,
        string logPrefix = "ansible")
    {
        var stdoutPath = Path.Combine(workDir, $"{logPrefix}.stdout.log");
        var stderrPath = Path.Combine(workDir, $"{logPrefix}.stderr.log");

        using var stdout = new StreamWriter(File.Open(stdoutPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        using var stderr = new StreamWriter(File.Open(stderrPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };

        var psi = new ProcessStartInfo
        {
            FileName = "ansible-playbook",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("inventory.ini");
        if (!string.IsNullOrWhiteSpace(limit)) { psi.ArgumentList.Add("-l"); psi.ArgumentList.Add(limit!); }
        if (forks.HasValue) { psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(forks.Value.ToString()); }
        if (verbose) { psi.ArgumentList.Add("-v"); }
        psi.ArgumentList.Add("site.yml");
        if (check) { psi.ArgumentList.Add("--check"); psi.ArgumentList.Add("--diff"); }
        if (askBecomePass) { psi.ArgumentList.Add("-K"); }

        var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) { Console.WriteLine(e.Data); stdout.WriteLine(e.Data); } };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) { Console.Error.WriteLine(e.Data); stderr.WriteLine(e.Data); } };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();

        return p.ExitCode;
    }
}

