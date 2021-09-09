using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wayfinder.DependencyResolver.Logger
{
    public class ConsoleLogger : ILogger
    {
        public void Log(string message, LogLevel level = LogLevel.Std)
        {
            if ((level & LogLevel.Err) != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            else if ((level & LogLevel.Wrn) != 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            else if ((level & LogLevel.Vrb) != 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
            }

            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}
