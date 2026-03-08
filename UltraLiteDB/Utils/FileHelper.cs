using System;
using System.IO;

namespace UltraLiteDB
{
	/// <summary>
	/// Static helper methods for temp file creation, safe deletion, and retry-with-timeout for locked files.
	/// </summary>
	internal static class FileHelper
    {
        /// <summary>
        /// Create a temp filename based on original filename - checks if file exists (if exists, append counter number)
        /// </summary>
        public static string GetTempFile(string filename, string suffix = "-temp", bool checkIfExists = true)
        {
            var count = 0;
            var temp = Path.Combine(Path.GetDirectoryName(filename), 
                Path.GetFileNameWithoutExtension(filename) + suffix + 
                Path.GetExtension(filename));

            while(checkIfExists && File.Exists(temp))
            {
                temp = Path.Combine(Path.GetDirectoryName(filename),
                    Path.GetFileNameWithoutExtension(filename) + suffix +
                    "-" + (++count) +
                    Path.GetExtension(filename));
            }

            return temp;
        }

        /// <summary>
        /// Try delete a file that can be in use by another
        /// </summary>
        public static bool TryDelete(string filename)
        {
            try
            {
                File.Delete(filename);
                return true;
            }
            catch(IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Retry an action until it succeeds or the timeout expires, sleeping on <see cref="IOException"/> (file lock).
        /// </summary>
        public static void TryExec(Action action, TimeSpan timeout)
        {
            var timer = DateTime.UtcNow.Add(timeout);

            do
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException ex)
                {
                    ex.WaitIfLocked(25);
                }
            }
            while (DateTime.UtcNow < timer);

            throw UltraLiteException.LockTimeout(timeout);
        }
    }
}
