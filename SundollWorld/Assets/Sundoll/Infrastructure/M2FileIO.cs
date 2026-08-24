using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sundoll.Infrastructure
{
    public static class M2FileIO
    {
        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Directory path is required.", nameof(path));
            }

            Directory.CreateDirectory(path);
        }

        public static void WriteUtf8Atomic(string path, string content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            WriteBytesAtomic(path, new UTF8Encoding(false).GetBytes(content));
        }

        public static void WriteBytesAtomic(string path, byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Atomic file path must include a directory.");
            }

            EnsureDirectory(directory);
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporaryPath, bytes);
                using (var stream = new FileStream(temporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    stream.Flush(true);
                }

                ReplaceFile(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public static void ReplaceFile(string temporaryPath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, null);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }

        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes ?? throw new ArgumentNullException(nameof(bytes)));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        public static string Sha256File(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        public static string Sha256Utf8(string value)
        {
            return Sha256(new UTF8Encoding(false).GetBytes(value ?? string.Empty));
        }

        public static bool IsSafeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var character in value)
            {
                if (!(char.IsLetterOrDigit(character) || character == '-' || character == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
