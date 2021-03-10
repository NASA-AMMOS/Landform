using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using OPS.Util;
using OPS.Pipeline.TilingServer;

namespace OPS.Pipeline
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

        public static PipelineExecutive MakeExecutive(PipelineCore pipeline, ExecutionMode mode,
                                                      bool supportAlignment = false)
        {
            switch (mode)
            {
                case ExecutionMode.Immediate: return new ImmediateExecutive(pipeline, supportAlignment);
                case ExecutionMode.Deferred: return new DeferredExecutive(pipeline, supportAlignment);
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
    }

    //single threaded executive - should be used for small workflows only
    //use DeferredExecutive for larger workflows
    //particularly those that involve a lot of back and forth messaging between master and workers
    //because in that case ImmediateExecutive will build up large call stacks
    //work is performed synchronously in the same call where a message is enqueued to the master
    //https://github.jpl.nasa.gov/OnSight/Landform/issues/699
    public class ImmediateExecutive : PipelineExecutive
    {
        public bool ThrowOnMasterError = true;
        public bool ThrowOnWorkerError = true;

        public ImmediateExecutive(PipelineCore pipeline, bool supportAlignment = false) : base(pipeline)
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
                        pipeline.LogException(ex, msg.Info() + ": master task error", stackTrace: true);
                        MasterError = ex;
                        if (ThrowOnMasterError)
                        {
                            throw;
                        }
                    }
                }

                return false; //now discard message
            };

            var workerDispatcher = StartWorker.MakeDispatcher(pipeline, supportAlignment);
            pipeline.EnqueuedToWorkers += msg => {
                try
                {
                    workerDispatcher.Handle(msg);
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, msg.Info() + ": worker task error", stackTrace: true);
                    WorkerError = ex;
                    if (ThrowOnWorkerError)
                    {
                        throw;
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
        private ConcurrentQueue<PipelineMessage> masterQueue;
        private ConcurrentQueue<PipelineMessage> workerQueue;

        private TypeDispatcher workerDispatcher;

        private Task masterTask;

        private Task[] workerTasks;

        private volatile bool quit = false;

        private const int THROTTLE_MS = 50;

        public DeferredExecutive(PipelineCore pipeline, bool supportAlignment = false) : base(pipeline)
        {
            if (!(pipeline is LocalPipeline))
            {
                throw new ArgumentException("DeferredExecutive must be used with LocalPipeline");
            }

            masterQueue = ((LocalPipeline)pipeline).MasterQueue;
            workerQueue = ((LocalPipeline)pipeline).WorkerQueue;

            masterTask = Task.Run(() => MasterLoop()); //lambda needed to compile

            workerDispatcher = StartWorker.MakeDispatcher(pipeline, supportAlignment);

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
                                   Action periodic = null)
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
                        string err = string.Format("{0}: {1} task error", msg.Info(), what);
                        pipeline.LogException(ex, err, stackTrace: true);
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
                try
                {
                    var stateMachine = GetStateMachine(msg);
                    if (stateMachine != null)
                    {
                        stateMachine.ProcessMessage(msg);
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, msg.Info() + ": master task error", stackTrace: true);
                    MasterError = ex;
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

            MessageLoop(masterQueue, handler, "master", periodic);
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
                    pipeline.LogException(ex, msg.Info() + ": worker task error", stackTrace: true);
                    WorkerError = ex;
                }
            }
            
            MessageLoop(workerQueue, handler, "worker");
        }
    }
}

