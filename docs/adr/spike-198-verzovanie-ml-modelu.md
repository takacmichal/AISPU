# ADR #198: Reprodukovateľnosť a verzovanie ML modelu

- Stav: Navrhnuté (výstup spiku #198, nadradené #6)
- Dátum: 2026-10-28
- Vzorový metadátový súbor: [assets/spike-198-model-metadata.example.json](assets/spike-198-model-metadata.example.json)
- Súvisí s: [ADR #201](spike-201-trenovanie-nasadenie-ml.md)

## Kontext

Model predikcie výroby z počasia sa bude opakovane pretrénovávať. Potrebujeme vedieť (1) ktorá verzia beží v produkcii, (2) z akých dát a kódu vznikla, (3) vedieť tréning zopakovať a (4) vrátiť sa na predošlú verziu. Rozsah: malý tabuľkový model (scikit-learn, dáta v rádoch MB až stoviek MB), jeden model, 2 až 3 ľudia v tíme, študentský rozpočet. V repozitári zatiaľ existuje len `ml/data` a `ml/notebooks`.

## Alternatívy

| Kritérium | A: MLflow | B: DVC | C: Metadáta (JSON) v Blob Storage + git tag |
|---|---|---|---|
| Čo rieši | tracking pokusov, model registry, artefakty | verzovanie dát a pipeline cez git + remote storage | verzia modelu + sledovateľnosť (dáta, kód, parametre) |
| Infraštruktúra | tracking server + backend DB + artifact store (alebo len lokálny súbor) | žiadny server, remote = Blob Storage | žiadna navyše (Blob už potrebujeme) |
| Cena | server by bežal ako ďalšia služba (Container App/VM) a DB; presná cena neoverená | len úložisko | len úložisko (centy) |
| Zložitosť pre tím | stredná až vysoká (server, auth, údržba) | stredná (nový nástroj, `.dvc` súbory, remote auth) | nízka (jeden Python skript) |
| Reprodukovateľnosť dát | len ak sa dáta logujú ako artefakt | silná (hash dát v gite) | hash dát (SHA-256) v metadátach; samotné dáta archivované v Blobe |
| Rollback | cez registry stage/alias | `git checkout` + `dvc checkout` | zmena premennej `MODEL_VERSION` |
| Riziko nad rámec projektu | vysoké (predimenzované pre 1 model) | stredné | nízke |

Poznámka: MLflow a DVC sú open-source nástroje (licencie som v tomto spiku neoveroval; pozri časť Neoverené). Porovnanie vychádza z ich známeho účelu, nie z nového merania.

## Rozhodnutie

Zvoliť **možnosť C: jednoduché metadáta v Blob Storage + git tag**, bez MLflow a DVC. Dôvod: pri jednom modeli a malých dátach by MLflow server a DVC pridali prevádzkovú záťaž a náklady bez zodpovedajúceho prínosu. Ak by tím neskôr potreboval porovnávať desiatky pokusov, MLflow (aj lokálny file backend) sa dá doplniť; formát metadát je naň mapovateľný.

### Čo ukladáme (viď vzorový súbor)

Ku každej verzii súbor `metadata.json` s týmito sekciami:

- `model`: názov, verzia, algoritmus, zoznam featur, `artifact_sha256`.
- `code`: `git_commit` (plný SHA), `git_tag`, `git_dirty` (tréning pre produkciu sa nesmie spúšťať s `git_dirty = true`).
- `data`: súbor, `sha256`, počet riadkov, časový rozsah, spôsob rozdelenia train/test.
- `training`: `seed`, hyperparametre, trvanie, hostiteľ.
- `metrics`: test metriky (MAE, RMSE, R2) a baseline na porovnanie.
- `environment`: verzia Pythonu, verzie knižníc, hash `requirements.lock`.
- `lineage`: predošlá verzia.

### Pomenovanie verzií

- SemVer `MAJOR.MINOR.PATCH`, model `pv-forecast`: `MAJOR` = zmena featur alebo kontraktu vstupu/výstupu (vyžaduje zmenu API), `MINOR` = pretrénovanie na nových dátach alebo zmena algoritmu/hyperparametrov, `PATCH` = oprava bez zmeny dát a modelu (napr. len metadáta).
- Git tag pre kód tréningu: `ml-v1.0.0`.
- Cesta v Blob Storage (kontajner `models`), verzie sú nemenné (nikdy sa neprepisujú):

```
models/pv-forecast/v1.0.0/model.joblib
models/pv-forecast/v1.0.0/metadata.json
models/pv-forecast/v1.1.0/...
datasets/pv-forecast/<sha256>.csv    # archív trénovacích dát podľa hashu
```

- Produkčná verzia = hodnota premennej prostredia `MODEL_VERSION` na ML službe (viď ADR #201). Jediný zdroj pravdy, každá zmena vytvorí novú revíziu služby, takže história nasadení je auditovateľná.

### Reprodukovateľný tréning

1. Skript `ml/train.py` číta pevný dátový súbor, počíta jeho SHA-256, nastavuje seed (`random`, `numpy`, parameter `random_state` modelu) a zapíše `metadata.json`.
2. Knižnice sú pripnuté v `requirements.lock` (presné verzie).
3. Po tréningu skript overí, že `artifact_sha256` sedí, a nahrá obe súbory do Blobu (`az storage blob upload-batch`).
4. Reprodukcia: `git checkout ml-vX.Y.Z`, stiahnuť dáta podľa `data.sha256`, vytvoriť prostredie z lock súboru, spustiť tréning; metriky majú vyjsť rovnaké (pri rovnakej platforme a verziách knižníc).

### Postup rollbacku

1. Zistiť predošlú verziu: `metadata.json` aktuálnej verzie → `lineage.previous_version` (alebo zoznam `models/pv-forecast/`).
2. Nastaviť premennú: `az containerapp update -n <ml-app> -g <rg> --set-env-vars MODEL_VERSION=1.0.0` (vytvorí novú revíziu; v Container Apps sa dá alternatívne aktivovať a presmerovať na staršiu revíziu, viď ADR #201).
3. Overiť `GET /health` (vracia aktuálne `model_version` a `artifact_sha256`) a smoke test predikcie.
4. Chybnú verziu v Blobe nemazať, v issue označiť ako `rejected` (pole `notes`), aby ostala sledovateľnosť.

## Dôsledky

- Žiadna nová služba ani závislosť; náklady sú len úložisko Blob (zanedbateľné).
- Zodpovednosť za disciplínu je na skripte a tíme (nič nevynucuje nástroj): treba test, že `train.py` zapíše všetky povinné polia, a CI kontrolu `git_dirty`.
- Nie je UI na porovnávanie pokusov; porovnanie je cez `metadata.json` (alebo notebook).
- Migrácia na MLflow neskôr je lacná: polia metadát sa mapujú na MLflow params/metrics/tags.
- Nadväzujúce issues: implementovať `train.py` s tvorbou metadát; endpoint `/health` s verziou modelu; kontajner `models` a `datasets` v Blobe; test reprodukcie (dvojitý tréning dá rovnaké metriky).

## Neoverené

- Licencie a aktuálny stav MLflow/DVC (nezisťované, len ich všeobecný účel).
- Cena samostatného MLflow servera v Azure (nebola vyčíslená, preto je v porovnaní len kvalitatívne).
- Bitová reprodukovateľnosť modelu naprieč platformami (macOS arm64 vs Linux kontajner) nie je zaručená; kritérium je zhoda metrík v tolerancii, nie identický hash súboru. Treba overiť experimentom.
- Zvolený algoritmus a zoznam featur vo vzorovom súbore sú ilustračné.
