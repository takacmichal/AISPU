# BPMN modely procesov SolarOps

Modely hlavných procesov firmy (Epic #2). Zdroj je jeden súbor draw.io s 9 stranami: L1 je celý proces v jednom flowe, L2 sú detaily procesov pre jednotlivé stories.

| Súbor | Obsah |
|---|---|
| [`solarops-bpmn.drawio`](solarops-bpmn.drawio) | zdrojový súbor, otvoriť v [draw.io](https://app.diagrams.net) alebo draw.io desktop |
| [`solarops-bpmn.drawio.pdf`](solarops-bpmn.drawio.pdf) | všetky strany na odovzdanie (PDF s vloženým diagramom, dá sa otvoriť v draw.io) |
| [`png/`](png) | náhľady strán (PNG s vloženým diagramom) |
| [`mapovanie-entit.md`](mapovanie-entit.md) | prepojenie úloh na entity, stavy a endpointy (#69) |

## Konvencie
- **Bazény:** `SolarOps (firma)` s lanes **Obchod**, **Projektant**, **Technik**. Externé strany **Zákazník**, **Distribučka** a **Detekčný systém (monitoring a ML)** sú zbalené bazény (black box), komunikácia s nimi ide len cez správové toky.
- **Úrovne:** L1 je celý životný cyklus FVE v jednom flowe (hlavné kroky a brány), L2 je jeden proces na stranu, číslovaný podľa story.
- **Názvy úloh:** sloveso + objekt (napr. „Odoslať ponuku zákazníkovi“).
- **Farby:** modrá = úloha, žltá = brána, zelená = štart, červená = koniec, hrubý okraj = call activity, `+` = podproces.
- Lehoty a hranica malého zdroja (10,8 kW) vychádzajú zo spiku #203.

## Strany

| Strana | Story | Náhľad |
|---|---|---|
| L1 Celý proces (jeden flow od dopytu po servis) | Epic #2 | [png](png/l1-prehlad.drawio.png) |
| #61 Dopyt a cenová ponuka | #61 | [png](png/61-dopyt-a-ponuka.drawio.png) |
| #62 Návrh FVE projektantom | #62 | [png](png/62-navrh-fve.drawio.png) |
| #63 Uzatvorenie zmluvy a platba zálohy | #63 | [png](png/63-zmluva-a-zaloha.drawio.png) |
| #64 Žiadosť o pripojenie k distribučke | #64 | [png](png/64-pripojenie.drawio.png) |
| #65 Inštalácia a revízia | #65 | [png](png/65-instalacia.drawio.png) |
| #66 Uvedenie do prevádzky a odovzdanie | #66 | [png](png/66-uvedenie-do-prevadzky.drawio.png) |
| #67 Servisný proces z hlásenia zákazníka | #67 | [png](png/67-servis-z-hlasenia.drawio.png) |
| #68 Automatický servis z detekcie podvýroby | #68 | [png](png/68-automaticky-servis.drawio.png) |

## Úprava
Upravuj `solarops-bpmn.drawio` v draw.io a potom pregeneruj náhľady a PDF:

```bash
drawio -x -f pdf -a -e -b 10 -o docs/bpmn/solarops-bpmn.drawio.pdf docs/bpmn/solarops-bpmn.drawio
drawio -x -f png -e -p 1 -b 10 -o docs/bpmn/png/l1-prehlad.drawio.png docs/bpmn/solarops-bpmn.drawio
```
