# ADR: Formát a frekvencia telemetrie simulátora FVE (spike #185)

- Stav: návrh na schválenie tímom
- Dátum: 2026-10-15
- Projekt: SolarOps (AISPU), issue #185 (takacmichal/AISPU)

## Kontext

SolarOps spravuje fotovoltické elektrárne (FVE). Simulátor musí posielať telemetriu na ingest endpoint .NET API s kontraktom, ktorý sa podobá reálnym striedačom, aby sa dal neskôr pripojiť reálny adaptér bez zmeny jadra. Dáta slúžia na dashboardy a na ML službu (detekcia podvýroby). Treba rozhodnúť: formát, frekvenciu, objem a spôsob generovania dát vrátane značiek (labels) porúch.

Poznámka k overeniu: SunSpec modely a Fronius kanály som overil v primárnych/polo-primárnych zdrojoch (sekcia Zdroje). Registre SMA, Huawei a Growatt som z webu **neoverili z oficiálnej dokumentácie** (oficiálne PDF nie sú voľne dostupné alebo sa nedali načítať); uvádzam len to, čo potvrdili komunitné zdroje, a označujem to.

## Prehľad reálnych formátov

### SunSpec Modbus (de facto štandard, použiteľný pre SMA, Fronius, SolarEdge, Huawei SUN2000 a ďalších)
Overené zo SunSpec JSON definícií modelov:

- Modely 101/102/103 (jednofázový / delená fáza / trojfázový striedač, celé čísla so scale factorom) a 111/112/113 (rovnaké body ako float).
- Body modelu 103/113: `A`, `AphA/B/C` (AC prúd, A), `PPVphAB/BC/CA` a `PhVphA/B/C` (napätia, V), `W` (AC výkon, W), `Hz`, `VA`, `VAr`, `PF`, `WH` (AC energia, Wh, acc32 = monotónny čítač), `DCA`, `DCV`, `DCW` (DC strana), `TmpCab`, `TmpSnk`, `TmpTrns`, `TmpOt` (teploty, °C), `St` (stav), `StVnd` (vendor stav), `Evt1`, `Evt2`, `EvtVnd1..4` (bitfieldy udalostí).
- Enum `St`: OFF(1), SLEEPING(2), STARTING(3), MPPT(4), THROTTLED(5), SHUTTING_DOWN(6), FAULT(7), STANDBY(8).
- `Evt1` bity (výber): GROUND_FAULT, DC_OVER_VOLT, AC_DISCONNECT, GRID_DISCONNECT, OVER_TEMP, BLOWN_STRING_FUSE, UNDER_TEMP...
- Model 160 (Multiple MPPT): opakovaná skupina `module` s `IDStr`, `DCA`, `DCV`, `DCW`, `DCWH`, `Tms`, `Tmp`, `DCSt`, `DCEvt`. Toto je predloha pre pole `strings` v našom kontrakte.
- Iba jedna hodnota DC v modeli 10x, preto viac MPPT/stringov = model 160.

### Fronius Solar API V1 (HTTP JSON)
Endpoint `/solar_api/v1/GetInverterRealtimeData.cgi`; odpoveď `Body.Data` obsahuje kanály s `Unit` a `Values` podľa ID zariadenia. Kanály potvrdené cez openHAB binding a ukážky: `PAC` (W), `FAC` (Hz), `IAC` (A), `UAC` (V), `IDC`/`IDC_2`/`IDC_3`, `UDC`/`UDC_2`/`UDC_3`, `DAY_ENERGY`, `YEAR_ENERGY`, `TOTAL_ENERGY` (Wh), `DeviceStatus` (`StatusCode`: 0-6 štart, 7 beží, 8 standby, 9 bootloading, 10 chyba; `ErrorCode`). DC výkon sa dopočítava (`UDC*IDC`). Hodnoty môžu byť `null` (napr. v noci). Presné definície z oficiálneho PDF som nemohol extrahovať.

