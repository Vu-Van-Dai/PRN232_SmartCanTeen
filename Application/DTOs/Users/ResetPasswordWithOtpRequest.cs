namespace Application.DTOs.Users
{
    public class ResetPasswordWithOtpRequest
    {
        public string Email { get; set; } = default!;
        public string Otp { get; set; } = default!;
        public string NewPassword { get; set; } = default!;
    }
}
