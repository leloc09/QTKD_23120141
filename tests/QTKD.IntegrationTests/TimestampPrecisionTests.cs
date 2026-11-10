using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QTKD.Api.Dtos;
using QTKD.IntegrationTests.Infrastructure;

namespace QTKD.IntegrationTests;

// Lỗi BUG-02 (tìm thấy ở tuần 07): PostgreSQL lưu thời gian đến micro giây, .NET trên Windows/Linux
// có độ chính xác 100ns. Thời gian API trả về phải khớp với giá trị lưu trong DB, kể cả khi gửi lặp.
// macOS chỉ có độ chính xác micro giây nên dùng đồng hồ giả để tái hiện trên mọi máy.
public class TimestampPrecisionTests(QtkdApiFactory factory) : ApiTestBase(factory)
{
    // Đồng hồ có phần lẻ 100ns (chữ số cuối của Ticks khác 0) như trên Windows/Linux
    private sealed class HundredNanosecondClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            var now = System.GetUtcNow();
            return now.AddTicks(7 - now.Ticks % 10);
        }
    }

    private HttpClient HighPrecisionClient()
    {
        var client = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new HundredNanosecondClock()))).CreateClient();
        client.DefaultRequestHeaders.Add("X-User", "tester");
        return client;
    }

    [Fact]
    public async Task SubmitLapCungKey_ThoiGianGuiTraVeGiongLanDau()
    {
        var client = HighPrecisionClient();
        var contract = await CreateSubmittableContractAsync();
        var key = Guid.NewGuid().ToString();

        async Task<SubmitResponse> Submit()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/contracts/{contract.Id}/submit");
            request.Headers.Add("Idempotency-Key", key);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ReadAsync<SubmitResponse>(response);
        }

        var first = await Submit();
        var replay = await Submit();

        Assert.True(replay.Replayed);
        Assert.Equal(first.SubmittedAt, replay.SubmittedAt);

        await using var db = Db();
        var stored = await db.ApprovalRequests.AsNoTracking().SingleAsync(a => a.Id == first.ApprovalId);
        Assert.Equal(stored.CreatedAt, first.SubmittedAt);
    }

    [Fact]
    public async Task TaoVaSuaContract_UpdatedAtTraVeKhopVoiDb()
    {
        var client = HighPrecisionClient();
        var customer = await CreateCustomerAsync();

        var created = await ReadAsync<ContractDetail>(await client.PostAsJsonAsync("/api/contracts", ContractBody(customer.Id)));
        var fetched = await client.GetFromJsonAsync<ContractDetail>($"/api/contracts/{created.Id}", Json);
        Assert.Equal(fetched!.UpdatedAt, created.UpdatedAt);

        var updated = await ReadAsync<ContractDetail>(await client.PutAsJsonAsync($"/api/contracts/{created.Id}",
            ContractBody(customer.Id, created.ContractNo, content: "Nội dung mới")));
        await using var db = Db();
        Assert.Equal((await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == created.Id)).UpdatedAt, updated.UpdatedAt);
    }
}
