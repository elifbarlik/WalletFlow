using FluentValidation;
using MediatR;
using WalletFlow.Application.Abstractions;
using WalletFlow.Application.Common;
using WalletFlow.Domain.Users;

namespace WalletFlow.Application.Features.Auth;

public sealed record RegisterCommand(string Email, string Password) : IRequest<UserDto>;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .MaximumLength(128)
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

internal sealed class RegisterHandler(
    IUserRepository users,
    IPasswordHasher hasher,
    IAuditTrail audit,
    IUnitOfWork uow,
    IClock clock) : IRequestHandler<RegisterCommand, UserDto>
{
    public async Task<UserDto> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        if (await users.EmailExistsAsync(email, ct))
        {
            throw new ConflictException("user.email_taken", "This e-mail address is already registered.");
        }

        // Self-registration can only ever create customers; privileged users are provisioned out of band.
        var user = User.Register(email, hasher.Hash(request.Password), Role.Customer, clock.UtcNow);
        users.Add(user);
        audit.Record(user.Id, "user.registered", nameof(User), user.Id.ToString());
        await uow.SaveChangesAsync(ct);
        return user.ToDto();
    }
}
