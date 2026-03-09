namespace API.Services
{
    internal static class FirebaseAdminInitLock
    {
        internal static readonly object Lock = new();
    }
}
