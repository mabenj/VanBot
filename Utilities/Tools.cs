﻿#region

using System;
using System.IO;

#endregion

namespace VanBot.Utilities {
    public class Tools {
        /// <summary>
        /// StreamToBytes - Converts a Stream to a byte array. Eg: Get a Stream from a file,url, or open file handle.
        /// </summary>
        /// <param name="input">input is the stream we are to return as a byte array</param>
        /// <returns>byte[] The Array of bytes that represents the contents of the stream</returns>
        private static byte[] StreamToBytes(Stream input) {
            var capacity = input.CanSeek ? (int) input.Length : 0; //Bitwise operator - If can seek, Capacity becomes Length, else becomes 0.
            using (var output = new MemoryStream(capacity)) //Using the MemoryStream output, with the given capacity.
            {
                int readLength;
                var buffer = new byte[capacity /*4096*/]; //An array of bytes
                do {
                    readLength = input.Read(buffer, 0, buffer.Length); //Read the memory data, into the buffer
                    output.Write(buffer, 0, readLength); //Write the buffer to the output MemoryStream incrementally.
                } while (readLength != 0); //Do all this while the readLength is not 0

                return output.ToArray(); //When finished, return the finished MemoryStream object as an array.
            }
        }

        public static string ExtractChromeDriverResource() {
            var tempPath = Path.Combine(Path.GetTempPath(), "vanbot_temp", "chromedriver.exe");
            if (File.Exists(tempPath)) {
                return Path.GetDirectoryName(tempPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            var assembly = typeof(Bots.VanBot).Assembly;
            using var input = assembly.GetManifestResourceStream("VanBot.drivers.chromedriver.exe");
            var byteData = StreamToBytes(input);
            File.WriteAllBytes(tempPath, byteData);

            return Path.GetDirectoryName(tempPath);
        }

        public static bool IsDebug() {
#if DEBUG
            return true;
#else
        return false;
#endif
        }

        public static string ReadPassword() {
            var pass = string.Empty;
            ConsoleKey key;
            do {
                var keyInfo = Console.ReadKey(intercept: true);
                key = keyInfo.Key;

                if (key == ConsoleKey.Backspace && pass.Length > 0) {
                    Console.Write("\b \b");
                    pass = pass[0..^1];
                } else if (!char.IsControl(keyInfo.KeyChar)) {
                    Console.Write("*");
                    pass += keyInfo.KeyChar;
                }
            } while (key != ConsoleKey.Enter);

            Console.WriteLine();

            return pass;
        }
    }
}