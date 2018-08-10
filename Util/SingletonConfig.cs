using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{
    public class SingletonConfig<T> : Config where T: SingletonConfig<T>, new()
    {
        public virtual void Validate() { }

        static T instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new T();
                    instance.Validate();
                }
                return instance;
            }
        }
    }
}
