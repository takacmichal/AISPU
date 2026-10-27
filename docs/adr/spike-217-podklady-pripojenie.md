# ADR spike #217: Podklady a údaje pre žiadosť o pripojenie FVE k distribučke

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #217 (nadradené #4)
- **Súvisí:** spike #203 (reálny proces pripojenia), BPMN proces #64 a mapovanie `ConnectionRequest` v `docs/bpmn` (PR #282), #207 (stavy)

## Kontext

Systém evidenciu žiadosti o pripojenie FVE k distribučnej sústave (ZSD, SSD, VSD) a sleduje stavy a lehoty (#85, #86). Distribučka je v projekte simulovaná (#87). Treba určiť povinné údaje a prílohy, lehoty a model žiadosti. Východisko: verejné postupy distribútorov a zákon 251/2012 v znení 133/2024 (od 1. 1. 2025, §5a až §5g) zozbierané v spiku #203. Zdroje sú weby distribútorov a neoficiálne znenie zákona; pred finálnym modelom treba ísť do platných prevádzkových poriadkov.

## Zvažované možnosti

| Možnosť | Popis | Plus | Mínus |
|---|---|---|---|
| A. Jeden model, dve vetvy podľa výkonu, lehoty v konfigurácii | `ConnectionRequest` s typom `SmallSource` (do 10,8 kW) / `LargeSource`; lehoty z `appsettings` | zodpovedá reálnemu procesu, jednoduchá zmena lehôt | simulácia, nie reálna integrácia |
| B. Samostatné entity pre malý a väčší zdroj | dva modely | presnejšie | duplicita, viac kódu |
| C. Presný model každého distribútora (ZSD/SSD/VSD) | rozdiely v lehotách a formulároch | najvernejšie | nepomer k rozsahu; zdroje sú neúplné |

## Rozhodnutie

**Možnosť A podľa spiku #203: malý zdroj do 10,8 kW (lehota 10 pracovných dní na návrh zmluvy), väčší zdroj nad 10,8 kW (lehota 30 dní). Lehoty sú konfigurovateľné v `appsettings`.** Jeden "virtuálny" distribútor s voliteľným prepísaním lehôt pre ZSD/SSD/VSD.

### Povinné údaje žiadosti (model `ConnectionRequest`, návrh)

- Žiadateľ (zákazník alebo splnomocnená osoba), odberné miesto (EIC, adresa), typ zdroja `SmallSource` / `LargeSource` odvodený z **celkového menovitého AC výkonu meničov** (prah 10,8 kW, ZSD) [1].
- Parametre zdroja: výkon, počet a typ meničov a panelov, hlavný istič odberného miesta (pri malom zdroji súvisí s istením do 16 A na fázu podľa vzorových zmlúv SSD a vývodovým ističom pri hlavnom ističi do 80 A, ZSD) [1][4].
- Stav (#207): `Draft`, `Submitted`, `InfoRequested`, `Rejected`, `ContractOffered`, `ContractSigned`, `ContractSubmitted`, `Concluded`; dátum podania spúšťa lehoty.
  - `Draft` zodpovedá stavu „Pripravená“ z ticketu #85: žiadosť je založená a majú k nej byť priložené podklady, ale ešte nebola podaná distribučke.
  - Prílohy (`SingleLineDiagram`, `OwnerConsent`) sa v MVP ukladajú aj s obsahom priamo v databáze (max. 5 MB na súbor, telo požiadavky max. cca 70 MB). Metadatoví klienti môžu poslať len metadáta; ak je obsah uložený, dá sa stiahnuť cez `GET /api/connection-requests/{id}/attachments/{attachmentId}`.
  - Súhlas vlastníka (`OwnerConsent`): ticket #85 ho vyžaduje pri každej žiadosti, tabuľka nižšie ho spomína len pri malom zdroji. Implementácia ho preto zatiaľ vyžaduje pri oboch typoch; definitívne rozhodnutie treba potvrdiť s tímom.
  - Jedna inštalácia môže mať viac žiadostí v čase (napr. po zamietnutí vzniká nová), preto duplicitu neblokuje unikátny index.

### Zoznam príloh a lehôt

| Fáza | Malý zdroj (do 10,8 kW) | Väčší zdroj (nad 10,8 kW) |
|---|---|---|
| Žiadosť (elektronicky) | žiadosť (e-žiadosť distribútora), jednopólová schéma, datasheety panelov a meničov, fotografia rozvádzača, splnomocnenie pri zastúpení [1] | žiadosť, jednopólová schéma, datasheety, EIC odberného miesta; voliteľne výpis z katastra pri nezáväznom posúdení [2][3] |
| Lehota na návrh zmluvy od úplnej žiadosti | **10 pracovných dní** (zákon §5g ods. 2; ZSD, SSD) [5] | **30 dní** (zákon §5c ods. 4); ZSD lokálne zdroje 15 pracovných dní [5][2] |
| Akceptácia zmluvy zákazníkom | SSD 10 kalendárnych dní, ZSD do 6 mesiacov od podpisu distribútora [1][4] | NN/VN 30 dní alebo úhrada do 14 dní od zálohovej faktúry (§5e ods. 1 a 2) [5] |
| Vybudovanie zdroja | platnosť zmluvy 6 mesiacov [1][4] | technické podmienky do 24 mesiacov [5] |
| Pri uvedení do prevádzky | zmluva o zodpovednosti za odchýlku, elektromer do 5 pracovných dní po splnení podmienok, oznámenie o pripojení [1] | revízna správa a protokoly (ZSD uvádza 13 príloh, doložiť min. 20 dní pred funkčnou skúškou), funkčná skúška, oznámenie o prevádzke [3] |
| Poplatok | pre nepodnikateľa bezodplatné [1] | podľa cenníka distribútora (**neoverené**) |

### Konfigurácia lehôt (návrh tvaru, kľúče sa môžu zmeniť v implementácii #85/#86)

```json
"ConnectionRequest": {
  "SmallSourceMaxKw": 10.8,
  "SmallSource": { "ProposalWorkingDays": 10, "AcceptanceCalendarDays": 10, "ContractValidityMonths": 6, "MeterInstallWorkingDays": 5 },
  "LargeSource": { "ProposalCalendarDays": 30, "AcceptanceCalendarDays": 30, "TechnicalConditionsMonths": 24, "PaymentCalendarDays": 14 }
}
```

### Zjednodušenia v aplikácii

Jeden virtuálny distribútor; dve vetvy podľa výkonu; zlúčenie štúdie pripojiteľnosti a stanoviska do jednej úlohy "Posúdenie distribučkou" (súhlas / nižší výkon / zamietnutie); platby a cenníky mimo rozsahu (poplatok = konfigurovateľná hodnota, 0 pre malé zdroje); revízna správa ako povinná príloha oboch vetiev (zjednodušenie); veľké komerčné zdroje (nad 100 kW), ÚRSO a SIEA mimo rozsahu.

## Dôsledky

- (+) Lehoty sa dajú meniť bez zásahu do kódu; model pokrýva proces z BPMN #64 a stories #85 až #87.
- (-) Presnosť závisí od neúplných zdrojov; pri reálnom nasadení by sa musel model overiť s distribútorom.
- Implementácia **musí** dodržať: prah 10,8 kW, lehoty 10 pracovných dní / 30 dní konfigurovateľné v `appsettings`.

## Riziká a neoverené

- Zákonná definícia malého zdroja (10 vs. 10,8 kW, §4a z. 309/2009); staršie dokumenty SSD/VSD uvádzajú 10 kW.
- Revízna správa a funkčné skúšky pre malý zdroj v aktuálnom prevádzkovom poriadku (v modeli sú povinné zjednodušene).
- Samostatná lehota a poplatok za štúdiu pripojiteľnosti; sumy poplatkov mimo malého zdroja.
- Lehota VSD: zdroje uvádzajú 10 pracovných dní, 15 pracovných dní aj 30 dní (rozpor).
- Plné znenie aktuálnych prevádzkových poriadkov ZSD/SSD/VSD (čítané len sekundárne súhrny), registrácia výrobcu (ÚRSO/OKTE), SIEA podpora, stavebné ohlásenie, lehoty zmluvy o dodávke.
- Znenie zákona je z neoficiálneho zdroja (zakonypreludi.sk), nie zo Slov-lex.

## Zdroje

1. ZSD, malé zdroje do 10 kW: https://www.zsdis.sk/Uvod/Vyrobcovia/Uzatvorenie-zmluvneho-vztahu/Male-zdroje-do-10-kW
2. ZSD, lokálne zdroje: https://www.zsdis.sk/Uvod/Vyrobcovia/Uzatvorenie-zmluvneho-vztahu/Lokalne-zdroje
3. ZSD, uvedenie do prevádzky lokálnych a komerčných zdrojov: https://www.zsdis.sk/Uvod/Vyrobcovia/Uvedenie-do-prevadzky/Lokalne-zdroje-a-komercne-zdroje
4. SSD, malé zdroje elektriny: https://www.ssd.sk/vyrobcovia-elektriny/male-zdroje-elektriny?page_id=5530
5. Zákon 251/2012 v znení 133/2024 (od 1. 1. 2025): https://www.zakonypreludi.sk/print/zz/2024-133/znenie-20250101.htm
6. VSD, sprievodca procesom a lokálny zdroj: https://www.vsds.sk/mdoc/dso.D1B00.A/doc/VSD_letak_sprievodca_procesom_V8.pdf, https://www.vsds.sk/edso/domov/vyrobcovia-elektriny/pripojenie-zdroja/lokalny-zdroj

## Nadväzujúce issues

- #85 Vytvoriť žiadosť o pripojenie, #86 Sledovať stav a lehoty žiadosti, #87 Simulovať odpoveď distribučky (feature #23); #244 Stepper procesu pripojenia (UI).
- Podľa spiku #203 navrhované (nevytvorené): parametrizácia lehôt distribútorov, overenie revíznej správy a funkčných skúšok u distribútora, overenie platného znenia prevádzkových poriadkov.
- Výsledok sa podľa zadania má zapísať aj ako komentár k issue; komentár som nepridával.
