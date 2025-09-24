using Xunit;
using System.IO;
using SageCli.Ansible;

namespace SageCli.Tests;

public class DriftDetectorTests
{
    [Fact]
    public void Sums_Changed_From_PlayRecap()
    {
        var log = """
        TASK [x] ****************************************************************
        ok: [host1]
        PLAY RECAP **************************************************************
        host1 : ok=7 changed=2 unreachable=0 failed=0
        host2 : ok=5 changed=1 unreachable=0 failed=0
        """;

        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, log);

        var sum = DriftDetector.SumChanged(tmp);
        Assert.Equal(3, sum);
    }
}
