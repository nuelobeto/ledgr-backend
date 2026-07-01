using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Dtos;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/recurring-transactions")]
[Authorize]
public class RecurringTransactionController(AppDbContext db) : ControllerBase
{
  [HttpPost("create")]
  public async Task<IActionResult> CreateRecurring(CreateRecurringTransactionRequest request)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    if (request.EndDate is not null && request.EndDate < request.NextDueDate)
      return BadRequest(new { message = "'endDate' must be on or after 'nextDueDate'." });

    // Category must exist, belong to this user, and not be archived.
    var category = await db.Categories
        .FirstOrDefaultAsync(c => c.Id == request.CategoryId
                               && c.UserId == userGuid
                               && !c.IsArchived);
    if (category is null)
      return BadRequest(new { message = "Category not found, not yours, or archived." });

    var recurring = new RecurringTransaction
    {
      Type = request.Type,
      Amount = request.Amount,
      Notes = request.Notes?.Trim(),
      CategoryId = category.Id,
      Category = category,          // reuse loaded entity so ToResponse has the name
      Frequency = request.Frequency,
      Interval = request.Interval,
      NextDueDate = request.NextDueDate,
      EndDate = request.EndDate,
      UserId = userGuid,
      // IsActive defaults to true on the entity — a rule is born active.
    };

    db.RecurringTransactions.Add(recurring);
    await db.SaveChangesAsync();

    return CreatedAtAction(nameof(GetRecurringById), new { id = recurring.Id }, recurring.ToResponse());
  }

  [HttpGet("{id:guid}")]
  public async Task<IActionResult> GetRecurringById(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var recurring = await db.RecurringTransactions
        .Where(r => r.Id == id && r.UserId == userGuid)
        .Select(r => new RecurringTransactionResponse(
            r.Id, r.Type, r.Amount, r.Notes,
            new CategoryRef(r.Category.Id, r.Category.Name),
            r.Frequency, r.Interval, r.NextDueDate, r.EndDate, r.IsActive,
            r.UserId, r.CreatedAtUtc, r.UpdatedAtUtc))
        .FirstOrDefaultAsync();

    if (recurring is null)
      return NotFound();

    return Ok(recurring);
  }

  [HttpGet]
  public async Task<IActionResult> GetMyRecurring(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string? search = null,
    [FromQuery] bool? isActive = null)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 100);

    var query = db.RecurringTransactions.Where(r => r.UserId == userGuid);

    if (isActive is not null)
      query = query.Where(r => r.IsActive == isActive.Value);

    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim();
      query = query.Where(r => r.Notes != null && EF.Functions.ILike(r.Notes, $"%{term}%"));
    }

    // Soonest-due first — the natural "what's coming up" ordering.
    var ordered = query
        .OrderBy(r => r.NextDueDate)
        .ThenBy(r => r.CreatedAtUtc);

    var totalCount = await ordered.CountAsync();

    var items = await ordered
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(r => new RecurringTransactionResponse(
            r.Id, r.Type, r.Amount, r.Notes,
            new CategoryRef(r.Category.Id, r.Category.Name),
            r.Frequency, r.Interval, r.NextDueDate, r.EndDate, r.IsActive,
            r.UserId, r.CreatedAtUtc, r.UpdatedAtUtc))
        .ToListAsync();

    return Ok(new PagedResponse<RecurringTransactionResponse>(items, page, pageSize, totalCount));
  }

  [HttpPut("{id:guid}")]
  public async Task<IActionResult> UpdateRecurring(Guid id, UpdateRecurringTransactionRequest request)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var recurring = await db.RecurringTransactions
        .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userGuid);
    if (recurring is null)
      return NotFound();

    if (request.EndDate is not null && request.EndDate < request.NextDueDate)
      return BadRequest(new { message = "'endDate' must be on or after 'nextDueDate'." });

    var category = await db.Categories
        .FirstOrDefaultAsync(c => c.Id == request.CategoryId
                               && c.UserId == userGuid
                               && !c.IsArchived);
    if (category is null)
      return BadRequest(new { message = "Category not found, not yours, or archived." });

    recurring.Type = request.Type;
    recurring.Amount = request.Amount;
    recurring.Notes = request.Notes?.Trim();
    recurring.CategoryId = category.Id;
    recurring.Category = category;
    recurring.Frequency = request.Frequency;
    recurring.Interval = request.Interval;
    recurring.NextDueDate = request.NextDueDate;
    recurring.EndDate = request.EndDate;
    recurring.UpdatedAtUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();

    return Ok(recurring.ToResponse());
  }

  [HttpPost("{id:guid}/pause")]
  public async Task<IActionResult> PauseRecurring(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var recurring = await db.RecurringTransactions
        .Include(r => r.Category)
        .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userGuid);
    if (recurring is null)
      return NotFound();

    if (!recurring.IsActive)
      return Ok(recurring.ToResponse()); // already paused — idempotent

    recurring.IsActive = false;
    recurring.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Ok(recurring.ToResponse());
  }

  [HttpPost("{id:guid}/resume")]
  public async Task<IActionResult> ResumeRecurring(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var recurring = await db.RecurringTransactions
        .Include(r => r.Category)
        .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userGuid);
    if (recurring is null)
      return NotFound();

    if (recurring.IsActive)
      return Ok(recurring.ToResponse()); // already active — idempotent

    recurring.IsActive = true;
    recurring.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Ok(recurring.ToResponse());
  }

  [HttpDelete("{id:guid}")]
  public async Task<IActionResult> DeleteRecurring(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var recurring = await db.RecurringTransactions
        .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userGuid);
    if (recurring is null)
      return NotFound();

    db.RecurringTransactions.Remove(recurring);
    await db.SaveChangesAsync();

    return NoContent();
  }
}