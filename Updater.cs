using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;

// Avtoyangilanish: GitHub Releases'dagi latest.json → yangi exe → agent o'zini almashtiradi.
// Agent foydalanuvchi papkasida turadi (%LOCALAPPDATA%\Programs) — admin huquqi kerak emas.
// Yangi versiya chiqarish — .github/workflows/release.yml (GitHub Actions).
static class Updater
{
    public const string Repo = "Kahramonovvich/mebelim-printer-agent";

    record Manifest(string Version, string Url, string Sha256);

    // Ishga tushgandan 1 daqiqa o'tib, keyin har 6 soatda. Xato bo'lsa — keyingi safar.
    public static async Task Loop(string manifestUrl, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            do
            {
                try { await CheckOnce(manifestUrl); } catch { }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    static async Task CheckOnce(string manifestUrl)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var m = await http.GetFromJsonAsync<Manifest>(manifestUrl, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var current = typeof(Updater).Assembly.GetName().Version!;
        if (m is null || !Version.TryParse(m.Version, out var latest) || Three(latest) <= Three(current)) return;

        var exe = Environment.ProcessPath!;
        var tmp = exe + ".new";
        await using (var file = File.Create(tmp))
            await (await http.GetStreamAsync(m.Url)).CopyToAsync(file);
        // Yarim yuklangan yoki buzilgan fayl bilan almashtirmaymiz
        if (!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tmp))).Equals(m.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tmp);
            return;
        }

        // Ishlab turgan exe'ni o'chirib bo'lmaydi, lekin nomini o'zgartirsa bo'ladi
        File.Move(exe, exe + ".old", overwrite: true);
        File.Move(tmp, exe);

        // Yangi nusxa shu argumentlar bilan; eski nusxa portni bo'shatguncha kutadi (Program.cs, --wait-pid)
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "--wait-pid") { i++; continue; }
            psi.ArgumentList.Add(args[i]);
        }
        psi.ArgumentList.Add("--wait-pid");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(psi);
        Environment.Exit(0);
    }

    static Version Three(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
