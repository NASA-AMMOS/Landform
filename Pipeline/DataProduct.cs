using OPS.Cloud;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
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
            res.guid = new Guid(sha.ComputeHash(data));
            return res;
        }

        public void UpdateGuid()
        {
            SHA1 sha = SHA1.Create();
            guid = new Guid(sha.ComputeHash(Serialize()));
        }

        public Guid guid { get; private set; } = Guid.Empty;
    }
}
