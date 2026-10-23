# Spike #203: Pripojenie FVE do distribučnej sústavy (SR)

Dátum prieskumu: 2026-10-15. Značka **neoverené** = nenašiel som to v primárnom zdroji; nič som nedomýšľal. Údaje sú z webov distribútorov a znenia zákona sprostredkovaného cez zakonypreludi.sk (nie slov-lex), preto pred finálnym modelom odporúčam overiť platné znenie.

## 1. Kľúčové zistenia

- Od 1.1.2025 (novela 133/2024 Z.z.) sú v zákone 251/2012 nové §5a až §5g o pripojení do sústavy. Pre malé zdroje platí osobitný §5g: návrh zmluvy do **10 pracovných dní**. [1]
- Prevádzkovateľ distribučnej sústavy (PDS) zodpovedá za pripojenie: ZSD (Západoslovenská, ZSDIS), SSD (Stredoslovenská), VSD (Východoslovenská). Pracujú na základe schváleného prevádzkového poriadku (PP, schvaľuje ÚRSO) a technických podmienok. [8][9]
- Hranica malého zdroja: ZSD uvádza **do 10,8 kW** (súčet menovitých AC výkonov meničov, podľa zákona 309/2009 v znení od 1.12.2022). [3] Staršie dokumenty SSD/VSD uvádzajú 10 kW. [6][10] Zákon 251/2012 §4 ods. 4: výroba do **11 kW** nie je podnikaním v energetike (ak sa neuplatňuje doplatok). [11] Presná zákonná definícia "malého zdroja" (§4a z. 309/2009) **neoverená** (nezískal som text).
- Pripojenie malého zdroja je pre nepodnikateľa **bezodplatné** (ZSD, VSD; podmienka: rovnaké odberné miesto, istenie do 16 A/fáza podľa vzorových zmlúv SSD). [3][4][10] Výška poplatku pre ostatných zdrojov **neoverená** (je v cenníku PDS a rozhodnutí ÚRSO).

## 2. Proces krok za krokom

### 2a. Malý zdroj (FVE do 10,8 kW; podnikanie podľa §4 ods. 4 nie je)

| # | Krok | Zodpovedná strana | Dokument | Lehota | Zdroj |
|---|---|---|---|---|---|
| 1 | Návrh a objednávka FVE (výber výkonu, meniča) | Zákazník + Obchod | ponuka, zmluva o dielo | neoverené (komerčná vec) | – |
| 2 | Kontrola voľnej kapacity (voliteľné) | Zákazník/Projektant | mapa voľnej kapacity PDS | bez lehoty | [2] |
| 3 | Žiadosť o pripojenie malého zdroja (elektronicky, e-Žiadosti ZSD / eVSD / portál SSD); príloha: jednopólová schéma, datasheety panelov a meničov, foto rozvádzača | Zákazník (resp. Obchod/Projektant splnomocnený) | žiadosť (+ splnomocnenie) | úplná žiadosť sa eviduje; neúplná sa vráti | [3][4][6] |
| 4 | Posúdenie žiadosti, návrh Zmluvy o pripojení a prístupe | PDS (Distribučka) | návrh zmluvy + technické podmienky pripojenia | **do 10 pracovných dní** od úplnej žiadosti (zákon §5g ods. 2; ZSD, SSD). VSD FAQ uvádza aj "do 30 dní", starý PP VSD 15 pr. dní: rozpor | [1][3][6][10] |
| 5 | Podpis zmluvy (e-podpis v portáli) | Zákazník, PDS | Zmluva o pripojení (a prístupe) | SSD: vrátiť podpísané do 10 kalendárnych dní; ZSD: do 6 mesiacov od podpisu PDS; zákon pre ostatné NN zdroje 30 dní | [3][6][1] |
| 6 | Úhrada pripojenia | Zákazník | zálohová faktúra | ZSD: 14 kalendárnych dní; nepodnikateľ neplatí, faktúra sa nevystavuje | [3] |
| 7 | Vybudovanie zdroja podľa technických podmienok (montáž, vývodový istič pri hlavnom ističi do 80 A) | Technik (+ Projektant) | projekt/schéma, protokoly, potvrdenie splnenia TP (čestné vyhlásenie pri FVE alebo nahratie dokumentácie) | platnosť zmluvy **6 mesiacov** od podpisu PDS, inak PDS môže odstúpiť (ZSD, SSD) | [3][5][6] |
| 8 | Odborná prehliadka a skúška (revízna správa) | Technik / revízny technik | revízna správa | pre malý zdroj ako samostatný krok na webe ZSD **neuvedená**: neoverené, či ju PDS vyžaduje. Je to povinnosť podľa technických predpisov (nepreverené) | neoverené |
| 9 | Zmluva o prevzatí zodpovednosti za odchýlku (cez dodávateľa) | Zákazník + dodávateľ | zmluva o zodpovednosti za odchýlku | bez lehoty; bez nej sa nepokračuje | [5] |
| 10 | Zmluva o dodávke/odbere (príp. výkup prebytkov) | Zákazník + dodávateľ elektriny | zmluva o dodávke/odkúpení | neoverené | [5] (len odkaz na dodávateľa) |
| 11 | Montáž/výmena elektromera (obojsmerný) | PDS | – | **do 5 pracovných dní** po splnení: podpísaná zmluva + splnené TP + zmluva o odchýlke | [5] |
| 12 | Oznámenie o pripojení (od tohto dátumu je zdroj pripojený; až potom možno zapnúť hlavný vypínač) | PDS | Oznámenie o pripojení malého zdroja | lehota neuvedená | [5] |
| 13 | Oznámenie o prevádzke | Zákazník | Oznámenie o prevádzke | pri elektronickej žiadosti netreba (ZSD); pri papierovej/SSD áno | [5][6] |
| 14 | Prevádzka, fakturácia | Zákazník, dodávateľ, PDS | – | – | – |

