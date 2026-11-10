using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Data;
using QTKD.Api.Domain;
using QTKD.Api.Dtos;

namespace QTKD.IntegrationTests.Infrastructure;

// Hàm tiện ích chung: gọi API, đọc body, đối chiếu DB.
// Mỗi test tự tạo Customer/Contract riêng (mã số thuế, số hợp đồng ngẫu nhiên) nên các test độc lập với nhau.
[Collection(ApiCollection.Name)]
public abstract class ApiTestBase
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected readonly QtkdApiFactory Factory;
    protected readonly HttpClient Client;

    // Ngày hiệu lực tính theo ngày chạy test để không "hết hạn" theo thời gian
    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    protected static DateOnly ValidStart => Today.AddDays(1);
    protected static DateOnly ValidEnd => Today.AddYears(1);

    protected ApiTestBase(QtkdApiFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-User", "tester");
    }

    protected QtkdDbContext Db() => Factory.CreateDbContext();

    protected static string NewTaxCode() => Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString();
    protected static string NewContractNo() => $"HD-T-{Guid.NewGuid():N}"[..20];

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    protected static async Task<ErrorResponse> ReadErrorAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        var error = await ReadAsync<ErrorResponse>(response);
        Assert.False(string.IsNullOrWhiteSpace(error.Code));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        return error;
    }

    protected async Task<CustomerResponse> CreateCustomerAsync(string? name = null, string? status = null)
    {
        var response = await Client.PostAsJsonAsync("/api/customers",
            new CustomerRequest(name ?? $"Công ty Test {Guid.NewGuid():N}"[..30], NewTaxCode(), "TP.HCM", status));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<CustomerResponse>(response);
    }

    protected static ContractRequest ContractBody(Guid customerId, string? contractNo = null,
        string? serviceGroup = "Dịch vụ cảng", string? content = "Nội dung hợp đồng",
        DateOnly? start = null, DateOnly? end = null) =>
        new(contractNo ?? NewContractNo(), customerId, start ?? ValidStart, end ?? ValidEnd, serviceGroup, content);

    protected async Task<ContractDetail> CreateContractAsync(ContractRequest body)
    {
        var response = await Client.PostAsJsonAsync("/api/contracts", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<ContractDetail>(response);
    }

    protected async Task AddDraftFileAsync(Guid contractId, string documentType = ContractRules.RequiredDocumentType)
    {
        var response = await Client.PostAsJsonAsync($"/api/contracts/{contractId}/attachments",
            new AttachmentRequest("du-thao.pdf", documentType));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Hợp đồng Draft đạt đủ CTR-02 (khách hàng Active, ngày hợp lệ, đủ nội dung, có tệp dự thảo)
    protected async Task<ContractDetail> CreateSubmittableContractAsync()
    {
        var customer = await CreateCustomerAsync();
        var contract = await CreateContractAsync(ContractBody(customer.Id));
        await AddDraftFileAsync(contract.Id);
        return contract;
    }

    protected Task<HttpResponseMessage> SubmitAsync(Guid contractId, string? idempotencyKey = null, string? comment = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/contracts/{contractId}/submit");
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (comment is not null) request.Content = JsonContent.Create(new SubmitRequest(comment));
        return Client.SendAsync(request);
    }

    // Ảnh chụp toàn bộ dữ liệu liên quan tới một hợp đồng trong DB: dùng để chứng minh "dữ liệu không đổi"
    protected async Task<ContractSnapshot> SnapshotAsync(Guid contractId)
    {
        await using var db = Db();
        var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == contractId);
        return new ContractSnapshot(
            c.Status, c.ContractNo, c.CustomerId, c.StartDate, c.EndDate, c.ServiceGroup, c.Content, c.UpdatedAt, c.Version,
            await db.ApprovalRequests.CountAsync(a => a.ContractId == contractId),
            await db.ContractHistory.CountAsync(h => h.ContractId == contractId),
            await db.ContractAttachments.CountAsync(a => a.ContractId == contractId));
    }

    protected record ContractSnapshot(
        ContractStatus Status, string ContractNo, Guid CustomerId, DateOnly StartDate, DateOnly EndDate,
        string? ServiceGroup, string? Content, DateTimeOffset UpdatedAt, uint Version,
        int ApprovalCount, int HistoryCount, int AttachmentCount);
}
