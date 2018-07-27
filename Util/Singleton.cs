using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{

    public abstract class Singleton<T> where T : Singleton<T>, new()
    {
        private static T instance = new T();
        public static T Instance
        {
            get
            {
                if(instance == null)
                {
                    instance = new T();
                }
                return instance;
            }
        }
    }
}
