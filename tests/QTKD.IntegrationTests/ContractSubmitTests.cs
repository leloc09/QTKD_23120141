using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Domain;
using QTKD.Api.Dtos;
using QTKD.IntegrationTests.Infrastructure;

namespace QTKD.IntegrationTests;

// US03 — Gửi hợp đồng trình duyệt: TC05 (CTR-02), TC06, TC07 (CTR-03), TC08 (gửi lặp)
public class ContractSubmitTests(QtkdApiFactory factory) : ApiTestBase(factory)
{
    private async Task AssertRejectedByCtr02(Guid contractId, params string[] expectedFailedConditions)
    {
        var before = await SnapshotAsync(contractId);
        Assert.Equal(ContractStatus.Draft, before.Status);

        var response = await SubmitAsync(contractId);

        var error = await ReadErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("CTR02_NOT_SATISFIED", error.Code);
        Assert.Equal(expectedFailedConditions.Order(), error.Errors.Select(e => e.Field).Order());
        Assert.All(error.Errors, e => Assert.Equal("NOT_SATISFIED", e.Code));

        var after = await SnapshotAsync(contractId);
        Assert.Equal(before, after); // vẫn Draft, không tạo ApprovalRequest, không ghi lịch sử
        Assert.Equal(ContractStatus.Draft, after.Status);
        Assert.Equal(0, after.ApprovalCount);
    }

    [Fact]
    public async Task TC05a_SubmitThieuNoiDung_BiTuChoiVanLaDraft()
    {
        var customer = await CreateCustomerAsync();
        var contract = await CreateContractAsync(ContractBody(customer.Id, content: null));
        await AddDraftFileAsync(contract.Id);

        await AssertRejectedByCtr02(contract.Id, "REQUIRED_CONTENT");
    }

    [Fact]
    public async Task TC05b_SubmitThieuTepDuThao_BiTuChoiVanLaDraft()
    {
        var customer = await CreateCustomerAsync();
        var contract = await CreateContractAsync(ContractBody(customer.Id));
        await AddDraftFileAsync(contract.Id, "PhuLuc"); // có tệp nhưng không phải loại bắt buộc

        await AssertRejectedByCtr02(contract.Id, "REQUIRED_FILES");
    }

    [Fact]
    public async Task TC05c_SubmitKhiKhachHangDaInactive_BiTuChoiVanLaDraft()
    {
        var contract = await CreateSubmittableContractAsync();
        var customer = await Client.GetFromJsonAsync<CustomerResponse>($"/api/customers/{contract.CustomerId}", Json);
        var deactivate = await Client.PutAsJsonAsync($"/api/customers/{contract.CustomerId}",
            new CustomerRequest(customer!.Name, customer.TaxCode, customer.Address, "Inactive"));
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        await AssertRejectedByCtr02(contract.Id, "CUSTOMER_VALID");
    }

    [Fact]
    public async Task TC05d_SubmitHopDongDaHetHieuLuc_BiTuChoiVanLaDraft()
    {
        var customer = await CreateCustomerAsync();
        var contract = await CreateContractAsync(ContractBody(customer.Id, start: Today.AddYears(-1), end: Today.AddDays(-1)));
        await AddDraftFileAsync(contract.Id);

        await AssertRejectedByCtr02(contract.Id, "EFFECTIVE_PERIOD_VALID");
    }

    [Fact]
    public async Task TC05e_SubmitThieuNhieuDieuKien_TraVeDuDanhSach()
    {
        var customer = await CreateCustomerAsync();
        var contract = await CreateContractAsync(ContractBody(customer.Id, serviceGroup: null, content: null));

        await AssertRejectedByCtr02(contract.Id, "REQUIRED_CONTENT", "REQUIRED_FILES");
    }

    [Fact]
    public async Task TC06_SubmitContractHopLe_ChuyenSangPendingApproval()
    {
        var contract = await CreateSubmittableContractAsync();
        var detail = await Client.GetFromJsonAsync<ContractDetail>($"/api/contracts/{contract.Id}", Json);
        Assert.True(detail!.SubmitCheck.CanSubmit);

        var response = await SubmitAsync(contract.Id, comment: "Kính gửi Trưởng phòng");

        // HTTP + body
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsync<SubmitResponse>(response);
        Assert.Equal((contract.Id, contract.ContractNo, "PendingApproval", false),
            (result.ContractId, result.ContractNo, result.Status, result.Replayed));
        Assert.NotEqual(Guid.Empty, result.ApprovalId);

        // Đọc lại trạng thái qua API
        var reloaded = await Client.GetFromJsonAsync<ContractDetail>($"/api/contracts/{contract.Id}", Json);
        Assert.Equal("PendingApproval", reloaded!.Status);
        Assert.False(reloaded.SubmitCheck.CanSubmit);

        // Dữ liệu trong DB: trạng thái, phiên phê duyệt (mô phỏng), lịch sử
        await using var db = Db();
        var row = await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id);
        Assert.Equal(ContractStatus.PendingApproval, row.Status);

        var approval = await db.ApprovalRequests.AsNoTracking().SingleAsync(a => a.ContractId == contract.Id);
        Assert.Equal(result.ApprovalId, approval.Id);
        Assert.Equal((ApprovalStatus.Pending, "tester", "Kính gửi Trưởng phòng"),
            (approval.Status, approval.RequestedBy, approval.Comment));

