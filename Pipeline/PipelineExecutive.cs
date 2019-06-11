using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using OPS.Util;
using OPS.Pipeline.TileServer;

//TODO: refactor so that local codepath does not have cloud dependencies
using QueueMessage = OPS.Cloud.QueueMessage;

namespace OPS.Pipeline
{
    public enum ExecutionMode { Immediate, Deferred }

    public class PipelineExecutive
    {
        protected PipelineCore pipeline;

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
                default: throw new ArgumentException("unknown execution mode: " + mode);
            }
        }
    }

    public class ImmediateExecutive : PipelineExecutive
    {
        public ImmediateExecutive(PipelineCore pipeline) : base(pipeline)
        {
            pipeline.EnqueuedToMaster += msg => {
                var projectType = PipelineStateMachine.GetProjectType(pipeline, msg);
                if (projectType.HasValue)
                {
                    var sm = PipelineStateMachine.CreateInstance(pipeline, projectType.Value, msg.ProjectName);
                    var masterDispatcher = sm.MakeDispatcher();
                    masterDispatcher.Handle(msg);
                }
                else
                {
                    pipeline.LogWarn("could not determine project type, discarding message: {0}", msg.Info());
                }
                return false; //now discard message
            };

            var workerDispatcher = StartWorker.MakeDispatcher(pipeline);
            pipeline.EnqueuedToWorkers += msg => {
                workerDispatcher.Handle(msg);
                return false; //now discard message
            };
        }
    }

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
                        catch (Exception e)
                        {
                            pipeline.LogError("error in master task ({0}): {1}", e.GetType().FullName, e.Message);
                            pipeline.LogError(e.StackTrace);
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
                            catch (Exception e)
                            {
                                pipeline.LogError("error in worker task ({0}): {1}", e.GetType().FullName, e.Message);
                                pipeline.LogError(e.StackTrace);
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
                if (queue.TryDequeue(out QueueMessage m))
                {
                    try
                    {
                        handler(m);
                    }
                    catch (Exception e)
                    {
                        pipeline.LogError("{0}: {1} task error ({w}): {3}", m.Info(), what, e.GetType().FullName,
                                          e.Message);
                        pipeline.LogError(e.StackTrace);
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
            void handler(QueueMessage m)
            {
                if (!stateMachines.ContainsKey(m.ProjectName))
                {
                    PipelineStateMachine.ProjectType? pt = PipelineStateMachine.GetProjectType(pipeline, m);
                    if (pt.HasValue)
                    {
                        stateMachines[m.ProjectName] =
                            PipelineStateMachine.CreateInstance(pipeline, pt.Value, m.ProjectName);
                    }
                    else
                    {
                        //this can happen if we get a duplicate DeleteProject message 
                        pipeline.LogWarn("could not determine project type, discarding message: {0}", m.Info());
                    }
                }
                
                if (stateMachines.ContainsKey(m.ProjectName))
                {
                    stateMachines[m.ProjectName].ProcessMessage(m);
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

