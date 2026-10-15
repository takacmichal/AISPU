# ADR spike #219: Generovanie PDF ponúk a zmlúv v .NET

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #219 (nadradené #3)
- **Súvisí:** #80 Vygenerovať PDF zmluvy, #79 zmluva z ponuky, #207 (stav `PdfGenerated`)

## Kontext

Ponuky a zmluvy sa generujú ako PDF na serveri (.NET 10 API, beží v Linux kontajneri v Azure Container Apps). Dokumenty sú v slovenčine (diakritika, formát čísel `12 345,67 €`), obsahujú tabuľku položiek, hlavičku a číslovanie strán. Rozhoduje licencia (školský projekt, no s možnosťou komerčného použitia), podpora Linuxu bez systémových fontov a jednoduchosť šablón v C#.

## Zvažované možnosti

| Kritérium | **QuestPDF** | iText (iText Core) | PDFsharp / MigraDoc | HTML na PDF cez headless Chromium |
|---|---|---|---|---|
| Licencia (k 15. 10. 2026) | Community License 3.0 (účinná od 6. 7. 2026): zdarma pre jednotlivcov a firmy s ročným obratom pod 1 000 000 USD, neziskové organizácie, akademické inštitúcie a open-source; **verejný sektor a burzové spoločnosti nie** [1] | AGPL, alebo platená komerčná licencia [2] | do verzie 6.2.4 MIT, **od 6.5.0 podľa nuspec AGPL-3.0-only** (duálna licencia) [3] | závisí od knižnice (**neoverené**) a vyžaduje Chromium v image |
| Šablóny | Fluent C# API (kód = šablóna), tabuľky, hlavička, päta, čísla strán | nízkoúrovňové API, `pdfHTML` je samostatný doplnok | MigraDoc pre dokumenty, PDFsharp pre kreslenie | HTML + CSS, najflexibilnejší vzhľad |
| Linux bez systémových fontov | funguje (vstavané fonty), **overené spustením** | neoverené | vyžaduje fonty alebo vlastný resolver (neoverené) | vyžaduje fonty v image |
| Veľkosť a štart | iba NuGet balík, generovanie v desiatkach ms | NuGet | NuGet | stovky MB v image, pomalý štart |
| Zhoda s rozhodnutím tímu | **vybrané** | zamietnuté (AGPL) | zamietnuté (AGPL v aktuálnej verzii) | zamietnuté (veľkosť a zložitosť) |

## Rozhodnutie

**QuestPDF (Community license), generovanie na serveri.** Nastavenie pri štarte: `QuestPDF.Settings.License = LicenseType.Community;`. Balík `QuestPDF` pridať do `Directory.Packages.props` (overená verzia **2026.9.1** na NuGet).

Licenčné odôvodnenie: projekt je školský (akademická inštitúcia a vzdelávanie) a SolarOps je malá firma pod hranicou obratu. Ak by sa produkt komerčne nasadil u subjektu mimo týchto kategórií (napr. verejný sektor), treba kúpiť platenú licenciu alebo vymeniť knižnicu; generovanie je za rozhraním (`IPdfDocumentGenerator`), takže výmena je lokálna.

### Overenie šablóny a diakritiky (spustené)

Vzorová zmluva (`docs/adr/assets/spike-219-sample.pdf`) obsahuje hlavičku, text so znakmi `ľščťžýáíéúäôňďĺŕ ĽŠČŤŽÝÁÍÉÚÄÔŇĎĹŔ`, eurá, tabuľku a číslovanie strán. Vygenerovaná bola konzolovou aplikáciou na .NET 10 a QuestPDF 2026.9.1:

- macOS: PDF 32 918 B, generovanie 132 ms; vizuálna kontrola: všetky diakritické znaky sa zobrazili správne.
- Linux (`mcr.microsoft.com/dotnet/sdk:10.0`, v kontajneri bez nainštalovaných fontov, `fc-list` vrátil 0): rovnaký výstup (32 918 B, 224 ms vrátane prvého spustenia), takže knižnica použila vstavané fonty a nepotrebuje fonty v image.

```csharp
QuestPDF.Settings.License = LicenseType.Community;
Document.Create(c => c.Page(p =>
{
    p.Size(PageSizes.A4); p.Margin(40);
    p.Header().Text("Zmluva o dielo č. 2026/001").Bold().FontSize(18);
    p.Content().Column(col => { /* texty, Table() s položkami */ });
    p.Footer().AlignCenter().Text(x => { x.Span("Strana "); x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
})).GeneratePdf(path);
```

Formátovanie čísel a dátumov robiť s `CultureInfo("sk-SK")` (nie je súčasť PDF knižnice).

## Dôsledky

- (+) Žiadna systémová závislosť (fonty, Chromium) v kontajneri; šablóny sú testovateľný C# kód.
- (+) Generovanie je rýchle, vhodné aj do synchrónneho endpointu `POST /api/contracts/{id}/pdf` (#80).
- (-) Licencia je viazaná na podmienky (obrat, typ organizácie); treba ich pri komerčnom nasadení znovu overiť. Licenčné podmienky sa menia (verzia 3.0 platí od 6. 7. 2026).
- (-) Vzhľad definovaný v kóde, nie v dizajnérskom nástroji; zložité grafické šablóny sú prácnejšie než HTML.
- Implementácia **musí** použiť QuestPDF Community a generovať na serveri; PDF sa neukladá do entity ako zdroj pravdy, ukladá sa (alebo regeneruje) podľa návrhu v #80.

## Riziká a neoverené

- Plný text *License Selection Guide* som nečítal; zhrnutie podmienok je z textu Community License [1] (**neoverené** pre hraničné prípady).
- Kvalitu výstupu som overil vizuálne na jednej vzorke; fonty pre tučné/kurzívne varianty a dlhé tabuľky cez viac strán nie sú preskúmané.
- Hodnoty iText a HTML na PDF vychádzajú z licenčných údajov balíkov, nie z vlastného testu.

## Zdroje

1. QuestPDF Community License 3.0: https://www.questpdf.com/license/community.html (prehľad: https://www.questpdf.com/license/)
2. iText 9.8.0 na NuGet (autor Apryse, licencia AGPL): https://www.nuget.org/packages/itext
3. PDFsharp na NuGet, nuspec verzií 6.2.4 (MIT) a 6.5.0 (AGPL-3.0-only): https://www.nuget.org/packages/PDFsharp

## Nadväzujúce issues

- #80 Vygenerovať PDF zmluvy (feature #21), #79 Vytvoriť zmluvu z prijatej ponuky, #241 Zmluva: náhľad PDF, podpis a záloha (UI).
- Výsledok spiku sa podľa zadania má zapísať aj ako komentár k issue; komentár som nepridával (stačí odkaz na ADR po merge).
