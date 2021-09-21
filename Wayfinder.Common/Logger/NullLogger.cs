using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.Common.Logger
{
    public class NullLogger : ILogger
    {
        public static readonly ILogger Singleton = new NullLogger();

        private NullLogger() { }

        public void Log(string message, LogLevel level = LogLevel.Std)
        {
        }
    }
}
