using Microsoft.EntityFrameworkCore;
using QTKD.Api.Domain;

namespace QTKD.Api.Data;

public class QtkdDbContext : DbContext
{
    public QtkdDbContext(DbContextOptions<QtkdDbContext> options) : base(options) { }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractAttachment> ContractAttachments => Set<ContractAttachment>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ContractHistory> ContractHistory => Set<ContractHistory>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceItem> PriceItems => Set<PriceItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.TaxCode).IsRequired().HasMaxLength(20);
            e.HasIndex(x => x.TaxCode).IsUnique();
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Contract>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ContractNo).IsRequired().HasMaxLength(50);
            e.HasIndex(x => x.ContractNo).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ServiceGroup).HasMaxLength(100);
            e.Property(x => x.Content).HasMaxLength(4000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne(x => x.Customer)
                .WithMany(c => c.Contracts)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContractAttachment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).IsRequired().HasMaxLength(255);
            e.Property(x => x.DocumentType).IsRequired().HasMaxLength(50);
            e.HasOne(x => x.Contract)
                .WithMany(c => c.Attachments)
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.RequestedBy).IsRequired().HasMaxLength(100);
            // Mỗi hợp đồng chỉ có tối đa 1 phiên phê duyệt đang chờ (AC05)
            e.HasIndex(x => x.ContractId).IsUnique().HasFilter("\"Status\" = 'Pending'");
            e.HasIndex(x => new { x.ContractId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasOne(x => x.Contract)
                .WithMany(c => c.ApprovalRequests)
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContractHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).IsRequired().HasMaxLength(50);
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.PerformedBy).IsRequired().HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => x.ContractId);
            e.HasOne(x => x.Contract)
                .WithMany(c => c.History)
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PriceList>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<PriceItem>(e =>
        {
            e.HasKey(x => new { x.PriceListId, x.ServiceCode });
            e.Property(x => x.ServiceCode).IsRequired().HasMaxLength(50);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasOne(x => x.PriceList)
                .WithMany(p => p.Items)
                .HasForeignKey(x => x.PriceListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        Seed(modelBuilder);
    }

    // Dữ liệu mẫu: 2 Customer, 1 Contract Draft, 1 PriceList có 2 PriceItem
    private static void Seed(ModelBuilder modelBuilder)
    {
        var minhPhat = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var dongA = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var priceList = Guid.Parse("33333333-3333-3333-3333-333333333333");

        modelBuilder.Entity<Customer>().HasData(
            new Customer { Id = minhPhat, Name = "Công ty Minh Phát", TaxCode = "0301234567", Status = CustomerStatus.Active },
            new Customer { Id = dongA, Name = "Công ty Đông Á", TaxCode = "0309876543", Status = CustomerStatus.Active });

        // Hợp đồng mẫu còn thiếu nội dung và tệp bắt buộc: gửi trình duyệt sẽ bị từ chối theo CTR-02
        modelBuilder.Entity<Contract>().HasData(new Contract
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ContractNo = "HD-2026-018",
            CustomerId = minhPhat,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2027, 9, 30),
            Status = ContractStatus.Draft,
            ServiceGroup = "Dịch vụ cảng",
            UpdatedAt = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero)
        });

        modelBuilder.Entity<PriceList>().HasData(new PriceList
        {
            Id = priceList,
            Name = "Bảng giá dịch vụ cảng 2026",
            EffectiveDate = new DateOnly(2026, 10, 1)
        });

        modelBuilder.Entity<PriceItem>().HasData(
            new PriceItem { PriceListId = priceList, ServiceCode = "CANG-BOC-XEP", UnitPrice = 150000m },
            new PriceItem { PriceListId = priceList, ServiceCode = "CANG-LUU-BAI", UnitPrice = 45000m });
    }
}
