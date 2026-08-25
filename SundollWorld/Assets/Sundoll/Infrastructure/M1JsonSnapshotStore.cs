using System;
using System.IO;
using System.Text;
using Sundoll.Application;
using Sundoll.Core;
using UnityEngine;

namespace Sundoll.Infrastructure
{
    public sealed class M1JsonSnapshotStore : IM1SnapshotStore
    {
        private readonly string path;

        public M1JsonSnapshotStore(string path)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Path => path;

        public void Save(M1WorldState state)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Snapshot path must include a directory.");
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp";
            var json = JsonUtility.ToJson(state, true);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            using (var stream = new FileStream(temporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }

        public M1WorldState Load()
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("M1 snapshot was not found.", path);
            }

            var json = File.ReadAllText(path, new UTF8Encoding(false));
            var state = JsonUtility.FromJson<M1WorldState>(json);
            if (state == null || (state.schemaVersion != 1 && state.schemaVersion != 2))
            {
                throw new InvalidDataException("M1 snapshot schema is invalid.");
            }

            state.EnsureSchema2Defaults();

            return state;
        }
    }
}
