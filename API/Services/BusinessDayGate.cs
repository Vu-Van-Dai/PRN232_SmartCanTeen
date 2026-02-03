using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace API.Services
{
    public class BusinessDayGate
    {
        private readonly AppDbContext _db;
        private readonly BusinessDayClock _clock;

        public BusinessDayGate(AppDbContext db, BusinessDayClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<(bool allowed, string? reason, DateOnly operationalDate)> EnsurePosOperationsAllowedAsync()
        {
            var localNow = _clock.LocalNow;

            // Locked window after 00:00 until DayStartHour.
            if (!_clock.IsPosWindowOpen(localNow))
            {
                var opensAt = new TimeOnly(_clock.DayStartHour, 0);
                return (false, $"System is locked. New day opens at {opensAt:HH\\:mm}.", _clock.GetOperationalDate(localNow));
            }

            var opDate = _clock.GetOperationalDate(localNow);
            var dayKeyUtc = DateTime.SpecifyKind(opDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var closed = await _db.DailyRevenues.AnyAsync(x => x.Date == dayKeyUtc);
            if (closed)
            {
                var opensAt = new TimeOnly(_clock.DayStartHour, 0);
                return (false, $"Day already closed. Next day opens at {opensAt:HH\\:mm}.", opDate);
            }

            // Auto-close lingering shifts from previous operational day at the moment a new day starts.
            var (fromUtc, _) = _clock.GetOperationalWindowUtc(opDate);
            var lingering = await _db.Shifts
                .Where(s => s.Status != Core.Enums.ShiftStatus.Closed && s.OpenedAt < fromUtc)
                .ToListAsync();
            if (lingering.Count > 0)
            {
                foreach (var s in lingering)
                {
                    s.Status = Core.Enums.ShiftStatus.Closed;
                    s.ClosedAt = fromUtc;
                }
                await _db.SaveChangesAsync();
            }

            return (true, null, opDate);
        }
    }
}
