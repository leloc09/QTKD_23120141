namespace QTKD.Api.Domain;

// Chỉ lưu thông tin tệp (metadata); chưa lưu nội dung tệp thật ở phiên bản này
public class ContractAttachment
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }

    public Contract? Contract { get; set; }
}
