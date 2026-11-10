using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Domain;
using QTKD.Api.Dtos;
using QTKD.IntegrationTests.Infrastructure;

namespace QTKD.IntegrationTests;

// Tự động hóa luồng demo tuần 06 (mục 4.3 bước 13):
// tạo Customer → tạo Contract Draft → gửi khi thiếu dữ liệu (thất bại) → bổ sung → gửi lại (thành công) → xem DB
public class DemoFlowTests(QtkdApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task LuongDemoTuan06_CustomerContractSubmit()
    {
        // 1. Nhân viên Kinh doanh tạo khách hàng Công ty Minh Phát
        var customer = await CreateCustomerAsync("Công ty Minh Phát (demo)");

        // 2. Lập hợp đồng Draft, chưa có nội dung và tệp
        var contractNo = $"HD-2026-018-{Random.Shared.Next(100000, 999999)}";
        var contract = await CreateContractAsync(ContractBody(customer.Id, contractNo, content: null));
        Assert.Equal("Draft", contract.Status);

        // 3. Gửi khi thiếu dữ liệu -> 400, vẫn Draft
        var fail = await ReadErrorAsync(await SubmitAsync(contract.Id), HttpStatusCode.BadRequest);
        Assert.Equal("CTR02_NOT_SATISFIED", fail.Code);
        Assert.Equal(new[] { "REQUIRED_CONTENT", "REQUIRED_FILES" }, fail.Errors.Select(e => e.Field).Order());

        // 4. Bổ sung nội dung + tệp dự thảo
        var update = await Client.PutAsJsonAsync($"/api/contracts/{contract.Id}",
            ContractBody(customer.Id, contractNo, content: "Cung cấp dịch vụ bốc xếp tại cảng"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        await AddDraftFileAsync(contract.Id);

        // 5. Gửi lại -> 200, PendingApproval
        var ok = await SubmitAsync(contract.Id);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("PendingApproval", (await ReadAsync<SubmitResponse>(ok)).Status);

        // 6. Xem trạng thái trong database
        await using var db = Db();
        var row = await db.Contracts.AsNoTracking().Include(c => c.Customer).SingleAsync(c => c.ContractNo == contractNo);
        Assert.Equal(ContractStatus.PendingApproval, row.Status);
        Assert.Equal("Công ty Minh Phát (demo)", row.Customer!.Name);
        Assert.Equal(1, await db.ApprovalRequests.CountAsync(a => a.ContractId == row.Id));
        var actions = await db.ContractHistory.AsNoTracking().Where(h => h.ContractId == row.Id)
            .OrderBy(h => h.CreatedAt).Select(h => h.Action).ToListAsync();
        Assert.Equal(new[] { "Created", "Updated", "AttachmentAdded", "Submitted" }, actions);
    }

    [Fact]
    public async Task DuLieuMauTuan05_DocDuocQuaApi()
    {
        // Migration tạo đủ dữ liệu mẫu: Customer Minh Phát + hợp đồng HD-2026-018 (Draft) + bảng giá 2 mục
        var customers = await Client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers", Json);
        Assert.Contains(customers!, c => c.TaxCode == "0301234567");

        var seed = await Client.GetFromJsonAsync<ContractDetail>("/api/contracts/44444444-4444-4444-4444-444444444444", Json);
        Assert.Equal(("HD-2026-018", "Công ty Minh Phát"), (seed!.ContractNo, seed.CustomerName));

        var items = await Client.GetFromJsonAsync<List<PriceItemResponse>>(
            "/api/pricelists/33333333-3333-3333-3333-333333333333/items", Json);
        Assert.Equal(2, items!.Count);
    }

    [Fact]
    public async Task Pricing_TaoBangGiaVaThemMuc_DonGiaKhongHopLeBiTuChoi()
    {
        var create = await Client.PostAsJsonAsync("/api/pricelists", new PriceListRequest("Bảng giá test",
            Today, new() { new("TEST-A", 1000m) }));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var priceList = await ReadAsync<PriceListResponse>(create);

        var bad = await Client.PostAsJsonAsync($"/api/pricelists/{priceList.Id}/items", new PriceItemRequest("TEST-B", -5m));
        Assert.Contains((await ReadErrorAsync(bad, HttpStatusCode.BadRequest)).Errors, e => e.Code == "MUST_BE_POSITIVE");

        var good = await Client.PostAsJsonAsync($"/api/pricelists/{priceList.Id}/items", new PriceItemRequest("TEST-B", 2500.5m));
        Assert.Equal(HttpStatusCode.Created, good.StatusCode);

        await using var db = Db();
        var rows = await db.PriceItems.AsNoTracking().Where(i => i.PriceListId == priceList.Id)
            .OrderBy(i => i.ServiceCode).Select(i => new { i.ServiceCode, i.UnitPrice }).ToListAsync();
        Assert.Equal(new[] { ("TEST-A", 1000m), ("TEST-B", 2500.5m) }, rows.Select(r => (r.ServiceCode, r.UnitPrice)));
    }
}
