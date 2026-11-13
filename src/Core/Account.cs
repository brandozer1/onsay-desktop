// Account.cs - the person's Onsay account on this computer. Signing in goes through Onsay's website, so the app needs no
// sign in screen: the browser shows a code, the person presses Connect, and the app, asking every few seconds, gets this
// computer's token. The floor then sends its own key to Onsay itself, signed by that key, so Onsay knows this computer by
// it, and the floor's log and its calls count for the account. The token is kept sealed for this Windows user in
// keys\account.token, and who it is in account.json.
//
// 1. Api                       Onsay's address, ONSAY_API for a test
// 2. Onsay(phrase, values)     what the app's windows ask: status, sign_in, sign_out, lingos (every computer of the
//                              account and what it offers), credits, ai, share and unshare: its answer, or why not in words
// 3. SignIn(), Finish(...)     a code and the page to open, then, asked every second, the token and the floor's key,
//                              and SignedIn, told the moment it is done, for him to jump in (BuddyWindow.Arrive)
// 4. Get(path)                 one of Onsay's own routes with this computer's token
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Onsay.Desktop;

static class Account
{
    // 1.
    public static readonly string Api = Environment.GetEnvironmentVariable("ONSAY_API") is { Length: > 0 } test ? test.TrimEnd('/') : "https://api.onsay.dev";

    static readonly string TokenFile = Path.Combine(Settings.Folder, "keys", "account.token");
    static readonly string WhoFile = Path.Combine(Settings.Folder, "account.json");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static bool signingIn;
    public static event Action? SignedIn;   // 3. told on a background thread
    static string? failed;

    ////////// 2. WHAT THE WINDOWS ASK //////////

    // 2. Why not ends with a full stop, as the windows show it.
    public static (JsonNode? answer, string? why) Onsay(string phrase, params JsonNode[] values)
    {
        try
        {
            return phrase switch
            {
                "status" => (Status(), null),
                "sign_in" => SignIn(),
                "sign_out" => SignOut(),
                "lingos" => Get("/lingos"),
                "credits" => Get("/credits"),
                "ai" => Get("/ai"),
                "share" => (null, Share(values.ElementAtOrDefault(0) as JsonObject)),
                "unshare" => (null, Unshare($"{values.ElementAtOrDefault(0)?["to"]}")),
                _ => (null, "That isn't something he can do with your account."),
            };
        }
        catch (Exception) { return (null, "Onsay couldn't be reached. Check your connection and try again."); }
    }

    // {signed_in, signing_in, failed, handle, device, connected}, connected while signed in, since the floor reaches
    // Onsay by itself.
    static JsonObject Status()
    {
        var who = Who();
        bool on = Token() is not null && who["handle"] is not null;
        var status = new JsonObject { ["signed_in"] = on, ["signing_in"] = signingIn, ["connected"] = on };
        if (failed is not null) status["failed"] = failed;
        if (on)
        {
            status["handle"] = who["handle"]?.DeepClone();
            status["device"] = who["device"]?.DeepClone();
        }
        return status;
    }

