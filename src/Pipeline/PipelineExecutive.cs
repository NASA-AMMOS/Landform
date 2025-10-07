using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using JPLOPS.Util;
using JPLOPS.Pipeline.TilingServer;

namespace JPLOPS.Pipeline
{
    public enum ExecutionMode { Immediate, Deferred, None }

    public class PipelineExecutive
    {
        public const double STATUS_SPEW_SEC = 15;
        public const double LONG_TASK_WARN_SEC = 5 * 60;

        public volatile Exception MasterError = null;
        public volatile Exception WorkerError = null;

        protected PipelineCore pipeline;

        //project name -> state machine
        protected Dictionary<string, PipelineStateMachine> stateMachines =
            new Dictionary<string, PipelineStateMachine>();

        protected PipelineExecutive(PipelineCore pipeline)
        {
            this.pipeline = pipeline;
        }

        public static PipelineExecutive MakeExecutive(PipelineCore pipeline, ExecutionMode mode)
        {
            switch (mode)
            {
                case ExecutionMode.Immediate: return new ImmediateExecutive(pipeline);
                case ExecutionMode.Deferred: return new DeferredExecutive(pipeline);
                case ExecutionMode.None: return null;
                default: throw new ArgumentException("unknown execution mode: " + mode);
            }
        }

        protected PipelineStateMachine GetStateMachine(PipelineMessage msg)
        {
            if (!stateMachines.ContainsKey(msg.ProjectName))
            {
                var projectType = PipelineStateMachine.GetProjectType(pipeline, msg);
                if (projectType.HasValue)
                {
                    stateMachines[msg.ProjectName] =
                        PipelineStateMachine.CreateInstance(pipeline, projectType.Value, msg.ProjectName);
                }
                else
                {
                    //this can happen if we get a duplicate DeleteProject message 
                    throw new Exception("could not determine project type");
                }
            }
            return stateMachines[msg.ProjectName];
        }

        protected TypeDispatcher MakeDispatcher(PipelineCore pipeline)
        {
            var ret = new TypeDispatcher()
                .Case((DefineTilesMessage m) => new DefineTiles(pipeline, m).Process())
                .Case((ChunkInputMessage m) => new ChunkInput(pipeline, m).Process())
                .Case((BuildLeavesMessage m) => new BuildLeaves(pipeline, m).Process())
                .Case((BuildParentMessage m) => new BuildParent(pipeline, m).Process())
                .Case((BuildTilesetJsonMessage m) => new BuildTilesetJson(pipeline, m).Process());

            ret.Unhandled = (t, x) => throw new Exception("unknown worker message type: " + t);
            return ret;
        }
    }

    //single threaded executive - should be used for small workflows only
    //use DeferredExecutive for larger workflows
    //particularly those that involve a lot of back and forth messaging between master and workers
    //because in that case ImmediateExecutive will build up large call stacks
    //work is performed synchronously in the same call where a message is enqueued to the master
    public class ImmediateExecutive : PipelineExecutive
    {
        public bool ThrowOnMasterError = true;
        public bool ThrowOnWorkerError = true;

        public ImmediateExecutive(PipelineCore pipeline) : base(pipeline)
        {
            pipeline.EnqueuedToMaster += msg => {

                var stateMachine = GetStateMachine(msg);

                if (stateMachine != null)
                {
                    try
                    {
                        stateMachine.ProcessMessage(msg);
                    }
                    catch (Exception ex)
                    {
                        MasterError = ex;
                        if (ThrowOnMasterError)
                        {
                            throw;
                        }
                        else
                        {
                            pipeline.LogException(ex, msg.Info() + ": master task error", stackTrace: true);
                        }
                    }
                }

                return false; //now discard message
            };

            var workerDispatcher = MakeDispatcher(pipeline);
            pipeline.EnqueuedToWorkers += msg => {
                try
                {
                    workerDispatcher.Handle(msg);
                }
                catch (Exception ex)
                {
                    WorkerError = ex;
                    if (ThrowOnWorkerError)
                    {
                        throw;
                    }
                    else
                    {
                        pipeline.LogException(ex, msg.Info() + ": worker task error", stackTrace: true);
                    }
                }
                return false; //now discard message
            };
        }
    }

