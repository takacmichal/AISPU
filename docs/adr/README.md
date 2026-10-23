# Architektonické rozhodnutia (ADR)

Výsledky spikov zo Sprintu 0. Názov súboru: `spike-<číslo issue>-<slug>.md`.
Nové ADR pridávaj v rovnakom formáte (Kontext, Možnosti, Rozhodnutie, Dôsledky, Zdroje) a doplň ich do tabuľky.

| Issue | Dokument | Rozhodnutie v skratke |
|---|---|---|
| #178 | [Dataset výroby FVE](spike-178-dataset-vyroby.md) | DKA Solar Centre Alice Springs 2023 a 2024 (5 min, reálne poruchy; licencia s limitom 5 000 buniek, treba súhlas), záloha NREL PVDAQ (CC BY 4.0) |
| #180 | [Zdroj počasia](spike-180-pocasie.md) | Open-Meteo: Historical Forecast API na tréning (Alice Springs aj SR, overené 2023 až 2024), Forecast API v prevádzke, hodinový join v UTC |
| #185 | [Telemetria simulátora](spike-185-telemetria.md) | JSON `readings[]`, interval 5 min, deduplikácia `(deviceId, timestamp)` |
| #203 | [Pripojenie k distribučke](spike-203-pripojenie-distribucka.md) | Proces pre malý zdroj ≤ 10,8 kW a väčšie zdroje, lehoty konfigurovateľné |
| #205 | [BPMN](spike-205-bpmn.md) | Camunda Modeler, BPMN 2.0 XML v `docs/bpmn/`, 2 úrovne detailu |
| #209 | [Štruktúra repa](spike-209-struktura-repa.md) | Monorepo, CI s `paths` filtrami, tajomstvá mimo gitu (frontend od Sprintu 1: React) |
| #213 | [Databáza](spike-213-databaza.md) | PostgreSQL (Azure Flexible Server B1ms) + EF Core + Npgsql |

Ďalšie záväzné rozhodnutia Sprintu 1 (autentifikácia #218, roly #207, PDF #219, časové rady #220, ML integrácia #196, hosting #211)
dopĺňajú samostatné dokumentačné PR.
