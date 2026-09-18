using System;
using System.IO;

namespace TrafficHunt.Maui
{
    /// <summary>
    /// Writes startup and unhandled-exception information to a log file next to the
    /// executable. Desktop apps have no console, so this is the only way to see what
    /// went wrong during start-up.
    /// </summary>
    internal static class StartupDiagnostics
    {
        private static readonly object Gate = new();

        public static string LogPath { get; } = Path.Combine(AppContext.BaseDirectory, "startup.log");

        public static void Log(string message)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Diagnostics must never take the app down.
            }
        }

        public static void Log(Exception exception, string context)
            => Log($"EXCEPTION [{context}] {exception}");
    }
}