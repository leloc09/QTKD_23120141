using Microsoft.AspNetCore.Mvc;
using QTKD.Api.Dtos;
using QTKD.Api.Services;

namespace QTKD.Api.Controllers;

[Route("api/customers")]
public class CustomersController : ApiControllerBase
{
    private readonly CustomerService _service;

    public CustomersController(CustomerService service) => _service = service;

    [HttpGet]
    [ProducesResponseType<List<CustomerResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll() => Ok(await _service.ListAsync());

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id) =>
        FromResult(await _service.GetAsync(id), Ok);

    [HttpPost]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CustomerRequest request) =>
        FromResult(await _service.CreateAsync(request), c => CreatedAtAction(nameof(GetById), new { id = c.Id }, c));

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, CustomerRequest request) =>
        FromResult(await _service.UpdateAsync(id, request), Ok);
}
