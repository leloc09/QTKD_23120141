using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Domain;
using QTKD.Api.Dtos;
using QTKD.IntegrationTests.Infrastructure;

namespace QTKD.IntegrationTests;

// Tạo / sửa hợp đồng — TC03, TC04 (CTR-01)
public class ContractApiTests(QtkdApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task TaoContract_LuuOTrangThaiDraftVaGhiLichSu()
    {
        var customer = await CreateCustomerAsync();
        var body = ContractBody(customer.Id, content: null);

        var response = await Client.PostAsJsonAsync("/api/contracts", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync<ContractDetail>(response);
        Assert.Equal("Draft", created.Status);
        Assert.Equal(customer.Name, created.CustomerName);
        Assert.False(created.SubmitCheck.CanSubmit); // còn thiếu content + tệp

        await using var db = Db();
        var row = await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal((body.ContractNo, customer.Id, ContractStatus.Draft), (row.ContractNo, row.CustomerId, row.Status));
        Assert.Equal((body.StartDate!.Value, body.EndDate!.Value), (row.StartDate, row.EndDate));
        var history = await db.ContractHistory.AsNoTracking().SingleAsync(h => h.ContractId == created.Id);
        Assert.Equal(("Created", ContractStatus.Draft, "tester"), (history.Action, history.ToStatus, history.PerformedBy));
    }

    [Fact]
    public async Task TaoContract_NgayKetThucTruocNgayBatDau_BiTuChoi()
    {
        var customer = await CreateCustomerAsync();
        var body = ContractBody(customer.Id, start: ValidEnd, end: ValidStart);

        var error = await ReadErrorAsync(await Client.PostAsJsonAsync("/api/contracts", body), HttpStatusCode.BadRequest);

        Assert.Contains(error.Errors, e => e is { Field: "endDate", Code: "INVALID_PERIOD" });
        await using var db = Db();
        Assert.False(await db.Contracts.AnyAsync(c => c.ContractNo == body.ContractNo));
    }

    [Fact]
    public async Task TaoContract_CustomerKhongTonTaiHoacInactive_BiTuChoi()
    {
        var missing = await ReadErrorAsync(
            await Client.PostAsJsonAsync("/api/contracts", ContractBody(Guid.NewGuid())), HttpStatusCode.BadRequest);
        Assert.Contains(missing.Errors, e => e is { Field: "customerId", Code: "NOT_FOUND" });

        var inactive = await CreateCustomerAsync(status: "Inactive");
        var error = await ReadErrorAsync(
            await Client.PostAsJsonAsync("/api/contracts", ContractBody(inactive.Id)), HttpStatusCode.BadRequest);
        Assert.Contains(error.Errors, e => e is { Field: "customerId", Code: "CUSTOMER_INACTIVE" });
    }

    [Fact]
    public async Task TaoContract_TrungSoHopDong_409()
    {
        var customer = await CreateCustomerAsync();
        var first = await CreateContractAsync(ContractBody(customer.Id));

        var error = await ReadErrorAsync(
            await Client.PostAsJsonAsync("/api/contracts", ContractBody(customer.Id, first.ContractNo)), HttpStatusCode.Conflict);
        Assert.Equal("CONTRACT_NO_EXISTS", error.Code);
    }

    [Fact]
    public async Task TC03_SuaContractODraft_ThanhCongKhiDuLieuHopLe()
    {
        var customer = await CreateCustomerAsync();
        var otherCustomer = await CreateCustomerAsync();
        var contract = await CreateContractAsync(ContractBody(customer.Id, content: null));
        var update = ContractBody(otherCustomer.Id, contract.ContractNo, "Dịch vụ kho bãi", "Nội dung đã bổ sung",
            ValidStart.AddDays(10), ValidEnd.AddDays(10));

        var response = await Client.PutAsJsonAsync($"/api/contracts/{contract.Id}", update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync<ContractDetail>(response);
        Assert.Equal(("Draft", otherCustomer.Name, "Nội dung đã bổ sung"), (body.Status, body.CustomerName, body.Content));

        await using var db = Db();
        var row = await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(ContractStatus.Draft, row.Status);
        Assert.Equal(otherCustomer.Id, row.CustomerId);
        Assert.Equal("Dịch vụ kho bãi", row.ServiceGroup);
        Assert.Equal("Nội dung đã bổ sung", row.Content);
        Assert.Equal((update.StartDate!.Value, update.EndDate!.Value), (row.StartDate, row.EndDate));
        Assert.True(row.UpdatedAt > contract.UpdatedAt);
        Assert.True(await db.ContractHistory.AnyAsync(h => h.ContractId == contract.Id && h.Action == "Updated"));
    }

    [Fact]
    public async Task TC03b_SuaContractODraft_DuLieuKhongHopLe_BiTuChoiVaKhongDoi()
    {
        var customer = await CreateCustomerAsync();
        var contract = await CreateContractAsync(ContractBody(customer.Id));
        var before = await SnapshotAsync(contract.Id);

        var response = await Client.PutAsJsonAsync($"/api/contracts/{contract.Id}",
            ContractBody(customer.Id, contract.ContractNo, start: ValidEnd, end: ValidStart));

        var error = await ReadErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains(error.Errors, e => e.Code == "INVALID_PERIOD");
        Assert.Equal(before, await SnapshotAsync(contract.Id));
    }

    [Fact]
    public async Task TC04_SuaContractOPendingApproval_BiTuChoiDuLieuGiuNguyen()
    {
        var contract = await CreateSubmittableContractAsync();
        Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(contract.Id)).StatusCode);
        var before = await SnapshotAsync(contract.Id);
        Assert.Equal(ContractStatus.PendingApproval, before.Status);

        var response = await Client.PutAsJsonAsync($"/api/contracts/{contract.Id}",
            ContractBody(contract.CustomerId, contract.ContractNo, "Nhóm khác", "Sửa trái phép"));

        var error = await ReadErrorAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("CTR01_NOT_EDITABLE", error.Code);
        Assert.Contains("PendingApproval", error.Message);
        Assert.Equal(before, await SnapshotAsync(contract.Id)); // kể cả UpdatedAt, xmin, số dòng lịch sử
    }

    [Fact]
    public async Task TC04b_ThemTepKhiPendingApproval_BiTuChoi()
    {
        var contract = await CreateSubmittableContractAsync();
        Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(contract.Id)).StatusCode);
        var before = await SnapshotAsync(contract.Id);

        var response = await Client.PostAsJsonAsync($"/api/contracts/{contract.Id}/attachments",
            new AttachmentRequest("them.pdf", "PhuLuc"));

        Assert.Equal("CTR01_NOT_EDITABLE", (await ReadErrorAsync(response, HttpStatusCode.Conflict)).Code);
        Assert.Equal(before, await SnapshotAsync(contract.Id));
    }

    [Fact]
    public async Task SuaContractKhongTonTai_404()
    {
        var customer = await CreateCustomerAsync();
        var error = await ReadErrorAsync(
            await Client.PutAsJsonAsync($"/api/contracts/{Guid.NewGuid()}", ContractBody(customer.Id)), HttpStatusCode.NotFound);
        Assert.Equal("CONTRACT_NOT_FOUND", error.Code);
    }
}
