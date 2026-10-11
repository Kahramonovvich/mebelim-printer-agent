// Mebelim Printer Agent — kassa kompyuterida yashirin ishlaydi, brauzerdan
// http://127.0.0.1:1337 ga keladigan buyurtmani chek qilib printerga yuboradi.
using System.Drawing;
using Mebelim.Printing;

var builder = WebApplication.CreateBuilder(args);

// Avtoyangilanishdan keyin ishga tushdik: eski nusxa 1337-portni bo'shatguncha kutamiz
if (int.TryParse(builder.Configuration["wait-pid"], out var oldPid))
    try { System.Diagnostics.Process.GetProcessById(oldPid).WaitForExit(15000); } catch { }
try { File.Delete(Environment.ProcessPath + ".old"); } catch { }

// --port 1338 — sinov uchun (1337 band bo'lsa)
builder.WebHost.UseUrls($"http://127.0.0.1:{builder.Configuration["port"] ?? "1337"}");
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .SetIsOriginAllowed(origin =>
    {
        // Platformaning barcha ilovalari: business./seller./manager./... .mebelim.uz va lokal dev
        var host = new Uri(origin).Host;
        return host == "mebelim.uz" || host.EndsWith(".mebelim.uz") || host == "localhost" || host == "127.0.0.1";
    })
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// --updateUrl — sinov uchun (lokal latest.json)
_ = Updater.Loop(
    builder.Configuration["updateUrl"] ?? $"https://github.com/{Updater.Repo}/releases/latest/download/latest.json",
    app.Lifetime.ApplicationStopping);

var version = typeof(Program).Assembly.GetName().Version?.ToString(3);

app.MapGet("/health", () => new { ok = true, version });

app.MapGet("/printers", () => new { selected = Printers.Saved, list = Printers.List() });

app.MapPost("/printer", (SelectRequest r) =>
{
    if (Printers.List().All(p => p.Name != r.Name))
        return Results.BadRequest(new { error = $"Printer topilmadi: {r.Name}" });
    Printers.Saved = r.Name;
    return Results.Ok(new { ok = true, selected = r.Name });
});

app.MapPost("/print", (PrintRequest r) =>
{
    var list = Printers.List();
    var printer = list.FirstOrDefault(p => p.Name == (r.PrinterName ?? Printers.Saved));

    if (r.Order is null && r.Product is null && string.IsNullOrEmpty(r.DataBase64))
        return Results.BadRequest(new { error = "order, product yoki dataBase64 kerak" });

    // Printer hali tanlanmagan (yoki o'chirib tashlangan) — front ro'yxatni ko'rsatib, so'raydi
    if (printer is null && !r.Preview)
        return Results.Json(new { error = "PRINTER_NOT_SELECTED", list }, statusCode: 409);

    var opt = new ReceiptOptions
    {
        Title = r.Title,
        StorePhone = r.StorePhone,
        IsDelivery = r.IsDelivery,
        Columns = printer?.Columns ?? 48,
        Logo = string.IsNullOrWhiteSpace(r.LogoBase64) ? null : Logo(r.LogoBase64!, printer?.Columns == 32 ? 300 : 384),
        SiteUrl = string.IsNullOrWhiteSpace(r.SiteUrl) ? "https://business.mebelim.uz" : r.SiteUrl!,
    };
    var (bytes, text) = r.Order is not null ? Receipt.Build(r.Order, opt)
        : r.Product is not null ? Receipt.BuildProduct(r.Product, opt)
        : (Convert.FromBase64String(r.DataBase64!), "");

    if (r.Preview) return Results.Ok(new { ok = true, printer = printer?.Name, text });

    return Printers.SendRaw(printer!.Name, bytes)
        ? Results.Ok(new { ok = true, printer = printer.Name, offline = printer.Offline })
        : Results.Json(new { error = $"{printer.Name} ga yuborib bo'lmadi" }, statusCode: 500);
});

// Sozlamalardagi "Sinov cheki" tugmasi
app.MapPost("/test", (SelectRequest? r) =>
{
    var name = r?.Name ?? Printers.Saved;
    var printer = Printers.List().FirstOrDefault(p => p.Name == name);
    if (printer is null) return Results.Json(new { error = "PRINTER_NOT_SELECTED" }, statusCode: 409);
    var bytes = System.Text.Encoding.ASCII.GetBytes(
        $"\u001B@\u001Ba\u0001\u001BE\u0001MEBELIM\u001BE\u0000\nSinov cheki\n{Receipt.Ascii(printer.Name)}\n" +
        $"{DateTime.Now:dd.MM.yyyy HH:mm}\n\u001Bd\u0005\u001DV\u0000");
    return Printers.SendRaw(printer.Name, bytes)
        ? Results.Ok(new { ok = true, printer = printer.Name, offline = printer.Offline })
        : Results.Json(new { error = $"{printer.Name} ga yuborib bo'lmadi" }, statusCode: 500);
});

app.Run();

// Logotip faylini (PNG/JPG, base64) kulrang piksellarga aylantiradi — Receipt.Image uchun
static byte[] Logo(string base64, int maxWidth)
{
    using var img = Image.FromStream(new MemoryStream(Convert.FromBase64String(base64)));
    int w = Math.Min(maxWidth, img.Width), h = (int)Math.Round(img.Height * (w / (double)img.Width));
    using var bmp = new Bitmap(w, h);
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.White);   // shaffof fon oq bo'lsin
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, 0, 0, w, h);
    }
    var gray = new byte[w * h];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = bmp.GetPixel(x, y);
            gray[y * w + x] = (byte)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
        }
    return Receipt.Image(gray, w, h);
}

record SelectRequest(string? Name);

record PrintRequest
{
    public ReceiptOrder? Order { get; init; }
    public ProductLabel? Product { get; init; }  // mahsulot yorlig'i (ProductGetDto)
    public string? SiteUrl { get; init; }        // mahsulot QR manzili: front window.location.origin beradi
    public string? Title { get; init; }
    public string? StorePhone { get; init; }
    public bool IsDelivery { get; init; }
    public string? LogoBase64 { get; init; }
    public string? PrinterName { get; init; }   // berilmasa — saqlangan printer
    public string? DataBase64 { get; init; }    // tayyor ESC/POS baytlar (bulutli chop etish uchun)
    public bool Preview { get; init; }          // chop etmay, chek matnini qaytaradi
}
