namespace Application.DTOs
{
    public class GoogleLoginRequest
    {
        // Firebase Auth ID token (from client after Google sign-in)
        public string FirebaseIdToken { get; set; } = null!;
    }
}
