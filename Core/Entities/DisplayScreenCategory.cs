namespace Core.Entities;

public class DisplayScreenCategory
{
    public Guid ScreenId { get; set; }
    public DisplayScreen Screen { get; set; } = default!;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = default!;
}
