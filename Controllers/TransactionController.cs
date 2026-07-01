using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Dtos;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]

public class TransactionController(AppDbContext db) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetMyTransactions(
  [FromQuery] int page = 1,
  [FromQuery] int pageSize = 20,
  [FromQuery] string? search = null,
  [FromQuery] DateOnly? from = null,
  [FromQuery] DateOnly? to = null)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    if (from is not null && to is not null && from > to)
      return BadRequest(new { message = "'from' must be on or before 'to'." });

    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 100);

    var query = db.Transactions.Where(t => t.UserId == userGuid);

    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim();
      query = query.Where(t => t.Notes != null && EF.Functions.ILike(t.Notes, $"%{term}%"));
    }

    if (from is not null)
      query = query.Where(t => t.Date >= from.Value);

    if (to is not null)
      query = query.Where(t => t.Date <= to.Value);

    var ordered = query
        .OrderByDescending(t => t.Date)
        .ThenByDescending(t => t.CreatedAtUtc);

    var totalCount = await ordered.CountAsync();

    var items = await ordered
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(t => new TransactionResponse(
            t.Id, t.Date, t.Type, t.Amount, t.Notes,
            new CategoryRef(t.Category.Id, t.Category.Name),
            t.UserId, t.CreatedAtUtc, t.UpdatedAtUtc))
        .ToListAsync();

    return Ok(new PagedResponse<TransactionResponse>(items, page, pageSize, totalCount));
  }

  [HttpGet("{id:guid}")]
  public async Task<IActionResult> GetTransactionById(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var transaction = await db.Transactions
        .Where(t => t.Id == id && t.UserId == userGuid)
        .Select(t => new TransactionResponse(
            t.Id, t.Date, t.Type, t.Amount, t.Notes,
            new CategoryRef(t.Category.Id, t.Category.Name),
            t.UserId, t.CreatedAtUtc, t.UpdatedAtUtc))
        .FirstOrDefaultAsync();

    if (transaction is null)
      return NotFound();

    return Ok(transaction);
  }

  [HttpPost("create")]
  public async Task<IActionResult> CreateTransaction(CreateTransactionRequest request)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    // Category must exist, belong to this user, and not be archived.
    var category = await db.Categories
        .FirstOrDefaultAsync(c => c.Id == request.CategoryId
                               && c.UserId == userGuid
                               && !c.IsArchived);
    if (category is null)
      return BadRequest(new { message = "Category not found, not yours, or archived." });

    var transaction = new Transaction
    {
      Date = request.Date,
      Type = request.Type,
      Amount = request.Amount,
      Notes = request.Notes?.Trim(),
      CategoryId = category.Id,
      Category = category,          // reuse the loaded entity so ToResponse has it — no re-query
      UserId = userGuid,
    };

    db.Transactions.Add(transaction);
    await db.SaveChangesAsync();

    return CreatedAtAction(nameof(GetTransactionById), new { id = transaction.Id }, transaction.ToResponse());
  }

  [HttpPut("{id:guid}")]
  public async Task<IActionResult> UpdateTransaction(Guid id, UpdateTransactionRequest request)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    // Ownership in the query — a transaction you don't own is a 404, never loaded.
    var transaction = await db.Transactions
        .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userGuid);
    if (transaction is null)
      return NotFound();

    // Re-validate the target category on every write — same check as create.
    var category = await db.Categories
        .FirstOrDefaultAsync(c => c.Id == request.CategoryId
                               && c.UserId == userGuid
                               && !c.IsArchived);
    if (category is null)
      return BadRequest(new { message = "Category not found, not yours, or archived." });

    transaction.Date = request.Date;
    transaction.Type = request.Type;
    transaction.Amount = request.Amount;
    transaction.Notes = request.Notes?.Trim();
    transaction.CategoryId = category.Id;
    transaction.Category = category;          // reuse loaded entity so ToResponse has the name
    transaction.UpdatedAtUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();

    return Ok(transaction.ToResponse());
  }

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> DeleteTransaction(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var transaction = await db.Transactions
        .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userGuid);
    if (transaction is null)
      return NotFound();

    db.Transactions.Remove(transaction);
    await db.SaveChangesAsync();

    return NoContent();
  }

  [HttpGet("balance")]
  public async Task<IActionResult> GetCurrentBalance()
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var balance = await db.Transactions
        .Where(t => t.UserId == userGuid)
        .SumAsync(t => (decimal?)(t.Type == TransactionType.Income ? t.Amount : -t.Amount)) ?? 0m;

    return Ok(new { balance });
  }
}