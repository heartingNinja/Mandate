// Mandate.Agents.Tests — plain-assert tests for the adapter layer, same style as Mandate.Tests.
//
// No API key is needed: a tiny HttpListener stands in for the providers and for a bring-your-own bot, so every
// adapter is exercised end to end (real sockets, real JSON) in about a second. Console: Tests.RunAll().
// Unity: the Mandate menu and the NUnit wrapper in Assets/Mandate/Tests enumerate All().

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Mandate.Core;
using Mandate.Sim;
using Mandate.Agents;

namespace Mandate.AgentTests
{
    public static class Tests
    {
        public static Action<string> Out = Console.WriteLine;
        static void Check(bool cond, string name) { Out((cond ? "PASS " : "FAIL ") + name); if (!cond) throw new Exception(name); }
        static readonly List<string> Names = new List<string> { "P1", "P2", "P3", "P4" };

        public static IEnumerable<(string name, Action run)> All()
        {
            yield return (nameof(JsonFindsObjectInProse), JsonFindsObjectInProse);
            yield return (nameof(ViewDtoSerialisesCamelCase), ViewDtoSerialisesCamelCase);
            yield return (nameof(SeatFactoryResolvesKinds), SeatFactoryResolvesKinds);
            yield return (nameof(HttpBotPlaysAFullGame), HttpBotPlaysAFullGame);
            yield return (nameof(OpenAiSeatSpeaksChatCompletions), OpenAiSeatSpeaksChatCompletions);
            yield return (nameof(AnthropicSeatSpeaksMessages), AnthropicSeatSpeaksMessages);
            yield return (nameof(GeminiSeatSpeaksGenerateContent), GeminiSeatSpeaksGenerateContent);
            yield return (nameof(BadRepliesBecomePassesAndKeepNotes), BadRepliesBecomePassesAndKeepNotes);
            yield return (nameof(TradeOffersReachThePromptAndLooseOffersParse), TradeOffersReachThePromptAndLooseOffersParse);
            yield return (nameof(PromptShowsObjectiveStatusAndFinalRound), PromptShowsObjectiveStatusAndFinalRound);
            yield return (nameof(FailedCallsRetryAndBeliefsNeverCostAMove), FailedCallsRetryAndBeliefsNeverCostAMove);
            yield return (nameof(AnswerOnlyInReasoningIsRescued), AnswerOnlyInReasoningIsRescued);
            yield return (nameof(LlmSeatsPlayAFullGame), LlmSeatsPlayAFullGame);
            yield return (nameof(ModelListsComeBackSortedForEveryFamily), ModelListsComeBackSortedForEveryFamily);
        }

        public static void RunAll()
        {
            int n = 0;
            foreach (var (name, run) in All()) { run(); n++; }
            Out($"all {n} agent tests passed");
        }

        // ---------- a local HTTP server standing in for a provider or a remote bot ----------
        sealed class FakeServer : IDisposable
        {
            public sealed class Request { public string Path; public string Body; public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }

            readonly HttpListener listener = new HttpListener();
            readonly Func<Request, string> handler;      // return "STATUS 500 body" to answer with another status code
            public readonly string Url;
            public readonly List<Request> Requests = new List<Request>();

            public FakeServer(Func<Request, string> handler)
            {
                this.handler = handler;
                Url = $"http://127.0.0.1:{FreePort()}/";
                listener.Prefixes.Add(Url);
                listener.Start();
                Task.Run(Loop);
            }