### SMA
Modbus register mapa je v dokumente "SMA and SunSpec Modbus Interface" (nepodarilo sa mi ho načítať). Komunitné zdroje potvrdzujú: Modbus TCP je predvolene vypnutý, unit ID býva 3, reg. 30775 = aktuálny AC výkon (S32, W), reg. 30529 = celkový výnos (U32, Wh). Ostatné adresy neuvádzam ako overené.

### Huawei FusionSolar
Northbound API (`getDevRealKpi` pre string/residential striedače) s prísnymi limitmi: komunitná dokumentácia uvádza 1 volanie za minútu na endpoint, reálne dáta sa obnovujú cca raz za minútu, denné/mesačné výnosy cca po 10 minútach, kiosk režim má cache 30 min. Názvy polí KPI som z oficiálnej referencie neoveril (nepoužívam ich). Huawei SUN2000 zároveň po Modbus TCP hovorí SunSpec model 103 (podľa komunitného zdroja).

### Growatt
Modbus RTU protokol (verzia V1.20 PDF). Komunitné zdroje: PV1 napätie/prúd/výkon, AC výkon ako 32-bit (high/low slovo, krok 0.1 W), `Eac_Today`/`Eac_Total` ako uint32 s krokom 0.1 kWh, teploty s krokom 0.1 °C. Dokumentácia a firmvér sa podľa používateľov rozchádzajú; adresy neoverené.

### Spoločný základ
Všetky rodiny poskytujú: AC výkon, DC výkon alebo U/I po MPPT vstupoch, AC napätie/prúd/frekvenciu, teplotu, stavový kód + chybový kód/bitfield a energiu (denná + celoživotná, monotónny čítač). To sú polia nášho kontraktu.

## Možnosti

### A. Frekvencia vzorkovania
| | 1 min | 5 min | 15 min |
|---|---|---|---|
| Realita | Huawei API cca 1/min; datalogery často 5 min | Bežný interval dataloggerov (Fronius, SMA portály 5/15 min, neoverené z oficiálnych zdrojov) | Bežný interval agregácie a reportovania |
| Riadkov/deň/striedač | 1440 | 288 | 96 |
| Chytí oblačnosť | áno | väčšinou | slabo, zahladí prechody |
| ML detekcia podvýroby | zbytočne detailné | dobrý kompromis | postačuje pre denné/hodinové features |

### B. Zdroj dát simulátora
1. Prehrávanie reálneho datasetu + šum.
2. Generatívny model (pvlib clear-sky + počasie + injekcia porúch).
3. Hybrid: generatívny model, ktorého parametre a profily sa kalibrujú/validujú na reálnych dátach.

## Rozhodnutie

1. **Frekvencia: 5 minút** (288 vzoriek/deň). Kontrakt nesie pole `intervalSeconds`, aby sa dal interval meniť (60/300/900) bez zmeny schémy. Pre ML a dashboardy sa 15-min a hodinové agregáty počítajú z 5-min dát na strane servera (nie naopak, informácia sa z 15 min nedá obnoviť). Ak budú náklady na úložisko problém, prepnúť na 15 min je triviálne (objem /3).
2. **Formát: JSON obálka** s poľom `readings[]` (podpora dávok a doplnenia po výpadku spojenia), pomenované polia s jednotkami v názve (`acPowerW`), **bez scale factorov** (simulátor aj ingest pracujú s fyzikálnymi jednotkami; scale factory sú detail Modbus transportu). Mapovanie stavov podľa SunSpec `St`, polia `strings[]` podľa modelu 160, energetické čítače podľa `WH`/`TOTAL_ENERGY`.
3. **Idempotencia:** `messageId` (UUID) v každom čítaní a unikátny kľúč `(deviceId, timestamp)` v DB; ingest vracia 202 a duplicitu ignoruje.
4. **Noc:** striedač posiela vzorky aj v noci, so `status = SLEEPING` a nulovým výkonom (zjednodušuje detekciu offline vs. noc). Reálne striedače v noci často vypnú komunikáciu; adaptér pre reálne zariadenia to musí tolerovať (`null`).
5. **Zdroj dát: hybrid, jadro generatívne** (pvlib clear-sky + počasie + degradácia + poruchy), s kalibráciou/validáciou voči reálnym dátam (napr. PVGIS pre počasie/ročný ťah, verejné datasety). Dôvody: presne známe labely porúch (potrebné pre supervised ML), reprodukovateľnosť (seed), škálovanie na 1000 inštalácií s rôznymi parametrami (sklon, azimut, kWp), žiadna licenčná závislosť na dátach. Čisté prehrávanie reálnych dát nemá pravdivé labely a nedá sa škálovať na rôzne lokality.
6. Ground truth (labely porúch) **nie je súčasťou ingest kontraktu**. Simulátor ich ukladá do samostatného súboru/tabuľky (`fault_labels`) a používajú sa len pri trénovaní/evaluácii ML.

