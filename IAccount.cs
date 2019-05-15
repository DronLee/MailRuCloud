using System.Net;

namespace MailRuCloud
{
    public interface IAccount
    {
        string LoginName { get; set; }

        string Password { get; set; }

        string AuthToken { get; }

        CookieContainer Cookies { get; }

        bool Login();

        void CheckAuth();
    }
}