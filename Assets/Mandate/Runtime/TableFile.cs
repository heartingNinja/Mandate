// TableFile — what the Table screen remembers between sessions, on disk.
//
// Saved to persistentDataPath/Mandate/table.json with JsonUtility when a game is started from the Table screen,
// and loaded when the view starts. Holds the player count, the "AI thinking" switch, the seed, bot kind and
// seat shuffle, one SeatSetup per seat and, if "remember the keys too" is ticked, the API keys in plain text. JsonUtility cannot write dictionaries, so the
// keys are a list of KeyEntry.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Mandate.Viewer
{
    [Serializable]
    public sealed class TableFile
    {
        public int players;
        public bool revealAgentNotes;

        // Seed, bots and seating for the next game. rulesSaved is false in files written before these were saved,
        // so loading such a file keeps the inspector's values instead of switching shuffling off.
        public bool rulesSaved;
        public int seed;
        public string botKind;
        public bool shuffleSeats;
        public List<SeatSetup> seats = new List<SeatSetup>();
        public List<KeyEntry> keys = new List<KeyEntry>();

        /// <summary>Reads the file. Returns null when there is no file or it cannot be read (a warning is logged).</summary>
        public static TableFile Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<TableFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Mandate: could not read " + path + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Removes one provider's key from the file on disk, leaving the rest of the table as it was.</summary>
        public static void ForgetKey(string path, string env)
        {
            var file = Load(path);
            if (file == null || file.keys.RemoveAll(k => k.env == env) == 0) return;
            file.Save(path);
        }

        /// <summary>Deletes the file (table and keys), if there is one. Failures are logged, not thrown.</summary>
        public static void Delete(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Delete(path);
                WebFiles.Save();   // in a browser, make the deletion stick too
            }
            catch (Exception e) { Debug.LogWarning("Mandate: could not delete " + path + ": " + e.Message); }
        }

        /// <summary>Writes the file, creating the folder first. Failures are logged, not thrown.</summary>
        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(this, true));
                WebFiles.Save();   // in a browser, keep it for the next visit
            }
            catch (Exception e) { Debug.LogWarning("Mandate: could not write " + path + ": " + e.Message); }
        }
    }

    /// <summary>One remembered API key: the environment variable it stands in for, and the key itself.</summary>
    [Serializable]
    public sealed class KeyEntry
    {
        public string env;
        public string key;
    }
}
