using Microsoft.EntityFrameworkCore;
using QTKD.Api.Data;
using QTKD.Api.Domain;
using QTKD.Api.Dtos;

namespace QTKD.Api.Services;

public class ContractService
{
    private readonly QtkdDbContext _db;
    private readonly TimeProvider _clock;

    public ContractService(QtkdDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    public Task<List<ContractSummary>> ListAsync() =>
        _db.Contracts.AsNoTracking()
            .OrderBy(c => c.ContractNo)
            .Select(c => new ContractSummary(c.Id, c.ContractNo, c.CustomerId, c.Customer!.Name,
                c.StartDate, c.EndDate, c.Status.ToString()))
            .ToListAsync();

    public async Task<Result<ContractDetail>> GetAsync(Guid id)
    {
        var contract = await LoadAsync(id, tracking: false);
        return contract is null ? NotFound(id) : ToDetail(contract);
    }

    public async Task<Result<List<HistoryResponse>>> GetHistoryAsync(Guid id)
    {
        if (!await _db.Contracts.AnyAsync(c => c.Id == id)) return NotFound(id);

        var history = await _db.ContractHistory.AsNoTracking()
            .Where(h => h.ContractId == id)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync();
        return history.Select(h => new HistoryResponse(h.Id, h.Action, h.FromStatus?.ToString(), h.ToStatus?.ToString(),
            h.PerformedBy, h.Note, h.CreatedAt)).ToList();
    }

    public async Task<Result<ContractDetail>> CreateAsync(ContractRequest request, string user)
    {
        var (errors, customer) = await ValidateAsync(request);
        if (errors.Count > 0) return ServiceError.Validation(errors);

        var now = _clock.GetUtcNow();
        var contract = new Contract { Id = Guid.NewGuid(), Status = ContractStatus.Draft, Customer = customer };
        Apply(contract, request, now);
        if (await ContractNoTakenAsync(contract)) return ContractNoExists(contract.ContractNo);

        _db.Contracts.Add(contract);
        AddHistory(contract, "Created", null, ContractStatus.Draft, user, null, now);
        return await SaveAsync(contract, () => ToDetail(contract));
    }

    public async Task<Result<ContractDetail>> UpdateAsync(Guid id, ContractRequest request, string user)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound(id);
        if (!ContractRules.IsEditable(contract.Status)) return NotEditable(contract);

        var (errors, customer) = await ValidateAsync(request);
        if (errors.Count > 0) return ServiceError.Validation(errors);

        var now = _clock.GetUtcNow();
        contract.Customer = customer;
        Apply(contract, request, now);
        if (await ContractNoTakenAsync(contract)) return ContractNoExists(contract.ContractNo);

        AddHistory(contract, "Updated", null, null, user, null, now);
        return await SaveAsync(contract, () => ToDetail(contract));
    }

    public async Task<Result<AttachmentResponse>> AddAttachmentAsync(Guid id, AttachmentRequest request, string user)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound(id);
        if (!ContractRules.IsEditable(contract.Status)) return NotEditable(contract);

        var errors = new List<FieldError>();
        var fileName = request.FileName?.Trim();
        if (string.IsNullOrEmpty(fileName))
            errors.Add(new("fileName", "REQUIRED", "Tên tệp là bắt buộc."));
        else if (fileName.Length > 255)
            errors.Add(new("fileName", "TOO_LONG", "Tên tệp tối đa 255 ký tự."));
        if (string.IsNullOrWhiteSpace(request.DocumentType))
            errors.Add(new("documentType", "REQUIRED", "Loại tài liệu là bắt buộc."));
        else if (!ContractRules.DocumentTypes.Contains(request.DocumentType))
            errors.Add(new("documentType", "INVALID_VALUE", $"Loại tài liệu chỉ nhận: {string.Join(", ", ContractRules.DocumentTypes)}."));
        if (errors.Count > 0) return ServiceError.Validation(errors);

