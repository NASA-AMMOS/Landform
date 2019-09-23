using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using OPS.Util;
using OPS.Pipeline.TilingServer;

//TODO: refactor so that local codepath does not have cloud dependencies
using QueueMessage = OPS.Cloud.QueueMessage;

namespace OPS.Pipeline
{
    public enum ExecutionMode { Immediate, Deferred, None }

    public class PipelineExecutive
    {
        protected PipelineCore pipeline;

        //project name -> state machine
        private ConcurrentDictionary<string, PipelineStateMachine> stateMachines =
            new ConcurrentDictionary<string, PipelineStateMachine>();

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

        protected PipelineStateMachine GetStateMachine(QueueMessage msg)
        {
            return stateMachines.GetOrAdd(msg.ProjectName, _ =>
                    {
                        var projectType = PipelineStateMachine.GetProjectType(pipeline, msg);
                        if (projectType.HasValue)
                        {
                            return PipelineStateMachine.CreateInstance(pipeline, projectType.Value, msg.ProjectName);
                        }
                        else
                        {
                            //this can happen if we get a duplicate DeleteProject message 
                            throw new Exception("could not determine project type"); //do not add to dictionary
                        }
                    });
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
                        pipeline.LogException(ex, msg.Info() + ": master task error", stackTrace: true);
                    }
                }

                return false; //now discard message
            };

            var workerDispatcher = StartWorker.MakeDispatcher(pipeline);
            pipeline.EnqueuedToWorkers += msg => {
                try
                {
                    workerDispatcher.Handle(msg);
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, msg.Info() + ": worker task error", stackTrace: true);
                }
                return false; //now discard message
            };
        }
    }

    //multi threaded executive - use for large workflows'
    //spins up one thread for the master and a pool of worker threads corresponding to number of available cores
    //enqueuing a message to the master is a low cost constant time operation
    //but the ensuing work will be performed asynchronously at a later point as messages are processed
    public class DeferredExecutive : PipelineExecutive
    {
        private ConcurrentQueue<QueueMessage> masterQueue;
        private ConcurrentQueue<QueueMessage> workerQueue;

        private Dictionary<string, PipelineStateMachine> stateMachines = new Dictionary<string, PipelineStateMachine>();

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

            masterTask = Task.Run(() =>
                    {
                        try
                        {
                            MasterLoop();
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogException(ex, "master task error", stackTrace: true);
                        }
                    });

            workerDispatcher = StartWorker.MakeDispatcher(pipeline);

            workerTasks = new Task[CoreLimitedParallel.GetMaxCores()];
            for (int i = 0; i < workerTasks.Length; i++)
            {
                workerTasks[i] = Task.Run(() =>
                        {
                            try
                            {
                                WorkerLoop();
                            }
                            catch (Exception ex)
                            {
                                pipeline.LogException(ex, "worker task error", stackTrace: true);
                            }
                        });
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

        protected void MessageLoop(ConcurrentQueue<QueueMessage> queue, Action<QueueMessage> handler, string what)
        {
            while (!quit)
            {
                //only take one message at a time when we are ready to process it
                Stopwatch sw = new Stopwatch();
                sw.Start();
                if (queue.TryDequeue(out QueueMessage msg))
                {
                    try
                    {
                        handler(msg);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogException(ex, msg.Info() + ": master task error", stackTrace: true);
                    }
                }

                int sleepMS = (int)(THROTTLE_MS - sw.ElapsedMilliseconds);
                if (sleepMS > 0)
                {
                    Thread.Sleep(sleepMS);
                }
            }
        }

        protected void MasterLoop()
        {
            void handler(QueueMessage msg)
            {
                var stateMachine = GetStateMachine(msg);
                if (stateMachine != null)
                {
                    stateMachine.ProcessMessage(msg);
                }
            }

            MessageLoop(masterQueue, handler, "master");
        }

        protected void WorkerLoop()
        {
            MessageLoop(workerQueue, m => workerDispatcher.Handle(m), "worker");
        }
    }
}

