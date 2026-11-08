# Architektonické rozhodnutia (ADR)

Výsledky spikov zo Sprintu 0. Názov súboru: `spike-<číslo issue>-<slug>.md`.
Nové ADR pridávaj v rovnakom formáte (Kontext, Možnosti, Rozhodnutie, Dôsledky, Zdroje) a doplň ich do tabuľky.

| Issue | Dokument | Rozhodnutie v skratke |
|---|---|---|
| #178 | [Dataset výroby FVE](spike-178-dataset-vyroby.md) | DKA Solar Centre Alice Springs 2023 a 2024 (5 min, reálne poruchy; licencia s limitom 5 000 buniek, treba súhlas), záloha NREL PVDAQ (CC BY 4.0) |
| #180 | [Zdroj počasia](spike-180-pocasie.md) | Open-Meteo: Historical Forecast API na tréning (Alice Springs aj SR, overené 2023 až 2024), Forecast API v prevádzke, hodinový join v UTC |
| #185 | [Telemetria simulátora](spike-185-telemetria.md) | JSON `readings[]`, interval 5 min, deduplikácia `(deviceId, timestamp)` |
| #198 | [Verzovanie ML modelu](spike-198-verzovanie-ml-modelu.md) | Verzia + `metadata.json` v Blob Storage (SemVer, nemenné), MLflow a DVC zamietnuté |
| #201 | [Tréning a nasadenie ML](spike-201-trenovanie-nasadenie-ml.md) | Lokálny tréning + Blob Storage, hosting v Container Apps, model sa sťahuje pri štarte |
| #203 | [Pripojenie k distribučke](spike-203-pripojenie-distribucka.md) | Proces pre malý zdroj ≤ 10,8 kW a väčšie zdroje, lehoty konfigurovateľné |
| #205 | [BPMN](spike-205-bpmn.md) | Camunda Modeler, BPMN 2.0 XML v `docs/bpmn/`, 2 úrovne detailu |
| #208 | [Metódy detekcie anomálií](spike-208-metody-detekcie-anomalii.md) | Ostať pri pravidle nad rezíduom (ADR #194); Isolation Forest zamietnutý, z-score len ako možný adaptívny prah |
| #209 | [Štruktúra repa](spike-209-struktura-repa.md) | Monorepo, CI s `paths` filtrami, tajomstvá mimo gitu (frontend od Sprintu 1: React) |
| #213 | [Databáza](spike-213-databaza.md) | PostgreSQL (Azure Flexible Server B1ms) + EF Core + Npgsql |
| #221 | [Poskytovateľ e-mailových notifikácií](spike-221-email-notifikacie.md) | Amazon SES cez SMTP (MailKit) za `IEmailSender`; ACS Email zamietnutý pre retirement 2028; lokálne Mailpit |
| #222 | [Obnovovanie dashboardu](spike-222-obnovovanie-dashboardu.md) | Polling: dashboard a výroba 5 min, alarmy 60 s; SignalR/SSE až pri reálnej potrebe |
| #277 | [Revízia nasadenia v Azure](spike-277-nasadenie-azure-revizia.md) | Poland Central, frontend v nginx kontajneri v Container Apps (SWA na predplatnom zakázané), migrácie a úlohy na pozadí ako Container Apps Jobs, heslo admina z tajomstva |

Ďalšie záväzné rozhodnutia Sprintu 1 (autentifikácia #218, roly #207, PDF #219, časové rady #220, ML integrácia #196, hosting #211)
dopĺňajú samostatné dokumentačné PR.
