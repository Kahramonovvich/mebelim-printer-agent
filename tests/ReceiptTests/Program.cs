// Chek va yorliq samples/*.json → samples/*.expected.txt bilan bir xilmi + QR ichidagi havolalar.
// Chek ataylab o'zgartirilsa — agentni preview:true bilan ishga tushirib, .expected.txt ni yangilang.
using System.Text;
using System.Text.Json;
using Mebelim.Printing;

var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples") + Path.DirectorySeparatorChar;
var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

var o = JsonDocument.Parse(File.ReadAllText(dir + "order.json")).RootElement;
var (ob, ot) = Receipt.Build(o.GetProperty("order").Deserialize<ReceiptOrder>(web)!, new ReceiptOptions
{
    Title = o.GetProperty("title").GetString(),
    StorePhone = o.GetProperty("storePhone").GetString(),
    Columns = 48,
});
Check(ot == File.ReadAllText(dir + "order.expected.txt"), "chek == order.expected.txt");
Check(Encoding.ASCII.GetString(ob).Contains("https://order.mebelim.uz/a1b2c3d4e5"), "chek QR = order.mebelim.uz/{sessionKey}");

var p = JsonDocument.Parse(File.ReadAllText(dir + "product.json")).RootElement;
var product = p.GetProperty("product").Deserialize<ProductLabel>(web)!;
var (pb, pt) = Receipt.BuildProduct(product, new ReceiptOptions
{
    Title = p.GetProperty("title").GetString(),
    StorePhone = p.GetProperty("storePhone").GetString(),
    SiteUrl = p.GetProperty("siteUrl").GetString()!,
    Columns = 48,
});
Check(pt == File.ReadAllText(dir + "product.expected.txt"), "yorliq == product.expected.txt");
Check(Encoding.ASCII.GetString(pb).Contains("https://business.mebelim.uz/products/11"), "yorliq QR = {siteUrl}/products/{id}");

var (_, narrow) = Receipt.BuildProduct(product, new ReceiptOptions { Columns = 32 });
Check(narrow.Split('\n').All(l => l.Length <= 32), "58 mm: qatorlar 32 dan oshmaydi");

return fails;
