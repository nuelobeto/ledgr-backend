using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Dtos;
using Api.Models;

namespace Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoryController(AppDbContext db) : ControllerBase
{
  [HttpGet]
  public async Task<IActionResult> GetMyCategories(
[FromQuery] int page = 1,
[FromQuery] int pageSize = 20,
[FromQuery] string? search = null,
[FromQuery] bool? archived = false)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    page = Math.Max(page, 1);
    pageSize = Math.Clamp(pageSize, 1, 100);

    var query = db.Categories.Where(c => c.UserId == userGuid);

    if (archived is not null)
      query = query.Where(c => c.IsArchived == archived.Value);

    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim();
      query = query.Where(c => EF.Functions.ILike(c.Name, $"%{term}%"));
    }

    var ordered = query.OrderBy(c => c.CreatedAtUtc);
    var totalCount = await ordered.CountAsync();

    var categories = await ordered
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

    var items = categories.Select(c => c.ToResponse()).ToList();

    return Ok(new PagedResponse<CategoryResponse>(items, page, pageSize, totalCount));
  }

  [HttpGet("{id:guid}")]
  public async Task<IActionResult> GetCategoryById(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var category = await db.Categories
        .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userGuid);

    if (category is null)
      return NotFound();

    return Ok(category.ToResponse());
  }

  [HttpPost("create")]
  public async Task<IActionResult> CreateCategory(CreateCategoryRequest request)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var name = request.Name.Trim();

    var exists = await db.Categories
        .AnyAsync(c => c.UserId == userGuid && c.Name == name);
    if (exists)
      return Conflict($"A category named '{name}' already exists.");

    var category = new Category { Name = name, UserId = userGuid };
    db.Categories.Add(category);
    await db.SaveChangesAsync();

    return CreatedAtAction(nameof(GetCategoryById), new { id = category.Id }, category.ToResponse());
  }

  [HttpPatch("{id:guid}")]
  public async Task<IActionResult> UpdateCategory(Guid id, UpdateCategoryRequest request)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var category = await db.Categories
        .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userGuid);
    if (category is null)
      return NotFound();

    var name = request.Name.Trim();

    // Only run the duplicate check if the name is actually changing.
    if (!string.Equals(name, category.Name, StringComparison.Ordinal))
    {
      var clash = await db.Categories
          .AnyAsync(c => c.UserId == userGuid && c.Name == name && c.Id != id);
      if (clash)
        return Conflict($"A category named '{name}' already exists.");
    }

    category.Name = name;
    category.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Ok(category.ToResponse());
  }

  [HttpPost("{id:guid}/archive")]
  public async Task<IActionResult> ArchiveCategory(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var category = await db.Categories
        .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userGuid);
    if (category is null)
      return NotFound();

    if (category.IsArchived)
      return NoContent();               // already archived — idempotent no-op

    category.IsArchived = true;
    category.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Ok(category.ToResponse());
  }

  [HttpPost("{id:guid}/unarchive")]
  public async Task<IActionResult> UnarchiveCategory(Guid id)
  {
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
    if (userId is null || !Guid.TryParse(userId, out var userGuid))
      return Unauthorized();

    var category = await db.Categories
        .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userGuid);
    if (category is null)
      return NotFound();

    if (!category.IsArchived)
      return NoContent();

    category.IsArchived = false;
    category.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Ok(category.ToResponse());
  }
}