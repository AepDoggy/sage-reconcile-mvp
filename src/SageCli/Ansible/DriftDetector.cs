using System.Text.RegularExpressions;

namespace SageCli.Ansible;

public static class DriftDetector
{
    private static readonly Regex ChangedRx = new(@"changed=(\d+)", RegexOptions.Compiled);

    public static int SumChanged(string stdoutLogPath)
    {
        if (!File.Exists(stdoutLogPath)) return 0;
        var text = File.ReadAllText(stdoutLogPath);
        var sum = 0;
        foreach (Match m in ChangedRx.Matches(text))
            if (int.TryParse(m.Groups[1].Value, out var n)) sum += n;
        return sum;
    }
}

