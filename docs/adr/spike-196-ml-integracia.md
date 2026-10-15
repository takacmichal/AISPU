# ADR spike #196: Integrácia ML služby s .NET API (REST vs. ONNX)

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #196 (nadradené #6); duplicitné issues #204 (Integrácia Python ML s .NET API) a #201 (nasadenie ML) sú pokryté týmto ADR
- **Súvisí:** #191 (LightGBM po baseline, LSTM odložené), #189 (join s počasím), #211 (hosting), #163 až #164 (kontajner a nasadenie ML)

## Kontext

Predikciu výroby počíta Python (pandas, pvlib, LightGBM), API je .NET 10. Feature engineering (clear-sky z pvlib, `kt = ghi / clearsky_ghi`, slnečná poloha, cyklické príznaky, `p_baseline` z fyzikálneho modelu) je v Pythone; model je LightGBM (#191). ML služba beží ako kontajner v Azure Container Apps so scale-to-zero (#211).

## Zvažované možnosti

| Kritérium | A. REST: Python FastAPI služba, .NET volá cez typed `HttpClient` | B. ONNX Runtime v .NET (model načítaný v API) |
|---|---|---|
| Feature engineering | zostáva v Pythone (pvlib, pandas), jeden kód pre tréning aj inferenciu | musí sa **zopakovať v C#** (pvlib nemá ekvivalent) alebo predpočítať mimo ONNX; riziko rozdielu train/serve |
| Export modelu | netreba | LightGBM na ONNX **overené** (nižšie); LSTM **neoverené** (odložené) |
| Latencia | sieť + serializácia (rádovo desiatky ms v rámci Container Apps, **neoverené**); cold start pri scale-to-zero | inferencia v procese, rádovo ms |
| Nasadenie | ďalší kontajner (už plánovaný, #164) | bez ďalšej služby, ale `.onnx` súbor a natívne knižnice v API image |
| Údržba | nezávislé verzie a nasadenie modelu bez zmeny API | zmena modelu = nasadenie API |
| Testovateľnosť | kontrakt testovateľný (contract tests), ML mock v .NET testoch | unit test načítania modelu, bez siete |
| Školské zadanie | počíta s Python ML službou | odchýlka od zadania |

## Rozhodnutie

**Možnosť A: REST. Python FastAPI služba, .NET API ju volá cez typed `HttpClient`. ONNX zamietnutý pre Sprint 1 až 2** (dôvod: feature engineering v Pythone, zhoda so zadaním, nezávislé verzovanie modelu).

### FastAPI vs. Flask

FastAPI: validácia vstupu cez Pydantic, automatické OpenAPI (`/openapi.json`, vhodné pre kontraktné testy), natívne `async`. Flask by potreboval rozšírenia pre validáciu a OpenAPI (podľa dokumentácie projektov, vlastné meranie sa nerobilo). Aktuálna verzia `fastapi` na PyPI: 0.143.2.

### Návrh API kontraktu (navrhované, spresní sa v #109/#110)

Základná cesta `/v1`. Čas vždy ISO 8601 UTC (`Z`), jednotky v názve poľa.

| Endpoint | Účel |
|---|---|
| `GET /health` | `200 {"status":"ok","modelVersion":"2026-10-15-lgbm-1"}` (liveness/readiness pre Container Apps) |
| `POST /v1/predictions/production` | hodinová predikcia výroby inštalácie z predpovede počasia |

Požiadavka:

```json
{
  "installationId": "7f3c2d9e-1b4a-4c55-9a1e-2d6f0b8e1a10",
  "plant": { "latitude": 48.15, "longitude": 17.11, "dcCapacityKw": 8.4, "tiltDeg": 30, "azimuthDeg": 180 },
  "weather": [
    { "timestamp": "2026-06-21T10:00:00Z", "ghiWm2": 780.0, "dniWm2": 640.0, "dhiWm2": 120.0,
      "cloudCoverPct": 20.0, "temperatureC": 24.5, "windSpeedMs": 2.1 }
  ]
}
```

Odpoveď `200`:

```json
{
  "modelVersion": "2026-10-15-lgbm-1",
  "generatedAt": "2026-10-15T08:00:03Z",
  "predictions": [ { "timestamp": "2026-06-21T10:00:00Z", "expectedPowerW": 6120.0 } ]
}
```

Pravidlá a chyby (formát `application/problem+json`, RFC 7807, rovnako ako backend):

- `422` neplatný vstup (Pydantic), napr. prázdne `weather`, viac ako 168 hodín, chýbajúce pole; `.NET` ho pri vlastnej chybe mapuje na `500`/`502`, nie na `400` používateľa.
- `503` model nenačítaný (štart); `500` neočakávaná chyba. Odpovede obsahujú `traceId`.
- Detekcia podvýroby (`P_actual / P_expected < 0,85`, #194) sa počíta v .NET API z `expectedPowerW`; samostatný ML endpoint na anomálie sa pridá až podľa #208.

Klient v .NET (`MlPredictionClient` cez `AddHttpClient`): **timeout 15 s** na požiadavku (zahŕňa prvý cold start služby so scale-to-zero, ktorého dĺžka je **neoverená**), **jedno opakovanie** pri `503`/timeout (`Microsoft.Extensions.Http.Resilience`), circuit breaker; pri zlyhaní API vráti `503` s `ProblemDetails` a použije poslednú uloženú predikciu (#111). Adresa služby: konfigurácia `Ml:BaseUrl` (env premenná). Predikcie sa cacheujú v DB (#111).

### Overenie exportu LightGBM do ONNX (spustené)

Skript `docs/adr/assets/spike-196-onnx-roundtrip.py` (Python 3.12, Docker `python:3.12`, lightgbm 4.7.0, onnxmltools 1.16.0, onnxruntime): syntetický regresný model (200 stromov, 8 príznakov) exportovaný na ONNX a porovnaný s pôvodným modelom.

| Údaj | Výsledok |
|---|---|
| Maximálna absolútna odchýlka predikcií (1 000 riadkov) | 1,1e-6 |
| Veľkosť `.onnx` | 442 kB |
| Inferencia 24 riadkov v Pythone | ONNX Runtime 0,095 ms, `lightgbm` 0,40 ms |

Export funguje; **načítanie v .NET (`Microsoft.ML.OnnxRuntime` 1.31.0) som nespúšťal**, a tiež nie export LSTM.

## Dôsledky

- (+) Jedna implementácia príznakov, model sa dá nasadiť nezávisle (#164), jednoduché experimentovanie (#191).
- (+) Kontrakt testovateľný z oboch strán (Python `pytest` voči FastAPI, .NET s mockom `HttpClient`).
- (-) Sieťová závislosť a cold start; potrebujeme timeout, opakovanie a fallback na uloženú predikciu.
- (-) Ďalší kontajner, verzie a monitorovanie navyše (Sprint 4).
- Implementácia **musí** použiť FastAPI službu volanú z .NET cez typed `HttpClient`; ONNX sa v Sprinte 1 až 2 nepoužije.

## Riziká a neoverené

- Latencia a cold start v Azure Container Apps (nemerané).
- ONNX v .NET a export LSTM (nespustené).
- Kontrakt je návrh a môže sa zmeniť podľa výsledkov #189 a #193 (napr. tvar príznakov).
- Porovnanie Flask vs. FastAPI vychádza z dokumentácie, nie z merania.

## Zdroje

1. FastAPI na PyPI (0.143.2): https://pypi.org/project/fastapi/
2. onnxmltools (1.16.0, konverzia LightGBM): https://pypi.org/project/onnxmltools/
3. Microsoft.ML.OnnxRuntime (1.31.0): https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime
4. RFC 7807 Problem Details: https://www.rfc-editor.org/rfc/rfc7807
5. ADR #191 (LightGBM po baseline) a #194 (prah podvýroby), PR #286/#287 a súvisiace.

## Nadväzujúce issues

- #109 Vytvoriť službu na predikciu výroby, #110 Zavolať predikciu z .NET API, #111 Ukladať predikcie a cache, #174 Testy ML služby a kontraktu predikcie; #163/#164 kontajner a nasadenie ML.
- Zastarané/duplicitné: #204 (duplikuje #196); nič som nevytváral ani neupravoval.
