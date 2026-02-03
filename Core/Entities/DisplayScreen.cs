using Core.Common;

namespace Core.Entities;

public class DisplayScreen : BaseEntity, ISoftDelete
{
    public string Key { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DisplayScreenCategory> ScreenCategories { get; set; } = new List<DisplayScreenCategory>();

    public bool IsDeleted { get; set; } = false;
}
