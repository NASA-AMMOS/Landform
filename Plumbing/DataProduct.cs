using Newtonsoft.Json;
using System;
using System.Linq;
using System.Security.Cryptography;

namespace OPS.Plumbing
{
    public abstract class DataProduct
    {
        public abstract void Deserialize(byte[] data);
        public abstract byte[] Serialize();

        public static T Load<T>(byte[] data) where T : DataProduct, new()
        {
            T res = new T();
            res.Deserialize(data);

            SHA1 sha = SHA1.Create();
            res.guid = new Guid(sha.ComputeHash(data).Take(16).ToArray());
            return res;
        }

        public void UpdateGuid()
        {
            SHA1 sha = SHA1.Create();
            guid = new Guid(sha.ComputeHash(Serialize()).Take(16).ToArray());
        }

        [JsonIgnore]
        public Guid guid { get; private set; } = Guid.Empty;
    }
}
