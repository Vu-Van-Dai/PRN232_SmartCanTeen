using Core.Common;

namespace Core.Entities
{
    public class MenuItemImage : BaseEntity
    {
        public Guid MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; } = default!;

        public string Url { get; set; } = default!;
        public int SortOrder { get; set; }
    }
}
