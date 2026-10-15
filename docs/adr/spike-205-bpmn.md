# ADR-205: BPMN - rozsah, granularita, nástroj

- Stav: prijaté (Sprint 0)
- Dátum: 2026-10-15
- Issue: #205

> Stav k 2026-10-15. Overené položky majú zdroj, neoverené sú označené **[NEOVERENÉ]**. Spike nemenil žiadne súbory projektu.

## Kontext
Potrebujeme procesné modely (BPMN 2.0) pre SolarOps. Účastníci: Obchod, Projektant, Technik, Zákazník, Distribučka. Tím má 3 ľudia, modely majú byť verzionovateľné v gite a odovzdateľné ako `.bpmn` + PNG/PDF. Dvaja z tímu môžu pracovať na macOS (nástroj musí fungovať aj mimo Windows).

## Možnosti

| Kritérium | bpmn.io (demo.bpmn.io, bpmn-js) | Camunda Modeler (desktop) | Bizagi Modeler | draw.io (diagrams.net) |
|---|---|---|---|---|
| Cena | zdarma, licencia vyžaduje viditeľný watermark bpmn.io | zdarma, MIT (v5.x) | Free tier; funkcie ako kolaborácia sú v platenej úrovni | zdarma |
| Natívny BPMN 2.0 XML | áno | áno | import BPMN je zdokumentovaný, export `.bpmn` na stránke nepotvrdený [NEOVERENÉ] | **nie je nativne**; má BPMN shapes, ale ukladá vlastný `.drawio` XML, nie BPMN 2.0 s `bpmndi` |
| Git-friendly | výborne (čistý XML) | výborne (`.bpmn` XML) | slabo (vlastný formát `.bpm`) | stredne (`.drawio` XML) |
| Validácia BPMN | áno (linting plugin) | áno (vstavaný lint) | áno | nie |
| Export PNG/SVG/PDF | PNG, SVG | PNG, SVG, PDF (cez export) | PDF, Word, Excel (PNG neuvedené) | PNG, SVG, PDF |
| Platformy | prehliadač | Win/macOS/Linux | OS nepotvrdený na stránke, historicky Windows [NEOVERENÉ] | prehliadač + desktop |

## Rozhodnutie
- **Nástroj: Camunda Modeler (Desktop, open source) ako primárny**, `demo.bpmn.io` ako záloha na rýchle úpravy bez inštalácie. Obe zapisujú rovnaký štandardný BPMN 2.0 XML, takže sa dajú zameniť. draw.io použiť len na ne-BPMN diagramy (architektúra, ERD). Bizagi zamietnuť (uzavretý formát, nejasná podpora macOS, horšie diffy).
- **Hĺbka detailu: 2 úrovne.**
  - L1 "Objednávka až prevádzka" (kolaborácia): jeden pool na účastníka (Zákazník, Obchod, Projektant, Technik, Distribučka). Distribučka a Zákazník ako *black-box pool* (bez interných krokov, iba message flow).
  - L2 detail procesu: pool "SolarOps / dodávateľ" s lanes Obchod, Projektant, Technik; max. ~15 aktivít na diagram.
  - Prvky: start/end events (none, message, timer), exclusive a parallel gateway, call activity pre podprocesy, data objects a data stores (Zákazka, Projekt, Telemetria, Žiadosť o pripojenie), message flow medzi poolmi, boundary timer events pre lehoty. Neodporúčame chaos prvky (compensation, escalation, event-based gateway), pokiaľ ich zadanie nežiada.
- **Pomenovanie:** aktivity sloveso + podstatné meno v neurčitku ("Vytvoriť cenovú ponuku"), eventy v minulom čase ("Ponuka schválená"), gateway ako otázka ("Zákazník súhlasí?"), vetvy "Áno/Nie". ID elementov: `Activity_<Lane>_<Slovo>` (v Modeleri ručne upraviť), súbory `kebab-case`.
- **Rozdelenie modelov:** 1 súbor = 1 proces: `docs/bpmn/00-prehlad.bpmn`, `10-obchod-ponuka.bpmn`, `20-projekt-navrh.bpmn`, `30-instalacia.bpmn`, `40-pripojenie-distribucka.bpmn`, `50-prevadzka-monitoring.bpmn`. Podprocesy cez call activity s názvom súboru v dokumentácii.
- **Odovzdanie:** `.bpmn` (zdroj) + PNG a PDF pre každý model v `docs/bpmn/export/`, plus jednostránkový prehľad. Exporty generovať pri verzii na odovzdanie, nie pri každom commite (zbytočné binárne diffy).

## Dôsledky
- (+) Čitateľné diffy a code review modelov v PR, modely použiteľné aj v Camunda engine, ak by sa to chcelo.
- (-) Každý člen musí nainštalovať Camunda Modeler; bpmn.io watermark sa nesmie odstrániť (licencia).
- (-) Konvencia ID a pomenovania nie je vynútená nástrojom, treba ju strážiť v review.

## Zdroje
- bpmn.io licencia (watermark): https://bpmn.io/license/
- Camunda Modeler download (MIT, 5.x): https://camunda.com/?p=71191
- Bizagi Modeler: https://www.bizagi.com/en/platform/modeler
- Akademické porovnanie BPMN nástrojov (2024): https://js.ugd.edu.mk/index.php/bjami/en/article/view/6882
- draw.io BPMN stránka vrátila 404, tvrdenie o `.drawio` formáte vychádza zo všeobecných znalostí [NEOVERENÉ].

## Nadväzujúce issues
- Dohodnúť zoznam procesov a hranice L1/L2 (workshop 1 h).
- Vytvoriť šablónu `docs/bpmn/_template.bpmn` s poolmi a lanes.
- Nastaviť bpmnlint konfiguráciu pre Camunda Modeler.
- Zdokumentovať konvenciu pomenovania v `docs/bpmn/README.md`.
