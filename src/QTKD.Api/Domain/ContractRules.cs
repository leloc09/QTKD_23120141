namespace QTKD.Api.Domain;

// Quy tắc nghiệp vụ hợp đồng. Không phụ thuộc database để unit test trực tiếp (tuần 07).
public static class ContractRules
{
    // Giả định cho bản demo: tệp bắt buộc là bản dự thảo hợp đồng
    public const string RequiredDocumentType = "DuThaoHopDong";

    public static readonly IReadOnlyList<string> DocumentTypes = new[] { "DuThaoHopDong", "PhuLuc", "Khac" };

    private static readonly Dictionary<ContractStatus, ContractStatus[]> Transitions = new()
    {
        [ContractStatus.Draft] = new[] { ContractStatus.PendingApproval },
        [ContractStatus.PendingApproval] = new[] { ContractStatus.RevisionRequested, ContractStatus.Rejected, ContractStatus.Approved },
        [ContractStatus.RevisionRequested] = new[] { ContractStatus.Draft },
        [ContractStatus.Approved] = new[] { ContractStatus.Active },
    };

    // CTR-01: chỉ sửa khi Draft hoặc RevisionRequested
    public static bool IsEditable(ContractStatus status) =>
        status is ContractStatus.Draft or ContractStatus.RevisionRequested;

    // CTR-03: chỉ cho chuyển theo sơ đồ trạng thái, không có Draft -> Approved
    public static bool CanTransition(ContractStatus from, ContractStatus to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool IsValidPeriod(DateOnly start, DateOnly end) => end > start;

    // CTR-02: trả về toàn bộ điều kiện (đạt/chưa đạt) để hiển thị như Prototype A/C
    public static IReadOnlyList<SubmitCondition> EvaluateSubmitConditions(Contract contract, Customer? customer, DateOnly today)
    {
        var customerValid = customer is { Status: CustomerStatus.Active };
        var periodValid = IsValidPeriod(contract.StartDate, contract.EndDate) && contract.EndDate >= today;
        var contentValid = !string.IsNullOrWhiteSpace(contract.ServiceGroup) && !string.IsNullOrWhiteSpace(contract.Content);
        var filesValid = contract.Attachments.Any(a => a.DocumentType == RequiredDocumentType);

        return new[]
        {
            new SubmitCondition("CUSTOMER_VALID", "Khách hàng hợp lệ", customerValid,
                customerValid ? null : "Khách hàng không tồn tại hoặc không ở trạng thái Active."),
            new SubmitCondition("EFFECTIVE_PERIOD_VALID", "Thời gian hiệu lực hợp lệ", periodValid,
                periodValid ? null : "Ngày kết thúc phải sau ngày bắt đầu và chưa qua ngày gửi."),
            new SubmitCondition("REQUIRED_CONTENT", "Đủ nội dung bắt buộc", contentValid,
                contentValid ? null : "Thiếu nhóm dịch vụ (serviceGroup) hoặc nội dung hợp đồng (content)."),
            new SubmitCondition("REQUIRED_FILES", "Đủ tệp bắt buộc", filesValid,
                filesValid ? null : $"Thiếu tệp loại {RequiredDocumentType} (bản dự thảo hợp đồng)."),
        };
    }
}

public record SubmitCondition(string Code, string Description, bool Passed, string? Detail);
