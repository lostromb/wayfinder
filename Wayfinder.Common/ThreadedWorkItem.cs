using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Wayfinder.Common
{
    public abstract class ThreadedWorkItem<T>
    {
        private readonly ManualResetEvent _finished;
        private T _returnVal;

        public ThreadedWorkItem()
        {
            _finished = new ManualResetEvent(false);
        }

        public void Run()
        {
            _returnVal = DoWork();
            _finished.Set();
        }

        public T Join()
        {
            _finished.WaitOne();
            _finished.Dispose();
            return _returnVal;
        }

        protected abstract T DoWork();
    }
}
