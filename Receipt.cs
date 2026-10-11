// Mebelim cheki (ESC/POS). Bu fayl ATAYLAB mustaqil: faqat .NET standart kutubxonasi,
// Windows/System.Drawing'ga bog'liq emas — bek uni o'z loyihasiga shundayligicha
// ko'chirib, `Receipt.Build(orderDetailDto, ...)` ni chaqira oladi.
// Kirish modeli = bekning `OrderDetailDto` / `OrderDto` (JSON nomlari bir xil).
using System.Globalization;
using System.Text;

namespace Mebelim.Printing;

public record ReceiptOrder
{
    public long? Id { get; init; }
    public long? Sku { get; init; }
    public string? SessionKey { get; init; }
    public DateTime? OrderDate { get; init; }
    public DateTime? ReadyDate { get; init; }
    public int? Status { get; init; }
    public string? CustomerFullName { get; init; }
    public string? CustomerPhoneNumber { get; init; }
    public string? SellerFullName { get; init; }          // OrderDto
    public ReceiptSeller? Seller { get; init; }           // OrderDetailDto
    public decimal? OriginalAmount { get; init; }
    public decimal? DiscountAmount { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? AdvancePayment { get; init; }
    public decimal? RemainingAmount { get; init; }
    public decimal? DeliveryFee { get; init; }
    public decimal? ServiceFee { get; init; }
    public decimal? ExchangeRateAtOrder { get; init; }
    public string? Notes { get; init; }
    public List<ReceiptItem>? Products { get; init; }
}

public record ReceiptSeller
{
    public string? FullName { get; init; }
}

public record ReceiptItem
{
    public long ProductId { get; init; }
    public string? ProductName { get; init; }
    public int Quantity { get; init; }
    public decimal? SalePrice { get; init; }      // qator JAMI, asl valyutada (bek 2026-07-21)
    public int? PriceCurrency { get; init; }      // 0 = UZS, 1 = USD
    public string? ColorName { get; init; }
    public string? ColorCode { get; init; }
    public ReceiptSize? ProductSize { get; init; }
}

public record ReceiptSize
{
    public decimal? SellPrice { get; init; }      // DOIM asl valyutada
}

// Mahsulot yorlig'i — bekning `ProductGetDto` (JSON nomlari bir xil)
public record ProductLabel
{
    public long Id { get; init; }
    public string? Sku { get; init; }
    public string? ProductBrandName { get; init; }
    public List<ProductLabelTranslation>? Translations { get; init; }
    public List<ProductLabelSize>? ProductSizes { get; init; }
}

public record ProductLabelTranslation
{
    public string? Language { get; init; }
    public string? ProductName { get; init; }
    public string? CategoryName { get; init; }
    public string? Material { get; init; }
}

public record ProductLabelSize
{
    public string? Width { get; init; }           // bekda string? (ProductSize entity)
    public string? Height { get; init; }
    public string? Length { get; init; }
    public int? PriceCurrency { get; init; }      // 0 = UZS, 1 = USD
    public decimal? SellPrice { get; init; }
    public decimal? SellPriceUzs { get; init; }
    public decimal? SellPriceUsd { get; init; }
    public bool? IsDiscount { get; init; }
    public decimal? NewPrice { get; init; }
}

public record ReceiptOptions
{
    public string? Title { get; init; }           // do'kon nomi
    public string? StorePhone { get; init; }
    public bool IsDelivery { get; init; }         // yetkazish cheki: narxlarsiz
    public int Columns { get; init; } = 48;       // 80 mm = 48, 58 mm = 32
    public byte[]? Logo { get; init; }            // Receipt.Image(...) natijasi
    public string SiteUrl { get; init; } = "https://business.mebelim.uz";   // mahsulot QR: {SiteUrl}/products/{id}
}

public static class Receipt
{
    // Mijoz buyurtmasini shu havoladan topadi: order.mebelim.uz/[key] → GET api/Order/session/{key}
    public const string OrderSiteUrl = "https://order.mebelim.uz/";

    static readonly string[] Statuses =
        { "-", "Yangi", "Qabul qilindi", "Jarayonda", "Yetkazilishga tayyor", "Yetkazilmoqda", "Topshirildi", "Bekor qilingan" };