        var submitted = await db.ContractHistory.AsNoTracking()
            .SingleAsync(h => h.ContractId == contract.Id && h.Action == "Submitted");
        Assert.Equal((ContractStatus.Draft, ContractStatus.PendingApproval),
            (submitted.FromStatus!.Value, submitted.ToStatus!.Value));

        var history = await Client.GetFromJsonAsync<List<HistoryResponse>>($"/api/contracts/{contract.Id}/history", Json);
        Assert.Equal(new[] { "Created", "AttachmentAdded", "Submitted" }, history!.Select(h => h.Action));
    }

    [Fact]
    public async Task TC07_ChuyenDraftThangApproved_BiTuChoi()
    {
        var contract = await CreateSubmittableContractAsync(); // dù đủ CTR-02 vẫn không được duyệt thẳng
        var before = await SnapshotAsync(contract.Id);

        var response = await Client.PostAsync($"/api/contracts/{contract.Id}/approve", null);

        var error = await ReadErrorAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("CTR03_INVALID_TRANSITION", error.Code);
        var after = await SnapshotAsync(contract.Id);
        Assert.Equal(before, after);
        Assert.Equal(ContractStatus.Draft, after.Status);
    }

    [Fact]
    public async Task TC07b_DuyetSauKhiDaGuiTrinhDuyet_DuocChapNhan()
    {
        // Trường hợp đúng của CTR-03: Draft -> PendingApproval -> Approved
        var contract = await CreateSubmittableContractAsync();
        Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(contract.Id)).StatusCode);

        var response = await Client.PostAsync($"/api/contracts/{contract.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Approved", (await ReadAsync<ContractDetail>(response)).Status);
        await using var db = Db();
        Assert.Equal(ContractStatus.Approved, (await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == contract.Id)).Status);
        Assert.Equal(ApprovalStatus.Approved,
            (await db.ApprovalRequests.AsNoTracking().SingleAsync(a => a.ContractId == contract.Id)).Status);
    }

    [Fact]
    public async Task TC08a_SubmitLapCungIdempotencyKey_TraLaiKetQuaCuKhongTaoMoi()
    {
        var contract = await CreateSubmittableContractAsync();
        var key = Guid.NewGuid().ToString();

        var first = await ReadAsync<SubmitResponse>(await SubmitAsync(contract.Id, key));
        var afterFirst = await SnapshotAsync(contract.Id);
        var secondResponse = await SubmitAsync(contract.Id, key);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await ReadAsync<SubmitResponse>(secondResponse);
        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.ApprovalId, second.ApprovalId);
        Assert.Equal(first.SubmittedAt, second.SubmittedAt);
        Assert.Equal(afterFirst, await SnapshotAsync(contract.Id)); // không thêm ApprovalRequest / lịch sử
        Assert.Equal(1, afterFirst.ApprovalCount);
    }

    [Fact]
    public async Task TC08b_SubmitLapKhongKey_BiTuChoi409KhongTaoMoi()
    {
        var contract = await CreateSubmittableContractAsync();
        Assert.Equal(HttpStatusCode.OK, (await SubmitAsync(contract.Id)).StatusCode);
        var afterFirst = await SnapshotAsync(contract.Id);

        var error = await ReadErrorAsync(await SubmitAsync(contract.Id), HttpStatusCode.Conflict);

        Assert.Equal("CONTRACT_ALREADY_SUBMITTED", error.Code);
        Assert.Equal(afterFirst, await SnapshotAsync(contract.Id));
        Assert.Equal(1, afterFirst.ApprovalCount);
    }

    [Fact]
    public async Task TC08c_SubmitDongThoi_ChiMotYeuCauThanhCong()
    {
        var contract = await CreateSubmittableContractAsync();

        // 5 yêu cầu gửi cùng lúc, mỗi yêu cầu một key khác nhau (giống bấm nút ở nhiều tab)
        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => SubmitAsync(contract.Id, Guid.NewGuid().ToString())));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        var snapshot = await SnapshotAsync(contract.Id);
        Assert.Equal(ContractStatus.PendingApproval, snapshot.Status);
        Assert.Equal(1, snapshot.ApprovalCount);
        await using var db = Db();
        Assert.Equal(1, await db.ContractHistory.CountAsync(h => h.ContractId == contract.Id && h.Action == "Submitted"));
    }

    [Fact]
    public async Task TC08d_SubmitDongThoiCungKey_TatCaNhanCungMotKetQua()
    {
        var contract = await CreateSubmittableContractAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SubmitAsync(contract.Id, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var results = await Task.WhenAll(responses.Select(ReadAsync<SubmitResponse>));
        Assert.Single(results.Select(r => r.ApprovalId).Distinct());
        Assert.Equal(1, results.Count(r => !r.Replayed));
        Assert.Equal(1, (await SnapshotAsync(contract.Id)).ApprovalCount);
    }

    [Fact]
    public async Task SubmitHopDongKhongTonTai_404()
    {
        var error = await ReadErrorAsync(await SubmitAsync(Guid.NewGuid()), HttpStatusCode.NotFound);
        Assert.Equal("CONTRACT_NOT_FOUND", error.Code);
    }
}
