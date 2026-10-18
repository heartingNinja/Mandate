// Mandate.Agents — seats that live outside the engine: hosted language models, local models, and bots in any
// language. Pure C# plus Newtonsoft.Json; no UnityEngine, so the same files compile in Unity and in the console.
//
// The shape:
//   SeatConfig     everything needed to seat one player (kind, model, endpoint, key). Inspector, JSON file, or code.
//   IChatClient    one method: system prompt + user prompt in, model text (and its thinking, if any) out. One
//                  implementation per API family.
//   LlmAgent       an IAgent that renders the PlayerView into a prompt, calls an IChatClient, parses the JSON reply
//                  through Mandate.Core.Wire, and carries the model's private notes from phase to phase.
//   HttpBotAgent   an IAgent that POSTs the wire-format view to a URL and expects the wire-format response back.
//   Seats.Make     the factory: SeatConfig -> IAgent, with sensible defaults per kind (see Mandate.Agents.Seats.cs).
//
// No vendor is baked into the engine. GameRunner only ever sees IAgent.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Mandate.Core;
using Mandate.Sim;

namespace Mandate.Agents
{
    // ---------- Seat configuration ----------
    // Plain data. Seats.Make fills in the blanks (endpoint, model, key variable) from the kind's defaults.
    public sealed class SeatConfig
    {
        public string Kind = "greedy";     // greedy | random | openai | openrouter | groq | ollama | anthropic | gemini | http ... (Seats.Kinds)
        public string Name;                // display name on the player card; null = kind:model
        public string Model;               // model id; null = the kind's default
        public string Endpoint;            // base URL; null = the kind's default. http kind: the full URL to POST to
        public string ApiKey;              // literal key. Prefer ApiKeyEnv so keys never land in a scene or a JSON file
        public string ApiKeyEnv;           // name of the environment variable holding the key; null = the kind's default
        public string Persona;             // extra system-prompt text: a personality, a play style, a language
        public string Effort;              // reasoning effort where the provider supports it (low | medium | high); null = provider default
        public float Temperature = -1f;    // < 0 = provider default. Not sent to Anthropic (current models reject it)
        public int MaxTokens = 0;          // 0 = provider default (Anthropic requires a value; 8192 is used)
        public bool JsonMode = false;      // OpenAI-compatible only: send response_format json_object. Not every server supports it
        public string Note;                // free text, ignored; lets a seats.json file carry comments

        public string Label() =>
            !string.IsNullOrWhiteSpace(Name) ? Name.Trim() :
            string.IsNullOrWhiteSpace(Model) ? Kind : Kind + ":" + Model;
    }

