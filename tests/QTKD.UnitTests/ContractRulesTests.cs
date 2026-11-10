using QTKD.Api.Domain;

namespace QTKD.UnitTests;

// Unit test cho quy tắc nghiệp vụ hợp đồng (ContractRules không phụ thuộc database).
// Mỗi rule có cả trường hợp đúng (được phép) và trường hợp sai (bị từ chối).
public class ContractRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    // ---------- CTR-01: chỉ sửa hợp đồng ở Draft hoặc RevisionRequested ----------

    [Theory]
    [InlineData(ContractStatus.Draft)]
    [InlineData(ContractStatus.RevisionRequested)]
    public void CTR01_DuocSua_KhiDraftHoacRevisionRequested(ContractStatus status)
    {
        Assert.True(ContractRules.IsEditable(status));
    }

    [Theory]
    [InlineData(ContractStatus.PendingApproval)]
    [InlineData(ContractStatus.Approved)]
    [InlineData(ContractStatus.Rejected)]
    [InlineData(ContractStatus.Active)]
    public void CTR01_KhongDuocSua_KhiDaGuiHoacDaXuLy(ContractStatus status)
    {
        Assert.False(ContractRules.IsEditable(status));
    }

    // ---------- CTR-02: chỉ gửi khi Customer, ngày hiệu lực, nội dung, tệp bắt buộc hợp lệ ----------

    private static Customer ActiveCustomer() =>
        new() { Id = Guid.NewGuid(), Name = "Công ty Minh Phát", TaxCode = "0301234567", Status = CustomerStatus.Active };

    // Hợp đồng đạt đủ 4 điều kiện; từng test bỏ bớt đúng một điều kiện
    private static Contract ValidContract(Customer customer) => new()
    {
        Id = Guid.NewGuid(),
        ContractNo = "HD-2026-018",
        CustomerId = customer.Id,
        Customer = customer,
        StartDate = new DateOnly(2026, 12, 1),
        EndDate = new DateOnly(2027, 11, 30),
        Status = ContractStatus.Draft,
        ServiceGroup = "Dịch vụ cảng",
        Content = "Nội dung hợp đồng",
        Attachments = { new ContractAttachment { FileName = "du-thao.pdf", DocumentType = ContractRules.RequiredDocumentType } }
    };

    private static SubmitCondition Condition(IReadOnlyList<SubmitCondition> conditions, string code) =>
        Assert.Single(conditions, c => c.Code == code);

    // Chỉ đúng một điều kiện (code) chưa đạt, các điều kiện khác vẫn đạt
    private static void AssertOnlyFailed(IReadOnlyList<SubmitCondition> conditions, string code)
    {
        var failed = conditions.Where(c => !c.Passed).ToList();
        var single = Assert.Single(failed);
        Assert.Equal(code, single.Code);
        Assert.False(string.IsNullOrWhiteSpace(single.Detail));
    }

    [Fact]
    public void CTR02_DatDuDieuKien_KhiDayDuThongTin()
    {
        var customer = ActiveCustomer();
        var conditions = ContractRules.EvaluateSubmitConditions(ValidContract(customer), customer, Today);

        Assert.Equal(4, conditions.Count);
        Assert.All(conditions, c => Assert.True(c.Passed, c.Code));
        Assert.All(conditions, c => Assert.Null(c.Detail));
    }

    [Fact]
    public void CTR02_KhongDat_KhiKhongCoKhachHang()
    {
        var contract = ValidContract(ActiveCustomer());
        AssertOnlyFailed(ContractRules.EvaluateSubmitConditions(contract, null, Today), "CUSTOMER_VALID");
    }

    [Fact]
    public void CTR02_KhongDat_KhiKhachHangInactive()
    {
        var customer = ActiveCustomer();
        customer.Status = CustomerStatus.Inactive;
        AssertOnlyFailed(ContractRules.EvaluateSubmitConditions(ValidContract(customer), customer, Today), "CUSTOMER_VALID");
    }

    [Fact]
    public void CTR02_KhongDat_KhiNgayKetThucKhongSauNgayBatDau()
    {
        var customer = ActiveCustomer();
        var contract = ValidContract(customer);
        contract.EndDate = contract.StartDate; // bằng nhau cũng không hợp lệ
        AssertOnlyFailed(ContractRules.EvaluateSubmitConditions(contract, customer, Today), "EFFECTIVE_PERIOD_VALID");
    }

    [Fact]
    public void CTR02_KhongDat_KhiHopDongDaHetHieuLuc()
    {
        var customer = ActiveCustomer();
        var contract = ValidContract(customer);
        contract.StartDate = new DateOnly(2025, 1, 1);
        contract.EndDate = Today.AddDays(-1);
        AssertOnlyFailed(ContractRules.EvaluateSubmitConditions(contract, customer, Today), "EFFECTIVE_PERIOD_VALID");
    }

    [Fact]
    public void CTR02_Dat_KhiNgayKetThucDungBangHomNay()
    {
        var customer = ActiveCustomer();
        var contract = ValidContract(customer);
        contract.StartDate = new DateOnly(2026, 1, 1);
        contract.EndDate = Today; // ranh giới: còn hiệu lực đến hết hôm nay
        Assert.True(Condition(ContractRules.EvaluateSubmitConditions(contract, customer, Today), "EFFECTIVE_PERIOD_VALID").Passed);
    }

    [Theory]
    [InlineData(null, "Nội dung")]
    [InlineData("   ", "Nội dung")]
    [InlineData("Dịch vụ cảng", null)]
    [InlineData("Dịch vụ cảng", "")]
    public void CTR02_KhongDat_KhiThieuNoiDungBatBuoc(string? serviceGroup, string? content)
    {
        var customer = ActiveCustomer();
        var contract = ValidContract(customer);
        contract.ServiceGroup = serviceGroup;
        contract.Content = content;
        AssertOnlyFailed(ContractRules.EvaluateSubmitConditions(contract, customer, Today), "REQUIRED_CONTENT");
    }

    [Fact]
    public void CTR02_KhongDat_KhiKhongCoTepNao()
    {
        var customer = ActiveCustomer();
        var contract = ValidContract(customer);
        contract.Attachments.Clear();
        AssertOnlyFailed(ContractRules.EvaluateSubmitConditions(contract, customer, Today), "REQUIRED_FILES");
    }

    [Fact]
    public void CTR02_KhongDat_KhiChiCoTepKhongPhaiDuThao()
    {
        var customer = ActiveCustomer();
        var contract = ValidContract(customer);
        contract.Attachments = new() { new ContractAttachment { FileName = "phu-luc.pdf", DocumentType = "PhuLuc" } };
        AssertOnlyFailed(ContractRules.EvaluateSubmitConditions(contract, customer, Today), "REQUIRED_FILES");
    }

    [Fact]
    public void CTR02_BaoTatCaDieuKienChuaDat_CungLuc()
    {
        // Seed HD-2026-018 kiểu "thiếu nhiều thứ": người dùng thấy toàn bộ điều kiện chưa đạt trong một lần gửi
        var contract = new Contract { StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2025, 1, 1) };
        var conditions = ContractRules.EvaluateSubmitConditions(contract, null, Today);
        Assert.Equal(4, conditions.Count(c => !c.Passed));
    }

    // ---------- CTR-03: không được chuyển Draft thẳng sang Approved ----------

    [Fact]
    public void CTR03_Draft_ChiDuocChuyenSangPendingApproval()
    {
        Assert.True(ContractRules.CanTransition(ContractStatus.Draft, ContractStatus.PendingApproval));
    }

    [Theory]
    [InlineData(ContractStatus.Approved)]
    [InlineData(ContractStatus.Active)]
    [InlineData(ContractStatus.Rejected)]
    [InlineData(ContractStatus.RevisionRequested)]
    public void CTR03_Draft_KhongDuocChuyenThang(ContractStatus target)
    {
        Assert.False(ContractRules.CanTransition(ContractStatus.Draft, target));
    }

    [Fact]
    public void CTR03_PendingApproval_DuocChuyenSangApproved()
    {
        // Approved chỉ đến được sau khi đã qua PendingApproval
        Assert.True(ContractRules.CanTransition(ContractStatus.PendingApproval, ContractStatus.Approved));
    }

    [Theory]
    [InlineData(ContractStatus.PendingApproval, ContractStatus.PendingApproval)] // gửi lại khi đang chờ duyệt
    [InlineData(ContractStatus.Approved, ContractStatus.PendingApproval)]
    [InlineData(ContractStatus.Rejected, ContractStatus.Approved)]
    [InlineData(ContractStatus.Active, ContractStatus.Draft)]
    public void CTR03_ChuyenTrangThaiNgoaiSoDo_BiTuChoi(ContractStatus from, ContractStatus to)
    {
        Assert.False(ContractRules.CanTransition(from, to));
    }

    // ---------- Ngày hiệu lực (dùng khi tạo/sửa hợp đồng) ----------

    [Theory]
    [InlineData("2026-12-01", "2027-11-30", true)]
    [InlineData("2026-12-01", "2026-12-02", true)]
    [InlineData("2026-12-01", "2026-12-01", false)]
    [InlineData("2026-12-01", "2026-11-30", false)]
    public void IsValidPeriod_NgayKetThucPhaiSauNgayBatDau(string start, string end, bool expected)
    {
        Assert.Equal(expected, ContractRules.IsValidPeriod(DateOnly.Parse(start), DateOnly.Parse(end)));
    }
}