        var now = _clock.GetUtcNow();
        var attachment = new ContractAttachment
        {
            Id = Guid.NewGuid(),
            ContractId = contract.Id,
            FileName = fileName!,
            DocumentType = request.DocumentType!,
            UploadedAt = now
        };
        _db.ContractAttachments.Add(attachment);
        // Cập nhật UpdatedAt để lệnh UPDATE kiểm tra xmin, tránh thêm tệp khi hợp đồng vừa được gửi
        contract.UpdatedAt = now;
        AddHistory(contract, "AttachmentAdded", null, null, user, $"{attachment.DocumentType}: {attachment.FileName}", now);
        return await SaveAsync(contract, () => new AttachmentResponse(attachment.Id, attachment.FileName, attachment.DocumentType, attachment.UploadedAt));
    }

    // US03 — Gửi hợp đồng trình duyệt (UC-CON-03)
    public async Task<Result<SubmitResponse>> SubmitAsync(Guid id, SubmitRequest? request, string? idempotencyKey, string user)
    {
        var errors = new List<FieldError>();
        if (idempotencyKey?.Length > 100)
            errors.Add(new("Idempotency-Key", "TOO_LONG", "Idempotency-Key tối đa 100 ký tự."));
        if (request?.Comment?.Length > 1000)
            errors.Add(new("comment", "TOO_LONG", "Ghi chú tối đa 1000 ký tự."));
        if (errors.Count > 0) return ServiceError.Validation(errors);

        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound(id);

        // A4: gửi lặp cùng Idempotency-Key -> trả lại kết quả lần gửi trước, không tạo phiên mới
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var previous = await _db.ApprovalRequests.AsNoTracking()
                .FirstOrDefaultAsync(a => a.ContractId == id && a.IdempotencyKey == idempotencyKey);
            if (previous is not null) return ToSubmitResponse(contract, previous, replayed: true);
        }

        // A2: sai trạng thái (bao gồm đã gửi rồi)
        if (contract.Status == ContractStatus.PendingApproval)
            return ServiceError.Conflict("CONTRACT_ALREADY_SUBMITTED",
                $"Hợp đồng {contract.ContractNo} đã được gửi trình duyệt và đang chờ phê duyệt.");
        if (contract.Status != ContractStatus.Draft || !ContractRules.CanTransition(contract.Status, ContractStatus.PendingApproval))
            return ServiceError.Conflict("INVALID_STATUS",
                $"Chỉ gửi trình duyệt hợp đồng ở trạng thái Draft (hiện tại: {contract.Status}).");

        // A3: không đạt CTR-02 -> trả danh sách điều kiện chưa đạt, không đổi dữ liệu
        var failed = ContractRules.EvaluateSubmitConditions(contract, contract.Customer, Today)
            .Where(c => !c.Passed)
            .Select(c => new FieldError(c.Code, "NOT_SATISFIED", c.Detail ?? c.Description))
            .ToList();
        if (failed.Count > 0)
            return new ServiceError(ErrorKind.BadRequest, "CTR02_NOT_SATISFIED",
                "Hợp đồng chưa đủ điều kiện gửi trình duyệt.", failed);

        // Bước 4–6: khởi tạo phiên phê duyệt (mô phỏng), chuyển trạng thái, ghi lịch sử — lưu trong một transaction
        var now = _clock.GetUtcNow();
        var approval = new ApprovalRequest
        {
            Id = Guid.NewGuid(),
            ContractId = contract.Id,
            Status = ApprovalStatus.Pending,
            Comment = string.IsNullOrWhiteSpace(request?.Comment) ? null : request.Comment.Trim(),
            IdempotencyKey = string.IsNullOrEmpty(idempotencyKey) ? null : idempotencyKey,
            RequestedBy = user,
            CreatedAt = now
        };
        _db.ApprovalRequests.Add(approval);
        var from = contract.Status;
        contract.Status = ContractStatus.PendingApproval;
        contract.UpdatedAt = now;
        AddHistory(contract, "Submitted", from, ContractStatus.PendingApproval, user, approval.Comment, now);

        var result = await SaveAsync(contract, () => ToSubmitResponse(contract, approval, replayed: false));

        // Hai yêu cầu cùng Idempotency-Key đến đồng thời: yêu cầu thua nhận lại kết quả của yêu cầu thắng thay vì 409
        if (!result.IsSuccess && approval.IdempotencyKey is not null)
        {
            var winner = await _db.ApprovalRequests.AsNoTracking()
                .FirstOrDefaultAsync(a => a.ContractId == id && a.IdempotencyKey == approval.IdempotencyKey);
            if (winner is not null)
                return new SubmitResponse(contract.Id, contract.ContractNo, nameof(ContractStatus.PendingApproval),
                    winner.Id, winner.CreatedAt, Replayed: true);
        }
        return result;
    }

    // Mô phỏng bước phê duyệt để kiểm tra CTR-03; quy trình Approval đầy đủ ngoài phạm vi Phase 2
    public async Task<Result<ContractDetail>> ApproveAsync(Guid id, string user)
    {
        var contract = await LoadAsync(id, tracking: true);
        if (contract is null) return NotFound(id);

        if (!ContractRules.CanTransition(contract.Status, ContractStatus.Approved))
            return ServiceError.Conflict("CTR03_INVALID_TRANSITION",
                $"Không được chuyển hợp đồng từ {contract.Status} sang Approved; hợp đồng phải được gửi trình duyệt (PendingApproval) trước.");

        var now = _clock.GetUtcNow();
        var approval = await _db.ApprovalRequests
            .FirstOrDefaultAsync(a => a.ContractId == id && a.Status == ApprovalStatus.Pending);
        if (approval is not null)
        {
            approval.Status = ApprovalStatus.Approved;
            approval.DecidedAt = now;
        }
        var from = contract.Status;
        contract.Status = ContractStatus.Approved;
        contract.UpdatedAt = now;
        AddHistory(contract, "Approved", from, ContractStatus.Approved, user, null, now);
        return await SaveAsync(contract, () => ToDetail(contract));
    }

    private async Task<(List<FieldError> Errors, Customer? Customer)> ValidateAsync(ContractRequest request)
    {
        var errors = new List<FieldError>();

        var contractNo = request.ContractNo?.Trim();
        if (string.IsNullOrEmpty(contractNo))
            errors.Add(new("contractNo", "REQUIRED", "Số hợp đồng là bắt buộc."));
        else if (contractNo.Length > 50)
            errors.Add(new("contractNo", "TOO_LONG", "Số hợp đồng tối đa 50 ký tự."));

        if (request.StartDate is null)
            errors.Add(new("startDate", "REQUIRED", "Ngày bắt đầu hiệu lực là bắt buộc."));
        if (request.EndDate is null)
            errors.Add(new("endDate", "REQUIRED", "Ngày kết thúc hiệu lực là bắt buộc."));
        if (request.StartDate is { } start && request.EndDate is { } end && !ContractRules.IsValidPeriod(start, end))
            errors.Add(new("endDate", "INVALID_PERIOD", "Ngày kết thúc phải sau ngày bắt đầu."));

        if (request.ServiceGroup?.Trim().Length > 100)
            errors.Add(new("serviceGroup", "TOO_LONG", "Nhóm dịch vụ tối đa 100 ký tự."));
        if (request.Content?.Trim().Length > 4000)
            errors.Add(new("content", "TOO_LONG", "Nội dung hợp đồng tối đa 4000 ký tự."));

        Customer? customer = null;
        if (request.CustomerId is not { } customerId)
            errors.Add(new("customerId", "REQUIRED", "Khách hàng là bắt buộc."));
        else
        {
            customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId);
            if (customer is null)
                errors.Add(new("customerId", "NOT_FOUND", $"Không tìm thấy khách hàng {customerId}."));
            else if (customer.Status != CustomerStatus.Active)
                errors.Add(new("customerId", "CUSTOMER_INACTIVE", "Khách hàng không ở trạng thái Active."));
        }

        return (errors, customer);
    }

    private static void Apply(Contract contract, ContractRequest request, DateTimeOffset now)
    {
        contract.ContractNo = request.ContractNo!.Trim();
        contract.CustomerId = request.CustomerId!.Value;
        contract.StartDate = request.StartDate!.Value;
        contract.EndDate = request.EndDate!.Value;
        contract.ServiceGroup = string.IsNullOrWhiteSpace(request.ServiceGroup) ? null : request.ServiceGroup.Trim();
        contract.Content = string.IsNullOrWhiteSpace(request.Content) ? null : request.Content.Trim();
        contract.UpdatedAt = now;
    }

    private Task<bool> ContractNoTakenAsync(Contract contract) =>
        _db.Contracts.AnyAsync(c => c.ContractNo == contract.ContractNo && c.Id != contract.Id);

    private Task<Contract?> LoadAsync(Guid id, bool tracking)
    {
        var query = _db.Contracts.Include(c => c.Customer).Include(c => c.Attachments).AsQueryable();
        if (!tracking) query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(c => c.Id == id);
    }

    private void AddHistory(Contract contract, string action, ContractStatus? from, ContractStatus? to,
        string user, string? note, DateTimeOffset now)
    {
        _db.ContractHistory.Add(new ContractHistory
        {
            Id = Guid.NewGuid(),
            ContractId = contract.Id,
            Action = action,
            FromStatus = from,
            ToStatus = to,
            PerformedBy = user,
            Note = note,
            CreatedAt = now
        });
    }

    // Lưu thay đổi; xung đột đồng thời (xmin hoặc unique index) -> 409, dữ liệu không đổi
    private async Task<Result<T>> SaveAsync<T>(Contract contract, Func<T> onSuccess)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceError.Conflict("CONCURRENT_UPDATE",
                $"Hợp đồng {contract.ContractNo} vừa được thay đổi bởi yêu cầu khác. Vui lòng tải lại và thử lại.");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            if ((ex.InnerException as Npgsql.PostgresException)?.ConstraintName?.Contains("ContractNo") == true)
                return ContractNoExists(contract.ContractNo);
            return ServiceError.Conflict("CONCURRENT_UPDATE",
                $"Hợp đồng {contract.ContractNo} vừa được gửi trình duyệt bởi yêu cầu khác.");
        }
        return onSuccess();
    }

    private ContractDetail ToDetail(Contract c)
    {
        var conditions = ContractRules.EvaluateSubmitConditions(c, c.Customer, Today);
        var canSubmit = c.Status == ContractStatus.Draft && conditions.All(x => x.Passed);
        return new ContractDetail(c.Id, c.ContractNo, c.CustomerId, c.Customer?.Name ?? string.Empty,
            c.StartDate, c.EndDate, c.Status.ToString(), c.ServiceGroup, c.Content, c.UpdatedAt,
            c.Attachments.OrderBy(a => a.UploadedAt)
                .Select(a => new AttachmentResponse(a.Id, a.FileName, a.DocumentType, a.UploadedAt)).ToList(),
            new SubmitCheck(canSubmit, conditions));
    }

    private static SubmitResponse ToSubmitResponse(Contract contract, ApprovalRequest approval, bool replayed) =>
        new(contract.Id, contract.ContractNo, contract.Status.ToString(), approval.Id, approval.CreatedAt, replayed);

    private static ServiceError NotFound(Guid id) =>
        ServiceError.NotFound("CONTRACT_NOT_FOUND", $"Không tìm thấy hợp đồng {id}.");

    private static ServiceError NotEditable(Contract contract) =>
        ServiceError.Conflict("CTR01_NOT_EDITABLE",
            $"Chỉ được sửa hợp đồng ở trạng thái Draft hoặc RevisionRequested (hiện tại: {contract.Status}).");

    private static ServiceError ContractNoExists(string contractNo) =>
        ServiceError.Conflict("CONTRACT_NO_EXISTS", $"Số hợp đồng {contractNo} đã tồn tại.");
}
