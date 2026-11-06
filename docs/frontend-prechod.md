# Prechod celého frontendu (manuálny test)

Kontrolný zoznam na ručné prejdenie všetkých obrazoviek. Postupuje sa po rolách, lebo každá rola vidí iné menu a iné dáta. Pri každom bode over, že sa obrazovka načíta bez chyby, dáta sedia s tabuľkou nižšie a akcia urobí to, čo má.

Dokument opisuje frontend po zlúčení PR z 6. 11. 2026: checklist, revízia a uvedenie do prevádzky (#422), skutočné dáta na dashboarde (#423), záložka Stringy (#424), výroba v portáli (#425), chyby PDF zmluvy (#426), schválenie návrhu FVE a potvrdenie tiketu zákazníkom.

## 0. Príprava

```bash
docker compose --profile app up -d --build      # PostgreSQL, API (http://localhost:5080) a ML služba z aktuálneho kódu
python3 scripts/demo/reset-demo.py              # zmaže staré demo dáta a vytvorí nové
cd frontend && pnpm install && pnpm dev         # frontend na http://localhost:5173
```

- Skript sa dá spustiť kedykoľvek znova. Zmaže len demo dáta (zákazníci `*@demo.solarops.local`, katalóg `DEMO-*`), účty nechá.
- Pri zmene roly sa najprv odhlás (menu vpravo hore → Odhlásiť) alebo použi okno inkognito.
- Po viacerých neúspešných prihláseniach API dočasne vráti „Too many requests“. Počkaj minútu.

### Účty

| Rola | E-mail | Heslo |
|---|---|---|
| Administrátor | `demo@solarops.local` | `DemoPass12345` |
| Obchod | `obchod@demo.solarops.local` | `DemoPass12345` |
| Projektant | `projektant@demo.solarops.local` | `DemoPass12345` |
| Operátor | `operator@demo.solarops.local` | `DemoPass12345` |
| Technik | `technik@demo.solarops.local` | `DemoTech12345` |
| Technik 2 | `technik2@demo.solarops.local` | `DemoTech12345` |
| Zákazník (Jan Novak) | `novak@demo.solarops.local` | `DemoPass12345` |

Účty vytvára skript, ak ešte neexistujú. Existujúcemu účtu heslo nemení, len mu nastaví rolu.

### Demo dáta

Mená sú v dátach bez diakritiky (vyhľadávaj napr. „Sabo“, „Kral“).

| Zákazník | Stav |
|---|---|
| Jan Novak (Nitra) | FVE 9,9 kWp uvedená do prevádzky, zákaznícky účet. Tiket „Invertor hlasi chybu (demo)“ vyriešený na diaľku a uzavretý pri spustení skriptu. |
| Maria Kovacova | Len žiadosť o návrh (stav Vyžiadaný). Začiatok živého dema. |
| Peter Horvath | Podpísaná zmluva, inštalácia Naplánovaná, bez žiadosti o pripojenie (plán B). |
| Tomas Hudak | Návrh V návrhu s uloženou verziou, odoslaná ponuka. |
| Eva Balazova | Prijatá ponuka, zmluva v koncepte (nepodpísaná). |
| Lucia Mrazova | Podpísaná zmluva, inštalácia Naplánovaná. Distribučka žiada doplnenie žiadosti o pripojenie. |
| Martin Sabo | Inštalácia v **montáži**, checklist 3 z 9 povinných krokov, zaregistrovaný striedač. |
| Jozef Kral | Inštalácia v **revízii**: montáž hotová, revízna správa chýba. |
| Zuzana Lacko | V prevádzke. Tiket „Nizsia vyroba v slnecnych dnoch (demo)“: výjazd technika 2 presunutý na pozajtra 13:00, verejný komentár. |
| Pavol Gajdos | V prevádzke. Tiket „Vypadok vyroby po burke (demo)“ uzavretý po výjazde s protokolom, materiálom a overením. |
| Katarina Vargova | V prevádzke. Tiket „Kontrola vyroby po uvedeni do prevadzky (demo)“ zatriedený, priradený technik, bez výjazdu. |

Všetky návrhy okrem Kovacovej sú V návrhu s jednou uloženou verziou, žiadny nie je schválený.

## 1. Verejná časť (odhlásený)

- [ ] `/` úvodná stránka: hero, kalkulačka výnosu (zmeň spotrebu a strechu, výsledok sa prepočíta), sekcia dopytu ukáže „Online dopyt pripravujeme“.
- [ ] Odkaz „Prihlásiť sa“ vedie na `/login`.
- [ ] `/login`: zlé heslo → chybová hláška; prázdny formulár → chyby pri poliach.
- [ ] `/register`: slabé heslo a nezhodné heslá → chyby.
- [ ] `/forgot-password`: odoslanie e-mailu → potvrdenie.
- [ ] `/customers` bez prihlásenia → presmerovanie na prihlásenie.
- [ ] `/nieco-neexistuje` → stránka 404 s adresou a odkazom späť.

## 2. Administrátor (`demo@solarops.local`)

**Obal aplikácie**
- [ ] Sidebar má sekcie Obchod, Realizácia, Servis, Systém; aktívna položka je zvýraznená.
- [ ] Počty v sidebare: Zákazníci 11, Ponuky 1 (otvorené), Servisné tikety 2 (otvorené). Na čistej databáze; iné záznamy mimo demo ich zvýšia.
- [ ] Karta „Výroba dnes“ dole v sidebare ukazuje 0 kWh a „4 elektrárne v prevádzke“.
- [ ] Zbalenie sidebaru na ikony (stav sa pamätá po obnovení stránky).
- [ ] Ctrl+K (Cmd+K) → rýchly skok, napíš „pon“ → Enter otvorí Ponuky.
- [ ] Breadcrumbs na vnorených stránkach (napr. detail zákazníka).
- [ ] Používateľské menu vpravo hore: e-mail, rola, Odhlásiť.
- [ ] Zvonček notifikácií otvorí okno; kým backend nemá `/api/notifications`, ukáže chybu s tlačidlom na opakovanie.

**Dashboard (`/app`)**
- [ ] KPI: Výroba dnes = 0 kWh („4 elektrárne v prevádzke“), Aktívne alarmy = 0, Otvorené tikety = 2 („0 čakajú na triáž“), Ponuky čakajú na odpoveď = 1.
- [ ] Graf „Výroba v čase“: „Za vybrané obdobie nie sú žiadne údaje“ (seed nemá telemetriu).
- [ ] Posledné alarmy: zoznam otvorených tiketov (Lacko, Vargova).
- [ ] Nikde nie je štítok „Ukážkové dáta“.

**Používatelia a roly (`/admin/users`)**
- [ ] Záložky Všetci / Interní / Zákazníci / Bez roly s počtami.
- [ ] Hľadanie podľa e-mailu, stránkovanie, stav v URL (obnov stránku, filter ostane).
- [ ] Upraviť roly: zmeň rolu niektorému používateľovi → toast „Roly boli uložené“. Odober všetky roly → chyba „Vyberte aspoň jednu rolu“.

**Katalóg (`/catalog`)**
- [ ] Položky `DEMO-*` (panel, striedač, práca), filter podľa typu, pridanie a úprava komponentu.

## 3. Obchod (`obchod@demo.solarops.local`)

- [ ] Menu obsahuje Dashboard, Obchod, Pripojenie k distribučke a Katalóg; `/tickets` cez URL → 403 so sekciou a povolenými rolami.
- [ ] **Zákazníci:** 11 demo zákazníkov, hľadanie („Sabo“), filter, stránkovanie.
- [ ] Detail Jana Novaka: prehľad, záložky s počtami (odberné miesta, návrhy, ponuky, zmluvy, inštalácie).
- [ ] Nový zákazník: firma so zlým IČO → chyba pri poli; osoba bez e-mailu → chyba; uloženie → detail.
- [ ] **Návrhy FVE:** Kovacova Vyžiadaný, ostatné V návrhu.
- [ ] **Schválenie návrhu:** detail návrhu Hudaka → sekcia „Schválenie a ponuka“ → Schváliť návrh → potvrdenie s číslom verzie → „Schválená verzia 1“. Vytvoriť ponuku → otvorí sa nová ponuka v koncepte.
- [ ] **Vrátenie návrhu:** na schválenom návrhu Vrátiť na prepracovanie → bez dôvodu chyba „Vyplňte toto pole.“, s dôvodom → stav V návrhu a text „Vrátené na prepracovanie: …“.
- [ ] **Ponuky:** odoslaná (Hudak), prijaté ostatné; ponuka v koncepte z predchádzajúceho kroku → Odoslať; detail s položkami, DPH a sumou.
- [ ] **Zmluvy:** Balazova v koncepte → Podpísať (vznikne inštalácia) → zaplatiť zálohu. Na podpísanej zmluve Zobraziť náhľad PDF → náhľad a Stiahnuť PDF.

## 4. Projektant (`projektant@demo.solarops.local`)

- [ ] Návrh Kovacovej (Vyžiadaný): zapíš obhliadku → vyplň parametre strechy → vytvor verziu s panelmi a striedačom; príliš málo panelov → chyba pomeru DC/AC.
- [ ] Projektant nevidí tlačidlá Schváliť návrh ani Vytvoriť ponuku.
- [ ] **Inštalácie:** zoznam v skupinách podľa fázy, počty, hľadanie.
- [ ] **Pripojenie k distribučke:** Mrazova so žiadosťou o doplnenie, ostatné ukončené; detail s prílohami a priebehom.
- [ ] Monitoring je dostupný len na čítanie.

## 5. Inštalácie podrobne (Administrátor)

- [ ] **Sabo (Montáž):** pri workflow text o checkliste a tlačidlo Otvoriť checklist. Záložka Checklist: „Povinné kroky: 3 z 9“. Odškrtni zvyšné povinné kroky → „Všetky povinné kroky sú splnené“ → Posunúť do ďalšej fázy (Montáž dokončená) → znova (Revízia).
- [ ] Na Sabovi skús Posunúť do ďalšej fázy pred dokončením checklistu → chyba so zoznamom chýbajúcich krokov.
- [ ] **Kral (Revízia):** v hlavičke Zapísať revíznu správu → záložka Revízia a odovzdanie. Formulár bez údajov → chyby pri poliach; s technikom, platnosťou a PDF → „Revízna správa bola zapísaná.“, stav Revízia vyhovela.
- [ ] Na Kralovi potom Uviesť do prevádzky → zadaj, kto elektráreň prevzal → stav Uvedená do prevádzky, objaví sa Dokumentácia.
- [ ] Na Sabovi (po presune do Revízie) zapíš revíziu s výsledkom Nevyhovela: bez popisu nedostatkov → chyba; s popisom → stav Revízia nevyhovela a tlačidlo Vrátiť na montáž.
- [ ] **Novak (V prevádzke):** záložky Prehľad, Zariadenia (filter typu), Stringy (len „Nepriradené panely“, seed stringy nevytvára), Revízia a odovzdanie (revízna správa, dokumentácia, Stiahnuť odovzdávací protokol v PDF).
- [ ] **Horvath / Mrazova (Naplánovaná):** uloženie termínu montáže v záložke Plán server odmietne, kým nie je schválená žiadosť o pripojenie.

## 6. Operátor (`operator@demo.solarops.local`)

- [ ] **Servisné tikety** sa otvoria ako Kanban s 8 stĺpcami; prepni na zoznam a späť (stav v URL).
- [ ] Vargova (Zatriedený) → Diagnostika na diaľku → naplánuj výjazd priradenému technikovi; kolízia s iným výjazdom technika → chyba.
- [ ] Detail tiketu Gajdosa: časová os (stavy, výjazd), protokol, materiál.
- [ ] **Monitoring výroby:** vyber elektráreň v prevádzke → bez telemetrie prázdny graf a KPI s nulami; prepni 7 / 30 dní a vlastné obdobie (prázdne pole → chyba).
- [ ] Dashboard operátora: výroba a tikety, bez ponúk.

## 7. Technik (`technik2@demo.solarops.local`)

- [ ] Po prihlásení sa otvorí zoznam inštalácií; menu má „Moje výjazdy“, Obchod nie je vidieť.
- [ ] **Moje výjazdy:** výjazd k tiketu Lacka pozajtra 13:00.
- [ ] Výjazd Lacka: Začať výjazd → zapíš materiál a protokol (príčina, zistenia, vykonaná práca) → Overiť obnovenie výroby → stav Overený.
- [ ] Inštalácia Kral je v zozname (pridelený technik 2).

## 8. Zákazník (`novak@demo.solarops.local`)

- [ ] Po prihlásení sa otvorí „Moje elektrárne“; menu má Moje elektrárne a Servisné tikety. `/customers` cez URL → 403.
- [ ] **Moje elektrárne → detail:** priebeh inštalácie, pripojenie, zariadenia, dokumenty. Sekcia Výroba: „Za posledných 30 dní zatiaľ nie sú namerané údaje.“ (bez telemetrie).
- [ ] **Servisné tikety:** len Novakov tiket (žiadne tikety iných zákazníkov), interné poznámky nie sú vidieť.
- [ ] Detail uzavretého tiketu: karta „Bola porucha odstránená?“
  - Potvrdiť opravu (komentár je nepovinný) → „Ďakujeme, opravu ste potvrdili.“ Opakované potvrdenie po obnovení stránky → „Opravu ste už potvrdili.“
  - Znovu otvoriť tiket bez komentára → chyba; s komentárom → „Tiket bol znovu otvorený.“, tiket je znova v diagnostike na diaľku. Znovu otvoriť sa dá len do 7 dní od uzavretia.
- [ ] **Nahlásiť poruchu:** výber elektrárne, popis, príloha (JPEG/PNG/PDF; prázdny popis → chyba) → nový tiket v zozname.

## 9. Prístupnosť a mobil

- [ ] Celý flow prihlásenia, zoznamu tiketov a detailu prejdi len klávesnicou (Tab, Enter, Esc v dialógoch); fokus je vždy viditeľný.
- [ ] Riadok tabuľky sa otvorí klávesom Enter.
- [ ] DevTools → šírka 390 px: hamburger menu, tabuľky sa dajú posúvať, formuláre v jednom stĺpci.
- [ ] Zoom prehliadača 200 %: nič sa neprekrýva.

## Známe obmedzenia (nie sú chyby)

- **Seed nevytvára telemetriu.** Výroba na dashboarde, v monitoringu a v portáli je preto 0 alebo prázdna a nevzniknú alarmy podvýroby. Dáta sa dajú poslať zo simulátora (`ml/src/solarops_ml/simulator`) na `POST /api/telemetry` s API kľúčom zariadenia.
- **Notifikácie:** zvonček volá `GET /api/notifications`, ktorý backend zatiaľ nemá.
- **Stringy** sa v UI dajú len prezerať; vytváranie a priraďovanie panelov je zatiaľ len cez API.
- **Potvrdenie tiketu:** API nevracia, či už zákazník opravu potvrdil, preto sa tlačidlo po obnovení stránky ukáže znova (druhé potvrdenie skončí hláškou „Opravu ste už potvrdili.“).
- **Dôvod zmeny termínu montáže** je povinný, ale backend ho zatiaľ neukladá.
- **Dopyt z úvodnej stránky** backend zatiaľ neprijíma.
- **Technik** vidí cez API všetkých zákazníkov a inštalácie; obmedzenie na pridelené záznamy je zatiaľ len v menu.
- Správa používateľov nemá stav účtu, 2FA ani pozvánky (backend ich nevracia).

## Zápis chýb

Pre každú chybu si poznač: **rola, adresa (URL), kroky, čo sa stalo, čo sa malo stať**, prípadne snímku obrazovky. Chyby potom založ ako issues s labelom `area:frontend`.
