using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using QTKD.Api.Dtos;
using QTKD.Api.Services;

namespace QTKD.Api.Controllers;

[Route("api/contracts")]
public class ContractsController : ApiControllerBase
{
    private readonly ContractService _service;

    public ContractsController(ContractService service) => _service = service;

    [HttpGet]
    [ProducesResponseType<List<ContractSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll() => Ok(await _service.ListAsync());

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ContractDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id) =>
        FromResult(await _service.GetAsync(id), Ok);

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType<List<HistoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistory(Guid id) =>
        FromResult(await _service.GetHistoryAsync(id), Ok);

    [HttpPost]
    [ProducesResponseType<ContractDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(ContractRequest request) =>
        FromResult(await _service.CreateAsync(request, CurrentUser),
            c => CreatedAtAction(nameof(GetById), new { id = c.Id }, c));

    // CTR-01: chỉ sửa khi Draft/RevisionRequested
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ContractDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, ContractRequest request) =>
        FromResult(await _service.UpdateAsync(id, request, CurrentUser), Ok);

    [HttpPost("{id:guid}/attachments")]
    [ProducesResponseType<AttachmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddAttachment(Guid id, AttachmentRequest request) =>
        FromResult(await _service.AddAttachmentAsync(id, request, CurrentUser),
            a => CreatedAtAction(nameof(GetById), new { id }, a));

    // US03 — Gửi hợp đồng trình duyệt
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType<SubmitResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(
        Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SubmitRequest? request) =>
        FromResult(await _service.SubmitAsync(id, request, idempotencyKey, CurrentUser), Ok);

    // Mô phỏng phê duyệt, dùng để kiểm tra CTR-03 (Draft không được chuyển thẳng sang Approved)
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<ContractDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id) =>
        FromResult(await _service.ApproveAsync(id, CurrentUser), Ok);
}
