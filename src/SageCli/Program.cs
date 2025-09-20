using SageCli.Config;
using SageCli.Validation;
using SageCli.Ansible;
using FluentValidation;
using SageCli.Models;

static void PrintUsage()
{
    Console.WriteLine("Usage: sage <validate|plan|apply> -f <config.yaml> [--limit host1,host2] [--ask-become-pass|-K]");
}

if (args.Length == 0) { PrintUsage(); return; }

string? cmd = args[0];
string? file = null;
string? limit = null;
bool askBecomePass = false;

for (int i = 1; i < args.Length; i++)
{
    var a = args[i];
    if ((a == "-f" || a == "--file") && i + 1 < args.Length) { file = args[++i]; }
    else if (a == "--limit" && i + 1 < args.Length) { limit = args[++i]; }
    else if (a == "--ask-become-pass" || a == "-K") { askBecomePass = true; }
}

if (string.IsNullOrWhiteSpace(cmd)) { PrintUsage(); return; }
if (string.IsNullOrWhiteSpace(file)) { Console.Error.WriteLine("Missing -f <config.yaml>"); return; }

RootConfig cfg;
try { cfg = ConfigLoader.Load(file!); }
catch (Exception ex) { Console.Error.WriteLine($"Failed to load YAML: {ex.Message}"); return; }

var validator = new RootConfigValidator();
var res = validator.Validate(cfg);

if (cmd == "validate")
{
    if (res.IsValid) Console.WriteLine("Config is valid ✔");
    else
    {
        Console.Error.WriteLine("Validation errors:");
        foreach (var e in res.Errors) Console.Error.WriteLine($" - {e.PropertyName}: {e.ErrorMessage}");
    }
    return;
}
else if (cmd == "plan" || cmd == "apply")
{
    if (!res.IsValid)
    {
        Console.Error.WriteLine("Config invalid. Run 'sage validate -f ...' and fix errors.");
        foreach (var e in res.Errors) Console.Error.WriteLine($" - {e.PropertyName}: {e.ErrorMessage}");
        return;
    }

    // Рабочая папка для артефактов Ansible
    var baseTmp = Path.Combine(Directory.GetCurrentDirectory(), ".sage-tmp");
    Directory.CreateDirectory(baseTmp);
    var gen = new AnsibleGenerator(baseTmp);

    // Шаблон site.yml в корне репозитория: <repo>/ansible_templates/site.yml
    var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "ansible_templates", "site.yml");
    if (!File.Exists(templatePath))
    {
        Console.Error.WriteLine($"site.yml template not found: {templatePath}");
        return;
    }

    gen.WriteSiteYml(templatePath);
    gen.WriteInventory(cfg);
    foreach (var h in cfg.Hosts) gen.WriteHostVars(h);

    Console.WriteLine($"Ansible workdir: {gen.WorkDir}");
    var code = AnsibleRunner.Run(
        gen.WorkDir,
        limit,
        check: cmd == "plan",
        askBecomePass: askBecomePass
    );
    Environment.ExitCode = code;
    return;
}
else
{
    PrintUsage();
}