    //multi threaded executive - use for large workflows
    //spins up one thread for the master and a pool of worker threads corresponding to number of available cores
    //enqueuing a message to the master is a low cost constant time operation
    //but the ensuing work will be performed asynchronously at a later point as messages are processed
    public class DeferredExecutive : PipelineExecutive
    {
        public bool QuitOnMasterError = true;
        public bool QuitOnWorkerError = true;

        private ConcurrentQueue<PipelineMessage> masterQueue;
        private ConcurrentQueue<PipelineMessage> workerQueue;

        private TypeDispatcher workerDispatcher;

        private Task masterTask;

        private Task[] workerTasks;

        private volatile bool quit = false;

        private const int THROTTLE_MS = 50;

        public DeferredExecutive(PipelineCore pipeline) : base(pipeline)
        {
            if (!(pipeline is LocalPipeline))
            {
                throw new ArgumentException("DeferredExecutive must be used with LocalPipeline");
            }

            masterQueue = ((LocalPipeline)pipeline).MasterQueue;
            workerQueue = ((LocalPipeline)pipeline).WorkerQueue;

            masterTask = Task.Run(() => MasterLoop()); //lambda needed to compile

            workerDispatcher = MakeDispatcher(pipeline);

            workerTasks = new Task[CoreLimitedParallel.GetMaxCores()];
            for (int i = 0; i < workerTasks.Length; i++)
            {
                workerTasks[i] = Task.Run(() => WorkerLoop()); //lambda needed to compile
            }
        }

        public void Quit()
        {
            quit = true;

            if (masterTask != null)
            {
                masterTask.Wait();
            }

            if (workerTasks != null)
            {
                Task.WaitAll(workerTasks);
            }
        }

        protected void MessageLoop(ConcurrentQueue<PipelineMessage> queue, Action<PipelineMessage> handler, string what,
                                   Action periodic = null, Func<Exception, bool> error = null)
        {
            while (!quit)
            {
                //only take one message at a time when we are ready to process it
                Stopwatch sw = new Stopwatch();
                sw.Start();
                if (queue.TryDequeue(out PipelineMessage msg))
                {
                    try
                    {
                        handler(msg);
                    }
                    catch (Exception ex)
                    {
                        string errMsg = $"{msg.Info()}: {what} task error";
                        if (error != null)
                        {
                            if (error(new Exception(errMsg, ex)))
                            {
                                quit = true;
                                return;
                            }
                        }
                        else
                        {
                            pipeline.LogException(ex, errMsg, stackTrace: true);
                        }
                    }
                }

                int sleepMS = (int)(THROTTLE_MS - sw.ElapsedMilliseconds);
                if (sleepMS > 0)
                {
                    Thread.Sleep(sleepMS);
                }

                if (periodic != null)
                {
                    periodic();
                }
            }
        }

        protected void MasterLoop()
        {
            void handler(PipelineMessage msg)
            {
                var stateMachine = GetStateMachine(msg);
                if (stateMachine != null)
                {
                    stateMachine.ProcessMessage(msg);
                }
            }

            double lastSpew = UTCTime.Now();
            void periodic()
            {
                double now = UTCTime.Now();
                if (now - lastSpew > STATUS_SPEW_SEC)
                {
                    lastSpew = now;
                    foreach (var sm in stateMachines.Values)
                    {
                        sm.SpewStatus(LONG_TASK_WARN_SEC);
                    }
                }
            }

            MessageLoop(masterQueue, handler, "master", periodic,
                        ex => { MasterError = ex; return QuitOnMasterError; } );
        }

        protected void WorkerLoop()
        {
            void handler(PipelineMessage msg)
            {
                void sendStatus(string status, bool done = false, bool error = false)
                {
                    pipeline.EnqueueToMaster(new StatusMessage(msg.ProjectName, msg.MessageId, msg.GetType().Name,
                                                               status, done, error));
                }

                try
                {
                    sendStatus("started");
                    workerDispatcher.Handle(msg); 
                    sendStatus("complete", done: true);
                }
                catch (Exception ex)
                {
                    sendStatus("error: " + ex.Message, done: true, error: true);
                    throw;
                }
            }
            
            MessageLoop(workerQueue, handler, "worker", null, ex => { WorkerError = ex; return QuitOnWorkerError; });
        }
    }
}