    // ---------- JSON ----------
    // camelCase on the wire, nulls omitted, unknown fields ignored. The same settings everywhere so the wire format
    // that Unity emits is byte-for-byte what the console and the future server emit.
    public static class Json
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Serialize(object o, bool indented = false) =>
            JsonConvert.SerializeObject(o, indented ? Formatting.Indented : Formatting.None, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>Parses the JSON object in a reply, however it is wrapped: bare, in a ```json fence, or inside prose.
        /// Small typos are repaired (see Repair) before falling back to objects nested inside the reply, so a cut-off
        /// reply gives back the whole object, not just the complete "action" inside it.</summary>
        public static JObject FindObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim();
            var repaired = Repair(t);
            return Whole(t) ?? Whole(repaired) ?? FirstSpan(repaired) ?? FirstSpan(t);
        }

        static JObject Whole(string t)
        {
            // 1. A fenced block, if there is one.
            int fence = t.IndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                int start = t.IndexOf('\n', fence);
                int end = start < 0 ? -1 : t.IndexOf("```", start, StringComparison.Ordinal);
                if (start >= 0 && end > start)
                {
                    var inner = TryParse(t.Substring(start + 1, end - start - 1));
                    if (inner != null) return inner;
                }
            }
            // 2. The whole reply.
            return TryParse(t);
        }

        // 3. Every balanced {...} span, first one that parses wins. Handles prose before and after, and braces in prose.
        static JObject FirstSpan(string t) => Spans(t).FirstOrDefault();

        // Every balanced {...} span that parses, in order of where it starts (so an object comes before the ones inside it).
        static IEnumerable<JObject> Spans(string t)
        {
            for (int i = t.IndexOf('{'); i >= 0; i = t.IndexOf('{', i + 1))
            {
                int depth = 0; bool inString = false;
                for (int j = i; j < t.Length; j++)
                {
                    char c = t[j];
                    if (inString) { if (c == '\\') j++; else if (c == '"') inString = false; continue; }
                    if (c == '"') inString = true;
                    else if (c == '{') depth++;
                    else if (c == '}' && --depth == 0)
                    {
                        var span = TryParse(t.Substring(i, j - i + 1));
                        if (span != null) yield return span;
                        break;
                    }
                }
            }
        }

        static readonly string[] ReplyFields = { "draft", "messages", "offers", "beliefs", "action", "accept", "guesses", "notes" };

        /// <summary>The LAST object in some text that parses and has a reply field ("action", "guesses", ...). For a thinking
        /// model's reasoning, where the settled answer comes last and earlier objects are drafts or quotes of the prompt.</summary>
        public static JObject LastReply(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            JObject last = null;
            foreach (var o in Spans(text)) if (ReplyFields.Any(f => o[f] != null)) last = o;
            return last;
        }

        // Three slips models make that lose a whole answer, fixed only outside strings so text inside notes is untouched:
        //   a stray quote after a number, as in {"to":6"}, which can never start a valid string there;
        //   a stray quote straight after a finished string, as in "notes": "...declined)."" } (GLM, game 5), same reason;
        //   a reply cut off part-way (token limit), closed by ending the open string and every open { and [.
        // The move usually comes before the notes, so a cut-off reply still has a complete action.
        static string Repair(string t)
        {
            var sb = new StringBuilder(t.Length + 8);
            var open = new Stack<char>();
            bool inString = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < t.Length) sb.Append(t[++i]);
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"')
                {
                    int prev = sb.Length - 1; while (prev >= 0 && char.IsWhiteSpace(sb[prev])) prev--;
                    int next = i + 1; while (next < t.Length && char.IsWhiteSpace(t[next])) next++;
                    bool afterValue = prev >= 0 && (char.IsDigit(sb[prev]) || sb[prev] == '"');   // a number, or a string that just closed
                    if (afterValue && next < t.Length && ",}]".IndexOf(t[next]) >= 0) continue;   // drop it
                    inString = true;
                }
                else if (c == '{') open.Push('}');
                else if (c == '[') open.Push(']');
                else if ((c == '}' || c == ']') && open.Count > 0) open.Pop();
                sb.Append(c);
            }
            if (inString) sb.Append('"');
            while (open.Count > 0) sb.Append(open.Pop());
            return sb.ToString();
        }

        /// <summary>FindObject, then bind to T. Null when there is no object or it does not bind.</summary>
        public static T Extract<T>(string text) where T : class
        {
            var o = FindObject(text);
            if (o == null) return null;
            try { return o.ToObject<T>(Serializer); } catch (JsonException) { return null; }
        }

        static JObject TryParse(string s)
        {
            try { return JObject.Parse(s); } catch (JsonException) { return null; }
        }
    }

    // ---------- One chat call ----------
    public interface IChatClient
    {
        /// <summary>System prompt and user prompt in; the model's reply out. Throw on transport or API errors.</summary>
        Task<ChatReply> Complete(string system, string user, CancellationToken ct);
    }

    /// <summary>What one chat call returns: the answer, plus the model's thinking when the provider sends it back.</summary>
    public sealed class ChatReply
    {
        public string Text;       // the answer the game parses
        public string Thinking;   // the model's reasoning, or a summary of it; null when the provider returns none (OpenAI, relay seats)

        public ChatReply(string text, string thinking = null) { Text = text; Thinking = thinking; }
    }

    // ---------- A language model in a seat ----------
    public sealed class LlmAgent : IAgent
    {
        public readonly SeatConfig Config;
        public readonly IChatClient Client;
        public const int MaxNoteChars = 3000;

        /// <summary>The seat's own scratchpad, rewritten by the model each phase. Private to this seat; revealed after the game.</summary>
        public string Notes { get; private set; } = "";

        /// <summary>Every prompt and reply, in order. For debugging prompts and for reading a game back afterwards.</summary>
        public readonly List<Exchange> Transcript = new List<Exchange>();

        public sealed class Exchange
        {
            public int Round; public string Phase; public string System; public string User; public string Reply; public string Error; public double Seconds;
            public string Thinking;   // what the model thought before replying, when its provider returns it. Transcript only; never shown to other seats
        }

        public LlmAgent(SeatConfig config, IChatClient client) { Config = config; Client = client; }
        public string Kind => Config.Label();

        public async Task<PhaseResponse> Respond(PlayerView v, CancellationToken ct)
        {
            string system = Prompting.SystemPrompt(v, Config);
            string user = Prompting.UserPrompt(v, Notes);
            var ex = new Exchange { Round = v.Round, Phase = Wire.PhaseName(v.Phase), System = system, User = user };
            var sw = Stopwatch.StartNew();
            PhaseResponse result;
            try
            {
                var reply = await CompleteWithRetry(system, user, ct).ConfigureAwait(false);
                ex.Reply = reply.Text;
                ex.Thinking = reply.Thinking;
                var obj = Json.FindObject(ex.Reply);
                if (obj == null) result = new PhaseResponse { Error = "reply was not JSON: " + Snip(ex.Reply) };
                else
                {
                    Loosen(obj);
                    ResponseDto dto;
                    try { dto = obj.ToObject<ResponseDto>(Json.Serializer); }
                    catch (JsonException e) { dto = null; ex.Error = e.Message; }
                    // Beliefs are optional and never scored, so a malformed one must not cost the seat its move.
                    if (dto == null && obj.Remove("beliefs"))
                    {
                        try { dto = obj.ToObject<ResponseDto>(Json.Serializer); }
                        catch (JsonException e) { dto = null; ex.Error = e.Message; }
                    }
                    if (dto == null) result = new PhaseResponse { Error = "reply did not match the response format: " + (ex.Error ?? Snip(ex.Reply)) };
                    else
                    {
                        result = Wire.ToResponse(dto, v.Phase);
                        if (!string.IsNullOrWhiteSpace(dto.Notes))
                            Notes = dto.Notes.Length > MaxNoteChars ? dto.Notes.Substring(0, MaxNoteChars) : dto.Notes;
                    }
                }
            }
            catch (OperationCanceledException) { result = new PhaseResponse { Error = "timed out" }; }
            catch (Exception) when (ct.IsCancellationRequested) { result = new PhaseResponse { Error = "timed out" }; }   // Unity reports a cancelled request as "The request was aborted"
            catch (Exception e) { result = new PhaseResponse { Error = e.Message }; }
            result.Notes = Notes;
            ex.Error = result.Error;
            ex.Seconds = sw.Elapsed.TotalSeconds;
            lock (Transcript) Transcript.Add(ex);
            return result;
        }

        // A failed call (busy server, dropped connection, empty reply) is tried once more if the phase still has time.
        // Running out of time is not retried: the runner's cancellation ends the phase for this seat.
        // The retry is not the identical request: a model that stopped without answering tends to do it again on the
        // same input (DeepSeek did, twice, on the guess phase), so it is told plainly what is missing.
        public const string RetryNudge = "\n\nYOUR LAST REPLY WAS EMPTY OR FAILED. Reply now with ONLY the JSON object for this phase, nothing else.";

        async Task<ChatReply> CompleteWithRetry(string system, string user, CancellationToken ct)
        {
            try { return await Client.Complete(system, user, ct).ConfigureAwait(false); }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                await Http.Transport.Pause(1000, ct).ConfigureAwait(false);   // not Task.Delay: browser builds have no timer thread
                return await Client.Complete(system, user + RetryNudge, ct).ConfigureAwait(false);
            }
        }

        // Common ways models bend the format that are cheap to accept: "action": "pass", "messages": ["hi"],
        // and a belief given as one objective id ("P2": "fortress") or a list of ids instead of id -> probability.
        static void Loosen(JObject o)
        {
            if (o["beliefs"]?["objectiveBeliefs"] is JObject ob)
                foreach (var p in ob.Properties().ToList())
                {
                    if (p.Value is JValue one && one.Type == JTokenType.String) p.Value = new JObject { [one.ToString()] = 1.0 };
                    else if (p.Value is JArray arr && arr.Count > 0 && arr.All(i => i.Type == JTokenType.String))
                    {
                        var ids = arr.Select(i => i.ToString()).Distinct().ToList();
                        p.Value = new JObject(ids.Select(id => new JProperty(id, 1.0 / ids.Count)));
                    }
                }
            if (o["action"] is JValue av && av.Type == JTokenType.String) o["action"] = new JObject { ["type"] = av.ToString() };
            if (o["messages"] is JArray ms)
                for (int i = 0; i < ms.Count; i++)
                    if (ms[i] is JValue mv && mv.Type == JTokenType.String) ms[i] = new JObject { ["text"] = mv.ToString() };
            if (o["draft"] is JObject dj && dj["id"] != null) o["draft"] = dj["id"].ToString();
            // One offer instead of a list ("offer": {...} or "offers": {...}), and "to" in place of "partner".
            if (o["offer"] is JObject single && o["offers"] == null) { o["offers"] = new JArray(single); o.Remove("offer"); }
            if (o["offers"] is JObject lone) o["offers"] = new JArray(lone);
            if (o["offers"] is JArray offers)
                foreach (var of in offers.OfType<JObject>())
                    if (of["partner"] == null && of["to"] != null) of["partner"] = of["to"];
            // "accept": "P2", "accepts": [...], or a list of offer objects ({"from": "P2"}) instead of seat names.
            foreach (var alias in new[] { "accepts", "acceptOffers", "accepted" })
                if (o["accept"] == null && o[alias] != null) { o["accept"] = o[alias]; o.Remove(alias); }
            if (o["accept"] is JValue av1 && av1.Type == JTokenType.String) o["accept"] = new JArray(av1.ToString());
            if (o["accept"] is JObject ao) o["accept"] = new JArray(ao);
            if (o["accept"] is JArray acc)
                for (int i = 0; i < acc.Count; i++)
                    if (acc[i] is JObject item && (item["from"] ?? item["partner"] ?? item["player"]) is JToken who) acc[i] = who.ToString();
        }

        public static string Snip(string s, int max = 160)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            s = s.Replace("\r", "").Replace('\n', ' ');
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }

    // ---------- Bring your own bot ----------
    // POSTs the wire-format view (ViewDto JSON) to a URL and expects wire-format ResponseDto JSON back.
    // Any language, any host. If a key is configured it is sent as a bearer token.
    public sealed class HttpBotAgent : IAgent
    {
        public readonly SeatConfig Config;
        readonly string key;

        public HttpBotAgent(SeatConfig config, string apiKey) { Config = config; key = apiKey; }
        public string Kind => Config.Label();

        public async Task<PhaseResponse> Respond(PlayerView v, CancellationToken ct)
        {
            try
            {
                var body = Json.Serialize(Wire.ToDto(v));
                var text = await Http.PostJson(Config.Endpoint, body, ct, ("Authorization", key == null ? null : "Bearer " + key)).ConfigureAwait(false);
                var dto = Json.Extract<ResponseDto>(text);
                return dto == null
                    ? new PhaseResponse { Error = "bot reply was not a response object: " + LlmAgent.Snip(text) }
                    : Wire.ToResponse(dto, v.Phase);
            }
            catch (OperationCanceledException) { return new PhaseResponse { Error = "timed out" }; }
            catch (Exception) when (ct.IsCancellationRequested) { return new PhaseResponse { Error = "timed out" }; }
            catch (Exception e) { return new PhaseResponse { Error = e.Message }; }
        }
    }
}
