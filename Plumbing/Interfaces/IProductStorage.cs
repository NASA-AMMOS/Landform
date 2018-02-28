using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public interface IProductStorage
    {
        T Get<T>(string project, Guid guid) where T : DataProduct, new();
        DataProduct Get(Type productType, string project, Guid guid);
        void Save(string project, DataProduct product);
    }
}
