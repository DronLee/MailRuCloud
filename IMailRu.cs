using System.IO;

namespace MailRuCloud
{
    public interface IMailRu
    {
        IAccount Account { get; }

        bool UploadFile(FileInfo file, string destinationPath);

        byte[] GetFile(string sourceFile);
    }
}