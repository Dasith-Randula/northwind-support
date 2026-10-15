using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Data.Configurations;

public sealed class TicketEscalationConfiguration : IEntityTypeConfiguration<TicketEscalation>
{
    public void Configure(EntityTypeBuilder<TicketEscalation> builder)
    {
        builder.ToTable("TicketEscalations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FromPriority).HasConversion<int>();
        builder.Property(x => x.ToPriority).HasConversion<int>();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.EscalatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FromDueAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.ToDueAtUtc).HasColumnType("datetime2(3)");
        builder.Property(x => x.EscalatedAtUtc).HasColumnType("datetime2(3)");
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TicketId, x.EscalatedAtUtc, x.Id })
            .HasDatabaseName("IX_TicketEscalations_TicketId_EscalatedAtUtc_Id");
    }
}
