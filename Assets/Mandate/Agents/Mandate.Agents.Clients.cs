// Mandate.Agents.Clients — one thin HTTP client per API family. Each turns (system, user) into that family's
// request body, sends it, and pulls the reply text back out, with the token counts (and cost, where the provider
// reports one) for the results report. Nothing about the game lives here.
//
//   OpenAiChatClient   POST {endpoint}/chat/completions          OpenAI, OpenRouter, Groq, Together, xAI, DeepSeek,
//                                                               Mistral, Ollama, LM Studio, vLLM, and anything else
//                                                               that speaks the OpenAI chat format
//   AnthropicClient    POST {endpoint}/v1/messages               Claude
//   GeminiClient       POST {endpoint}/v1beta/models/{m}:generateContent   Google Gemini
//
// Raw HTTP rather than vendor SDKs because this file has to compile inside Unity, where NuGet packages are not
// available, and because a 60-line client is easier to audit than a dependency.
//
// How a request is actually sent is swappable (Http.Transport). The default, HttpClientTransport, is .NET's
// HttpClient: the console harness and the tests use it. Unity swaps in UnityWebRequest (Runtime/WebRequests.cs)
// because a WebGL build runs in a browser, which has no sockets and no threads, only the browser's own fetch.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mandate.Agents
{
    /// <summary>Sends one HTTP request. Swap Http.Transport to change how every seat's calls go out.</summary>
    public interface IHttpTransport
    {
        /// <summary>Sends the request (body null = GET) and returns the status code and reply text, whatever the
        /// status. Throws only when no reply came back at all (no connection, blocked by the browser, cancelled).</summary>
        Task<HttpReply> Send(string method, string url, string body, (string name, string value)[] headers, CancellationToken ct);

        /// <summary>Waits about this many milliseconds, or less if cancelled. Used before a retry.</summary>
        Task Pause(int milliseconds, CancellationToken ct);
    }

    /// <summary>What came back from one request.</summary>
    public sealed class HttpReply
    {
        public int Status;     // 200, 404, ...
        public string Text;    // the reply body, "" if none
    }

    public static class Http
    {
        /// <summary>How requests are sent. HttpClient by default; the Unity view swaps in UnityWebRequest while it runs.</summary>
        public static IHttpTransport Transport = new HttpClientTransport();

        public static Task<string> PostJson(string url, string body, CancellationToken ct, params (string name, string value)[] headers) =>
            Send("POST", url, body, ct, headers);

        public static Task<string> GetJson(string url, CancellationToken ct, params (string name, string value)[] headers) =>
            Send("GET", url, null, ct, headers);

        static async Task<string> Send(string method, string url, string body, CancellationToken ct, (string name, string value)[] headers)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("no endpoint configured");
            var reply = await Transport.Send(method, url, body, headers, ct).ConfigureAwait(false);
            if (reply.Status < 200 || reply.Status > 299)
                throw new HttpRequestException($"HTTP {reply.Status} from {Host(url)}: {LlmAgent.Snip(reply.Text, 300)}");
            return reply.Text ?? "";
        }

        public static string Join(string baseUrl, string path) => baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

        public static string Host(string url) { try { return new Uri(url).Host; } catch { return url; } }

        /// <summary>A token count from a reply's usage block; 0 when it is missing or not a whole number.</summary>
        public static int Int(JToken t) => t != null && t.Type == JTokenType.Integer ? (int)t : 0;
    }

    // ---------- The default transport: .NET's HttpClient (console harness, tests) ----------
    public sealed class HttpClientTransport : IHttpTransport
    {
        // One shared client, made on first use (so a WebGL build, which never uses it, never makes it). Per-call
        // time limits come from the runner's cancellation token, not from here.
        static HttpClient client;
        static HttpClient Client
        {
            get
            {
                if (client != null) return client;
                // Unity's runtime allows only 2 requests per website at once by default, so with 4+ seats on the same
                // provider the extra seats queue and time out without ever being sent. .NET (the console harness) ignores this.
                ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 32);
                client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                return client;
            }
        }

        public async Task<HttpReply> Send(string method, string url, string body, (string name, string value)[] headers, CancellationToken ct)
        {
            using (var req = new HttpRequestMessage(new HttpMethod(method), url))
            {
                if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                foreach (var (name, value) in headers)
                    if (value != null) req.Headers.TryAddWithoutValidation(name, value);
                using (var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false))
                    return new HttpReply { Status = (int)resp.StatusCode, Text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false) };
            }
        }

        public Task Pause(int milliseconds, CancellationToken ct) => Task.Delay(milliseconds, ct);
    }

    // ---------- OpenAI chat completions and everything compatible with it ----------
    public sealed class OpenAiChatClient : IChatClient
    {
        readonly SeatConfig cfg; readonly string key;
        public OpenAiChatClient(SeatConfig cfg, string apiKey) { this.cfg = cfg; key = apiKey; }

        public async Task<ChatReply> Complete(string system, string user, CancellationToken ct)
        {
            var body = new JObject
            {
                ["model"] = cfg.Model,
                ["messages"] = new JArray(
                    new JObject { ["role"] = "system", ["content"] = system },
                    new JObject { ["role"] = "user", ["content"] = user }),
            };
            if (cfg.Temperature >= 0) body["temperature"] = cfg.Temperature;
            if (cfg.MaxTokens > 0) body["max_tokens"] = cfg.MaxTokens;
            if (cfg.JsonMode) body["response_format"] = new JObject { ["type"] = "json_object" };
            if (!string.IsNullOrWhiteSpace(cfg.Effort))
            {
                // OpenRouter passes its own "reasoning" object on to every thinking model it hosts; the rest use OpenAI's field.
                if (cfg.Kind == "openrouter") body["reasoning"] = new JObject { ["effort"] = cfg.Effort };
                else body["reasoning_effort"] = cfg.Effort;
            }
            if (cfg.Kind == "openrouter") body["usage"] = new JObject { ["include"] = true };   // asks OpenRouter to put the call's cost in "usage"

            var text = await Http.PostJson(Http.Join(cfg.Endpoint, "chat/completions"), body.ToString(Formatting.None), ct,
                ("Authorization", key == null ? null : "Bearer " + key)).ConfigureAwait(false);

            var j = JObject.Parse(text);
            var err = j["error"]?["message"]?.ToString();
            if (err != null) throw new Exception(err);                        // some servers answer 200 with an error body
            var choice = j["choices"]?[0];
            if (choice == null) throw new Exception("no choices in reply: " + LlmAgent.Snip(text));
            var message = choice["message"];
            var content = message?["content"];
            string reply = content is JArray parts ? string.Concat(parts.Select(p => p["text"]?.ToString() ?? ""))   // multi-part content
                         : content?.ToString() ?? "";
            // A thinking model's reasoning comes back as "reasoning" (OpenRouter) or "reasoning_content" (DeepSeek's own
            // API, vLLM). OpenAI's own API sends none. Kept for the transcript.
            string thinking = Text(message?["reasoning"]) ?? Text(message?["reasoning_content"]);
            if (!string.IsNullOrWhiteSpace(reply)) return Billed(new ChatReply(reply, thinking), j);

            // Thinking models sometimes settle the answer while thinking and then stop without writing it out, so look
            // for the answer in the thinking before giving up. The marker in front shows in the transcript; the parser
            // skips past it.
            var rescued = Json.LastReply(thinking);
            if (rescued != null) return Billed(new ChatReply("[answer taken from the model's reasoning] " + rescued.ToString(Formatting.None), thinking), j);
            // Say why, so it can be retried and the transcript explains it.
            throw new Exception($"empty reply (finish_reason: {choice["finish_reason"]?.ToString() ?? "none"}; "
                + (thinking == null ? "no reasoning text)" : "reasoning: " + LlmAgent.Snip(thinking, 300) + ")"));
        }

        // Copies the model name and "usage": { prompt_tokens, completion_tokens, cost? } onto the reply.
        // completion_tokens already counts reasoning tokens. Only OpenRouter sends a cost.
        static ChatReply Billed(ChatReply r, JObject j)
        {
            r.Model = Text(j["model"]);
            var usage = j["usage"];
            r.InputTokens = Http.Int(usage?["prompt_tokens"]);
            r.OutputTokens = Http.Int(usage?["completion_tokens"]);
            if (usage?["cost"] is JValue cost && (cost.Type == JTokenType.Float || cost.Type == JTokenType.Integer)) r.Cost = (double)cost;
            return r;
        }

        // A JSON field as text, or null when it is missing, null or blank.
        static string Text(JToken t) => t == null || t.Type == JTokenType.Null || string.IsNullOrWhiteSpace(t.ToString()) ? null : t.ToString();
    }

    // ---------- Anthropic Messages API ----------
    public sealed class AnthropicClient : IChatClient
    {
        readonly SeatConfig cfg; readonly string key;
        public AnthropicClient(SeatConfig cfg, string apiKey) { this.cfg = cfg; key = apiKey; }

        // Anthropic refuses calls from a web page unless this header says the page knows what it is doing. In a web
        // build the key is the player's own and stays in their browser, so that is fine. Elsewhere it changes nothing.
        public static readonly (string, string) BrowserHeader = ("anthropic-dangerous-direct-browser-access", "true");

        // Models from before adaptive thinking (Claude 4.6). They reject the "adaptive" setting, so they get no thinking field.
        static readonly string[] OlderModels =
        {
            "claude-2", "claude-instant", "claude-3", "claude-opus-4-0", "claude-opus-4-1", "claude-opus-4-5", "claude-opus-4-2025",
            "claude-sonnet-4-0", "claude-sonnet-4-5", "claude-sonnet-4-2025", "claude-haiku-4-5",
        };

        public async Task<ChatReply> Complete(string system, string user, CancellationToken ct)
        {
            // Adaptive thinking with a readable summary, so the transcript shows what the model thought. Without
            // "summarized" current models think but send the thinking back empty. Temperature is not sent: current
            // models reject it. The system prompt is marked cacheable because it is identical for every phase.
            var body = new JObject
            {
                ["model"] = cfg.Model,
                ["max_tokens"] = cfg.MaxTokens > 0 ? cfg.MaxTokens : 8192,
                ["system"] = new JArray(new JObject { ["type"] = "text", ["text"] = system, ["cache_control"] = new JObject { ["type"] = "ephemeral" } }),
                ["messages"] = new JArray(new JObject { ["role"] = "user", ["content"] = user }),
            };
            if (!OlderModels.Any(old => (cfg.Model ?? "").StartsWith(old)))
                body["thinking"] = new JObject { ["type"] = "adaptive", ["display"] = "summarized" };
            if (!string.IsNullOrWhiteSpace(cfg.Effort)) body["output_config"] = new JObject { ["effort"] = cfg.Effort };

            var text = await Http.PostJson(Http.Join(cfg.Endpoint, "v1/messages"), body.ToString(Formatting.None), ct,
                ("x-api-key", key), ("anthropic-version", "2023-06-01"), BrowserHeader).ConfigureAwait(false);

            var j = JObject.Parse(text);
            if (j["type"]?.ToString() == "error") throw new Exception(j["error"]?["message"]?.ToString() ?? LlmAgent.Snip(text));
            if (j["stop_reason"]?.ToString() == "refusal")
                throw new Exception("model declined: " + (j["stop_details"]?["explanation"]?.ToString() ?? "no explanation given"));
            var parts = j["content"] as JArray;
            if (parts == null) throw new Exception("no content in reply: " + LlmAgent.Snip(text));
            string answer = string.Concat(parts.Where(p => p["type"]?.ToString() == "text").Select(p => p["text"]?.ToString() ?? ""));
            string thinking = string.Join("\n\n", parts.Where(p => p["type"]?.ToString() == "thinking")
                .Select(p => p["thinking"]?.ToString()).Where(t => !string.IsNullOrWhiteSpace(t)));
            // "usage": cached prompt tokens are counted apart from input_tokens, so add them back for the full prompt size.
            var usage = j["usage"];
            return new ChatReply(answer, thinking.Length > 0 ? thinking : null)
            {
                Model = j["model"]?.ToString(),
                InputTokens = Http.Int(usage?["input_tokens"]) + Http.Int(usage?["cache_creation_input_tokens"]) + Http.Int(usage?["cache_read_input_tokens"]),
                OutputTokens = Http.Int(usage?["output_tokens"]),
            };
        }
    }

    // ---------- Google Gemini ----------
    public sealed class GeminiClient : IChatClient
    {
        readonly SeatConfig cfg; readonly string key;
        public GeminiClient(SeatConfig cfg, string apiKey) { this.cfg = cfg; key = apiKey; }

        public async Task<ChatReply> Complete(string system, string user, CancellationToken ct)
        {
            var gen = new JObject { ["responseMimeType"] = "application/json" };
            if (cfg.Temperature >= 0) gen["temperature"] = cfg.Temperature;
            if (cfg.MaxTokens > 0) gen["maxOutputTokens"] = cfg.MaxTokens;
            // Ask for a summary of the model's thinking, for the transcript. Gemini 1.x and 2.0 do not think and reject it.
            string model = cfg.Model ?? "";
            if (!model.StartsWith("gemini-1") && !model.StartsWith("gemini-2.0"))
                gen["thinkingConfig"] = new JObject { ["includeThoughts"] = true };
            var body = new JObject
            {
                ["system_instruction"] = new JObject { ["parts"] = new JArray(new JObject { ["text"] = system }) },
                ["contents"] = new JArray(new JObject { ["role"] = "user", ["parts"] = new JArray(new JObject { ["text"] = user }) }),
                ["generationConfig"] = gen,
            };

            var url = Http.Join(cfg.Endpoint, $"v1beta/models/{cfg.Model}:generateContent");
            var text = await Http.PostJson(url, body.ToString(Formatting.None), ct, ("x-goog-api-key", key)).ConfigureAwait(false);

            var j = JObject.Parse(text);
            var err = j["error"]?["message"]?.ToString();
            if (err != null) throw new Exception(err);
            var parts = j["candidates"]?[0]?["content"]?["parts"] as JArray;
            if (parts == null)
            {
                var block = j["promptFeedback"]?["blockReason"]?.ToString() ?? j["candidates"]?[0]?["finishReason"]?.ToString();
                throw new Exception(block != null ? "no reply: " + block : "no candidates in reply: " + LlmAgent.Snip(text));
            }
            // Thought-summary parts carry "thought": true; the rest are the answer.
            bool IsThought(JToken p) => p["thought"]?.Type == JTokenType.Boolean && (bool)p["thought"];
            string answer = string.Concat(parts.Where(p => !IsThought(p)).Select(p => p["text"]?.ToString() ?? ""));
            string thinking = string.Join("\n\n", parts.Where(IsThought).Select(p => p["text"]?.ToString()).Where(t => !string.IsNullOrWhiteSpace(t)));
            // "usageMetadata": thinking tokens are billed as output but counted apart from the answer's.
            var usage = j["usageMetadata"];
            return new ChatReply(answer, thinking.Length > 0 ? thinking : null)
            {
                Model = j["modelVersion"]?.ToString(),
                InputTokens = Http.Int(usage?["promptTokenCount"]),
                OutputTokens = Http.Int(usage?["candidatesTokenCount"]) + Http.Int(usage?["thoughtsTokenCount"]),
            };
        }
    }
}
