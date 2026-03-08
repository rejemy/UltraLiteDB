using System;

namespace UltraLiteDB
{
    /// <summary>
    /// Bitmask-based logger for database diagnostics. Each subsystem (DISK, CACHE, QUERY, etc.) has a
    /// bit flag; messages are emitted only when the corresponding bit is set in <see cref="Level"/>.
    /// Log messages fire before the operation executes (useful for debugging hangs).
    /// </summary>
    public class Logger
    {
        public const byte NONE = 0;
        public const byte ERROR = 1;
        public const byte RECOVERY = 2;
        public const byte COMMAND = 4;
        public const byte LOCK = 8;
        public const byte QUERY = 16;
        public const byte JOURNAL = 32;
        public const byte CACHE = 64;
        public const byte DISK = 128;
        public const byte FULL = 255;

        /// <summary>
        /// Initialize logger class using a custom logging level (see Logger.NONE to Logger.FULL)
        /// </summary>
        public Logger(byte level = NONE, Action<string> logging = null)
        {
            this.Level = level;

            if (logging != null)
            {
                this.Logging += logging;
            }
        }

        /// <summary>
        /// Event when log writes a message. Fire on each log message
        /// </summary>
        public event Action<string> Logging = null;

        /// <summary>
        /// To full logger use Logger.FULL or any combination of Logger constants like Level = Logger.ERROR | Logger.COMMAND | Logger.DISK
        /// </summary>
        public byte Level { get; set; }

        public Logger()
        {
            this.Level = NONE;
        }

        /// <summary>
        /// Execute msg function only if level are enabled
        /// </summary>
        public void Write(byte level, Func<string> fn)
        {
            if ((level & this.Level) == 0) return;

            this.Write(level, fn());
        }

        /// <summary>
        /// Write log text to output using inside a component (statics const of Logger)
        /// </summary>
        public void Write(byte level, string message, params object[] args)
        {
            if ((level & this.Level) == 0 || string.IsNullOrEmpty(message)) return;

            if (this.Logging != null)
            {
                var text = string.Format(message, args);

                var str =
                    level == ERROR ? "ERROR" :
                    level == RECOVERY ? "RECOVERY" :
                    level == COMMAND ? "COMMAND" :
                    level == JOURNAL ? "JOURNAL" :
                    level == LOCK ? "LOCK" :
                    level == QUERY ? "QUERY" :
                    level == CACHE ? "CACHE" : 
                    level == DISK ? "DISK" : "";

                var msg = DateTime.Now.ToString("HH:mm:ss.ffff") + " [" + str + "] " + text;

                try
                {
                    this.Logging(msg);
                }
                catch
                {
                }
            }
        }
    }
}