    public static (byte[] Bytes, string Text) Build(ReceiptOrder o, ReceiptOptions opt)
    {
        var p = new EscPos(opt.Columns);
        var rate = o.ExchangeRateAtOrder ?? 0;

        p.Raw(0x1B, 0x40);
        if (opt.Logo is { Length: > 0 })
            p.Align(1).Raw(0x1B, 0x33, 24).Raw(opt.Logo).Raw(0x1B, 0x32);

        p.Align(1).Bold(true).Size(0x11).Line(opt.Title ?? "MEBELIM")
            .Line($"{(opt.IsDelivery ? "YETKAZISH" : "SOTUV")} #{o.Sku ?? o.Id}")
            .Size(0).Bold(false).Line().Align(0);

        p.Line(Date(o.OrderDate ?? DateTime.UtcNow, true));
        if (o.ReadyDate is { } ready) p.Line($"Tayyor bo'lish: {Date(ready, false)}");
        var seller = o.Seller?.FullName ?? o.SellerFullName;
        if (!string.IsNullOrWhiteSpace(seller)) p.Line($"Sotuvchi: {seller}");
        if (!opt.IsDelivery && o.Status is > 0 and < 8) p.Line($"Holati: {Statuses[o.Status.Value]}");
        if (!string.IsNullOrWhiteSpace(o.CustomerFullName)) p.Bold(true).Line($"Mijoz: {o.CustomerFullName}").Bold(false);
        if (!string.IsNullOrWhiteSpace(o.CustomerPhoneNumber)) p.Line($"Tel: {Phone(o.CustomerPhoneNumber)}");
        if (!opt.IsDelivery)
        {
            if (o.DeliveryFee > 0) p.LR("Yetkazib berish:", Money(o.DeliveryFee.Value));
            if (o.ServiceFee > 0) p.LR("O'rnatish / xizmat:", Money(o.ServiceFee.Value));
            if (rate > 0 && (o.Products ?? new()).Any(i => i.PriceCurrency == 1)) p.LR("Kurs (1 USD):", Money(rate));
        }
        p.Rule();

        foreach (var i in o.Products ?? new())
        {
            p.Bold(true).Line(string.IsNullOrWhiteSpace(i.ProductName) ? $"Mahsulot #{i.ProductId}" : i.ProductName!).Bold(false);
            var color = string.Join(" ", new[] { i.ColorName, string.IsNullOrWhiteSpace(i.ColorCode) ? null : "#" + i.ColorCode }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (color.Length > 0) p.Line($"Rang: {color}");

            if (opt.IsDelivery) p.Line($"Soni: {i.Quantity} ta");
            else
            {
                var usd = i.PriceCurrency == 1;
                var (native, uzs) = Line(i, rate);
                var unit = i.Quantity > 0 ? native / i.Quantity : native;
                p.LR($"{i.Quantity} x {Cur(unit, usd)}", Cur(native, usd));
                if (usd && rate > 0) p.LR("", "~ " + Money(uzs));
            }
            p.Rule();
        }

        decimal total = o.TotalAmount ?? 0, advance = o.AdvancePayment ?? 0, discount = o.DiscountAmount ?? 0;
        if (!opt.IsDelivery)
        {
            if (discount > 0 && o.OriginalAmount > 0) p.LR("Boshlang'ich summa:", Money(o.OriginalAmount.Value));
            if (discount > 0) p.LR("Chegirma:", "- " + Money(discount));
            p.Bold(true).LR("Jami:", Money(total)).Bold(false);
            p.LR("To'langan:", Money(advance));
        }
        p.Bold(true).LR("Qolgan qarz:", Money(o.RemainingAmount ?? total - advance)).Bold(false);

        if (!string.IsNullOrWhiteSpace(o.Notes)) p.Line().Line($"Izoh: {o.Notes}");

        p.Line().Align(1).Bold(true).Line("XARIDINGIZ UCHUN RAHMAT!").Bold(false);
        if (!string.IsNullOrWhiteSpace(o.SessionKey))
            p.Line("Buyurtmangizni kuzatish:").Raw(Qr(OrderSiteUrl + o.SessionKey)).Line();
        if (!string.IsNullOrWhiteSpace(opt.StorePhone))
            p.Bold(true).Size(0x10).Line(Phone(opt.StorePhone!)).Size(0).Bold(false);
        p.Align(0).Raw(0x1B, (byte)'d', 7).Raw(0x1D, (byte)'V', 0);

        return (p.Bytes(), p.Text());
    }

    // Mahsulot yorlig'i: nomi, kategoriya/brend/material, artikul, har bir o'lcham narxi va QR.
    // QR → {SiteUrl}/products/{id}: xodim telefon bilan skanerlasa mahsulot kartochkasi ochiladi.
    public static (byte[] Bytes, string Text) BuildProduct(ProductLabel pr, ReceiptOptions opt)
    {
        var p = new EscPos(opt.Columns);
        var t = pr.Translations?.FirstOrDefault(x => x.Language == "uz") ?? pr.Translations?.FirstOrDefault();

        p.Raw(0x1B, 0x40);
        if (opt.Logo is { Length: > 0 })
            p.Align(1).Raw(0x1B, 0x33, 24).Raw(opt.Logo).Raw(0x1B, 0x32);
        p.Align(1).Bold(true).Size(0x11).Line(opt.Title ?? "MEBELIM").Size(0).Line()
            .Size(0x01).Line(string.IsNullOrWhiteSpace(t?.ProductName) ? $"Mahsulot #{pr.Id}" : t!.ProductName!)
            .Size(0).Bold(false).Align(0).Rule();

        if (!string.IsNullOrWhiteSpace(t?.CategoryName)) p.Line($"Kategoriya: {t!.CategoryName}");
        if (!string.IsNullOrWhiteSpace(pr.ProductBrandName)) p.Line($"Brend: {pr.ProductBrandName}");
        if (!string.IsNullOrWhiteSpace(t?.Material)) p.Line($"Material: {t!.Material}");
        if (!string.IsNullOrWhiteSpace(pr.Sku)) p.Line($"Artikul: {pr.Sku}");
        p.Line($"ID: {pr.Id}").Rule();

        // Narx — mahsulotlar ro'yxatidagi kabi (MahsulotlarContent getVariantPrice)
        var sizes = pr.ProductSizes ?? new();
        for (int i = 0; i < sizes.Count; i++)
        {
            var s = sizes[i];
            var usd = s.PriceCurrency == 1;
            var regular = (usd ? s.SellPriceUsd : s.SellPriceUzs) ?? 0;
            if (regular <= 0) regular = s.SellPrice ?? 0;
            var price = s.IsDiscount == true && s.NewPrice > 0 ? s.NewPrice.Value : regular;

            var dims = new[] { s.Width, s.Height, s.Length }.Select(d => string.IsNullOrWhiteSpace(d) ? "0" : d.Trim()).ToArray();
            var label = dims.Any(d => d != "0") ? string.Join(" x ", dims) : sizes.Count > 1 ? $"Variant {i + 1}" : "Narxi:";
            p.Bold(true).LR(label, Cur(price, usd)).Bold(false);
            if (s.IsDiscount == true && regular > price) p.LR("", $"Chegirma! Eski narx: {Cur(regular, usd)}");
        }
        if (sizes.Count > 0) p.Rule();

        p.Align(1).Raw(Qr($"{opt.SiteUrl.TrimEnd('/')}/products/{pr.Id}")).Line().Line($"#{pr.Id}");
        if (!string.IsNullOrWhiteSpace(opt.StorePhone))
            p.Bold(true).Size(0x10).Line(Phone(opt.StorePhone!)).Size(0).Bold(false);
        p.Align(0).Raw(0x1B, (byte)'d', 5).Raw(0x1D, (byte)'V', 0);

        return (p.Bytes(), p.Text());
    }

    // Frontdagi utils.resolveLineMoney bilan bir xil: bek `salePrice`ni ba'zan asl valyutada,
    // ba'zan UZS'ga o'girilgan holda saqlaydi. `productSize.sellPrice` doim asl —
    // salePrice qaysi biriga yaqinroq ekaniga qarab aniqlaymiz.
    public static (decimal Native, decimal Uzs) Line(ReceiptItem i, decimal rate)
    {
        decimal sp = i.SalePrice ?? 0, nativeRef = (i.ProductSize?.SellPrice ?? 0) * i.Quantity;
        if (i.PriceCurrency != 1 || rate <= 0)
        {
            var v = sp > 0 ? sp : nativeRef;
            return (v, v);
        }
        var spIsNative = sp <= 0 || Math.Abs(sp - nativeRef) <= Math.Abs(sp - nativeRef * rate);
        var native = sp > 0 ? (spIsNative ? sp : sp / rate) : nativeRef;
        return (native, native * rate);
    }

    // O'zbekiston UTC+5, yozgi vaqt yo'q. Kind=Unspecified ("Z"siz) — brauzerdagi kabi
    // mahalliy vaqt deb olinadi (frontdagi new Date() ham shunday ko'rsatadi).
    static string Date(DateTime d, bool withTime)
    {
        var t = d.Kind == DateTimeKind.Unspecified ? d : d.ToUniversalTime().AddHours(5);
        return t.ToString(withTime ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    static string Money(decimal v) =>
        Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ') + " so'm";

    static string Cur(decimal v, bool usd) =>
        usd ? "$" + v.ToString("#,0.##", CultureInfo.InvariantCulture).Replace(',', ' ') : Money(v);

    static string Phone(string raw)
    {
        var d = new string(raw.Where(char.IsDigit).ToArray());
        if (d.Length == 9) d = "998" + d;
        return d.Length == 12 && d.StartsWith("998")
            ? $"+998 ({d[3..5]}) {d[5..8]}-{d[8..10]}-{d[10..]}"
            : raw;
    }

    static byte[] Qr(string text, int size = 8)
    {
        var data = Encoding.UTF8.GetBytes(text);
        int len = data.Length + 3;
        var b = new List<byte>();
        b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 });          // model 2
        b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, (byte)size });          // o'lcham
        b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31 });                // xato tuzatish M
        b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, (byte)(len & 0xFF), (byte)(len >> 8), 0x31, 0x50, 0x30 });
        b.AddRange(data);
        b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 });                // chop etish
        return b.ToArray();
    }

    // Logotip: kulrang piksellar (0..255, qatorma-qator, w*h) → ESC * 24-nuqtali tasvir.
    // Rasmni o'qish platformaga bog'liq (agentda System.Drawing, bekda ImageSharp/Skia) —
    // shuning uchun bu yerga faqat tayyor piksellar keladi.
    public static byte[] Image(byte[] gray, int w, int h, double gamma = 1.9)
    {
        int[,] bayer =
        {
            { 0, 48, 12, 60, 3, 51, 15, 63 }, { 32, 16, 44, 28, 35, 19, 47, 31 },
            { 8, 56, 4, 52, 11, 59, 7, 55 }, { 40, 24, 36, 20, 43, 27, 39, 23 },
            { 2, 50, 14, 62, 1, 49, 13, 61 }, { 34, 18, 46, 30, 33, 17, 45, 29 },
            { 10, 58, 6, 54, 9, 57, 5, 53 }, { 42, 26, 38, 22, 41, 25, 37, 21 },
        };
        var b = new List<byte>(w * h / 8 + h);
        for (int y = 0; y < h; y += 24)
        {
            b.AddRange(new byte[] { 0x1B, (byte)'*', 33, (byte)(w & 0xFF), (byte)(w >> 8) });
            for (int x = 0; x < w; x++)
            {
                var col = new byte[3];
                for (int k = 0; k < 24 && y + k < h; k++)
                {
                    var lum = Math.Pow(gray[(y + k) * w + x] / 255.0, 1.0 / gamma);
                    if (lum < (bayer[(y + k) & 7, x & 7] + 0.5) / 64.0) col[k / 8] |= (byte)(0x80 >> (k % 8));
                }
                b.AddRange(col);
            }
            b.Add(0x0A);
        }
        return b.ToArray();
    }

    // Printer shriftida faqat ASCII bor: oʻ/gʻ, kirill va boshqa belgilar "?" bo'lib chiqardi.
    public static string Ascii(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c < 128) { sb.Append(c); continue; }
            var lower = char.ToLowerInvariant(c);
            if (Translit.TryGetValue(lower, out var t))
            {
                sb.Append(c != lower && t.Length > 0 ? char.ToUpperInvariant(t[0]) + t[1..] : t);
                continue;
            }
            var bare = c.ToString().Normalize(NormalizationForm.FormD)[0];   // é → e
            sb.Append(bare < 128 ? bare : '?');
        }
        return sb.ToString();
    }

    static readonly Dictionary<char, string> Translit = new()
    {
        ['ʻ'] = "'", ['ʼ'] = "'", ['‘'] = "'", ['’'] = "'", ['´'] = "'", ['“'] = "\"", ['”'] = "\"",
        ['«'] = "\"", ['»'] = "\"", ['–'] = "-", ['—'] = "-", ['…'] = "...", ['№'] = "No", ['≈'] = "~",
        ['×'] = "x", [' '] = " ", [' '] = " ",
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo",
        ['ж'] = "j", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "x", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "'",
        ['ы'] = "i", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        ['ў'] = "o'", ['қ'] = "q", ['ғ'] = "g'", ['ҳ'] = "h",
    };

    sealed class EscPos(int columns)
    {
        readonly List<byte> bytes = new();
        readonly StringBuilder text = new();

        public EscPos Raw(params byte[] b) { bytes.AddRange(b); return this; }
        public EscPos Align(int a) => Raw(0x1B, (byte)'a', (byte)a);
        public EscPos Bold(bool on) => Raw(0x1B, (byte)'E', (byte)(on ? 1 : 0));
        public EscPos Size(byte n) => Raw(0x1D, (byte)'!', n);
        public EscPos Rule() => Line(new string('-', columns));

        public EscPos Line(string s = "")
        {
            var t = Ascii(s);
            bytes.AddRange(Encoding.ASCII.GetBytes(t + "\n"));
            text.Append(t).Append('\n');   // AppendLine — \r\n Windows'da, \n Linux'da: test bekda yiqilardi
            return this;
        }

        // Chap matn + o'ng chetga tekislangan qiymat; sig'masa chap qismi qisqaradi
        public EscPos LR(string left, string right)
        {
            left = Ascii(left); right = Ascii(right);
            var room = columns - right.Length - 1;
            if (left.Length > room) left = left[..Math.Max(0, room)];
            return Line(left + new string(' ', Math.Max(1, columns - left.Length - right.Length)) + right);
        }

        public byte[] Bytes() => bytes.ToArray();
        public string Text() => text.ToString();
    }
}
