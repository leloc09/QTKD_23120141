using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QTKD.Api.Data;
using Testcontainers.PostgreSql;

namespace QTKD.IntegrationTests.Infrastructure;

// Chạy API thật (WebApplicationFactory) trên một database PostgreSQL kiểm thử riêng:
//  - Mặc định: Testcontainers tạo container postgres:16 tạm thời, xóa sau khi chạy xong (cần Docker).
//  - Hoặc: đặt biến môi trường QTKD_TEST_DB = chuỗi kết nối tới database có tên chứa "test"
//    (database này sẽ bị XÓA và tạo lại từ migration ở đầu mỗi lần chạy).
// Không bao giờ dùng database "qtkd" có dữ liệu thật.
public class QtkdApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("QTKD_TEST_DB");
        if (!string.IsNullOrWhiteSpace(external))
        {
            var database = new NpgsqlConnectionStringBuilder(external).Database ?? string.Empty;
            if (!database.Contains("test", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"QTKD_TEST_DB phải trỏ tới database kiểm thử (tên chứa 'test'), không phải '{database}'.");
            ConnectionString = external;
        }
        else
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithDatabase("qtkd_test")
                .Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        // Tạo schema bằng chính các migration của dự án (kiểm tra luôn migration chạy được trên DB trống)
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Môi trường "Testing": không nạp User Secrets (chuỗi kết nối DB thật) của môi trường Development
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:QtkdDb", ConnectionString);
    }

    // DbContext độc lập với API để đối chiếu dữ liệu sau mỗi lần gọi (không dùng chung change tracker)
    public QtkdDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<QtkdDbContext>().UseNpgsql(ConnectionString).Options);

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<QtkdApiFactory>
{
    public const string Name = "QTKD API";
}
