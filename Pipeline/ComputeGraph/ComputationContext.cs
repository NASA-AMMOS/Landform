using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static OPS.Pipeline.Computation;

namespace OPS.Pipeline
{
    /// <summary>
    /// A context for evaluating Computation objects.
    /// </summary>
    public class ComputationContext
    {
        public ComputationContext(bool useParallel, string persistenceRoot = null)
        {
            this.UseParallel = useParallel;
            this.persistenceRoot = persistenceRoot;
            computations = new Dictionary<Reference, ComputationState>();
        }

        public bool UseParallel;

        [Flags]
        enum NodeState
        {
            UNMARKED = 0,
            TEMP_MARK = 1,
            PERM_MARK = 2
        };

        /// <summary>
        /// Perform the referenced calculation and return the result.
        /// </summary>
        /// <param name="reference">Reference to evaluate</param>
        /// <returns>Finished Computation object</returns>
        public Computation Evaluate(Reference reference)
        {
            // Get a topologically-sorted list of all computations necessary
            Stack<Reference> toDo = new Stack<Reference>();
            Dictionary<Reference, NodeState> nodeStates = new Dictionary<Reference, NodeState>();
            Action<Reference> visit = null;
            visit = (r) =>
            {
                if (!nodeStates.ContainsKey(r)) nodeStates[r] = NodeState.UNMARKED;
                else if (nodeStates[r].HasFlag(NodeState.TEMP_MARK))
                {
                    throw new Exception("Cycle in computation graph. Chaos reigns.");
                }

                nodeStates[r] |= NodeState.TEMP_MARK;

                var s = GetState(r);
                if (!s.Done)
                {
                    foreach (var dep in s.Computation.Dependencies())
                    {
                        visit(dep);
                    }
                }
                nodeStates[r] |= NodeState.PERM_MARK;
                nodeStates[r] &= ~NodeState.TEMP_MARK;

                toDo.Push(r);
            };
            visit(reference);
            nodeStates.Clear();

            // Execute the computations
            List<Reference> toDoList = toDo.ToList();
            if (UseParallel) ExecuteParallel(toDoList);
            else ExecuteSerial(toDoList);
            
            return GetState(reference).Computation;
        }


        /// <summary>
        /// Return True if the referenced computation has already been
        /// performed in this context.
        /// 
        /// This means the result is instantaneously available.
        /// </summary>
        public bool Done(Reference reference)
        {
            return computations.ContainsKey(reference)
                && computations[reference].Done;
        }

        /// <summary>
        /// True if a computation can begin without blocking on dependencies.
        /// </summary>
        public bool Ready(Reference reference)
        {
            var state = GetState(reference);
            if (state.Done) return true;
            return state.Computation.Dependencies().All(dep => Done(dep));
        }
        
        /// <summary>
        /// Flush all computations of type <typeparamref name="T"/> from the cache.
        /// </summary>
        /// <typeparam name="T">Type to remove</typeparam>
        public void FlushType<T>()
            where T : Computation
        {
            List<Reference> bad = new List<Reference>();
            foreach (var p in computations)
            {
                if (p.Key.ComputationType == typeof(T))
                {
                    bad.Add(p.Key);
                }
            }
            foreach (var r in bad)
            {
                computations.Remove(r);
            }
        }

        /// <summary>
        /// Remove all computations from the cache.
        /// </summary>
        public void FlushAll()
        {
            computations.Clear();
        }
        
        private void ExecuteNow(Reference reference)
        {
            ComputationState state;
            lock (this)
            {
                state = GetState(reference);
            }
            state.Computation.Compute(this);
            state.Done = true;
        }

        private void ExecuteSerial(List<Reference> toDo)
        {
            foreach (var reference in toDo)
            {
                ExecuteNow(reference);
            }
        }

        private void ExecuteParallel(List<Reference> toDo)
        {
            int queued = 0;
            while (queued < toDo.Count)
            {
                for (; queued < toDo.Count; queued++)
                {
                    var wi = toDo[queued];
                    if (Ready(wi))
                    {
                        ThreadPool.QueueUserWorkItem((r) =>
                        {
                            ExecuteNow((Reference)r);
                        }, wi);
                        queued++;
                    }
                    else
                    {
                        break;
                    }
                }

                Thread.Sleep(0);
            }

            // The last item must be the root of the (sub)tree, so when it
            // has completed we can be sure all computations have run
            while (!Done(toDo[toDo.Count - 1]))
            {
                Thread.Sleep(0);
            }
        }

        private class ComputationState
        {
            public Computation Computation;
            public bool Done;
        }

        private ComputationState GetState(Reference reference)
        {
            if (!computations.ContainsKey(reference))
            {
                var state = computations[reference] = new ComputationState();
                state.Computation = (Computation)Activator.CreateInstance(reference.ComputationType);
                state.Done = false;

                state.Computation.Init(reference.Argument);

                // If this computation is persisted to disk, try to
                // deserialize it instead of performing this entire branch
                // of the tree.
                if (persistenceRoot != null && state.Computation is PersistedComputation)
                {
                    PersistedComputation pc = (PersistedComputation)state.Computation;
                    string path = Path.Combine(persistenceRoot, pc.Identifier);
                    if (File.Exists(path))
                    {
                        byte[] data = File.ReadAllBytes(path);
                        if (pc.Deserialize(data))
                        {
                            state.Done = true;
                            return state;
                        }
                    }
                }
            }
            return computations[reference];
        }

        private Dictionary<Reference, ComputationState> computations;
        private string persistenceRoot;
    }
}
