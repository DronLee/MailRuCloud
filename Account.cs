using MailRuCloud.ResponseModels;
using System;
using System.Net;
using System.Text;
using System.Web;

namespace MailRuCloud
{
    public class Account:IAccount
    {
        private CookieContainer cookies = null;

        public Account(string login, string password)
        {
            LoginName = login;
            Password = password;
        }

        public string LoginName { get; set; }

        public string Password { get; set; }

        public string AuthToken { get; private set; }

        public CookieContainer Cookies
        {
            get
            {
                return cookies ?? (cookies = new CookieContainer());
            }

            private set
            {
                cookies = value ?? new CookieContainer();
            }
        }

        public bool Login()
        {
            if (string.IsNullOrEmpty(LoginName))
            {
                throw new ArgumentException("LoginName is null or empty.");
            }

            if (string.IsNullOrEmpty(Password))
            {
                throw new ArgumentException("Password is null or empty.");
            }

            string reqString = string.Format("Login={0}&Domain={1}&Password={2}", LoginName, MailRu.Domain, HttpUtility.UrlEncode(Password));
            byte[] requestData = Encoding.UTF8.GetBytes(reqString);
            var request = (HttpWebRequest)WebRequest.Create(string.Format("{0}/cgi-bin/auth", MailRu.AuthDomen));
            request.CookieContainer = Cookies;
            request.Method = "POST";
            request.ContentType = MailRu.DefaultRequestType;
            request.Accept = MailRu.DefaultAcceptType;
            request.UserAgent = MailRu.UserAgent;
            using (var requestStream = request.GetRequestStream())
            {
                requestStream.Write(requestData, 0, requestData.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new Exception($"{response.StatusCode}: {response.StatusDescription}");

                    if (Cookies != null && Cookies.Count > 0)
                    {
                        EnsureSdcCookie();
                        return GetAuthToken();
                    }
                    else
                        return false;
                }
            }
        }

        public void CheckAuth()
        {
            if (LoginName == null || Password == null)
                throw new Exception("Login or password is empty.");

            if (string.IsNullOrEmpty(AuthToken) && !Login())
                throw new Exception("Auth token has't been retrieved.");
        }

        private bool GetAuthToken()
        {
            var uri = new Uri(string.Format("{0}/api/v2/tokens/csrf", MailRu.CloudDomain));
            var request = (HttpWebRequest)WebRequest.Create(uri.OriginalString);
            request.CookieContainer = Cookies;
            request.Method = "GET";
            request.ContentType = MailRu.DefaultRequestType;
            request.Accept = "application/json";
            request.UserAgent = MailRu.UserAgent;
            using (var response = (HttpWebResponse)request.GetResponse())
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var responseText = MailRu.ReadResponseAsText(response);
                    return !string.IsNullOrEmpty((AuthToken = Newtonsoft.Json.JsonConvert.DeserializeObject<AuthResponse>(responseText).body.token));
                }
                else
                    throw new Exception($"{response.StatusCode}: {response.StatusDescription}");
        }

        private void EnsureSdcCookie()
        {
            var request = (HttpWebRequest)WebRequest.Create(string.Format("{0}/sdc?from={1}/home", MailRu.AuthDomen, MailRu.CloudDomain));
            request.CookieContainer = Cookies;
            request.Method = "GET";
            request.ContentType = MailRu.DefaultRequestType;
            request.Accept = MailRu.DefaultAcceptType;
            request.UserAgent = MailRu.UserAgent;

            using (var response = (HttpWebResponse)request.GetResponse())
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new Exception($"{response.StatusCode}: {response.StatusDescription}");
        }
    }
}