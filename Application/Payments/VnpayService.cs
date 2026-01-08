using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Application.Payments
{
    public class VnpayService
    {
        private readonly IConfiguration _config;

        public VnpayService(IConfiguration config)
        {
            _config = config;
        }

        public string CreatePaymentUrl(decimal amount, string txnRef, string orderInfo)
        {
            var vnpParams = new SortedDictionary<string, string>
            {
                ["vnp_Version"] = "2.1.0",
                ["vnp_Command"] = "pay",
                ["vnp_TmnCode"] = _config["Vnpay:TmnCode"]!,
                ["vnp_Amount"] = ((long)(amount * 100)).ToString(),
                ["vnp_CurrCode"] = "VND",
                ["vnp_TxnRef"] = txnRef,
                ["vnp_OrderInfo"] = orderInfo,
                ["vnp_OrderType"] = "other",
                ["vnp_Locale"] = "vn",
                ["vnp_ReturnUrl"] = _config["Vnpay:ReturnUrl"]!,
                ["vnp_IpAddr"] = "127.0.0.1",
                ["vnp_CreateDate"] = DateTime.UtcNow.ToString("yyyyMMddHHmmss")
            };

            var query = string.Join("&", vnpParams.Select(x => $"{x.Key}={x.Value}"));
            var secureHash = HmacSHA512(query, _config["Vnpay:HashSecret"]!);

            return $"{_config["Vnpay:BaseUrl"]}?{query}&vnp_SecureHash={secureHash}";
        }

        private static string HmacSHA512(string input, string key)
        {
            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
            return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)))
                .Replace("-", "")
                .ToLower();
        }
    }
}