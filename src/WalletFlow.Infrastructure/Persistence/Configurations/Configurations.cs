using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.Accounts;
using WalletFlow.Domain.Auditing;
using WalletFlow.Domain.Idempotency;
using WalletFlow.Domain.Outbox;
using WalletFlow.Domain.Transactions;
using WalletFlow.Domain.Users;

namespace WalletFlow.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the domain
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.Email).IsUnique();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the domain
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("accounts", t => t.HasCheckConstraint("ck_accounts_balance_non_negative", "balance >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the domain
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Balance).HasPrecision(18, 2);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        // Optimistic concurrency: a uint property is mapped to PostgreSQL's xmin system column.
        b.Property(x => x.Version).IsRowVersion();
        b.Ignore(x => x.CurrentBalance);
        b.HasIndex(x => x.OwnerId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("transactions", t => t.HasCheckConstraint("ck_transactions_amount_positive", "amount > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the domain
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(140).IsRequired();
        b.Property(x => x.FailureReason).HasMaxLength(200);
        b.HasIndex(x => x.SourceAccountId);
        b.HasIndex(x => x.DestinationAccountId);
        b.HasIndex(x => x.CreatedAt);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Entries).WithOne().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.ToTable("ledger_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the domain
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Ignore(x => x.SignedAmount);
        b.HasIndex(x => x.AccountId);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the domain
        b.Property(x => x.Key).HasMaxLength(64).IsRequired();
        b.Property(x => x.RequestName).HasMaxLength(100).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        // The unique index is what makes the reservation race-safe.
        b.HasIndex(x => new { x.UserId, x.Key }).IsUnique();
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the domain
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.Details).HasMaxLength(500);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever(); // ids are assigned by the domain
        b.Property(x => x.Type).HasMaxLength(200).IsRequired();
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasIndex(x => x.CreatedAt).HasFilter("processed_at IS NULL");
    }
}

