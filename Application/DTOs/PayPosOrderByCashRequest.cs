namespace Application.DTOs
{
    public class PayPosOrderByCashRequest
    {
        public decimal? AmountReceived { get; set; }
        public decimal? ChangeAmount { get; set; }
    }
}
