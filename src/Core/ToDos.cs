// ToDos.cs - the person's to-dos, kept by the app in todos.json: each what they asked, run by the agent on this
// computer's port (Core.cs, /agent/ask), the steps it shows coming as it works, and its turns, each prompt and its reply.
// Not quite on a done one makes a new one carrying everything before it. The chat (ChatWindow.cs) reaches them through
// Local, answered in this app.
//
// 1. List()                       every to-do, newest first: {session, prompt, status, step, text, from, show}, status
//                                 working, asking or done, show whether the person watches its work
//    History(words)               the 20 newest, or those whose prompt holds the words, {session, prompt, status, reply}
// 2. Prompt(message)              {prompt, session, from, show}: a new to-do, or a reply to one, run and waited for:
//                                 {session, text} or {session, questions}, or null with why. show, given, is the
//                                 to-do's from then on. A run that fails leaves what went wrong in the conversation, so
//                                 nothing they said is lost
//    Show(id, on), Showing(run)   whether the person watches a to-do's work
// 3. Turns(id), Live(id)          one to-do's turns, {turns: [{prompt, text or questions}]}, and what it does now,
//                                 {running, steps: [{text, depth}], questions}
// 4. Step(run, text)              a step the agent shows, for the to-do it runs for
//    Ask(run, questions, seconds) questions waited on until the person answers, or seconds pass
//    Asks(), Answer(id, answers)  the questions waiting that are no to-do's, {id, questions}, and the person's answers
// 5. Local                        the chat's requests, answered here: POST / a prompt, GET /sessions, /sessions/<id>,
//                                 /sessions/<id>/live, POST /sessions/<id>/show and /sessions/<id>/answers
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace Onsay.Desktop;

static class ToDos
{
    const int Most = 200;   // the newest to-dos kept

    // A to-do: log is its whole conversation as the agent reads it, chat messages in OpenAI's shape.
    sealed class ToDo
    {
        public string Session = "", Prompt = "", Status = "done";
        public string? From;
        public bool Show;   // the person watches its work: he works in front of them (Use desktop)
        public JsonArray Turns = new(), Log = new();
        public List<string> Steps = new();
    }

    // A question a lingo waits on the person for: the to-do it is for, if any, and its answers once they come.
    sealed class Asking
    {
        public long Id;
        public string? Run;
        public JsonNode Questions = new JsonArray();
        public readonly TaskCompletionSource<JsonArray?> Answers = new();
    }

    static readonly string File_ = Path.Combine(Settings.Folder, "todos.json");
    static readonly object gate = new();
    static List<ToDo>? all;
    static readonly List<Asking> asking = new();
    static long asked;

    ////////// 1. THE LIST //////////

    // Read from todos.json the first time: one that was working when the app last ended is done.
    static List<ToDo> All()
    {
        if (all is not null) return all;
        all = new();
        try
        {
            foreach (var t in JsonNode.Parse(File.ReadAllText(File_)) as JsonArray ?? new())
                all.Add(new ToDo
                {
                    Session = $"{t?["session"]}",
                    Prompt = $"{t?["prompt"]}",
                    From = t?["from"]?.ToString(),
                    Show = (bool?)t?["show"] == true,
                    Turns = t?["turns"]?.DeepClone() as JsonArray ?? new(),
                    Log = t?["log"]?.DeepClone() as JsonArray ?? new(),
                });
        }
        catch (Exception) { }
        return all;
    }

    static void Save()
    {
        var kept = new JsonArray(All().Take(Most).Select(t => (JsonNode)new JsonObject
        {
            ["session"] = t.Session, ["prompt"] = t.Prompt, ["from"] = t.From, ["show"] = t.Show, ["turns"] = t.Turns.DeepClone(), ["log"] = t.Log.DeepClone(),
        }).ToArray());
        try { File.WriteAllText(File_, kept.ToJsonString()); }
        catch (Exception) { }
    }

    // 1.
    public static JsonArray List()
    {
        lock (gate)
            return new JsonArray(All().Select(t =>
            {
                var last = t.Turns.LastOrDefault();
                var item = new JsonObject { ["session"] = t.Session, ["prompt"] = t.Prompt, ["status"] = t.Status, ["from"] = t.From, ["show"] = t.Show };
                if (t.Steps.Count > 0) item["step"] = t.Steps[^1];
                if (last?["text"] is JsonNode text) item["text"] = text.DeepClone();
                return (JsonNode)item;
            }).ToArray());
    }