    // Onsay forgets this computer as well, so its key counts for nothing until it signs in again.
    static (JsonNode?, string?) SignOut()
    {
        if (Token() is string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Api}/connect/out") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new("Bearer", token);
            try { Http.SendAsync(request).GetAwaiter().GetResult().Dispose(); }
            catch (Exception) { }
        }
        try { File.Delete(TokenFile); } catch (Exception) { }
        var who = Who();
        who.Remove("handle");
        who.Remove("device");
        File.WriteAllText(WhoFile, who.ToJsonString());
        return (new JsonObject(), null);
    }

    /* Shared on this computer: a grant in the floor's policy.json, {to, allow}, to their handle, allow this computer's
       routes alone, its own name taken off the front, like laptop.brandon/agent to /agent. */
    static string? Share(JsonObject? values)
    {
        var to = $"{values?["to"]}";
        if (to.Length == 0) return "Say who to share it with.";
        var allow = new JsonArray((values?["allow"] as JsonArray ?? new()).Select(a =>
        {
            var address = $"{a}";
            int slash = address.IndexOf('/');
            return (JsonNode)(slash > 0 ? address.Substring(slash) : address);
        }).ToArray());
        var policy = Core.Policy();
        var grants = policy["grants"] as JsonArray ?? new JsonArray();
        grants.Add(new JsonObject { ["to"] = to, ["allow"] = allow });
        policy["grants"] = grants.DeepClone();
        return Core.Keep(policy);
    }

    // Every grant to them goes.
    static string? Unshare(string to)
    {
        var policy = Core.Policy();
        var kept = (policy["grants"] as JsonArray ?? new()).Where(g => $"{g?["to"]}" != to).Select(g => g?.DeepClone()).ToArray();
        policy["grants"] = new JsonArray(kept);
        return Core.Keep(policy);
    }

    ////////// 3. SIGNING IN //////////

    /* 3. {code, url}: what the browser opens and the code it shows. The rest goes on by itself, and status says how it
          went: signing_in until it is done, then signed_in, or failed with why. */
    static (JsonNode?, string?) SignIn()
    {
        var who = Who();
        var install = $"{who["install"] ?? Guid.NewGuid().ToString("N")}";
        who["install"] = install;
        Directory.CreateDirectory(Settings.Folder);
        File.WriteAllText(WhoFile, who.ToJsonString());
        var body = new JsonObject { ["name"] = Environment.MachineName, ["install"] = install };
        using var res = Http.PostAsync($"{Api}/connect", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
        var got = JsonNode.Parse(res.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        if (!res.IsSuccessStatusCode) return (null, $"{got?["error"] ?? "Onsay couldn't start signing in"}.");
        signingIn = true;
        failed = null;
        _ = Task.Run(() => Finish($"{got?["poll"]}"));
        return (new JsonObject { ["code"] = got?["code"]?.DeepClone(), ["url"] = got?["url"]?.DeepClone() }, null);
    }

    // 3. For up to 10 minutes, while the code is good. The floor sends its key with the token, through lingo.use. Asked
    //    every second, so he arrives here as he dives into the screen on the page.
    static async Task Finish(string poll)
    {
        try
        {
            var until = DateTime.Now.AddMinutes(10);
            string? token = null;
            while (token is null && DateTime.Now < until)
            {
                await Task.Delay(1000);
                using var res = await Http.PostAsync($"{Api}/connect/poll", new StringContent(new JsonObject { ["poll"] = poll }.ToJsonString(), Encoding.UTF8, "application/json"));
                var got = JsonNode.Parse(await res.Content.ReadAsStringAsync());
                if (!res.IsSuccessStatusCode) throw new Exception($"{got?["error"] ?? "Signing in didn't finish"}");
                if ($"{got?["status"]}" == "done") token = $"{got?["token"]}";
            }
            if (token is null) throw new Exception("Signing in took too long. Try again");
            var (ready, why) = Core.Call("/lingo/use", new JsonArray($"{Api}/connect/key", new JsonObject { ["token"] = token }));
            if (ready is null) throw new Exception(why);
            Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
            File.WriteAllBytes(TokenFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
            var who = Who();
            who["handle"] = ready["handle"]?.DeepClone();
            who["device"] = ready["device"]?.DeepClone();
            File.WriteAllText(WhoFile, who.ToJsonString());
            signingIn = false;
            SignedIn?.Invoke();
        }
        catch (Exception e) { failed = e.Message.TrimEnd('.'); }
        signingIn = false;
    }

    ////////// 4. ONSAY'S ROUTES //////////

    // 4.
    static (JsonNode?, string?) Get(string path)
    {
        if (Token() is not string token) return (null, "Sign in to Onsay first.");
        using var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
        request.Headers.Authorization = new("Bearer", token);
        using var res = Http.SendAsync(request).GetAwaiter().GetResult();
        var got = JsonNode.Parse(res.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        return res.IsSuccessStatusCode ? (got, null) : (null, $"{got?["error"] ?? "Onsay couldn't do that"}.");
    }

    static string? Token()
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(TokenFile), null, DataProtectionScope.CurrentUser)); }
        catch (Exception) { return null; }
    }

    static JsonObject Who()
    {
        try { return JsonNode.Parse(File.ReadAllText(WhoFile)) as JsonObject ?? new(); }
        catch (Exception) { return new(); }
    }
}
