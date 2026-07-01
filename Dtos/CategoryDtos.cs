using System.ComponentModel.DataAnnotations;

using Api.Models;

namespace Api.Dtos;

public record CreateCategoryRequest(
    [param: Required]
    string Name
);

public record UpdateCategoryRequest(
    [param: Required]
    string Name
);

public record CategoryResponse(
  Guid Id,
  string Name,
  Guid UserId,
  bool IsArchived,
  DateTime CreatedAtUtc,
  DateTime UpdatedAtUtc);

public static class CategoryMappingExtensions
{
  public static CategoryResponse ToResponse(this Category category) =>
    new(
      category.Id,
      category.Name!,
      category.UserId,
      category.IsArchived,
      category.CreatedAtUtc,
      category.UpdatedAtUtc);
}