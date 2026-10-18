// ResultsRelaySettings — where "Send results..." posts finished games: the results relay's URL and shared token.
//
// They live in Assets/Resources/MandateResultsRelay.json, which .gitignore keeps OUT of the public repository, so
// people reading the code on GitHub don't get a working address to send reports to. A copy with blank values is
// Tools/MandateResultsRelay/MandateResultsRelay.example.json; copy it into Assets/Resources and fill it in.
//
// The file is still built into every copy of the game (it has to be, the game posts to it). Keeping it out of git
// stops casual use; the relay's daily caps and a token change are what stop anyone who digs it out of a build.
//
// No file, or an empty URL: Send only saves reports to persistentDataPath/Mandate/results-pending.

using System;
using UnityEngine;

namespace Mandate.Viewer
{
    [Serializable]
    public sealed class ResultsRelaySettings
    {
        /// <summary>Resources path of the settings file (Assets/Resources/MandateResultsRelay.json), without the extension.</summary>
        public const string ResourceName = "MandateResultsRelay";

        /// <summary>Web app URL of the results relay (Tools/MandateResultsRelay/README.md), ending in /exec.</summary>
        public string url = "";

        /// <summary>Must equal the relay's SHARED_TOKEN script property. The relay refuses reports without it.</summary>
        public string token = "";

        /// <summary>Reads the settings file, or returns empty settings when there is none.</summary>
        public static ResultsRelaySettings Load()
        {
            var file = Resources.Load<TextAsset>(ResourceName);
            if (file == null) return new ResultsRelaySettings();

            try { return JsonUtility.FromJson<ResultsRelaySettings>(file.text) ?? new ResultsRelaySettings(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Results] could not read Assets/Resources/{ResourceName}.json: {e.Message}");
                return new ResultsRelaySettings();
            }
        }
    }
}
