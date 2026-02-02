namespace Application.DTOs.Users
{
    public class UpdateMeProfileRequest
    {
        public string? AvatarUrl { get; set; }
        public bool? OrderReadyNotificationsEnabled { get; set; }
    }
}
