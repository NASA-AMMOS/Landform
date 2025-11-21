using System;
using System.Reflection.Emit;
using System.Collections.Concurrent;

namespace JPLOPS.Util
{
    //https://stackoverflow.com/questions/8173239
    public class Sizeof
    {
        private static ConcurrentDictionary<Type, int> cache = new ConcurrentDictionary<Type, int>();

        // GetManagedSize() returns the size of a structure whose type
        // is 'type', as stored in managed memory. For any reference type
        // this will simply return the size of a pointer (4 or 8).
        public static int GetManagedSize(Type type)
        {
            return cache.GetOrAdd(type, _ =>
                    {
                        var dm = new DynamicMethod("func", typeof(int), Type.EmptyTypes, typeof(Sizeof));

                        ILGenerator il = dm.GetILGenerator();
                        il.Emit(OpCodes.Sizeof, type);
                        il.Emit(OpCodes.Ret);

                        var func = (Func<int>)dm.CreateDelegate(typeof(Func<int>));
                        return func();
                    });
        }
    }
}
