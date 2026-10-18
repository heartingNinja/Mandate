// WebFiles — what files need in a browser (WebGL) build. Does nothing in the editor and desktop builds.
//
// In a browser, Application.persistentDataPath is the browser's own storage (IndexedDB) for this page's address,
// so table.json (and the keys, if remembered) stay in that player's browser and never reach anyone else. Writes
// land in memory first, so Save() is called after each one to keep it for the next visit. Uploading a new build
// to a different address (itch.io gives each upload its own) starts with an empty table.
//
// A browser cannot open a folder, so the scoreboard offers the transcript as a download instead (Download()).
// The browser side is WebFiles.jslib in this folder.

using System.Runtime.InteropServices;

namespace Mandate.Viewer
{
    public static class WebFiles
    {
        /// <summary>True in a WebGL build running in a browser (not in the editor).</summary>
        public static bool InBrowser
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void MandateSaveFiles();
        [DllImport("__Internal")] static extern void MandateDownloadText(string name, string text);
#endif

        /// <summary>Keeps files written under persistentDataPath for the next visit. Call after writing.</summary>
        public static void Save()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MandateSaveFiles();
#endif
        }

        /// <summary>Offers text to the player as a downloaded file. Browser builds only; elsewhere it does nothing.</summary>
        public static void Download(string fileName, string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            MandateDownloadText(fileName, text ?? "");
#endif
        }
    }
}
