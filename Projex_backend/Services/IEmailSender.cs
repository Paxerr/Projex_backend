namespace Projex_backend.Services
{
    public interface IEmailSender
    {
        Task SendPasswordResetCodeAsync(string toEmail, string code, DateTime expiresAt);
    }
}
