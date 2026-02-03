using Microsoft.Extensions.Options;

namespace API.Services
{
    public class BusinessDayClock
    {
        private readonly TimeZoneInfo _tz;
        private readonly int _dayStartHour;

        public BusinessDayClock(IOptions<BusinessDayOptions> options)
        {
            var opt = options.Value;
            _dayStartHour = opt.DayStartHour;

            try
            {
                _tz = TimeZoneInfo.FindSystemTimeZoneById(opt.TimeZoneId);
            }
            catch
            {
                _tz = TimeZoneInfo.Local;
            }
        }

        public int DayStartHour => _dayStartHour;

        public DateTime UtcNow => DateTime.UtcNow;

        public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);

        public DateTime ConvertUtcToLocal(DateTime utc)
        {
            var utcFixed = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(utcFixed, _tz);
        }

        public DateOnly GetOperationalDateFromUtc(DateTime utc)
            => GetOperationalDate(ConvertUtcToLocal(utc));

        public DateOnly GetOperationalDate(DateTime localNow)
        {
            // Between 00:00 and before DayStartHour => still belongs to previous business date,
            // but POS is locked in that window.
            var date = localNow.Hour < _dayStartHour
                ? localNow.Date.AddDays(-1)
                : localNow.Date;
            return DateOnly.FromDateTime(date);
        }

        public DateTime GetOperationalStartLocal(DateOnly date)
            => date.ToDateTime(new TimeOnly(_dayStartHour, 0));

        public (DateTime fromUtc, DateTime toUtc) GetOperationalWindowUtc(DateOnly date)
        {
            var startLocal = GetOperationalStartLocal(date);
            var endLocal = startLocal.AddDays(1);
            return (
                TimeZoneInfo.ConvertTimeToUtc(startLocal, _tz),
                TimeZoneInfo.ConvertTimeToUtc(endLocal, _tz)
            );
        }

        public bool IsPosWindowOpen(DateTime localNow)
            => localNow.TimeOfDay >= TimeSpan.FromHours(_dayStartHour);
    }
}
