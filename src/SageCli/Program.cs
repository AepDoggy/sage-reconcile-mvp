using SageCli.Config;
using SageCli.Validation;
using SageCli.Ansible;
using SageCli.Models;

static void PrintUsage()
{
    Console.WriteLine(@"Usage:
  sage validate  -f <config.yaml>
  sage plan      -f <config.yaml> [--limit host1,host2] [--forks N] [-v]
  sage apply     -f <config.yaml> [--limit host1,host2] [--forks N] [-v] [--ask-become-pass|-K]
  sage reconcile -f <config.yaml> [--limit host1,host2] [--forks N] [-v] [--ask-become-pass|-K]");
}

if (args.Length == 0) { PrintUsage(); return; }

string cmd = args[0];
string? file = null, limit = null;
bool askBecomePass = false, verbose = false;
int? forks = null;

for (int i = 1; i < args.Length; i++)
{
    var a = args[i];
    if ((a == "-f" || a == "--file") && i + 1 < args.Length) { file = args[++i]; }
    else if (a == "--limit" && i + 1 < args.Length) { limit = args[++i]; }
    else if (a == "--ask-become-pass" || a == "-K") { askBecomePass = true; }
    else if (a == "--forks" && i + 1 < args.Length && int.TryParse(args[i+1], out var f)) { forks = f; i++; }
    else if (a == "-v" || a == "--verbose") { verbose = true; }
}

if (string.IsNullOrWhiteSpace(file)) { Console.Error.WriteLine("Missing -f <config.yaml>"); PrintUsage(); Environment.ExitCode = 2; return; }

RootConfig cfg;
try { cfg = ConfigLoader.Load(file!); }
catch (Exception ex) { Console.Error.WriteLine($"Failed to load YAML: {ex.Message}"); Environment.ExitCode = 2; return; }

var validator = new RootConfigValidator();
var res = validator.Validate(cfg);

bool IsCmd(string c) => string.Equals(cmd, c, StringComparison.OrdinalIgnoreCase);

if (IsCmd("validate"))
{
    if (res.IsValid) { Console.WriteLine("Config is valid ✔"); Environment.ExitCode = 0; }
    else { Console.Error.WriteLine("Validation errors:"); foreach (var e in res.Errors) Console.Error.WriteLine($" - {e.PropertyName}: {e.ErrorMessage}"); Environment.ExitCode = 1; }
    return;
}

if (!(IsCmd("plan") || IsCmd("apply") || IsCmd("reconcile")))
{
    PrintUsage(); Environment.ExitCode = 2; return;
}

if (!res.IsValid)
{
    Console.Error.WriteLine("Config invalid. Run 'sage validate -f ...' and fix errors.");
    foreach (var e in res.Errors) Console.Error.WriteLine($" - {e.PropertyName}: {e.ErrorMessage}");
    Environment.ExitCode = 1; return;
}

// Подготовка артефактов
var baseTmp = Path.Combine(Directory.GetCurrentDirectory(), ".sage-tmp");
Directory.CreateDirectory(baseTmp);
var gen = new SageCli.Ansible.AnsibleGenerator(baseTmp, cfg.Defaults);

var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "ansible_templates", "site.yml");
if (!File.Exists(templatePath)) { Console.Error.WriteLine($"site.yml template not found: {templatePath}"); Environment.ExitCode = 2; return; }

gen.WriteSiteYml(templatePath);
gen.WriteInventory(cfg);
foreach (var h in cfg.Hosts) gen.WriteHostVars(h);

Console.WriteLine($"Ansible workdir: {gen.WorkDir}");

if (IsCmd("plan"))
{
    var code = AnsibleRunner.Run(gen.WorkDir, limit, check: true, askBecomePass: askBecomePass, forks: forks, verbose: verbose, logPrefix: "ansible.plan");
    var stdoutPath = Path.Combine(gen.WorkDir, "ansible.plan.stdout.log");
    var stderrPath = Path.Combine(gen.WorkDir, "ansible.plan.stderr.log");
    Console.WriteLine(code == 0 ? $"✔ PLAN finished. Logs: {stdoutPath} / {stderrPath}" : $"✖ PLAN failed (exit {code}). Logs: {stdoutPath} / {stderrPath}");
    Environment.ExitCode = code; return;
}

if (IsCmd("apply"))
{
    var code = AnsibleRunner.Run(gen.WorkDir, limit, check: false, askBecomePass: askBecomePass, forks: forks, verbose: verbose, logPrefix: "ansible.apply");
    var stdoutPath = Path.Combine(gen.WorkDir, "ansible.apply.stdout.log");
    var stderrPath = Path.Combine(gen.WorkDir, "ansible.apply.stderr.log");
    Console.WriteLine(code == 0 ? $"✔ APPLY finished. Logs: {stdoutPath} / {stderrPath}" : $"✖ APPLY failed (exit {code}). Logs: {stdoutPath} / {stderrPath}");
    Environment.ExitCode = code; return;
}

// reconcile
{
    var planCode = AnsibleRunner.Run(gen.WorkDir, limit, check: true, askBecomePass: askBecomePass, forks: forks, verbose: verbose, logPrefix: "ansible.plan");
    var planLog = Path.Combine(gen.WorkDir, "ansible.plan.stdout.log");
    var changed = DriftDetector.SumChanged(planLog);
    Console.WriteLine($"Drift detected changed={changed}");

    if (planCode != 0) { Console.WriteLine("PLAN failed, aborting APPLY."); Environment.ExitCode = planCode; return; }

    if (changed > 0)
    {
        Console.WriteLine("Applying drift...");
        var applyCode = AnsibleRunner.Run(gen.WorkDir, limit, check: false, askBecomePass: askBecomePass, forks: forks, verbose: verbose, logPrefix: "ansible.apply");
        Console.WriteLine((applyCode == 0 ? "✔" : "✖") + $" RECONCILE apply exit={applyCode}. See logs in {gen.WorkDir}");
        Environment.ExitCode = applyCode; return;
    }
    else
    {
        Console.WriteLine("Nothing to reconcile. System is in desired state.");
        Environment.ExitCode = 0; return;
    }
}

