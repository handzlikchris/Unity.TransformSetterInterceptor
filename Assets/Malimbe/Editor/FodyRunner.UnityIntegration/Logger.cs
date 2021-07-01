namespace Malimbe.FodyRunner.UnityIntegration
{
    using System.Collections.Generic;
    using UnityEngine;
    using ILogger = Malimbe.FodyRunner.ILogger;

    internal sealed class Logger : ILogger
    {
        private static readonly Dictionary<LogLevel, LogType> _logMap = new Dictionary<LogLevel, LogType>
        {
            [LogLevel.Debug] = LogType.Log,
            [LogLevel.Info] = LogType.Log,
            [LogLevel.Warning] = LogType.Warning,
            [LogLevel.Error] = LogType.Error
        };

        public void Log(LogLevel level, string message) =>
            Debug.unityLogger.Log(_logMap[level], message);
    }
}
