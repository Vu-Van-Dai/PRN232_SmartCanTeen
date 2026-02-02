using System;
using System.Collections.Generic;

namespace Application.DTOs.Users
{
    public class MeProfileResponse
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = default!;
        public string? FullName { get; set; }
        public string? StudentCode { get; set; }
        public string? AvatarUrl { get; set; }
        public bool OrderReadyNotificationsEnabled { get; set; }
        public List<string> Roles { get; set; } = new();
    }
}
