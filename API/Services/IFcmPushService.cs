namespace API.Services
{
    public interface IFcmPushService
    {
        Task SendOrderReadyAsync(
            IEnumerable<string> registrationTokens,
            Guid orderId,
            DateTime? pickupTime,
            CancellationToken cancellationToken = default);
    }
}
