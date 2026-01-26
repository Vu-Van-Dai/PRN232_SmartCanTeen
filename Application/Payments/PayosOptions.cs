using System;

namespace Application.Payments
{
    public class PayosOptions
    {
        public string BaseUrl { get; set; } = "https://api-merchant.payos.vn";
        public string ClientId { get; set; } = null!;
        public string ApiKey { get; set; } = null!;
        public string ChecksumKey { get; set; } = null!;

        // Where PayOS redirects the user after payment/cancel
        public string ReturnUrl { get; set; } = null!;
        public string CancelUrl { get; set; } = null!;
    }
}
