using System.Collections.Generic;

namespace MailRuCloud.ResponseModels
{
    struct ShardResponse
    {
        public string email { get; set; }
        public Dictionary<string, ShardElement[]> body { get; set; }
        public long time { get; set; }
        public short status { get; set; }
    }
}