Poznámky: ÚRSO oznámenie podľa §6 ods. 5/6 z. 251/2012 sa týka zdrojov do 1 MW (oznámenie do 30 dní), pri zdroji do 11 kW nie je podnikaním v energetike, takže povinnosť odpadá [11] (znenie zákona z neoficiálneho zdroja, nepreverené voči aktuálnemu stavu). Registrácia v SIEA / podpora (Zelená domácnostiam): **neoverené**, nesúvisí priamo s pripojením.

### 2b. Väčší zdroj (nad 10,8 kW, "lokálny zdroj", nad 100 kW komerčný)

| # | Krok | Zodpovedná strana | Dokument | Lehota | Zdroj |
|---|---|---|---|---|---|
| 1 | Nezáväzné posúdenie možnosti pripojenia (voliteľne; "štúdia pripojiteľnosti" je v PP pomenovaná ako samostatná časť, lehota neoverená) | Projektant -> PDS | žiadosť o nezáväznú územnotechnickú informáciu + výpis z katastra | ZSD: do 30 dní; nezáväzné, nepoužije sa na stavebné konanie; poplatok neoverený | [7][12] |
| 2 | Žiadosť o pripojenie (elektronicky; papierové VSD od 10/2022 vracia) | Zákazník/Projektant | žiadosť, jednopólová schéma, datasheety, EIC odberného miesta | PDS má **30 dní** na zaslanie návrhu zmluvy (zákon §5c ods. 4); ZSD lokálne zdroje: 15 pracovných dní; SSD lokálny zdroj: stanovisko 15 pr. dní; neúplná žiadosť sa zamietne do 30 dní | [1][4][9][13] |
| 3 | Rezervácia kapacity (od zaslania návrhu zmluvy); ak treba stavebné povolenie, od stanoviska; bez žiadosti o povolenie do 30 dní kapacita zaniká | PDS | stanovisko / návrh zmluvy | 30 dní | [1] |
| 4 | Podpis Zmluvy o pripojení (a prístupe) | Zákazník, PDS | zmluva s technickými podmienkami | NN/VN: akceptácia do **30 dní** alebo úhrada plnej ceny do 14 dní od zálohovej faktúry (§5e ods. 2); ZSD lokálne zdroje: vrátiť do 12 mesiacov | [1][4] |
| 5 | Projekt, povolenia (stavebné konanie), výstavba | Projektant, Technik | projektová dokumentácia, skutočné vyhotovenie | technické podmienky treba splniť do **24 mesiacov** od uzavretia zmluvy (§5e ods. 1) | [1] |
| 6 | Revízna správa, protokoly (správa o odbornej prehliadke a skúške, kalibrácia meračov, protokol nastavenia ochrán, datasheety, ďalšie podľa PDS) | Technik | zoznam 13 príloh podľa ZSD | doložiť **min. 20 dní pred** termínom funkčných skúšok | [12] |
| 7 | Zmluva o zodpovednosti za odchýlku | Zákazník + dodávateľ | zmluva | podmienka na pokračovanie | [12] |
| 8 | Elektromer | PDS | – | do 5 pracovných dní po podpísanej zmluve o prístupe | [12] |
| 9 | Skúšky riadiaceho systému (nad 100 kW) + 72 h prevádzka | Technik + certifikovaná firma | vecný časový program | program 10 dní pred skúškou | [12] |
| 10 | Funkčná skúška, protokol | PDS + Technik | protokol o vykonaní funkčnej skúšky | termín dohodou; poplatok za zbytočný výjazd (suma neoverená) | [12] |
| 11 | Oznámenie o prevádzke, uvedenie do prevádzky | Zákazník -> PDS | oznámenie o prevádzke | po protokole | [12] |
| 12 | Registrácia/oznámenie ÚRSO (nad 11 kW, do 1 MW oznámenie do 30 dní; nad 1 MW povolenie) | Zákazník | oznámenie / povolenie | 30 dní | [11] (nepreverené) |