## JSON schéma a príklad

### Príklad payloadu
```json
{
  "schemaVersion": "1.0",
  "deviceId": "7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10",
  "readings": [
    {
      "messageId": "0b6f5a52-3c1d-4a07-8e0c-6d2f9a7b4c11",
      "timestamp": "2026-06-21T10:35:00Z",
      "intervalSeconds": 300,
      "status": "MPPT",
      "acPowerW": 7420.5,
      "dcPowerW": 7615.0,
      "acVoltageV": 231.4,
      "acCurrentA": 10.7,
      "frequencyHz": 50.01,
      "powerFactor": 0.99,
      "temperatureC": 46.2,
      "strings": [
        { "id": 1, "voltageV": 612.3, "currentA": 6.31, "powerW": 3863.5 },
        { "id": 2, "voltageV": 608.9, "currentA": 6.17, "powerW": 3756.9 }
      ],
      "energy": { "dayWh": 31250.0, "totalWh": 18452310.0 },
      "errorCode": 0
    }
  ]
}
```
Jednotky: W, V, A, Hz, °C, Wh. `timestamp` je koniec/okamih vzorky v UTC (ISO 8601 s `Z`). `acPowerW` je okamžitý výkon, `energy.totalWh` je monotónny čítač (analógia SunSpec `WH` acc32).

