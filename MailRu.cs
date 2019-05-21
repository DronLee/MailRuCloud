using MailRuCloud.ResponseModels;
using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web;

namespace MailRuCloud
{
    public class MailRu : IMailRu
    {
        public const string Domain = "mail.ru";
        public const string AuthDomen = "https://auth.mail.ru";
        public const string CloudDomain = "https://cloud.mail.ru";
        public const string DefaultRequestType = "application/x-www-form-urlencoded";
        public const string DefaultAcceptType = "text / html,application / xhtml + xml,application / xml; q = 0.9,*/*;q=0.8";
        public const string UserAgent = "Mozilla / 5.0(Windows; U; Windows NT 5.1; en - US; rv: 1.9.0.1) Gecko / 2008070208 Firefox / 3.0.1";

        public MailRu(IAccount account)
        {
            Account = account;
        }

        public IAccount Account { get; private set; }

        public bool UploadFile(FileInfo file, string destinationPath)
        {
            return UploadFile(file.Name, file.FullName, 0, file.Length, destinationPath);
        }

        public byte[] GetFile(string sourceFile)
        {
            Account.CheckAuth();
            var shard = GetShardInfo(ShardType.Get, null);
            using (MemoryStream memoryStream = new MemoryStream())
            {
                var request = (HttpWebRequest)WebRequest.Create(string.Format("{0}{1}", shard.Url, sourceFile.TrimStart('/')));
                request.CookieContainer = Account.Cookies;
                request.Method = "GET";
                request.ContentType = DefaultRequestType;
                request.Accept = DefaultAcceptType;
                request.UserAgent = UserAgent;
                request.AllowReadStreamBuffering = false;
                using (var response = request.GetResponse())
                using (var responseStream = response.GetResponseStream())
                    responseStream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }

        internal static string ReadResponseAsText(WebResponse resp)
        {
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private ShardInfo GetShardInfo(ShardType shardType, CookieContainer cookie)
        {
            Account.CheckAuth();
            var uri = new Uri(string.Format("{0}/api/v2/dispatcher?{2}={1}", CloudDomain, Account.AuthToken, "token"));
            var request = (HttpWebRequest)WebRequest.Create(uri.OriginalString);

            request.CookieContainer = Account.Cookies;
            request.Method = "GET";
            request.ContentType = DefaultRequestType;
            request.Accept = "application/json";
            request.UserAgent = UserAgent;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    cookie = request.CookieContainer;
                    var responseText = ReadResponseAsText(response);

                    var shardTypeDescription = shardType.GetType().GetField(shardType.ToString()).GetCustomAttribute<DescriptionAttribute>(false).Description;

                    var shardResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<ShardResponse>(responseText);
                    var shardElement = shardResponse.body[shardTypeDescription][0];
                    return new ShardInfo() { Type = shardType, Url = shardElement.url, Count = int.Parse(shardElement.count) };
                }
                else
                    throw new Exception($"{response.StatusCode}: {response.StatusDescription}");
            }
        }

        private byte[] GetBoundaryRequest(Guid boundary, string filePath)
        {
            var boundaryBuilder = new StringBuilder();
            boundaryBuilder.AppendFormat("------{0}\r\n", boundary);
            boundaryBuilder.AppendFormat("Content-Disposition: form-data; name=\"file\"; filename=\"{0}\"\r\n", Path.GetFileName(filePath));
            boundaryBuilder.AppendFormat("Content-Type: {0}\r\n\r\n", "application/octet-stream");

            return Encoding.UTF8.GetBytes(boundaryBuilder.ToString());
        }

        private byte[] GetEndBoundaryRequest(Guid boundary)
        {
            var endBoundaryBuilder = new StringBuilder();
            endBoundaryBuilder.AppendFormat("\r\n------{0}--\r\n", boundary);

            return Encoding.UTF8.GetBytes(endBoundaryBuilder.ToString());
        }

        private void WriteBytesInStream(byte[] bytes, Stream outputStream)
        {
            using (var stream = new MemoryStream(bytes))
                stream.CopyTo(outputStream);
        }

        private void WriteBytesInStream(string fullFilePath, Stream outputStream)
        {
            using (var stream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                stream.CopyTo(outputStream);
        }

        private bool UploadFile(string fileName, string fullFilePath, long startPosition, long size, string destinationPath)
        {
            if (!destinationPath.EndsWith("/"))
                destinationPath += "/";

            Account.CheckAuth();
            var shard = GetShardInfo(ShardType.Upload, null);
            var boundary = Guid.NewGuid();

            var boundaryRequest = GetBoundaryRequest(boundary, fullFilePath);
            var endBoundaryRequest = GetEndBoundaryRequest(boundary);

            var request = GetUploadRequest(shard, boundary, size + boundaryRequest.LongLength + endBoundaryRequest.LongLength, destinationPath);

            try
            {
                using (var requestStream = request.GetRequestStream())
                {
                    WriteBytesInStream(boundaryRequest, requestStream);
                    WriteBytesInStream(fullFilePath, requestStream);
                    WriteBytesInStream(endBoundaryRequest, requestStream);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var responseParts = ReadResponseAsText(response).Split(';');
                        var hashResult = responseParts[0];
                        var sizeResult = long.Parse(responseParts[1].Replace("\r\n", string.Empty));

                        var result = new File() { Name = fileName, FulPath = HttpUtility.UrlDecode(destinationPath) + fileName, Hash = hashResult, Size = size };
                        return AddFileInCloud(result);
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool AddFileInCloud(File fileInfo)
        {
            var filePart = string.Format("&hash={0}&size={1}", fileInfo.Hash, fileInfo.Size);
            var addFileRequest = Encoding.UTF8.GetBytes(string.Format("home={0}&conflict=rewrite&api={1}&token={2}", fileInfo.FulPath, 2, Account.AuthToken) + filePart);

            var url = new Uri(string.Format("{0}/api/v2/{1}/add", CloudDomain, "file"));
            var request = (HttpWebRequest)WebRequest.Create(url.OriginalString);
            request.CookieContainer = Account.Cookies;
            request.Method = "POST";
            request.ContentLength = addFileRequest.LongLength;
            request.Referer = string.Format("{0}/home{1}", CloudDomain, HttpUtility.UrlEncode(fileInfo.FulPath.Substring(0, fileInfo.FulPath.LastIndexOf(fileInfo.Name))));
            request.Headers.Add("Origin", CloudDomain);
            request.Host = url.Host;
            request.ContentType = DefaultRequestType;
            request.Accept = "*/*";
            request.UserAgent = UserAgent;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(addFileRequest, 0, addFileRequest.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new Exception();
                    return true;
                }
            };
        }

        private WebRequest GetUploadRequest(ShardInfo shard, Guid boundary, long size, string destinationPath)
        {
            var url = new Uri(string.Format("{0}?cloud_domain=2&{1}", shard.Url, Account.LoginName));
            var request = WebRequest.Create(url.OriginalString) as HttpWebRequest;
            request.CookieContainer = Account.Cookies;
            request.Method = "POST";
            request.ContentLength = size;
            request.Referer = string.Format("{0}/home{1}", CloudDomain, HttpUtility.UrlEncode(destinationPath));
            request.Headers.Add("Origin", CloudDomain);
            request.Host = url.Host;
            request.ContentType = string.Format("multipart/form-data; boundary=----{0}", boundary.ToString());
            request.Accept = "*/*";
            request.UserAgent = UserAgent;
            request.AllowWriteStreamBuffering = false;

            return request;
        }
    }
}