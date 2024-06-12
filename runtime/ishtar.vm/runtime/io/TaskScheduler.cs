namespace ishtar.io;
using collections;
using runtime;
using runtime.gc;
using System.Threading;
using vein.runtime;
using static VirtualMachine;
using static vm.libuv.LibUV;

public unsafe struct TaskScheduler(NativeQueue<IshtarTask>* queue) : IDisposable
{
    private ulong task_index;
    private void* async_header;
    private nint loop;
    private readonly NativeQueue<IshtarTask>* _queue = queue;


    public static TaskScheduler* Create()
    {
        var scheduler = IshtarGC.AllocateImmortal<TaskScheduler>();
        var queue = IshtarGC.AllocateQueue<IshtarTask>();
        *scheduler = new TaskScheduler(queue);

        var asyncHeader = IshtarGC.AllocateImmortal<nint>();
        scheduler->loop = uv_default_loop();
        Assert(uv_async_init(scheduler->loop, (nint)asyncHeader, on_async) == 0,
            WNE.THREAD_STATE_CORRUPTED, "scheduler has failed create async io");
        ((uv_async_t*)asyncHeader)->data = scheduler;
        scheduler->async_header = asyncHeader;
        scheduler->task_index = 0;

        return scheduler;
    }


    public static void Free(TaskScheduler* scheduler)
    {
        // TODO
    }

    public static void on_async(nint handler)
    {
        var bw = (uv_async_t*)handler;
        var taskScheduler = (TaskScheduler*)bw->data;
        var queue = taskScheduler->_queue;
        while (queue->TryDequeue(out var task))
        {
            task->Frame->vm.exec_method(task->Frame);
            uv_sem_post(ref task->Data->semaphore);
        }
    }
    public void execute_method(CallFrame* frame)
    {
        if ((frame->method->Flags & MethodFlags.Async) != 0)
            doAsyncExecute(frame);
        else
            doExecute(frame);
    }

    private void doExecute(CallFrame* frame)
        => frame->vm.exec_method(frame);

    private void doAsyncExecute(CallFrame* frame)
    {
        // TODO remove using interlocked
        var taskIdx = Interlocked.Increment(ref task_index);
        var task = IshtarGC.AllocateImmortal<IshtarTask>();

        *task = new IshtarTask(frame, taskIdx);

        uv_sem_init(out task->Data->semaphore, 0);

        _queue->Enqueue(task);

        uv_async_send((nint)async_header);

        uv_sem_wait(ref task->Data->semaphore);
        uv_sem_destroy(ref task->Data->semaphore);
        task->Dispose();
        IshtarGC.FreeImmortal(task);
    }

    public void Dispose() => IshtarGC.FreeQueue(_queue);

    public void run() => uv_run(loop, uv_run_mode.UV_RUN_DEFAULT);
    public void stop() => uv_stop(loop);


    public void start_threading(RuntimeIshtarModule* entryModule)
    {
        static void execute_scheduler(IshtarRawThread* thread)
        {
            var vm = thread->MainModule->vm;

            var gcInfo = new GC_stack_base();

            vm.GC.get_stack_base(&gcInfo);

            vm.GC.register_thread(&gcInfo);

            vm.task_scheduler->run();

            vm.GC.unregister_thread();
        }

        new Thread((x) =>
        {
            RuntimeIshtarModule* module = (RuntimeIshtarModule*)(nint)x;
            var vm = module->vm;
            var gcInfo = new GC_stack_base();

            vm.GC.get_stack_base(&gcInfo);

            vm.GC.register_thread(&gcInfo);

            vm.task_scheduler->run();

            vm.GC.unregister_thread();
        }).Start((nint)entryModule);
    }
}