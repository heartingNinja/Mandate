// ResultSender — sends a ResultReport to the results relay, and keeps any that could not be sent to retry later.
//
// The relay is a Google Apps Script web app (Tools/MandateResultsRelay) running on the developer's Google
// account. It emails each report to the developer's email with the transcript attached and adds rows to
// a "Mandate Results" Google Sheet. The game never holds an email password: only the relay's URL and a shared token.
//
// How a delivery is recognised: Apps Script runs doPost, then answers with a 302 redirect to a one-time
// script.googleusercontent.com URL that serves the reply. The redirect is followed by hand with a GET, and only a
// reply containing "ok":true counts. A 302 to accounts.google.com means the script was deployed without "Anyone"
// access. The body is sent as text/plain so a browser (WebGL) posts it without a CORS preflight, which Apps Script
// cannot answer; in a browser redirectLimit is left alone so the redirect is followed automatically and the reply
// arrives as a plain 200.
//
// Unsent reports wait in persistentDataPath/Mandate/results-pending (newest few kept) and are retried when the
// game next starts. Same pattern as the Rooms project's BugReportUploader.

using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Mandate.Viewer
{
    public sealed class ResultSender
    {
        const int TimeoutSeconds = 60;
        const int MaxPending = 5;

        readonly string endpoint, token, pendingDir;

        public ResultSender(string endpointUrl, string sharedToken, string pendingFolder)
        {
            endpoint = (endpointUrl ?? "").Trim();
            token = sharedToken ?? "";
            pendingDir = pendingFolder;
        }

        /// <summary>True when a relay URL is set. Without one, reports are only saved to the pending folder.</summary>
        public bool HasEndpoint => endpoint.Length > 0;

        /// <summary>Sends one report, or saves it to retry later. onDone gets (delivered, a line to show the player).</summary>
        public IEnumerator Send(ResultReport report, Action<bool, string> onDone)
        {
            report.Token = token;
            string json = JsonUtility.ToJson(report);
            bool delivered = false;
            string why = "no results address is set in this build";

            if (HasEndpoint) yield return Post(json, (ok, reason) => { delivered = ok; why = reason; });

            if (delivered) { onDone(true, "Sent. Thank you!"); yield break; }
            bool saved = SavePending(report.ReportId, json);
            onDone(false, saved ? $"Not sent yet ({why}). Saved; it will be sent next time the game starts." : $"Not sent ({why}).");
        }

        /// <summary>Tries again to send every report left over from earlier sessions, oldest first. Stops at the first failure.</summary>
        public IEnumerator RetryPending()
        {
            if (!HasEndpoint) yield break;
            foreach (var file in PendingOldestFirst())
            {
                string json;
                try
                {
                    var report = JsonUtility.FromJson<ResultReport>(File.ReadAllText(file));
                    report.Token = token;   // a token changed in a later build must not strand old reports
                    json = JsonUtility.ToJson(report);
                }
                catch (Exception e) { Debug.LogWarning("[Results] dropping unreadable " + file + ": " + e.Message); TryDelete(file); continue; }

                bool delivered = false;
                yield return Post(json, (ok, reason) => delivered = ok);
                if (!delivered) yield break;   // still offline or refused; try the rest another time
                TryDelete(file);
                Debug.Log("[Results] sent saved report " + Path.GetFileNameWithoutExtension(file));
            }
        }

        // POSTs one report. onDone gets (accepted, reason when not).
        IEnumerator Post(string json, Action<bool, string> onDone)
        {
            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "text/plain;charset=utf-8");
                request.timeout = TimeoutSeconds;
                // Desktop: stop at the redirect and follow it by hand below. Not in a browser: Unity's WebGL turns a
                // limit of 0 into fetch's redirect "error", which fails every send with "Unknown Error".
                if (!WebFiles.InBrowser) request.redirectLimit = 0;
                yield return request.SendWebRequest();

                long code = request.responseCode;
                string location = request.GetResponseHeader("Location") ?? "";
                string body = request.downloadHandler?.text ?? "";

                if ((code == 301 || code == 302 || code == 303) && location.Contains("script.googleusercontent.com"))
                {
                    yield return ReadReply(location, onDone);
                    yield break;
                }
                if (code == 200) { onDone(IsOk(body), ReasonFrom(body)); yield break; }
                if (location.Contains("accounts.google.com"))
                {
                    Debug.LogWarning("[Results] the relay asked for a Google sign-in. Redeploy it with 'Who has access' set to 'Anyone'.");
                    onDone(false, "the results relay is not open to the public");
                    yield break;
                }
                Debug.LogWarning($"[Results] not delivered (HTTP {code}, {request.error}): {Short(body)}");
                onDone(false, string.IsNullOrEmpty(request.error) ? "HTTP " + code : request.error);
            }
        }

        // Fetches the relay's reply from the one-time URL it redirected to.
        IEnumerator ReadReply(string url, Action<bool, string> onDone)
        {
            using (var reply = UnityWebRequest.Get(url))
            {
                reply.timeout = TimeoutSeconds;
                yield return reply.SendWebRequest();
                string body = reply.downloadHandler?.text ?? "";
                bool ok = reply.result == UnityWebRequest.Result.Success && IsOk(body);
                if (!ok) Debug.LogWarning($"[Results] the relay did not accept the report (HTTP {reply.responseCode}): {Short(body)}");
                onDone(ok, ok ? null : ReasonFrom(body) ?? reply.error ?? "no reply");
            }
        }

        static bool IsOk(string body) => body.Contains("\"ok\":true") || body.Contains("\"ok\": true");

        // The relay's "error" text from a {"ok":false,"error":"..."} reply, or null.
        static string ReasonFrom(string body)
        {
            const string tag = "\"error\":\"";
            int i = body.IndexOf(tag, StringComparison.Ordinal);
            if (i < 0) return IsOk(body) ? null : "unexpected reply";
            int end = body.IndexOf('"', i + tag.Length);
            return end < 0 ? "unexpected reply" : body.Substring(i + tag.Length, end - i - tag.Length);
        }

        static string Short(string s) => s.Length <= 200 ? s : s.Substring(0, 200) + "...";

        // Writes an unsent report to the pending folder, then trims the folder to the newest few. False if it could not be saved.
        bool SavePending(string id, string json)
        {
            try
            {
                Directory.CreateDirectory(pendingDir);
                File.WriteAllText(Path.Combine(pendingDir, id + ".json"), json);
                var files = PendingOldestFirst();
                for (int i = 0; i < files.Length - MaxPending; i++) TryDelete(files[i]);
                WebFiles.Save();   // in a browser, keep it for the next visit
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Results] could not save the report: " + e.Message); return false; }
        }

        string[] PendingOldestFirst()
        {
            try
            {
                if (!Directory.Exists(pendingDir)) return new string[0];
                var files = Directory.GetFiles(pendingDir, "*.json");
                var times = Array.ConvertAll(files, File.GetLastWriteTimeUtc);
                Array.Sort(times, files);
                return files;
            }
            catch (Exception e) { Debug.LogWarning("[Results] could not list saved reports: " + e.Message); return new string[0]; }
        }

        static void TryDelete(string path)
        {
            try { File.Delete(path); WebFiles.Save(); }
            catch (Exception e) { Debug.LogWarning("[Results] could not delete " + path + ": " + e.Message); }
        }
    }
}