## 3. Malý vs. veľký zdroj

| Vlastnosť | Malý zdroj (do 10,8 kW) | Väčší zdroj |
|---|---|---|
| Zmluva | Zmluva o pripojení a prístupe, jednoduchá | Zmluva o pripojení + zmluva o prístupe, technické podmienky |
| Lehota návrhu | 10 pracovných dní | 30 dní (ZSD lokálne 15 pr. dní) |
| Poplatok | zadarmo pre nepodnikateľa | podľa cenníka PDS (neoverené) |
| Funkčné skúšky | nie sú povinné (staršie PP 2015, platnosť neoverená) | povinné |
| Revízna správa | neoverené | povinná príloha |
| Oznámenie o prevádzke | pri e-žiadosti netreba | áno |
| ÚRSO | nie je podnikanie (do 11 kW) | oznámenie/povolenie |
| Platnosť zmluvy | 6 mesiacov | 12 až 24 mesiacov |

## 4. Zjednodušenia pre simuláciu v aplikácii

- Jeden "virtuálny PDS" (Distribučka) s parametrom ZSD/SSD/VSD; rovnaký proces, rozdiely len v lehotách (konfigurácia).
- Dve vetvy podľa výkonu: <=10,8 kW (malý zdroj) a >10,8 kW (lokálny zdroj). Veľké komerčné zdroje (>100 kW, skúšky riadiaceho systému, VVN) mimo rozsahu.
- Lehoty ako konštanty v pracovných dňoch (10 / 30 / 5 / 14 / 6 mes.), simulovaný čas.
- Zlúčiť štúdiu pripojiteľnosti a stanovisko do jednej úlohy "Posúdenie PDS" (výsledok: súhlas / nižší výkon / zamietnutie).
- Vynechať platby a cenníky; poplatok = nastaviteľná hodnota (0 pre malé zdroje).
- Revízna správa ako povinný krok pre obe vetvy (zjednodušenie, keďže presná povinnosť pri malom zdroji je neoverená).
- Dodávateľ elektriny a zodpovednosť za odchýlku ako jedna úloha "Uzavretie zmluvy s dodávateľom".
- Vynechať SIEA/ÚRSO, stavebné povolenie (ako voliteľný subproces pre >10,8 kW).

## 5. Návrh BPMN prvkov (vstup pre #64/#65/#66)

Pooly/roly: Zákazník, Obchod, Projektant, Technik, Distribučka.

1. Start event: "Zákazník má záujem o FVE" (Zákazník)
2. Task: Konzultácia a ponuka (Obchod)
3. Task: Návrh FVE, jednopólová schéma (Projektant)
4. Gateway (XOR): výkon <= 10,8 kW? (malý zdroj / väčší zdroj)
5. Task: Podať žiadosť o pripojenie (Projektant/Zákazník); správa -> Distribučka
6. Task: Skontrolovať úplnosť žiadosti (Distribučka)
7. Gateway (XOR): žiadosť úplná? ne -> Task: Doplniť (Zákazník), späť na 6
8. Task: Posúdiť možnosť pripojenia / kapacitu (Distribučka); [väčší zdroj: voliteľne štúdia pripojiteľnosti]
9. Gateway (XOR): pripojenie možné? nie -> alternatívna ponuka (nižší výkon) alebo koniec (zamietnutie)
10. Timer boundary: 10 pr. dní (malý) / 30 dní (väčší) na návrh zmluvy
11. Task: Zaslať návrh zmluvy o pripojení a technické podmienky (Distribučka)
12. Task: Podpísať zmluvu (Zákazník); timer: lehota na akceptáciu (10 dní SSD / 30 dní / 6 mes.) -> koniec "návrh zanikol"
13. Gateway (XOR): podnikateľ / väčší zdroj? ano -> Task: Uhradiť pripojenie (14 dní)
14. Task: Realizácia a montáž FVE podľa TP (Technik); timer: 6 mes. (malý) / 24 mes. (väčší)
15. Task: Odborná prehliadka a skúška, revízna správa (Technik)
16. Task: Uzavrieť zmluvu o dodávke/odchýlke s dodávateľom (Zákazník)
17. Gateway (XOR): väčší zdroj? ano -> Task: Funkčná skúška (Technik + Distribučka), protokol
18. Task: Montáž elektromera (Distribučka), do 5 pr. dní
19. Task: Vydať oznámenie o pripojení (Distribučka)
20. Task: Oznámenie o prevádzke (Zákazník; len ak nepodané elektronicky / väčší zdroj)
21. End event: "FVE pripojená a v prevádzke"
Alt. end events: "Žiadosť zamietnutá", "Zmluva zanikla".