### JSON Schema (draft 2020-12)
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://solarops.example/schemas/telemetry-1.0.json",
  "title": "SolarOps telemetry batch",
  "type": "object",
  "required": ["schemaVersion", "deviceId", "readings"],
  "additionalProperties": false,
  "properties": {
    "schemaVersion": { "const": "1.0" },
    "deviceId": { "type": "string", "format": "uuid" },
    "readings": {
      "type": "array",
      "minItems": 1,
      "maxItems": 288,
      "items": { "$ref": "#/$defs/reading" }
    }
  },
  "$defs": {
    "reading": {
      "type": "object",
      "required": ["messageId", "timestamp", "status", "acPowerW"],
      "additionalProperties": false,
      "properties": {
        "messageId": { "type": "string", "format": "uuid" },
        "timestamp": { "type": "string", "format": "date-time", "pattern": "Z$" },
        "intervalSeconds": { "type": "integer", "enum": [60, 300, 900], "default": 300 },
        "status": {
          "type": "string",
          "enum": ["OFF", "SLEEPING", "STARTING", "MPPT", "THROTTLED",
                   "SHUTTING_DOWN", "FAULT", "STANDBY"]
        },
        "acPowerW": { "type": ["number", "null"], "minimum": -1000 },
        "dcPowerW": { "type": ["number", "null"], "minimum": 0 },
        "acVoltageV": { "type": ["number", "null"], "minimum": 0, "maximum": 500 },
        "acCurrentA": { "type": ["number", "null"], "minimum": 0 },
        "frequencyHz": { "type": ["number", "null"], "minimum": 40, "maximum": 70 },
        "powerFactor": { "type": ["number", "null"], "minimum": -1, "maximum": 1 },
        "temperatureC": { "type": ["number", "null"], "minimum": -60, "maximum": 150 },
        "strings": {
          "type": "array",
          "maxItems": 24,
          "items": {
            "type": "object",
            "required": ["id"],
            "additionalProperties": false,
            "properties": {
              "id": { "type": "integer", "minimum": 1 },
              "voltageV": { "type": ["number", "null"], "minimum": 0 },
              "currentA": { "type": ["number", "null"], "minimum": 0 },
              "powerW": { "type": ["number", "null"], "minimum": 0 }
            }
          }
        },
        "energy": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "dayWh": { "type": ["number", "null"], "minimum": 0 },
            "totalWh": { "type": ["number", "null"], "minimum": 0 }
          }
        },
        "errorCode": { "type": "integer", "minimum": 0 }
      }
    }
  }
}
```
Návrhové poznámky: `null` povolené pre merané hodnoty (reálne zariadenia posielajú `null`, napr. Fronius v noci); `status` je zrkadlený SunSpec `St`; `errorCode` je vendor-neutrálny (mapovanie SunSpec `Evt1`/Fronius `ErrorCode` robí adaptér). Validácia na API: odmietnuť timestamp ďalej ako cca 5 min v budúcnosti.

## Odhad objemu

Predpoklady: 1 striedač na inštaláciu, 2 stringy, 288 riadkov/deň (5 min, vrátane noci), 30 dní/mesiac. Veľkosti riadkov sú **hrubý odhad**, nie meranie; ľahko sa overia `pg_total_relation_size` po vložení 1 M testovacích riadkov.

Úzka tabuľka `telemetry` (ID zariadenia, čas, ~8 skalárov, 2 stringy ako stĺpce alebo `jsonb`):
- PostgreSQL: hlavička tuple ~24 B + dáta ~90-110 B + item pointer 4 B, plus index (device, time) ~40-50 B na riadok, spolu cca **180 B/riadok** (bez TOAST, bez kompresie). JSON `strings` ako `jsonb` by zväčšil riadok o desiatky bajtov.
- Azure SQL: režia riadka ~10 B + dáta ~90-110 B, clustered index na (device, time) = bez samostatného indexu, cca **120-130 B/riadok**; s PAGE kompresiou zhruba polovica.
- JSON na drôte (ingest): ~700-900 B na čítanie bez kompresie (gzip cca 4-5x menej).

| N inštalácií | Riadkov/deň (5 min) | Riadkov/mesiac | MB/mesiac PG (180 B) | MB/mesiac Azure SQL (125 B) | Ingest msg/s | JSON MB/deň (800 B) |
|---|---|---|---|---|---|---|
| 10 | 2 880 | 86 400 | ~16 | ~11 | 0,03 | ~2,3 |
| 100 | 28 800 | 864 000 | ~155 | ~108 | 0,33 | ~23 |
| 1 000 | 288 000 | 8 640 000 | ~1 555 (1,6 GB) | ~1 080 (1,1 GB) | 3,3 | ~230 |

- Pri 15 min všetko vydeliť tromi (N=1000: ~0,5 GB/mes. v PG).
- Ročne pri N=1000, 5 min: ~19 GB (PG) / ~13 GB (Azure SQL). Pri 1000 inštaláciách treba partitioning po mesiacoch (PG deklaratívne, Azure SQL partition function) a retenciu surových dát (napr. 13 mesiacov) + agregáty 15 min/hodina/deň.
- Nulové noci (nevkladať riadky, ak `SLEEPING` a výkon 0) by znížili objem asi o 40-50 % v lete, viac v zime; v tejto fáze neoptimalizovať.
- Pre školský projekt je objem do N=100 zanedbateľný pre obe databázy; N=1000 je stále triviálne pre jeden server, kritická je len indexácia a partitioning dotazov na časové rozsahy.

## Generovanie dát a injekcia porúch

### Generatívny model (odporúčané jadro)
1. Poloha, sklon, azimut, kWp na inštaláciu (náhodne okolo Slovenska, lat ~48-49, rôzne orientácie).
2. Clear-sky GHI/DNI/DHI cez `pvlib` (Ineichen je predvolený model clear-sky v ModelChain).
3. Počasie: faktor oblačnosti (clearness index) ako autokorelovaný náhodný proces (AR(1)/Markov stavy jasno-polooblačno-zamračené) alebo reálne hodinové počasie z PVGIS, interpolované na 5 min; teplota okolia z dennej/sezónnej krivky.
4. DC/AC model: pvlib ModelChain s PVWatts (teplotný koeficient, strata kabeláže, účinnosť striedača, clipping pri DC/AC pomere). Teplota modulu -> teplota striedača (TmpCab).
5. Šum a realizmus: multiplikatívny šum merania cca 0,5-1 %, kvantizácia na realistické kroky (0,1 V, 0,01 A, 1 W), občasné chýbajúce vzorky.
6. Čítače: `energy.totalWh` kumulatívne, `dayWh` reset o polnoci miestneho času.

### Prehrávanie reálnych dát
Vhodné len ako validácia (porovnať tvar denných kriviek, rozdelenie výkonov, distribúciu oblačnosti) a na kalibráciu parametrov. Nevýhody: nepravdivé/nepokryté labely, obmedzený počet lokalít, licencie, škálovanie. Konkrétny verejný dataset som neoveril; treba ho vybrať v nadväzujúcom issue.

### Injekcia podvýroby (labely pre ML)
Referenčný "očakávaný" výkon `P_expected` sa počíta z rovnakého modelu bez poruchy (clear-sky + počasie). Porucha sa aplikuje ako transformácia výstupu a zapíše sa do `fault_labels` (`deviceId`, `faultId`, `type`, `start`, `end`, `severity`, a per-vzorka `isFaulty`, `expectedPowerW`).

| Typ poruchy | Efekt | Charakter |
|---|---|---|
| Zašpinenie (soiling) | multiplikatívna strata 0,1-0,3 %/deň, reset po daždi/čistení | postupný drift |
| Čiastočné tienenie | strata 20-60 % v časovom okne dňa na jednom stringu | opakujúce sa denné |
| Výpadok stringu | string = 0 V/0 A, celkový výkon klesne o podiel stringu | skokový |
| Degradácia | -0,5 %/rok, pomalý trend | veľmi pomalý |
| Derating (prehriatie) | strop výkonu pri vysokej teplote, `status = THROTTLED` | podmienený |
| Chyba striedača | `status = FAULT`, `errorCode != 0`, výkon 0 | skokový, s kódom |
| Chyby dát | výpadok spojenia (chýbajúce vzorky), zamrznutá hodnota, odľahlá hodnota | nie je podvýroba, ale negatívna trieda pre robustnosť |

Pravidlá pre kvalitu labelov:
- Label podvýroby definovať jednotne: `isFaulty = (P_actual / P_expected < 0,85)` počas denného svetla (napr. `P_expected` nad 5 % menovitého výkonu), plus samostatný `type`. Prah a definíciu zdokumentovať a nemeniť medzi experimentmi.
- 3-8 % inštalácií mať v danom týždni aktívnu poruchu, aby bola trieda nevyvážená ako v realite (nie 50 %); poruchy generovať Poissonovým procesom s trvaním v hodinách až týždňoch.
- Fixný `seed` na inštaláciu a run, aby bolo trénovanie reprodukovateľné; train/test deliť podľa inštalácií a času, nie náhodne podľa riadkov (únik dát).
- Zámerne dodať aj "ťažké negatívy" (oblačnosť, noc, orezanie výkonu), aby model nezamieňal počasie s poruchou. ML služba preto potrebuje ako feature aj očakávaný výkon alebo irradianciu (zdroj: počasie API v produkcii, tu simulátor).

## Dôsledky

- Plus: kontrakt je blízky SunSpec/Fronius, adaptér pre reálny striedač je mapovanie polí, nie redizajn. Labely sú pravdivé a reprodukovateľné.
- Plus: ingest je idempotentný a podporuje dávky, takže simulátor aj budúce zariadenia môžu doplniť dáta po výpadku.
- Mínus: simulátor je vlastný model, jeho realizmus závisí od kalibrácie; ML natrénované len na syntetike sa musí overiť na reálnych dátach (riziko domain gap). Treba to uviesť v dokumentácii projektu.
- Mínus: scale factory a registre nie sú zachované; reálne Modbus zariadenia vyžadujú adaptér (mimo rozsahu tejto fázy).
- Riziko: odhady veľkosti riadkov sú hrubé; pri N=1000 treba partitioning a retenčnú politiku od začiatku.
- Nevyriešené: či ingest pri `SLEEPING` ukladať; zdroj reálneho počasia v produkcii; meteo feature pre ML; čas "dnes" pre `dayWh` (UTC vs. lokálny čas inštalácie).

## Zdroje

Overené (načítané):
- SunSpec model 103 (JSON definícia): https://raw.githubusercontent.com/sunspec/models/master/json/model_103.json
- SunSpec model 113 (float): https://raw.githubusercontent.com/sunspec/models/master/json/model_113.json
- SunSpec model 160 (Multiple MPPT): https://raw.githubusercontent.com/sunspec/models/master/json/model_160.json
- openHAB Fronius binding (kanály, status kódy, refreshInterval): https://v43.openhab.org/addons/bindings/fronius/
- HomeAssistant FusionSolar (limity rozhrania, `getDevRealKpi`): https://github.com/tijsverkoyen/homeassistant-fusionsolar

Len z výsledkov vyhľadávania (komunitné, neoverené z primárnej dokumentácie):
- Fronius mapa registrov (model 103 hlavička): https://www.forum-fronius.pl/wp-content/uploads/asgarosforum/2812/Inverter_register_map.pdf
- Fronius Solar API, ukážky odpovedí: https://helpdesk.paessler.com/en/support/solutions/articles/76000076615-how-to-monitor-fronius-inverter
- Fronius Solar API V1 oficiálne PDF (nepodarilo sa extrahovať text): https://www.fronius.com/~/downloads/Solar%20Energy/Operating%20Instructions/42%2C0410%2C2012.pdf
- SMA Modbus (komunitné adresy 30775, 30529): https://community.openhab.org/t/modbus-binding-with-sunny-boy-sma-inverter/81847 a https://forum.iobroker.net/post/993733
- Huawei FusionSolar API (limity): https://github.com/tijsverkoyen/homeassistant-fusionsolar a https://docs.rs/crate/huawei_client/latest/source/docs/Communicate%20with%20FusionSolar%20through%20an%20openAPI%20account.pdf
- Growatt Modbus (komunitné adresy): https://www.aggsoft.com/modbus-data-logging/growatt.htm a https://esphome.io/components/sensor/growatt_solar/
- pvlib ModelChain (Ineichen, PVWatts, predvolené modely): https://pvlib-python.readthedocs.io/en/v0.8.1/generated/pvlib.modelchain.ModelChain.html

Tvrdenia o bežnom 5/15-min intervale dataloggerov a o PVGIS/verejných datasetoch sú z doménovej znalosti, nie z načítaného zdroja.

## Nadväzujúce issues

1. Implementovať ingest endpoint `POST /api/telemetry` (validácia JSON Schema, idempotencia podľa `(deviceId, timestamp)`, 202 Accepted, limity veľkosti dávky).
2. DB schéma `telemetry` + indexy + partitioning po mesiacoch + retenčná politika + agregáty (15 min/hodina/deň) pre PostgreSQL/Azure SQL; zmerať skutočnú veľkosť riadka na 1 M riadkoch.
3. Simulátor v Pythone (pvlib): jadro clear-sky + počasie + šum, konfigurácia N inštalácií, seed, reálny čas aj backfill.
4. Modul injekcie porúch + export `fault_labels` (definícia labelu, prahy, typy porúch).
5. Výber a validácia na reálnom datasete (PVGIS/verejný dataset) a kalibrácia simulátora; porovnanie štatistík.
6. ML: feature set (očakávaný vs. nameraný výkon, string pomery), delenie train/test podľa inštalácie a času, baseline detektor podvýroby.
7. Rozhodnutie o nočných vzorkách a lokálnom čase pre `dayWh`; dokument kontraktu (OpenAPI + JSON Schema v repozitári).
8. (Voliteľné) adaptér pre reálny striedač: Fronius Solar API alebo SunSpec Modbus -> náš kontrakt.
