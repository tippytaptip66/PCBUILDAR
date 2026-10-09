using System.IO;
using UnityEngine;

namespace BuildAR.Save
{
    public static class SaveSystem
    {
        private const string FileName = "buildar_save.json";
        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static SaveData Load()
        {
            if (!File.Exists(FilePath)) return new SaveData();
            try
            {
                string json = File.ReadAllText(FilePath);
                return JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"BuildAR: failed to load save file, starting fresh. {e.Message}");
                return new SaveData();
            }
        }

        public static void Save(SaveData data)
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(data, true)); }
            catch (System.Exception e) { Debug.LogError($"BuildAR: failed to save progress. {e.Message}"); }
        }
    }
}
