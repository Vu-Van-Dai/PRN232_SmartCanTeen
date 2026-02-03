namespace API.Services
{
    public class BusinessDayOptions
    {
        /// <summary>
        /// Windows timezone id. Default is Vietnam time.
        /// </summary>
        public string TimeZoneId { get; set; } = "SE Asia Standard Time";

        /// <summary>
        /// Business day starts at this local hour (e.g. 5 = 05:00).
        /// </summary>
        public int DayStartHour { get; set; } = 5;
    }
}
