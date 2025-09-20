using FluentValidation;
using SageCli.Models;
using System.Text.RegularExpressions;

namespace SageCli.Validation;

public class RootConfigValidator : AbstractValidator<RootConfig>
{
    public RootConfigValidator()
    {
        RuleFor(x => x.Version).Equal(1);
        RuleFor(x => x.Ssh).NotNull().SetValidator(new SshConfigValidator());
        RuleFor(x => x.Hosts).NotEmpty();
        RuleForEach(x => x.Hosts).SetValidator(new HostConfigValidator());
    }
}

public class SshConfigValidator : AbstractValidator<SshConfig>
{
    public SshConfigValidator()
    {
        RuleFor(x => x.User).NotEmpty();                 
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.PrivateKeyPath).NotEmpty();
    }
}

public class HostConfigValidator : AbstractValidator<HostConfig>
{
    private static readonly Regex HostnameRegex = new(
        "^(?=.{1,253}$)(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*$"
    );
    private static readonly Regex IpRegex = new(
        "^(?:[0-9]{1,3}\\.){3}[0-9]{1,3}$"
    );

    public HostConfigValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Address)
            .NotEmpty()
            .Must(addr => IpRegex.IsMatch(addr) || HostnameRegex.IsMatch(addr))
            .WithMessage("Address должен быть IP или DNS именем");

        When(x => x.DockerApps != null, () =>
        {
            RuleForEach(x => x.DockerApps!).SetValidator(new DockerAppValidator());
        });

        When(x => x.HostsEntries != null, () =>
        {
            RuleFor(x => x.HostsEntries!.ManagedBlockName).NotEmpty();
        });
    }
}

public class DockerAppValidator : AbstractValidator<DockerApp>
{
    public DockerAppValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Image).NotEmpty();
        When(x => x.Cpus.HasValue, () =>
            RuleFor(x => x.Cpus!.Value).GreaterThan(0)
        );
        // Memory позже проверю на формат (например, "256m", "1g") — не обязательно для MVP
    }
}

