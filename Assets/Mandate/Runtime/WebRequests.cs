// WebRequests — sends the AI seats' HTTP calls with UnityWebRequest, so the same game works in a browser (WebGL).
//
// The agent layer (Mandate.Agents) sends every request through Http.Transport. Outside Unity that is .NET's
// HttpClient, but a browser has no sockets and no threads: the only way out is the browser's own fetch, which
// UnityWebRequest uses in a WebGL build. The view installs this while it runs (in the editor and desktop builds
// too, so the path the browser takes gets played every time) and puts HttpClient back when it stops.
//
// Everything happens on Unity's main thread. Send() only queues a request; Tick(), called from the view's Update,
// starts queued requests, hands back finished ones, aborts ones whose phase timed out, and ends pauses. Nothing
// here needs a timer or a worker thread, which is what makes it safe in a browser.
//
// One trap, found in the first browser build: when a task finishes on Unity's main thread, .NET does not run the
// code waiting on it there and then (Unity's SynchronizationContext makes it unsafe in .NET's eyes), it queues it
// for a worker thread. A browser has no worker threads, so the waiting code never ran and every request seemed to
// hang. Finish() completes tasks with that context switched off for a moment, so the waiting code runs at once.
//
// In a browser, a provider must allow calls from web pages (CORS). Checked 2026-10-18: OpenRouter, OpenAI,
// Anthropic, Gemini, Together, DeepSeek, xAI and Mistral do. A blocked call shows up as "could not reach".

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Mandate.Agents;

namespace Mandate.Viewer
{
    public sealed class WebRequests : IHttpTransport
    {
        // One request: what to send, the UnityWebRequest once started, and the task the agent is waiting on.
        sealed class Call
        {
            public string Method, Url, Body;
            public (string name, string value)[] Headers;
            public CancellationToken Ct;
            public UnityWebRequest Request;
            public TaskCompletionSource<HttpReply> Done;
        }

        // One pause before a retry.
        sealed class Wait
        {
            public float Until;   // Time.realtimeSinceStartup when it ends
            public int Milliseconds;
            public CancellationToken Ct;
            public TaskCompletionSource<bool> Done;
        }

        // Send() and Pause() may be called from any thread (lock), Tick() runs on the main thread.
        readonly List<Call> calls = new List<Call>();
        readonly List<Wait> waits = new List<Wait>();

        /// <summary>Requests still on their way, for the curious.</summary>
        public int InFlight { get { lock (calls) return calls.Count; } }

        public Task<HttpReply> Send(string method, string url, string body, (string name, string value)[] headers, CancellationToken ct)
        {
            var call = new Call { Method = method, Url = url, Body = body, Headers = headers, Ct = ct, Done = new TaskCompletionSource<HttpReply>() };
            lock (calls) calls.Add(call);
            return call.Done.Task;
        }

        public Task Pause(int milliseconds, CancellationToken ct)
        {
            var wait = new Wait { Until = -1f, Milliseconds = milliseconds, Ct = ct, Done = new TaskCompletionSource<bool>() };
            lock (waits) waits.Add(wait);
            return wait.Done.Task;
        }

        /// <summary>Starts, finishes and cancels requests. Call once per frame from the main thread.</summary>
        public void Tick()
        {
            Call[] callsNow;
            lock (calls) callsNow = calls.ToArray();
            foreach (var c in callsNow)
                if (Step(c)) lock (calls) calls.Remove(c);

            Wait[] waitsNow;
            lock (waits) waitsNow = waits.ToArray();
            float now = Time.realtimeSinceStartup;
            foreach (var w in waitsNow)
            {
                if (w.Until < 0f) w.Until = now + w.Milliseconds / 1000f;   // the clock starts on the first tick
                if (w.Ct.IsCancellationRequested) Finish(() => w.Done.TrySetCanceled());
                else if (now >= w.Until) Finish(() => w.Done.TrySetResult(true));
                else continue;
                lock (waits) waits.Remove(w);
            }
        }

        // Completes a task so that the code awaiting it runs right now on this thread (see the trap at the top).
        static void Finish(Action complete)
        {
            var unityContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try { complete(); }
            finally { SynchronizationContext.SetSynchronizationContext(unityContext); }
        }

        /// <summary>Cancels everything still waiting. Call when the view goes away.</summary>
        public void CancelAll()
        {
            Call[] callsNow;
            lock (calls) { callsNow = calls.ToArray(); calls.Clear(); }
            foreach (var c in callsNow) { c.Request?.Abort(); c.Request?.Dispose(); Finish(() => c.Done.TrySetCanceled()); }
            Wait[] waitsNow;
            lock (waits) { waitsNow = waits.ToArray(); waits.Clear(); }
            foreach (var w in waitsNow) Finish(() => w.Done.TrySetCanceled());
        }

        // Moves one call along. True when it is finished (answered, failed or cancelled) and can be dropped.
        // The task is completed last, because the agent's next step (parse, or a retry) runs straight from it.
        bool Step(Call c)
        {
            if (c.Ct.IsCancellationRequested)
            {
                c.Request?.Abort();
                c.Request?.Dispose();
                Finish(() => c.Done.TrySetCanceled());
                return true;
            }
            if (c.Request == null)
            {
                try { c.Request = Start(c); }
                catch (Exception e) { Finish(() => c.Done.TrySetException(e)); return true; }
                return false;
            }
            if (!c.Request.isDone) return false;

            var req = c.Request;
            bool noReply = req.result == UnityWebRequest.Result.ConnectionError || req.responseCode == 0;
            string error = req.error;
            var reply = new HttpReply { Status = (int)req.responseCode, Text = req.downloadHandler?.text ?? "" };
            req.Dispose();
            if (noReply) Finish(() => c.Done.TrySetException(new Exception(CouldNotReach(c.Url, error))));
            else Finish(() => c.Done.TrySetResult(reply));
            return true;
        }

        static UnityWebRequest Start(Call c)
        {
            var req = new UnityWebRequest(c.Url, c.Method) { downloadHandler = new DownloadHandlerBuffer() };
            if (c.Body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(c.Body));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            foreach (var (name, value) in c.Headers ?? new (string, string)[0])
                if (value != null) req.SetRequestHeader(name, value);
            req.SendWebRequest();   // no timeout here: the phase timeout cancels it
            return req;
        }

        static string CouldNotReach(string url, string error)
        {
            string why = $"could not reach {Http.Host(url)} ({(string.IsNullOrEmpty(error) ? "no reply" : error)})";
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                why += ". In a web browser this usually means the provider does not accept calls from web pages, or the address is"
                     + " plain http (a local model needs to allow this page's address, e.g. OLLAMA_ORIGINS=*)";
            return why;
        }
    }
}
