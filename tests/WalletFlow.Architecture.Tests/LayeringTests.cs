using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace WalletFlow.Architecture.Tests;

/// <summary>Executable documentation of the Clean Architecture dependency rule.</summary>
public class LayeringTests
{
    private static readonly Assembly Domain = typeof(WalletFlow.Domain.Common.Money).Assembly;
    private static readonly Assembly Application = typeof(WalletFlow.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(WalletFlow.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(WalletFlow.Api.Controllers.AuthController).Assembly;

    [Fact]
    public void Domain_DependsOnNothingElse()
    {
        var result = Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("WalletFlow.Application", "WalletFlow.Infrastructure", "WalletFlow.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "MediatR", "Npgsql")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failing(result));
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrApi()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("WalletFlow.Infrastructure", "WalletFlow.Api", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failing(result));
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnApi()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot().HaveDependencyOn("WalletFlow.Api").GetResult();

        result.IsSuccessful.Should().BeTrue(Failing(result));
    }

    [Fact]
    public void Controllers_TalkToMediatorOnly_NotToPersistence()
    {
        var result = Types.InAssembly(Api).That().HaveNameEndingWith("Controller").ShouldNot()
            .HaveDependencyOnAny("WalletFlow.Infrastructure", "Microsoft.EntityFrameworkCore", "WalletFlow.Domain.Accounts.Account")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(Failing(result));
    }

    [Fact]
    public void Handlers_AreSealed_AndIndependentOfTheApi()
    {
        var handlers = Types.InAssembly(Application).That().HaveNameEndingWith("Handler");

        handlers.Should().NotHaveDependencyOn("WalletFlow.Api").GetResult().IsSuccessful.Should().BeTrue();
        handlers.Should().BeSealed().GetResult().IsSuccessful.Should().BeTrue();
    }

    private static string Failing(TestResult result) =>
        result.FailingTypeNames is { Count: > 0 } names ? "Violating types: " + string.Join(", ", names) : string.Empty;
}

