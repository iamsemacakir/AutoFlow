using AutoFlow.Domain.Customers;
using AutoFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoFlow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps staff accounts to the <c>users</c> table. The login is unique
/// (<c>ux_users_email</c>, stored lower-case by the value object), so two
/// accounts can never share an e-mail address even when created at once.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string EmailIndex = "ux_users_email";

    private const int EnumNameMaxLength = 20;

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table => table.HasCheckConstraint(
            "ck_users_role",
            $"role IN ({string.Join(", ", Enum.GetNames<UserRole>().Select(name => $"'{name}'"))})"));

        builder.HasKey(u => u.Id).HasName("pk_users");
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(EmailAddress.MaxLength)
            .HasConversion(ValueConverters.EmailAddress)
            .IsRequired();
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName(EmailIndex);

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(User.DisplayNameMaxLength)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasMaxLength(EnumNameMaxLength)
            .HasConversion<string>();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(User.PasswordHashMaxLength)
            .IsRequired();

        builder.Property(u => u.IsActive).HasColumnName("is_active");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.LastSignInAt).HasColumnName("last_sign_in_at");

        builder.ConfigureRowVersion();
    }
}
