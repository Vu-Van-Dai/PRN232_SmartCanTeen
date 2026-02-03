using Core.Enums;

namespace Core.Entities;

public class OrderStationTask
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public Guid ScreenId { get; set; }
    public DisplayScreen Screen { get; set; } = default!;

    public StationTaskStatus Status { get; set; } = StationTaskStatus.Pending;

    public DateTime? StartedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