    // 1. For the chat lingo's history: the 20 newest, or those whose prompt holds the words.
    public static JsonArray History(string? words)
    {
        lock (gate)
            return new JsonArray(All()
                .Where(t => string.IsNullOrWhiteSpace(words) || t.Prompt.Contains(words!.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(20)
                .Select(t =>
                {
                    var reply = $"{t.Turns.LastOrDefault()?["text"]}";
                    if (reply.Length > 300) reply = reply.Substring(0, 300) + "...";
                    return (JsonNode)new JsonObject { ["session"] = t.Session, ["prompt"] = t.Prompt, ["status"] = t.Status, ["reply"] = reply };
                }).ToArray());
    }

    ////////// 2. A PROMPT //////////

    /* 2. Run by agent.ask with the conversation so far, a follow-up starting from the one it follows, its steps and
          questions coming as the run named by its id. The agent gives back its reply, or questions, and the messages it
          added, which the conversation keeps. */
    public static (JsonObject? answer, string? why) Prompt(JsonObject message)
    {
        var prompt = $"{message["prompt"]}".Trim();
        var session = $"{message["session"]}";
        if (prompt.Length == 0 || session.Length == 0) return (null, "A to-do needs what to do, and its session.");
        ToDo t;
        JsonArray log;
        string task;
        lock (gate)
        {
            t = All().FirstOrDefault(x => x.Session == session) ?? new ToDo { Session = session, Prompt = prompt, From = message["from"]?.ToString() };
            if (!All().Contains(t))
            {
                if (All().FirstOrDefault(x => x.Session == t.From) is { } before) t.Log = (JsonArray)before.Log.DeepClone();
                All().Insert(0, t);
            }
            if (t.Status != "done") return (null, "That to-do is still working.");
            if (message["show"] is JsonValue show) t.Show = (bool?)show == true;
            t.Status = "working";
            t.Steps.Clear();
            log = (JsonArray)t.Log.DeepClone();
            log.Add(new JsonObject { ["role"] = "user", ["content"] = prompt });
            // The job: the first to-do of the ones this follows up, so a follow-up and a reply count as asking it again.
            var first = t;
            while (first.From is { Length: > 0 } from && All().FirstOrDefault(x => x.Session == from) is { } earlier) first = earlier;
            task = first.Session;
        }
        // The agent on the port, with the conversation so far, its steps shown as they come.
        var (got, why) = Core.Ask("/agent/ask", new JsonObject { ["prompt"] = prompt, ["messages"] = Newest(log), ["show"] = t.Show }, step => Step(session, "step", step));
        // The person never sees an error: what went wrong is kept in port.log and in the conversation, so the agent knows
        // next time, and they get a line they can act on.
        if (why is not null)
        {
            try { File.AppendAllText(Core.Log, $"{DateTime.Now:s} to-do: {why}\n"); } catch (Exception) { }
            Kept(log, null, why);
            why = "Something went wrong on my side, so I stopped here. Send it again and I'll pick up where I left off.";
        }
        lock (gate)
        {
            t.Status = "done";
            var turn = new JsonObject { ["prompt"] = prompt };
            // An agent answers {text, messages} or {questions}; anything else the search found answers with its value.
            var answered = got as JsonObject;
            if (answered?["questions"] is JsonArray questions) turn["questions"] = questions.DeepClone();
            else if (why is not null) turn["text"] = why;
            else if (answered?["text"] is JsonNode text) turn["text"] = $"{text}";
            else turn["text"] = "Done.";
            t.Turns.Add(turn);
            if (why is null) Kept(log, got, null);
            t.Log = log;
            Save();
            if (why is not null) return (null, why);
            var answer = new JsonObject { ["session"] = session };
            if (turn["questions"] is JsonNode q) answer["questions"] = q.DeepClone();
            else answer["text"] = turn["text"]!.DeepClone();
            return (answer, null);
        }
    }

    // The newest messages of a conversation that fit, from one of the person's, so a long to-do still reaches Onsay.
    static JsonArray Newest(JsonArray log)
    {
        var newest = new JsonArray();
        int room = 150_000;
        for (int i = log.Count - 1; i >= 0 && (room -= log[i]!.ToJsonString().Length) > 0; i--) newest.Insert(0, log[i]!.DeepClone());
        while (newest.Count > 1 && $"{newest[0]?["role"]}" != "user") newest.RemoveAt(0);
        return newest;
    }

    // What a go adds to the conversation: the messages an agent gave, else what answered as the assistant's words, or, when
    // it failed, what went wrong, for the agent to read next time.
    static void Kept(JsonArray log, JsonNode? got, string? failed)
    {
        if (failed is not null)
        {
            log.Add(new JsonObject { ["role"] = "assistant", ["content"] = $"(That stopped before it was done: {failed})" });
            return;
        }
        if (got?["messages"] is JsonArray added)
        {
            foreach (var m in added) log.Add(m?.DeepClone());
            return;
        }
        var said = got?["text"] ?? got?["questions"] ?? got;
        log.Add(new JsonObject { ["role"] = "assistant", ["content"] = said is JsonValue ? $"{said}" : said?.ToJsonString() ?? "Done." });
    }

    ////////// 3. ONE TO-DO //////////

    // 3.
    public static JsonObject Turns(string id)
    {
        lock (gate) return new JsonObject { ["turns"] = All().FirstOrDefault(x => x.Session == id)?.Turns.DeepClone() ?? new JsonArray() };
    }

    // 3.
    public static JsonObject Live(string id)
    {
        lock (gate)
        {
            var t = All().FirstOrDefault(x => x.Session == id);
            var now = new JsonObject
            {
                ["running"] = t is { Status: not "done" },
                ["steps"] = new JsonArray(t?.Steps.Select(s => (JsonNode)new JsonObject { ["text"] = s, ["depth"] = 0 }).ToArray() ?? Array.Empty<JsonNode>()),
            };
            if (asking.FirstOrDefault(a => a.Run == id) is { } q) now["questions"] = q.Questions.DeepClone();
            return now;
        }
    }

    // 2. A working one changes from its next step, as the agent asks again each turn.
    public static void Show(string id, bool on)
    {
        lock (gate)
        {
            if (All().FirstOrDefault(x => x.Session == id) is not { } t) return;
            t.Show = on;
            Save();
        }
    }

    // 2. A run that is no to-do's, like a program's, is never watched.
    public static bool Showing(string? run)
    {
        lock (gate) return All().FirstOrDefault(x => x.Session == run)?.Show == true;
    }

    ////////// 4. WHAT THE AGENT BRINGS //////////

    // 4.
    public static void Step(string? run, string kind, string text)
    {
        lock (gate)
        {
            if (All().FirstOrDefault(x => x.Session == run && x.Status != "done") is { } t)
            {
                t.Steps.Add(text);
            }
        }
    }

    // 4. Its to-do says it is asking while it waits. Nothing when nobody answered in time.
    public static async Task<JsonArray?> Ask(string? run, JsonNode questions, double seconds)
    {
        var a = new Asking { Run = run, Questions = questions is JsonArray ? questions.DeepClone() : new JsonArray(questions.DeepClone()) };
        ToDo? t;
        lock (gate)
        {
            a.Id = ++asked;
            asking.Add(a);
            t = All().FirstOrDefault(x => x.Session == run && x.Status == "working");
            if (t is not null)
            {
                a.Run = t.Session;
                t.Status = "asking";
            }
            else a.Run = null;
        }
        var answered = await Task.WhenAny(a.Answers.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        lock (gate)
        {
            asking.Remove(a);
            if (t is { Status: "asking" }) t.Status = "working";
        }
        return answered == a.Answers.Task ? a.Answers.Task.Result : null;
    }

    // 4.
    public static JsonArray Asks()
    {
        lock (gate)
            return new JsonArray(asking.Where(a => a.Run is null).Select(a => (JsonNode)new JsonObject { ["id"] = a.Id, ["questions"] = a.Questions.DeepClone() }).ToArray());
    }

    // 4. By the question's id, or by the to-do it is for.
    public static void Answer(long? id, string? run, JsonArray answers)
    {
        Asking? a;
        lock (gate) a = asking.FirstOrDefault(x => id is not null ? x.Id == id : x.Run == run);
        a?.Answers.TrySetResult((JsonArray)answers.DeepClone());
    }
    ////////// 5. THE CHAT'S REQUESTS //////////

    // 5. Each answered as JSON, the way a small web server would, with nothing listening.
    public sealed class Local : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            var given = body.Length > 0 ? JsonNode.Parse(body) : null;
            var rest = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            var get = request.Method == HttpMethod.Get;
            JsonNode? answer = null;
            string? why = null;
            if (!get && rest is [""]) (answer, why) = await Task.Run(() => Prompt(given as JsonObject ?? new()), cancel);
            else if (get && rest is ["sessions"]) answer = List();
            else if (get && rest is ["sessions", var id]) answer = Turns(id);
            else if (get && rest is ["sessions", var live, "live"]) answer = Live(live);
            else if (rest is ["sessions", var watched, "show"]) Show(watched, (bool?)given?["show"] == true);
            else if (rest is ["sessions", var asked, "answers"]) Answer(null, asked, given?["answers"] as JsonArray ?? new());
            else why = "Nothing is there";
            var status = why is null ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;
            var json = why is null ? (answer ?? new JsonObject()).ToJsonString() : new JsonObject { ["error"] = why }.ToJsonString();
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