## 6. Zdroje

- [1] Zákon 251/2012 v znení 133/2024 (od 1.1.2025), §5a až §5g: https://www.zakonypreludi.sk/print/zz/2024-133/znenie-20250101.htm
- [2] ZSD, výrobcovia, malé zdroje: https://www.zsdis.sk/Uvod/Vyrobcovia/Uzatvorenie-zmluvneho-vztahu/Male-zdroje-do-10-kW
- [3] (ZSD, ten istý zdroj, kroky A až C: 10 pr. dní, 6 mes., 14 dní)
- [4] ZSD, lokálne zdroje: https://www.zsdis.sk/Uvod/Vyrobcovia/Uzatvorenie-zmluvneho-vztahu/Lokalne-zdroje
- [5] ZSD, uvedenie do prevádzky malých zdrojov: https://www.zsdis.sk/Uvod/Vyrobcovia/Uvedenie-do-prevadzky/Male-zdroje-do-10-kW
- [6] SSD, malé zdroje: https://www.ssd.sk/vyrobcovia-elektriny/male-zdroje-elektriny?page_id=5530 (cez vyhľadávací súhrn, stránku som nenačítal celú)
- [7] ZSD, posúdenie možnosti pripojenia: https://www.zsdis.sk/Uvod/Vyrobcovia/Pred-pripojenim/Posudenie-moznosti-pripojenia
- [8] SSD, všeobecné podmienky pripojenia výrobných zdrojov: https://www.ssd.sk/buxus/docs/dokumenty/spolocne_dokumenty_pre_vsetky_segmenty/G%20Podmienky%20pripojenia%20vyrobnych%20zdrojov.pdf (PDF som nedokázal prečítať, len zhrnutie vyhľadávača)
- [9] VSD, prevádzkový poriadok 2024: https://www.vsds.sk/mdoc/dso.B6000.A/doc/PP_VSD_2024.pdf (nečítané celé)
- [10] VSD, sprievodca procesom (zdroj do 10,8 kW): https://www.vsds.sk/mdoc/dso.D1B00.A/doc/VSD_letak_sprievodca_procesom_V8.pdf (cez súhrn vyhľadávača)
- [11] Zákon 251/2012, §4 a §6 (neoficiálny zdroj): https://www.zakonypreludi.sk/zz/2012-251
- [12] ZSD, uvedenie do prevádzky lokálnych a komerčných zdrojov: https://www.zsdis.sk/Uvod/Vyrobcovia/Uvedenie-do-prevadzky/Lokalne-zdroje-a-komercne-zdroje
- [13] VSD, lokálny zdroj: https://www.vsds.sk/edso/domov/vyrobcovia-elektriny/pripojenie-zdroja/lokalny-zdroj
- Prevádzkový poriadok ZSD (návrh zmeny 2025): https://zsdis.sk/documents/93691/Navrh-na-zmenu-PP_2025_ver_konzul.pdf (nečítané)
- Starý PP/ÚRSO 0012/2015/E-PP (funkčné skúšky do 10 kW): https://data.urso.gov.sk/CISRES/Agenda.nsf/0/0678760125AA6EDDC1257E0D003FF4C1/$FILE/0012_2015_E-PP.pdf (zastarané)

## 7. Neoverené

- Zákonná definícia malého zdroja (10 vs 10,8 kW) a text §4a z. 309/2009.
- Revízna správa a funkčné skúšky pre malý zdroj v aktuálnom PP.
- Samostatná lehota a poplatok za štúdiu pripojiteľnosti.
- Konkrétne sumy poplatkov pre podnikateľov a zdroje nad 10,8 kW (cenník PDS, ÚRSO).
- Plné znenie aktuálnych PP ZSD/SSD/VSD (len sekundárne súhrny), lehoty pri VSD (10 pr. dní vs 15 vs 30).
- Registrácia výrobcu (ÚRSO/OKTE), SIEA podpora, stavebné ohlásenie.
- Zmluva o dodávke/odbere: nešpecifikované lehoty.

## 8. Nadväzujúce issues

- #64, #65, #66: BPMN príbehy; použiť sekciu 5 ako kostru, lehoty z konfigurácie.
- Navrhnuté nové issues: "Parametrizácia lehôt PDS (ZSD/SSD/VSD)", "Overiť revíznu správu a funkčné skúšky u PDS (e-mail/telefonát)", "Overiť platné znenie PP na slov-lex/ÚRSO".
