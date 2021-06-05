using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver.Logger
{
    public class DebugLogger : ILogger
    {
        public void Log(string message, LogLevel level = LogLevel.Std)
        {
            Debug.WriteLine(message);
        }
    }
}
