# Mebelim Printer Agent

Kassa kompyuteridagi chek printerini Mebelim bilan ulaydi: sotuv cheki, yetkazish cheki va mahsulot yorlig'i (QR bilan).

## Yuklab olish

**[MebelimPrinterAgent-Setup.exe](https://github.com/Kahramonovvich/mebelim-printer-agent/releases/latest/download/MebelimPrinterAgent-Setup.exe)** — har doim oxirgi versiya.

## O'rnatish

1. Printer ulangan kompyuterda faylni ishga tushiring. Admin huquqi kerak emas.
2. Windows «Windows protected your PC» desa: **More info / Подробнее → Run anyway / Выполнить в любом случае**.
3. Mebelim'da birinchi chekni chiqaring — printerni bir marta tanlaysiz, keyin so'ramaydi.

Agent Windows bilan birga yashirin ishga tushadi va yangi versiyalarni o'zi o'rnatadi.
Telefon va boshqa kompyuterlarga hech narsa o'rnatish shart emas.

## Dasturchilar uchun

- `127.0.0.1:1337`: `GET /health`, `GET /printers`, `POST /printer {name}`, `POST /print {order | product | dataBase64, …, preview}`, `POST /test`.
- `Receipt.cs` — chek va yorliq (ESC/POS), mustaqil fayl: bek ham shu faylni ishlatadi.
- Test: `dotnet run --project tests/ReceiptTests` — `samples/*.json` → `samples/*.expected.txt`.
  Chek ataylab o'zgarsa: agentni `preview: true` bilan chaqirib, `.expected.txt` ni yangilang.

### Yangi versiya chiqarish

Kodni `main` ga push qiling, keyin **Actions → release → Run workflow → versiya** (masalan `2.0.1`)
yoki `git tag v2.0.1 && git push origin v2.0.1`. GitHub o'zi test qiladi, agent va o'rnatuvchini yig'adi,
reliz chiqaradi. Do'konlardagi agentlar 6 soat ichida o'zi yangilanadi. Versiyalar faqat oldinga:
orqaga qaytish uchun eski kodni yangi raqam bilan chiqaring.
