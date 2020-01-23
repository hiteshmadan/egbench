// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using McMaster.Extensions.CommandLineUtils;

namespace EGBench
{
    public static class EGBenchLogger
    {
        private static string Timestamp => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        public static void WriteLine(string s, [CallerFilePath] string callerFilePath = default, [CallerMemberName] string callerMemberName = default, [CallerLineNumber] int callerLineNumber = 0)
        {
            string fileName = Path.GetFileNameWithoutExtension(callerFilePath);
            string message = $"{Timestamp}:[{nameof(EGBenchLogger)}] - [{fileName}:{callerMemberName}()@line {callerLineNumber}] {s}";
            Console.WriteLine(message);

            if (Debugger.IsAttached)
            {
                Debug.WriteLine(message);
            }
        }

        public static void WriteLine(IConsole c, string s, [CallerFilePath] string callerFilePath = default, [CallerMemberName] string callerMemberName = default, [CallerLineNumber] int callerLineNumber = 0)
        {
            string fileName = Path.GetFileNameWithoutExtension(callerFilePath);
            string message = $"{Timestamp}:[{nameof(EGBenchLogger)}] - [{fileName}:{callerMemberName}()@line {callerLineNumber}] {s}";
            c.WriteLine(message);

            if (Debugger.IsAttached)
            {
                Debug.WriteLine(message);
            }
        }
    }
}
