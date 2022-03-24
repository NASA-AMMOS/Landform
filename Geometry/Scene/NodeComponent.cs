namespace JPLOPS.Geometry
{
    /// <summary>
    /// Base class for node components.
    /// </summary>
    public abstract class NodeComponent
    {
        /// <summary>
        /// The node this component is attached to.
        /// </summary>
        public SceneNode Node;

        public virtual void Initialize() { }
    }
}
