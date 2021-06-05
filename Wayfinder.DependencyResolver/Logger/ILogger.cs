﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver.Logger
{
    public interface ILogger
    {
        void Log(string message, LogLevel level = LogLevel.Std);
    }
}
