using System.ComponentModel.DataAnnotations;

namespace WahSpices.Application.DTOs.Category;

public record CategoryDto(
    int Id,
    string Name,
    string Slug,
    string? Icon,
    string? Description,
    int DisplayOrder,
    bool IsActive,
    int? ParentId = null
);

public record CreateCategoryRequest
{
    [Required, StringLength(128)] public string Name { get; init; } = "";
    [Required, StringLength(128)] public string Slug { get; init; } = "";
    public string? Icon        { get; init; }
    public string? Description { get; init; }
    public int     DisplayOrder { get; init; } = 0;
    public int?    ParentId     { get; init; }
}

public record UpdateCategoryRequest
{
    [Required, StringLength(128)] public string Name { get; init; } = "";
    public string? Icon         { get; init; }
    public string? Description  { get; init; }
    public int     DisplayOrder { get; init; }
    public bool    IsActive     { get; init; } = true;
    public int?    ParentId     { get; init; }
}

public record ReorderItem(int Id, int DisplayOrder);
