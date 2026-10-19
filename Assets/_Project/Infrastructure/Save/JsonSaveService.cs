using System;
using System.IO;
using TinyTumble.Core.Logging;
using TinyTumble.Core.Save;
using UnityEngine;

namespace TinyTumble.Infrastructure.Save
{
    /// <summary>
    /// JSON-файлы в отдельной папке, по файлу на ключ. Запись через временный файл,
    /// чтобы падение посреди записи не оставило полупустой сейв.
    /// </summary>
    public sealed class JsonSaveService : ISaveService
    {
        private const string LogTag = "Save";
        private const string Extension = ".json";
        private const string TempExtension = ".tmp";

        private readonly string _directory;
        private readonly IGameLog _log;

        public JsonSaveService(string directory, IGameLog log)
        {
            _directory = directory;
            _log = log;
        }

        public bool Exists(string key)
        {
            return File.Exists(GetPath(key));
        }

        public bool TryLoad<T>(string key, out T data) where T : class, new()
        {
            data = null;
            string path = GetPath(key);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var loaded = new T();
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), loaded);
                data = loaded;
                return true;
            }
            catch (Exception exception)
            {
                _log.Warning(LogTag, $"Save '{key}' is corrupted and will be ignored: {exception.Message}");
                return false;
            }
        }

        public void Save<T>(string key, T data) where T : class
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            Directory.CreateDirectory(_directory);
            string path = GetPath(key);
            string tempPath = path + TempExtension;

            File.WriteAllText(tempPath, JsonUtility.ToJson(data, prettyPrint: true));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(tempPath, path);
        }

        public void Delete(string key)
        {
            string path = GetPath(key);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private string GetPath(string key)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"Invalid save key '{key}'.", nameof(key));
            }

            return Path.Combine(_directory, key + Extension);
        }
    }
}
