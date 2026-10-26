using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Data;

namespace QTKD.Api.Controllers;

[ApiController]
[Route("api/contracts")]
public class ContractsController : ControllerBase
{
    private readonly QtkdDbContext _db;

    public ContractsController(QtkdDbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var contract = await _db.Contracts
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Id,
                c.ContractNo,
                c.CustomerId,
                CustomerName = c.Customer!.Name,
                c.StartDate,
                c.EndDate,
                Status = c.Status.ToString()
            })
            .FirstOrDefaultAsync();

        return contract is null ? NotFound(new { error = "CONTRACT_NOT_FOUND" }) : Ok(contract);
    }
}
