using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;

namespace Wayfinder.Common
{
    public abstract class ThreadedWorkItem<T>
    {
        private readonly ManualResetEvent _finished;
        private ExceptionDispatchInfo _exception;
        private T _returnVal;

        public ThreadedWorkItem()
        {
            _finished = new ManualResetEvent(false);
            _exception = null;
            _returnVal = default(T);
        }

        public void Run()
        {
            try
            {
                _returnVal = DoWork();
            }
            catch (Exception e)
            {
                _exception = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                _finished.Set();
            }
        }

        public T Join()
        {
            _finished.WaitOne();
            _finished.Dispose();
            if (_exception != null)
            {
                _exception.Throw();
            }

            return _returnVal;
        }

        protected abstract T DoWork();
    }
}
