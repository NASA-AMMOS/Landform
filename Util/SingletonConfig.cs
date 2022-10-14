namespace JPLOPS.Util
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
