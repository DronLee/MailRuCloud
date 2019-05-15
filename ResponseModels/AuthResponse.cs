namespace MailRuCloud.ResponseModels
{
    internal struct AuthResponse
    {
        public string email { get; set; }

        public Body body { get; set; }

        internal struct Body
        {
            public string token { get; set; }

            public long time { get; set; }

            public short status { get; set; }
        }
    }
}