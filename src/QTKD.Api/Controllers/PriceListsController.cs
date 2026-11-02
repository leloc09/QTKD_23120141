using Microsoft.AspNetCore.Mvc;
using QTKD.Api.Dtos;
using QTKD.Api.Services;

namespace QTKD.Api.Controllers;

[Route("api/pricelists")]
public class PriceListsController : ApiControllerBase
{
    private readonly PricingService _service;

    public PriceListsController(PricingService service) => _service = service;

    [HttpGet]
    [ProducesResponseType<List<PriceListSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll() => Ok(await _service.ListAsync());

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PriceListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id) =>
        FromResult(await _service.GetAsync(id), Ok);

    [HttpPost]
    [ProducesResponseType<PriceListResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(PriceListRequest request) =>
        FromResult(await _service.CreateAsync(request), p => CreatedAtAction(nameof(GetById), new { id = p.Id }, p));

    [HttpGet("{id:guid}/items")]
    [ProducesResponseType<List<PriceItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItems(Guid id) =>
        FromResult(await _service.ListItemsAsync(id), Ok);

    [HttpPost("{id:guid}/items")]
    [ProducesResponseType<PriceItemResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem(Guid id, PriceItemRequest request) =>
        FromResult(await _service.AddItemAsync(id, request), i => CreatedAtAction(nameof(GetItems), new { id }, i));
}
