using SageCli.Config;
using SageCli.Validation;
using SageCli.Ansible;
using FluentValidation;
using SageCli.Models;

static void PrintUsage()
{
    Console.WriteLine(@"Usage:
  sage validate -f <config.yaml>
  sage plan     -f <config.yaml> [--limit host1,host2] [--forks N] [-v]
  sage apply    -f <config.yaml> [--limit host1,host2] [--forks N] [-v] [--ask-become-pass|-K]");
}

if (args.Length == 0) { PrintUsage(); return; }

string cmd = args[0];
string? file = null;
string? limit = null;
bool askBecomePass = false;
int? forks = null;
bool verbose = false;

for (int i = 1; i < args.Length; i++)
{
    var a = args[i];
    if ((a == "-f" || a == "--file") && i + 1 < args.Length) { file = args[++i]; }
    else if (a == "--limit" && i + 1 < args.Length) { limit = args[++i]; }
    else if (a == "--ask-become-pass" || a == "-K") { askBecomePass = true; }
    else if (a == "--forks" && i + 1 < args.Length && int.TryParse(args[i + 1], out var f)) { forks = f; i++; }
    else if (a == "-v" || a == "--verbose") { verbose = true; }
}

if (string.IsNullOrWhiteSpace(file))
{
    Console.Error.WriteLine("Missing -f <config.yaml>");
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}

RootConfig cfg;
try { cfg = ConfigLoader.Load(file!); }
catch (Exception ex) { Console.Error.WriteLine($"Failed to load YAML: {ex.Message}"); Environment.ExitCode = 2; return; }

var validator = new RootConfigValidator();
var res = validator.Validate(cfg);

if (cmd == "validate")
{
    if (res.IsValid) { Console.WriteLine("Config is valid ✔"); Environment.ExitCode = 0; }
    else {
        Console.Error.WriteLine("Validation errors:");
        foreach (var e in res.Errors) Console.Error.WriteLine($" - {e.PropertyName}: {e.ErrorMessage}");
        Environment.ExitCode = 1;
    }
    return;
}

if (cmd != "plan" && cmd != "apply")
{
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}

if (!res.IsValid)
{
    Console.Error.WriteLine("Config invalid. Run 'sage validate -f ...' and fix errors.");
    foreach (var e in res.Errors) Console.Error.WriteLine($" - {e.PropertyName}: {e.ErrorMessage}");
    Environment.ExitCode = 1;
    return;
}

// Рабочая папка для артефактов
var baseTmp = Path.Combine(Directory.GetCurrentDirectory(), ".sage-tmp");
Directory.CreateDirectory(baseTmp);
var gen = new SageCli.Ansible.AnsibleGenerator(baseTmp);

// Шаблон site.yml в репозитории
var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "ansible_templates", "site.yml");
if (!File.Exists(templatePath))
{
    Console.Error.WriteLine($"site.yml template not found: {templatePath}");
    Environment.ExitCode = 2;
    return;
}

gen.WriteSiteYml(templatePath);
gen.WriteInventory(cfg);
foreach (var h in cfg.Hosts) gen.WriteHostVars(h);

Console.WriteLine($"Ansible workdir: {gen.WorkDir}");

var check = (cmd == "plan");
var code = AnsibleRunner.Run(
    gen.WorkDir,
    limit: limit,
    check: check,
    askBecomePass: askBecomePass,
    forks: forks,
    verbose: verbose);

// Итоговая сводка
var mode = check ? "PLAN (dry-run)" : "APPLY";
var stdoutPath = Path.Combine(gen.WorkDir, "ansible.stdout.log");
var stderrPath = Path.Combine(gen.WorkDir, "ansible.stderr.log");
if (code == 0)
    Console.WriteLine($"✔ {mode} finished successfully. Logs: {stdoutPath} / {stderrPath}");
else
    Console.WriteLine($"✖ {mode} failed (exit code {code}). See logs: {stdoutPath} / {stderrPath}");

Environment.ExitCode = code;