            async Task Loop()
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await listener.GetContextAsync().ConfigureAwait(false); } catch { return; }
                    _ = Task.Run(() => Handle(ctx));
                }
            }

            void Handle(HttpListenerContext ctx)
            {
                int status = 200; string reply;
                try
                {
                    var req = new Request { Path = ctx.Request.Url.AbsolutePath };
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) req.Body = reader.ReadToEnd();
                    foreach (string h in ctx.Request.Headers.AllKeys) req.Headers[h] = ctx.Request.Headers[h];
                    lock (Requests) Requests.Add(req);
                    reply = handler(req) ?? "";
                    if (reply.StartsWith("STATUS ")) { status = int.Parse(reply.Substring(7, 3)); reply = reply.Substring(11); }
                }
                catch (Exception e) { status = 500; reply = e.Message; }
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(reply);
                    ctx.Response.StatusCode = status;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }
                catch { }
                finally { try { ctx.Response.Close(); } catch { } }
            }

            public Request Single() { lock (Requests) return Requests.Single(); }
            public int Count { get { lock (Requests) return Requests.Count; } }

            static int FreePort()
            {
                var l = new TcpListener(IPAddress.Loopback, 0);
                l.Start();
                int p = ((IPEndPoint)l.LocalEndpoint).Port;
                l.Stop();
                return p;
            }

            public void Dispose() { try { listener.Stop(); listener.Close(); } catch { } }
        }

        static string ChatReply(string content, string reasoning = null, bool billed = false)
        {
            var message = new JObject { ["role"] = "assistant", ["content"] = content };
            if (reasoning != null) message["reasoning"] = reasoning;   // OpenRouter's field for a thinking model's reasoning
            var reply = new JObject { ["choices"] = new JArray(new JObject { ["message"] = message }) };
            if (billed)   // what OpenRouter sends back: the real model behind an alias, and usage with a cost
            {
                reply["model"] = "fake-model-20261001";
                reply["usage"] = new JObject { ["prompt_tokens"] = 1200, ["completion_tokens"] = 340, ["cost"] = 0.0021 };
            }
            return reply.ToString();
        }

        static Referee InActPhase(int seed = 9)
        {
            var r = new Referee("t", Names, seed);
            r.EndDraft(); r.EndNegotiation();
            return r;
        }

        // ---------- JSON ----------
        static void JsonFindsObjectInProse()
        {
            var fenced = Json.FindObject("Sure! Here is my move:\n```json\n{\"action\": {\"type\": \"pass\"}, \"notes\": \"ok\"}\n```\nGood luck everyone.");
            Check(fenced != null && fenced["notes"].ToString() == "ok", "fenced json found");
            var leading = Json.FindObject("{\"a\":1} trailing text with } stray braces {");
            Check(leading != null && (int)leading["a"] == 1, "leading object found despite trailing junk");
            var embedded = Json.FindObject("I'll pass this round. {\"action\":{\"type\":\"pass\"}} Thanks.");
            Check(embedded != null && embedded["action"]["type"].ToString() == "pass", "object inside prose found");
            Check(Json.FindObject("no json here at all") == null, "no object is null");
            var dto = Json.Extract<ResponseDto>("{\"action\":{\"type\":\"move\",\"from\":1,\"to\":2},\"messages\":[{\"to\":\"P2\",\"text\":\"hi\"}],\"guesses\":{\"P2\":\"trader\"},\"extra\":true}");
            Check(dto.Action.From == 1 && dto.Messages[0].To == "P2" && dto.Guesses["P2"] == "trader", "response dto binds, unknown fields ignored");

            // The real R2 reply from GPT-OSS that cost it a move: a stray quote after the 6.
            var stray = Json.FindObject("{\"action\":{\"type\":\"move\",\"from\":5,\"to\":6\"},\"notes\":\"Capture node 6\"}");
            Check(stray != null && (int)stray["action"]["to"] == 6 && stray["notes"].ToString() == "Capture node 6", "stray quote after a number repaired");
            // GLM's round 8 reply in game 5: an extra quote after the notes. The fallback used to return the inner action
            // object on its own, so the move was lost as "reply had no action".
            var doubled = Json.FindObject("{\"action\": {\"type\": \"fortify\", \"node\": 2}, \"notes\": \"No trades accepted (declined).\"\"}");
            Check(doubled != null && doubled["action"]?["type"]?.ToString() == "fortify" && doubled["notes"].ToString() == "No trades accepted (declined).", "stray quote after a string repaired, whole reply kept");
            var fine = Json.FindObject("{\"messages\": [{\"to\": \"P2\", \"text\": \"\"}, {\"text\": \"say \\\"hi\\\"\"}], \"notes\": \"\"}");
            Check(fine != null && fine["messages"][1]["text"].ToString() == "say \"hi\"" && fine["notes"].ToString() == "", "empty strings and escaped quotes are left alone");
            var quoted = Json.FindObject("{\"notes\":\"I said \\\"take 6\\\", then left\",\"action\":{\"type\":\"pass\"}}");
            Check(quoted != null && quoted["notes"].ToString() == "I said \"take 6\", then left", "valid quotes inside strings untouched");
            var cut = Json.FindObject("{\"action\":{\"type\":\"fortify\",\"node\":2},\"notes\":\"Plan: hold 2 and then");
            Check(cut != null && (int)cut["action"]["node"] == 2, "reply cut off mid-notes still yields its action");
        }

        static void ViewDtoSerialisesCamelCase()
        {
            var r = InActPhase(5);
            r.State.TradesCompleted["P1"] = 1;
            var json = Json.Serialize(Wire.ToDto(r.ViewFor("P1")));
            Check(json.Contains("\"legalActions\"") && json.Contains("\"gameId\"") && json.Contains("\"version\":" + WireFormat.Version), "camelCase field names, version present");
            Check(json.Contains("\"tradesCompleted\":{\"P1\":1}"), "player ids are not renamed");
            Check(!json.Contains("Item1") && !json.Contains("\"objectiveId\":null"), "no tuple leakage, nulls omitted");
            var back = Json.Deserialize<ViewDto>(json);
            Check(back.You == "P1" && back.LegalActions.Count > 1 && back.Board.Count == r.State.Nodes.Count && back.Rules.MaxRounds == Rules.MaxRounds && back.Rules.MinRounds == Rules.MinRounds, "view dto round-trips");
        }

        // ---------- factory ----------
        static void SeatFactoryResolvesKinds()
        {
            Check(Seats.Make(new SeatConfig { Kind = "greedy" }, 1) is BotAgent g && g.Kind == "greedy", "greedy is a scripted bot");
            Check(Seats.Make(new SeatConfig { Kind = "Random" }, 1).Kind == "random", "kind is case-insensitive");

            var or = Seats.Make(new SeatConfig { Kind = "openrouter", ApiKey = "x" }, 1) as LlmAgent;
            Check(or != null && or.Client is OpenAiChatClient && or.Config.Endpoint == "https://openrouter.ai/api/v1" && or.Config.Model.EndsWith(":free"), "openrouter defaults to a free model over the OpenAI-compatible client");
            var claude = Seats.Make(new SeatConfig { Kind = "claude", ApiKey = "x" }, 1) as LlmAgent;
            Check(claude != null && claude.Client is AnthropicClient && claude.Config.Model == "claude-opus-5", "claude alias -> anthropic client, opus 5");
            var google = Seats.Make(new SeatConfig { Kind = "google", ApiKey = "x" }, 1) as LlmAgent;
            Check(google != null && google.Client is GeminiClient && google.Config.Endpoint.Contains("googleapis"), "google alias -> gemini client");
            Check(Seats.Make(new SeatConfig { Kind = "ollama" }, 1) is LlmAgent local && local.Config.Endpoint.Contains("11434"), "ollama needs no key");
            Check(Seats.Make(new SeatConfig { Kind = "http", Endpoint = "http://localhost:1/x" }, 1) is HttpBotAgent, "http kind is a bring-your-own bot");
            Check(Seats.Make(new SeatConfig { Kind = "openai", ApiKey = "x", Name = "GPT" }, 1).Kind == "GPT", "name becomes the label");

            string err = null;
            try { Seats.Make(new SeatConfig { Kind = "openai", ApiKeyEnv = "MANDATE_NO_SUCH_VARIABLE" }, 1); } catch (Exception e) { err = e.Message; }
            Check(err != null && err.Contains("MANDATE_NO_SUCH_VARIABLE"), "missing key names the environment variable");
            err = null;
            try { Seats.Make(new SeatConfig { Kind = "warp-drive" }, 1); } catch (Exception e) { err = e.Message; }
            Check(err != null && err.Contains("warp-drive"), "unknown kind is a clear error");
            err = null;
            try { Seats.Make(new SeatConfig { Kind = "human" }, 1); } catch (Exception e) { err = e.Message; }
            Check(err != null, "human seats come from the host, not the factory");
        }

        // ---------- bring your own bot ----------
        // A bot "in another language": reads the view JSON, answers with the response JSON. Nothing else.
        static string ScriptedBot(FakeServer.Request req)
        {
            var v = Json.Deserialize<ViewDto>(req.Body);
            var r = new ResponseDto { Notes = "seen round " + v.Round };
            switch (v.Phase)
            {
                case "draft": r.Draft = v.DraftOptions[0].Id; break;
                case "negotiate": r.Messages = new List<MessageDto> { new MessageDto { Text = "hello from " + v.You }, new MessageDto { To = v.Left, Text = "psst" } }; break;
                case "act": r.Action = v.LegalActions.FirstOrDefault(a => a.Type == "move") ?? v.LegalActions[0]; break;
                case "guess": r.Guesses = v.Players.Where(p => p != v.You).ToDictionary(p => p, p => v.GuessCandidates[0].Id); break;
            }
            return Json.Serialize(r);
        }

        static void HttpBotPlaysAFullGame()
        {
            using (var server = new FakeServer(ScriptedBot))
            {
                var seats = Names.ToDictionary(p => p, p => (IAgent)new HttpBotAgent(new SeatConfig { Kind = "http", Endpoint = server.Url + "bot" }, "secret"));
                var run = new GameRunner(new Referee("t", Names, 3), seats) { PhaseTimeout = TimeSpan.FromSeconds(20) };
                run.RunToEnd();
                Check(run.Finished, "http bots finish a game");
                Check(run.State.ActionLog.All(a => a.wasLegal), "http bots only ever submit legal actions");
                Check(run.State.Messages.Count(m => m.To == null) == 4 * run.State.LastRound, "one public message per bot per round");
                Check(run.State.Messages.Count(m => m.To != null) == 4 * run.State.LastRound, "one private message per bot per round");
                Check(run.State.Guesses.Count == 4 && run.State.Guesses["P1"].Count == 3, "guesses submitted");
                Check(run.LastResponses.Values.All(r => r.Error == null) && run.LastResponses["P1"].Notes == "seen round " + run.State.LastRound, "no errors; bot notes come back");
                lock (server.Requests)
                {
                    Check(server.Requests.Count == 4 * (1 + 2 * run.State.LastRound + 1), "one request per seat per phase");
                    Check(server.Requests.All(q => q.Path == "/bot" && q.Headers["Authorization"] == "Bearer secret"), "posted to the endpoint with the key");
                    Check(server.Requests.All(q => q.Body.Contains("\"version\":" + WireFormat.Version)), "every request carries the wire version");
                }
            }
        }

        // ---------- provider adapters ----------
        static void OpenAiSeatSpeaksChatCompletions()
        {
            using (var server = new FakeServer(q => ChatReply("Thinking...\n```json\n{\"action\":{\"type\":\"pass\"},\"notes\":\"n1\"}\n```", "P2 looks weak; passing keeps my walls.", billed: true)))
            {
                var seat = Seats.Make(new SeatConfig { Kind = "openai", Endpoint = server.Url + "v1", ApiKey = "k1", Model = "fake-model", JsonMode = true, Temperature = 0.5f }, 0);
                var r = seat.Respond(InActPhase().ViewFor("P1"), CancellationToken.None).Result;
                Check(r.Error == null && r.Action != null && r.Action.Type == ActionType.Pass && r.Notes == "n1", "chat completion parsed through fences");
                var ex = ((LlmAgent)seat).Transcript[0];
                Check(ex.Thinking == "P2 looks weak; passing keeps my walls.", "reasoning kept in the transcript");
                Check(ex.Model == "fake-model-20261001" && ex.InputTokens == 1200 && ex.OutputTokens == 340 && Math.Abs(ex.Cost - 0.0021) < 1e-9, "model, tokens and cost read from usage");
                var q = server.Single();
                Check(q.Path == "/v1/chat/completions" && q.Headers["Authorization"] == "Bearer k1", "posted to /chat/completions with bearer key");
                var body = JObject.Parse(q.Body);
                Check(body["model"].ToString() == "fake-model" && body["messages"].Count() == 2 && body["messages"][0]["role"].ToString() == "system", "system + user messages");
                Check(body["response_format"]["type"].ToString() == "json_object" && (float)body["temperature"] == 0.5f, "json mode and temperature sent when configured");
                Check(body["messages"][1]["content"].ToString().Contains("LEGAL ACTIONS") && body["messages"][0]["content"].ToString().Contains("RESPONSE FORMAT"), "prompts carry legal actions and the format");
            }
        }

        static void ModelListsComeBackSortedForEveryFamily()
        {
            using (var server = new FakeServer(q =>
                q.Path == "/v1/models" && q.Headers.ContainsKey("anthropic-version")
                    ? "{\"data\":[{\"id\":\"claude-b\",\"display_name\":\"Claude B\"},{\"id\":\"claude-a\"}]}"
                : q.Path == "/v1beta/models"
                    ? "{\"models\":[{\"name\":\"models/gem-text\",\"supportedGenerationMethods\":[\"generateContent\"]},{\"name\":\"models/gem-embed\",\"supportedGenerationMethods\":[\"embedContent\"]}]}"
                : q.Path == "/api/v1/models"
                    ? "{\"data\":[" +
                      "{\"id\":\"big/pricey\",\"name\":\"Pricey\",\"pricing\":{\"prompt\":\"0.000003\",\"completion\":\"0.000015\"}}," +
                      "{\"id\":\"router/auto\",\"pricing\":{\"prompt\":\"-1\",\"completion\":\"-1\"}}," +
                      "{\"id\":\"small/cheap\",\"pricing\":{\"prompt\":\"0.0000001\",\"completion\":\"0.0000004\"}}," +
                      "{\"id\":\"tiny/model:free\",\"pricing\":{\"prompt\":\"0\",\"completion\":\"0\"}}]}"
                : "STATUS 404 {}"))
            {
                // OpenRouter shape, no key: free first, then cheapest, unknown price last.
                var or = ModelCatalog.Fetch(new SeatConfig { Kind = "openrouter", Endpoint = server.Url + "api/v1", ApiKeyEnv = "MANDATE_NO_SUCH_VARIABLE" }, CancellationToken.None).Result;
                Check(string.Join(",", or.Select(m => m.Id)) == "tiny/model:free,small/cheap,big/pricey,router/auto", "openrouter list sorted free, cheap, pricey, unknown");
                Check(or[0].Free && Math.Abs(or[2].InPerMillion - 3) < 1e-9 && Math.Abs(or[2].OutPerMillion - 15) < 1e-9 && !or[3].PriceKnown, "prices converted to dollars per million tokens");
                Check(server.Requests.Last().Headers.ContainsKey("Authorization") == false, "no key, no Authorization header");

                var claude = ModelCatalog.Fetch(new SeatConfig { Kind = "anthropic", Endpoint = server.Url, ApiKey = "k2" }, CancellationToken.None).Result;
                Check(string.Join(",", claude.Select(m => m.Id)) == "claude-a,claude-b" && server.Requests.Last().Headers["x-api-key"] == "k2", "anthropic list read with the key");

                var gem = ModelCatalog.Fetch(new SeatConfig { Kind = "gemini", Endpoint = server.Url, ApiKey = "k3" }, CancellationToken.None).Result;
                Check(gem.Count == 1 && gem[0].Id == "gem-text", "gemini list keeps text models and drops the models/ prefix");

                string err = null;
                try { ModelCatalog.Fetch(new SeatConfig { Kind = "anthropic", Endpoint = server.Url, ApiKeyEnv = "MANDATE_NO_SUCH_VARIABLE" }, CancellationToken.None).Wait(); }
                catch (AggregateException e) { err = e.InnerException?.Message; }
                Check(err != null && err.Contains("paste the key"), "anthropic without a key asks for it");
            }
        }

        static void AnthropicSeatSpeaksMessages()
        {
            var reply = new JObject
            {
                ["type"] = "message", ["stop_reason"] = "end_turn", ["model"] = "fake-claude",
                ["usage"] = new JObject { ["input_tokens"] = 50, ["cache_creation_input_tokens"] = 900, ["cache_read_input_tokens"] = 0, ["output_tokens"] = 210 },
                ["content"] = new JArray(
                    new JObject { ["type"] = "thinking", ["thinking"] = "OPT fits my start." },
                    new JObject { ["type"] = "text", ["text"] = "{\"draft\":\"OPT\",\"notes\":\"n2\"}" }),
            }.ToString();
            using (var server = new FakeServer(q => reply))
            {
                var seat = Seats.Make(new SeatConfig { Kind = "anthropic", Endpoint = server.Url, ApiKey = "k2", Model = "fake-claude", Effort = "low", Temperature = 0.9f }, 0);
                var r = seat.Respond(new Referee("t", Names, 4).ViewFor("P1"), CancellationToken.None).Result;   // draft phase
                Check(r.Error == null && r.Draft == "OPT" && r.Notes == "n2", "messages reply parsed, thinking block not taken as the answer");
                var ex = ((LlmAgent)seat).Transcript[0];
                Check(ex.Thinking == "OPT fits my start.", "thinking summary kept in the transcript");
                Check(ex.Model == "fake-claude" && ex.InputTokens == 950 && ex.OutputTokens == 210 && ex.Cost < 0, "cached tokens counted in the prompt; no cost from anthropic");
                var q = server.Single();
                Check(q.Path == "/v1/messages" && q.Headers["x-api-key"] == "k2" && q.Headers["anthropic-version"] != null, "posted to /v1/messages with key and version");
                var body = JObject.Parse(q.Body);
                Check(body["max_tokens"] != null && body["output_config"]["effort"].ToString() == "low" && body["temperature"] == null, "anthropic body shape: max_tokens, effort, no temperature");
                Check(body["system"][0]["text"].ToString().Contains("RESPONSE FORMAT") && body["system"][0]["cache_control"] != null, "system prompt carries the format and is cacheable");
                Check(body["thinking"]["type"].ToString() == "adaptive" && body["thinking"]["display"].ToString() == "summarized", "asks for a thinking summary");
            }
        }

        static void GeminiSeatSpeaksGenerateContent()
        {
            var reply = new JObject
            {
                ["candidates"] = new JArray(new JObject
                {
                    ["content"] = new JObject { ["parts"] = new JArray(
                        new JObject { ["text"] = "Offer P3 a deal.", ["thought"] = true },
                        new JObject { ["text"] = "{\"messages\":[{\"text\":\"hello\"},{\"to\":\"P3\",\"text\":\"deal?\"}],\"notes\":\"n3\"}" }) }
                }),
                ["modelVersion"] = "fake-gemini-001",
                ["usageMetadata"] = new JObject { ["promptTokenCount"] = 700, ["candidatesTokenCount"] = 40, ["thoughtsTokenCount"] = 160 },
            }.ToString();
            using (var server = new FakeServer(q => reply))
            {
                var seat = Seats.Make(new SeatConfig { Kind = "gemini", Endpoint = server.Url, ApiKey = "k3", Model = "fake-gemini" }, 0);
                var referee = new Referee("t", Names, 4); referee.EndDraft();   // negotiate phase
                var r = seat.Respond(referee.ViewFor("P1"), CancellationToken.None).Result;
                Check(r.Error == null && r.Messages.Count == 2 && r.Messages[0].to == null && r.Messages[1].to == "P3" && r.Notes == "n3", "generateContent reply parsed");
                var q = server.Single();
                Check(q.Path == "/v1beta/models/fake-gemini:generateContent" && q.Headers["x-goog-api-key"] == "k3", "posted to generateContent with the key header");
                var body = JObject.Parse(q.Body);
                Check(body["generationConfig"]["responseMimeType"].ToString() == "application/json" && body["system_instruction"] != null, "gemini body shape");
                Check((bool)body["generationConfig"]["thinkingConfig"]["includeThoughts"] && ((LlmAgent)seat).Transcript[0].Thinking == "Offer P3 a deal.", "thought summary asked for and kept apart from the answer");
                var ex = ((LlmAgent)seat).Transcript[0];
                Check(ex.Model == "fake-gemini-001" && ex.InputTokens == 700 && ex.OutputTokens == 200, "thinking tokens counted as output");
            }
        }

        static void BadRepliesBecomePassesAndKeepNotes()
        {
            int calls = 0;
            using (var server = new FakeServer(q =>
            {
                switch (Interlocked.Increment(ref calls))
                {
                    case 1: return ChatReply("{\"action\":{\"type\":\"pass\"},\"notes\":\"keep\"}");
                    case 2: case 3: return "STATUS 500 {\"error\":{\"message\":\"server on fire\"}}";   // the retry fails too
                    case 4: return ChatReply("I would rather not answer in JSON today.");
                    case 5: return ChatReply("{\"action\":{\"type\":\"teleport\",\"to\":0}}");
                    default: return ChatReply("{\"action\":\"pass\"}");
                }
            }))
            {
                var seat = Seats.Make(new SeatConfig { Kind = "openai", Endpoint = server.Url, ApiKey = "k", Model = "m" }, 0);
                var view = InActPhase().ViewFor("P1");
                var ok = seat.Respond(view, CancellationToken.None).Result;
                Check(ok.Error == null && ok.Notes == "keep", "good reply sets notes");
                var http = seat.Respond(view, CancellationToken.None).Result;
                Check(http.Action == null && http.Error.Contains("HTTP 500") && http.Error.Contains("server on fire") && http.Notes == "keep", "HTTP error -> no action, error text, notes kept");
                var prose = seat.Respond(view, CancellationToken.None).Result;
                Check(prose.Action == null && prose.Error.Contains("not JSON"), "prose reply -> no action");
                var odd = seat.Respond(view, CancellationToken.None).Result;
                Check(odd.Action == null && odd.Error.Contains("not understood"), "unknown action type -> no action");
                var loose = seat.Respond(view, CancellationToken.None).Result;
                Check(loose.Error == null && loose.Action.Type == ActionType.Pass, "\"action\": \"pass\" is accepted");
                var llm = (LlmAgent)seat;
                lock (llm.Transcript) Check(llm.Transcript.Count == 5 && llm.Transcript[1].Error != null, "every exchange is in the transcript with its error");
            }
        }

        static void TradeOffersReachThePromptAndLooseOffersParse()
        {
            var r = new Referee("t", Names, 1); r.EndDraft();   // P1 owns 1,2; P2 owns 4,5; node 3 is neutral
            r.SubmitOffers("P1", new List<(string, int, int)> { ("P2", 1, 4), ("P3", 1, 3) });
            r.EndNegotiation();
            var p2 = Prompting.UserPrompt(r.ViewFor("P2"), "");
            Check(p2.Contains("P1 offers you: you get node 1") && p2.Contains("you give node 4") && p2.Contains("add \"accept\": [\"P1\"]"),
                "partner's act prompt shows the offer and how to accept it");
            var p1 = Prompting.UserPrompt(r.ViewFor("P1"), "");
            Check(p1.Contains("REJECTED: node 3 is neutral"), "offerer's act prompt says why an offer was rejected");
            Check(!p1.Contains("\"type\":\"trade\""), "trades are no longer among the legal actions");

            using (var server = new FakeServer(q => ChatReply("{\"messages\":[],\"offer\":{\"to\":\"p2\",\"give\":1,\"get\":4}}")))
            {
                var seat = Seats.Make(new SeatConfig { Kind = "openai", Endpoint = server.Url, ApiKey = "k", Model = "m" }, 0);
                var fresh = new Referee("t", Names, 1); fresh.EndDraft();
                var resp = seat.Respond(fresh.ViewFor("P1"), CancellationToken.None).Result;
                Check(resp.Error == null && resp.Offers.Count == 1 && resp.Offers[0] == ("P2", 1, 4), "single \"offer\" with \"to\" and a lower-case seat is accepted");
            }
            // Accepting, the ways models write it: a seat name, a list of offer objects, or under another key.
            foreach (var reply in new[] { "{\"action\":{\"type\":\"pass\"},\"accept\":\"p1\"}", "{\"action\":{\"type\":\"pass\"},\"accept\":[{\"from\":\"P1\"}]}", "{\"action\":{\"type\":\"pass\"},\"accepts\":[\"Player 1\"]}" })
                using (var server = new FakeServer(q => ChatReply(reply)))
                {
                    var seat = Seats.Make(new SeatConfig { Kind = "openai", Endpoint = server.Url, ApiKey = "k", Model = "m" }, 0);
                    var resp = seat.Respond(r.ViewFor("P2"), CancellationToken.None).Result;
                    Check(resp.Error == null && resp.Accepts != null && resp.Accepts.SequenceEqual(new[] { "P1" }), "accept parsed from " + reply);
                }
        }

        static void PromptShowsObjectiveStatusAndFinalRound()
        {
            var draft = Prompting.UserPrompt(new Referee("t", Names, 2).ViewFor("P1"), "");
            Check(!draft.Contains("YOUR OBJECTIVE RIGHT NOW"), "no objective status during the draft");

            var r = InActPhase(2);
            foreach (var p in Names)
            {
                bool met = r.State.Assigned[p].Achieved(r.State, p);
                var text = Prompting.UserPrompt(r.ViewFor(p), "");
                Check(text.Contains(met ? "YOUR OBJECTIVE RIGHT NOW: MET" : "YOUR OBJECTIVE RIGHT NOW: NOT MET"), $"{p}'s prompt matches the referee's check of its card");
            }
            var act = Prompting.UserPrompt(r.ViewFor("P1"), "");
            Check(act.Contains("WHAT YOUR MOVES AND SABOTAGES WOULD DO") && act.Contains("takes neutral node") && act.Contains("DANGER TO YOUR NODES THIS ROUND"), "the referee's board reading is in the prompt");
            Check(!act.Contains("FINAL ROUND") && !act.Contains("MAY BE THE LAST ROUND"), "no end-of-game warning in round 1");
            Check(!Json.Serialize(Wire.ToDto(r.ViewFor("P1"))).ToLowerInvariant().Contains("lastround"), "the secret last round never reaches a seat");
            // Game 6: Gemma never worked out which node was its gap and traded it away, so every prompt now spells it out.
            var p3 = Prompting.UserPrompt(r.ViewFor("P3"), "");
            Check(p3.Contains("your left gap is node 9 (between you and P4)") && p3.Contains("your right gap is node 6 (between you and P2)"), "a seat is told its own gap nodes");
            Check(p3.Contains("P1: left neighbour P2, right neighbour P4, left gap node 3, right gap node 12"), "the SEATS table covers every seat");
            Check(p3.Contains("6: ") && p3.Contains("<- gap between P2 and P3") && p3.Contains("<- hub"), "gap nodes are marked on the board");
            var rules = Prompting.SystemPrompt(r.ViewFor("P3"), new SeatConfig());
            Check(rules.Contains("STAYS THEIRS") && rules.Contains("GAP NODES are ring nodes 3, 6, 9, 12"), "sabotage and gap rules are spelled out");
            r.State.Round = Rules.MinRounds;
            var maybe = Prompting.UserPrompt(r.ViewFor("P1"), "");
            Check(maybe.Contains("THIS MAY BE THE LAST ROUND") && !maybe.Contains("FINAL ROUND"), "from round 7 the game may end, and the prompt says so");
            r.State.Round = Rules.MaxRounds;
            Check(Prompting.UserPrompt(r.ViewFor("P1"), "").Contains("FINAL ROUND"), "round 9 is always the last, and the prompt says so");
        }

        static void FailedCallsRetryAndBeliefsNeverCostAMove()
        {
            int calls = 0;
            using (var server = new FakeServer(q =>
            {
                switch (Interlocked.Increment(ref calls))
                {
                    case 1: return ChatReply("");   // a thinking model that sent no text
                    case 2: return ChatReply("{\"messages\":[{\"text\":\"hi\"}],\"beliefs\":{\"objectiveBeliefs\":{\"P2\":\"fortress\",\"P3\":[\"trader\",\"pacifist\"]}},\"notes\":\"a\"}");
                    default: return ChatReply("{\"messages\":[{\"text\":\"still here\"}],\"beliefs\":{\"objectiveBeliefs\":{\"P2\":5}},\"notes\":\"b\"}");
                }
            }))
            {
                var seat = Seats.Make(new SeatConfig { Kind = "openrouter", Endpoint = server.Url, ApiKey = "k", Model = "m" }, 0);
                var r = new Referee("t", Names, 9);
                r.EndDraft();
                var view = r.ViewFor("P1");

                var retried = seat.Respond(view, CancellationToken.None).Result;
                Check(retried.Error == null && retried.Messages.Count == 1 && calls == 2, "empty reply is retried once and the second answer is used");
                var b = retried.Beliefs.ObjectiveBeliefs;
                Check(b["P2"]["fortress"] == 1.0 && b["P3"]["trader"] == 0.5, "a belief given as one id or a list of ids is accepted");

                var junk = seat.Respond(view, CancellationToken.None).Result;
                Check(junk.Error == null && junk.Messages[0].text == "still here" && junk.Notes == "b", "unreadable beliefs are dropped, the rest of the answer is kept");

                lock (server.Requests)
                {
                    var body = JObject.Parse(server.Requests[0].Body);
                    Check(body["reasoning"]?["effort"]?.ToString() == "low" && body["reasoning_effort"] == null, "openrouter seats ask for low effort in OpenRouter's own field");
                    string Prompt(int i) => JObject.Parse(server.Requests[i].Body)["messages"][1]["content"].ToString();
                    Check(!Prompt(0).Contains("LAST REPLY WAS EMPTY") && Prompt(1).EndsWith(LlmAgent.RetryNudge), "the retry is not the identical request: it says what was missing");
                }
            }
        }

        // What DeepSeek did on the guess phase: no text at all, finish_reason "stop", the answer only in its reasoning.
        static string ThinkingOnly(string reasoning, string field = "reasoning") =>
            new JObject { ["choices"] = new JArray(new JObject { ["finish_reason"] = "stop",
                ["message"] = new JObject { ["role"] = "assistant", ["content"] = "", [field] = reasoning } }) }.ToString();

        static void AnswerOnlyInReasoningIsRescued()
        {
            var r = new Referee("t", Names, 9);
            r.EndDraft();
            while (r.State.Phase != Phase.Guess) { r.EndNegotiation(); r.ResolveRound(new Dictionary<string, GameAction>()); }
            var view = r.ViewFor("P1");
            string thinking = "The prompt wants {\"guesses\": {\"P2\": \"<id>\"}}. P2 fortified a lot. Draft: {\"guesses\":{\"P2\":\"trader\"}}. "
                            + "No, P2 is fortress. Final: {\"guesses\":{\"P2\":\"fortress\",\"P3\":\"trader\"},\"notes\":\"settled\"}";
            foreach (var field in new[] { "reasoning", "reasoning_content" })
                using (var server = new FakeServer(q => ThinkingOnly(thinking, field)))
                {
                    var seat = Seats.Make(new SeatConfig { Kind = "openrouter", Endpoint = server.Url, ApiKey = "k", Model = "m" }, 0);
                    var resp = seat.Respond(view, CancellationToken.None).Result;
                    Check(resp.Error == null && resp.Guesses["P2"] == "fortress" && resp.Guesses["P3"] == "trader" && resp.Notes == "settled" && server.Count == 1,
                        $"an answer left only in \"{field}\" is used: the last reply object, not a draft or a quote of the prompt");
                }
            using (var server = new FakeServer(q => ThinkingOnly("I am not sure what to say.")))
            {
                var seat = Seats.Make(new SeatConfig { Kind = "openrouter", Endpoint = server.Url, ApiKey = "k", Model = "m" }, 0);
                var resp = seat.Respond(view, CancellationToken.None).Result;
                Check(resp.Guesses == null && resp.Error.Contains("finish_reason: stop") && resp.Error.Contains("I am not sure") && server.Count == 2,
                    "with no answer anywhere it is retried once, then the error says why and quotes the reasoning");
            }
        }

        static void LlmSeatsPlayAFullGame()
        {
            // The fake model reads the phase from the prompt and always answers in a valid shape.
            using (var server = new FakeServer(q =>
            {
                var user = JObject.Parse(q.Body)["messages"][1]["content"].ToString();
                if (user.Contains("DRAFT PHASE")) return ChatReply("{\"draft\":\"not_a_real_card\",\"notes\":\"n\"}");
                if (user.Contains("GUESS PHASE")) return ChatReply("{\"guesses\":{\"P1\":\"trader\",\"P2\":\"trader\",\"P3\":\"trader\",\"P4\":\"trader\"},\"notes\":\"n\"}");
                if (user.Contains("ACT PHASE")) return ChatReply("{\"action\":{\"type\":\"pass\"},\"notes\":\"n\"}");
                return ChatReply("{\"messages\":[{\"text\":\"gl hf\"}],\"beliefs\":{\"objectiveBeliefs\":{\"P2\":{\"trader\":1.0}}},\"notes\":\"n\"}");
            }))
            {
                var seats = Names.ToDictionary(p => p, p => Seats.Make(new SeatConfig { Kind = "openai", Endpoint = server.Url, ApiKey = "k", Model = "m", Name = "Fake " + p }, 0));
                var run = new GameRunner(new Referee("t", Names, 8), seats) { PhaseTimeout = TimeSpan.FromSeconds(20) };
                run.RunToEnd();
                Check(run.Finished, "llm seats finish a game");
                Check(run.State.Messages.Count == 4 * run.State.LastRound && run.State.Messages.All(m => m.To == null && m.Text == "gl hf"), "public chat came through");
                Check(run.State.Beliefs.Count == 4 * run.State.LastRound, "beliefs logged every round");
                Check(run.State.ActionLog.All(a => a.wasLegal && a.action.Type == ActionType.Pass), "actions applied");
                Check(run.State.Guesses.Count == 4 && run.State.Guesses["P1"].Count == 3, "guesses applied, self-guess dropped");
                Check(run.LastResponses.Values.All(r => r.Error == null), "no errors reported");
                Check(seats.Values.Cast<LlmAgent>().All(s => s.Notes == "n" && s.Transcript.Count == 1 + 2 * run.State.LastRound + 1), "notes carried; one exchange per phase");
                Check(run.Seats["P2"].Kind == "Fake P2", "label shows on the seat");
                Check(server.Count == 4 * (1 + 2 * run.State.LastRound + 1), "one request per seat per phase");
            }
        }
    }
}
