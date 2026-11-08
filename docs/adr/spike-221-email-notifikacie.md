# ADR spike #221: Poskytovateľ e-mailových notifikácií

- **Stav:** Navrhnuté
- **Dátum:** 2026-11-08
- **Issue:** #221 (nadradené #8)
- **Súvisí:** #130 (e-maily o tikete), #131 (upozornenie technika), #271 (notifikačné centrum), #134 (reset hesla, PR #307), ADR #211 (hosting), ADR #215 (náklady), ADR #218 (autentifikácia)

## Kontext

SolarOps dnes nemá žiadneho poskytovateľa e-mailov. Reset hesla končí v logu: `AuthService` volá `IPasswordResetSender`, ktorého jediná implementácia `LoggingPasswordResetSender` zapíše odkaz do logu a nič neodošle. `AuthModule` ju registruje cez `TryAddSingleton`, takže výmena je pripravená, ale chýba reálna implementácia. Rovnako chýba odosielanie pri servisných tiketoch (#130), výjazdoch technikov (#131) a ďalších udalostiach.

Notifikačné centrum v aplikácii už existuje (#271): `GET /api/notifications` počíta položky pri čítaní z existujúcich dát, nemá tabuľku ani úlohu na pozadí. E-mail je samostatný kanál; nesmie blokovať biznis operáciu a nesmie zobrazovať osobné údaje v logoch.

### Požiadavky

| Požiadavka | Dôsledok pre výber |
|---|---|
| Transakčné e-maily v slovenčine (diakritika, `sk-SK`) | UTF-8, HTML aj plain-text, šablóny s premennými |
| Reset hesla, tiket, výjazd, ponuka, zmluva, inštalácia, alarm | viac šablón, spoločné rozloženie a renderer |
| Malý objem, študentský rozpočet | pay-as-you-go alebo free tier, žiadny vysoký mesačný paušál |
| Európska účasť na spracovaní (GDPR) | región EÚ, DPA, minimalizácia osobných údajov |
| Lokálny vývoj a testy bez odosielania reálnych e-mailov | lokálny SMTP capture (Mailpit) alebo fake sender |
| Výmena poskytovateľa bez zásahu do biznis kódu | rozhranie `IEmailSender`, konfigurácia mimo gitu |
| Výpadok e-mailu nesmie zhodiť operáciu | outbox, retry, idempotencia, logovanie bez citlivých údajov |
| Azure Container Apps a Azure for Students | odchádzajúci port 25 je blokovaný, použiteľný je 587/465 |

### Odhad objemu

Konzervatívny odhad pre demo a prvú prevádzku (jeden mesiac):

| Zdroj | Počet/mesiac |
|---|---:|
| Servisné tikety: vytvorenie, pridelenie, vyriešenie, uzavretie (20 tiketov x 4) | 80 |
| Výjazdy: naplánovanie a zmena termínu (10 x 2) | 20 |
| Reset hesla (20 x 1) | 20 |
| Ponuky a zmluvy (10 x 2) | 20 |
| Alarmy podvýroby pre operátorov (100 inštalácií x 1) | 100 |
| **Spolu** | **cca 240** |

Návrh preto dimenzujeme na **500 e-mailov/mesiac** s rezervou do **5 000/mesiac**; denný objem sa v demonštrácii pohybuje v desiatkach, nie tisícoch. To je dôležité pri provideroch s denným limitom alebo sandboxom.

## Overené východiská

- **Kód:** `src/SolarOps.Api/Features/Auth/PasswordReset.cs` obsahuje `IPasswordResetSender` a `LoggingPasswordResetSender`; `AuthModule` ho registruje ako náhradnú implementáciu. Reálny sender teda chýba, ale rozhranie existuje.
- **Notifikácie:** `Features/Notifications` je iba in-app zvonček; e-mailová tabuľka ani fronta neexistujú.
- **BPMN:** `docs/bpmn/mapovanie-entit.md` počíta s entitou `Notification` v stavoch `Pending`, `Sent`, `Failed` pre #130, #131 a #271. Outbox v tomto ADR je jej konkrétna realizácia.
- **Hosting:** ADR #211 určuje Azure Container Apps (Consumption) a Static Web Apps Free. Azure blokuje odchádzajúci SMTP na porte 25 pre študentské a väčšinu non-enterprise predplatných; autentifikovaný SMTP na porte 587 alebo 465 blokovaný nie je [1].
- **Rozpočet:** Azure for Students dáva 100 USD kredit (#215). E-mailový provider mimo Azure sa platí samostatne, pri našom objeme však ide o centy.
- **GDPR:** zákaznícke e-maily a mená sú osobné údaje. Musia mať región EÚ, DPA a minimalizáciu; audit a anonymizácia z #140 nesmú byť obídené e-mailovým logom.

## Zvažované možnosti

| Kritérium | **A. Amazon SES (SMTP/API)** | B. SendGrid | C. Azure Communication Services Email | D. Vlastný alebo univerzitný SMTP relay |
|---|---|---|---|---|
| Model | pay-as-you-go, SMTP aj API | SaaS, SMTP aj API | pay-as-you-go v Azure, SMTP aj API | vlastná prevádzka alebo školský server |
| Cena | Essentials **0,16 USD/1 000** e-mailov; à la carte **0,10 USD/1 000** [2] | trial 60 dní (100/deň), potom **19,95 USD/mesiac** za 50 000 [3] | **0,00025 USD/e-mail** + 0,00012 USD/MB [4] | 0 USD za správu, ale platíte server, IP a prácu |
| Free tier | nový AWS účet: do **200 USD** kreditov na 6-mesačnom free pláne, platnosť 12 mesiacov [2] | žiadny trvalý free tier; len 60-dňový trial [3] | žiadny e-mailový free tier [4] | podľa školy, zvyčajne bez SLA |
| Predvolené limity | sandbox: 200 e-mailov/24 h, 1/s; po production accesse podľa use case [5] | podľa plánu, 50 000/mesiac v Essentials | custom doména: 30/min a 100/h; managed doména: 5/min a 10/h, vyššie limity len pre custom doménu [6] | bez záruky, často denný limit alebo blokovanie |
| Protokoly a .NET | SMTP 587/465, AWS SDK, MailKit | SMTP aj REST API, oficiálne SDK | SMTP aj REST API, `Azure.Communication.Email` 1.1.0 [7] | ľubovoľný SMTP klient |
| EÚ a GDPR | región `eu-central-1` (Frankfurt) alebo `eu-west-1` (Írsko); AWS DPA [12] | EÚ data residency dostupná; DPA overiť | geografia Europe, EU Data Boundary; DPA [9] | závisí od školy, často bez DPA |
| Lokálny vývoj | Mailpit alebo fake sender, bez zmeny kódu | Mailpit alebo fake sender | Mailpit alebo fake sender | Mailpit alebo fake sender |
| Prevádzková zložitosť | stredná: AWS účet, IAM/SMTP credentials, overenie domény, žiadosť o production access | nízka: účet, overenie domény, API key | nízka až stredná: Azure resource, overenie domény, kvóty | vysoká: IP reputácia, SPF/DKIM/DMARC, blocklisty, 24/7 |
| Životný cyklus | stabilná služba | stabilná služba | **ACS Email sa ruší 30. 9. 2028**; maintenance mode (len bezpečnostné a kritické opravy). Nové ACS Email resources sa zatiaľ vytvárať dajú, ale Microsoft odporúča migráciu namiesto nových nasadení a dostupnosť môže kedykoľvek obmedziť [10] | závisí od univerzity |
| Verdikt | **zvolené** | záložná platená možnosť | **zamietnuté pre nový vývoj** | zamietnuté pre produkt |

### A. Amazon SES

- Najnižšia cena pri našom objeme a žiadny mesačný paušál. Pri 500 e-mailoch/mesiac je to **0,08 USD** v Essentials alebo **0,05 USD** à la carte; nový účet má navyše 200 USD kreditov.
- Podporuje SMTP aj API. SMTP je univerzálne, funguje s MailKitom a s lokálnym Mailpitom; neskôr sa dá pridať API pre delivery eventy bez zmeny biznis kódu.
- Región `eu-central-1` alebo `eu-west-1` je v EÚ; TLS endpointy `email-smtp.eu-central-1.amazonaws.com:587` a `email-smtp.eu-west-1.amazonaws.com:587` sme overili (TLS 1.3, platný certifikát).
- Nevýhody: samostatný AWS účet a billing mimo Azure, sandbox a žiadosť o production access (AWS uvádza prvú odpoveď do 96 hodín), overenie domény a SPF/DKIM/DMARC. Pre školský tím je to jednorazové nastavenie, ktoré sa dá zdokumentovať.

### B. SendGrid

- Najjednoduchší onboarding a dobrá dokumentácia; 60 dní zdarma (100 e-mailov/deň), potom **19,95 USD/mesiac** za Essentials s 50 000 e-mailmi [3].
- Má SMTP aj API, EÚ data residency a DPA; pre transakčné e-maily je dostatočný.
- Pre náš objem je mesačný paušál zbytočne vysoký a po skončení trialu sa nedá zostať na nule. Nemá trvalý free tier, ktorý by študentský projekt využil.
- Záver: vhodná záložná možnosť, ak AWS onboarding alebo production access zlyhá a tím je ochotný platiť.

### C. Azure Communication Services Email

- Azure-native, jednotný billing a monitoring, nízka cena za e-mail a možnosť EÚ geografie [4][9].
- **Kľúčové zistenie:** Microsoft 23. 9. 2026 oznámil ukončenie samostatného ACS k 30. 9. 2028. ACS Email je v zozname **Retired**; po oznámení je v maintenance mode (len bezpečnostné a kritické opravy). Nové ACS Email resources sa zatiaľ vytvárať dajú, ale Microsoft výslovne odporúča migráciu existujúcich workloadov namiesto onboardingu nových riešení a dostupnosť môže kedykoľvek obmedziť [10].
- Predvolené limity pre custom doménu sú 30/min a 100/h; managed doména je iba na testovanie (5/min, 10/h) a vyššie limity sa pre ňu nedajú získať [6]. Pre demo to stačí, pre prevádzku to vyžaduje overenie vlastnej domény a žiadosť o kvótu.
- Záver: pre nový projekt **neodporúčame** stavať na službe, ktorá má pevný dátum ukončenia a je v maintenance mode. Ak by tím trval na Azure-only riešení, ACS môže byť dočasný adaptér za `IEmailSender` s naplánovanou migráciou pred 30. 9. 2028, nie strategická voľba.

### D. Vlastný alebo univerzitný SMTP relay

- Priamym odosielaním z vlastného servera sa vyhneme poplatku za správu, ale preberáme celú zodpovednosť za IP reputáciu, SPF/DKIM/DMARC, blocklisty, bounce a complaint handling.
- Azure navyše blokuje port 25 pre študentské predplatné [1], takže priamy MTA z Container Apps neprichádza do úvahy; museli by sme ísť cez relay alebo povolený port 587, čo je v podstate služba poskytovateľa.
- Univerzitný SMTP môže byť dostupný, ale zvyčajne nemá SLA, DPA ani garanciu doručiteľnosti pre aplikáciu a môže mať limity alebo podmienky, ktoré transakčný e-mail nesplní.
- Záver: **zamietnuté** pre produkt; je to len núdzová možnosť pre lokálne demo.

### Ďalší kandidáti (mimo hlavného porovnania)

- **Resend:** free 3 000 e-mailov/mesiac, 100/deň, SMTP relay aj API, GDPR a 3 domény [11]. Pre demo je to najlacnejšia a najrýchlejšia cesta, ale mladší vendor a free tier s denným limitom; vhodný ako dočasný fallback, nie ako jediná dlhodobá voľba.
- **Brevo:** free plán s denným limitom a EÚ sídlom, vhodný skôr pre marketing; transakčné limity a podmienky free plánu treba overiť pred použitím.
- **Mailgun/Postmark:** kvalitné transakčné služby, ale platené alebo s krátkym trialom; pre náš objem bez výhody oproti SES.

## Rozhodnutie

1. **Poskytovateľ: Amazon SES**, región **`eu-central-1`** (záložný `eu-west-1`), typ prevádzky **transakčný e-mail**. Pri objeme do 5 000 e-mailov/mesiac je cena do 1 USD/mesiac; nový účet má 200 USD kreditov.
2. **Transport: SMTP na porte 587 s STARTTLS** (alebo 465 s implicitným TLS), klientská knižnica **MailKit** (MIT, aktuálna stabilná verzia 4.18.2; staršie 4.14.x mali známe zraniteľnosti). SMTP je za rozhraním `IEmailSender`, takže poskytovateľa možno vymeniť bez zásahu do biznis kódu.
3. **ACS Email sa pre nový vývoj nepoužije** pre oznámené ukončenie k 30. 9. 2028 a maintenance mode [10]. Ak by Azure-only požiadavka bola tvrdá, implementuje sa dočasný `AcsEmailSender` za tým istým rozhraním a migration issue musí byť otvorená pred koncom roka 2027.
4. **SendGrid je záložná platená možnosť**, ak zlyhá AWS onboarding alebo production access. **Resend free** je prípustný len ako dočasný demo fallback; nie je súčasťou produkčného rozhodnutia.
5. **Lokálny vývoj:** Mailpit v docker-compose (SMTP 1025, UI 8025); v Development je predvolený `LoggingEmailSender`, ktorý do logu zapíše iba `TemplateKey` a maskovaného príjemcu (`j***@example.com`), nikdy telo správy, plnú adresu ani reset token. Testy používajú fake sender; integračné testy môžu použiť Mailpit.
6. **Šablóny a outbox:** HTML + plain-text šablóny v slovenčine, renderer s escapovaním; obchodné notifikácie idú cez outbox (`Pending/Sent/Failed`), reset hesla sa odosiela priamo, aby sa token neukladal.
7. **Bezpečnosť a GDPR:** TLS vždy, secrets len v env/user-secrets/Key Vault, žiadne tokeny a telá e-mailov v logoch, SPF/DKIM/DMARC, DPA s AWS, minimalizácia a retencia.

## Návrh integrácie

### Abstrakcia a DI

```csharp
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record EmailMessage(
    string ToEmail,
    string? ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    string? ReplyTo = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
```

- `SmtpEmailSender` používa MailKit; `SmtpClient` sa vytvára a zatvára pre každú správu (nie je thread-safe), s timeoutom a TLS.
- `LoggingEmailSender` je predvolený v Development a v testoch; zapíše `TemplateKey` a maskovaného príjemcu, ale nie telo správy ani plnú e-mailovú adresu.
- `EmailPasswordResetSender` nahradí `LoggingPasswordResetSender` tak, že zostaví správu z šablóny `password_reset` a zavolá `IEmailSender`. Rozhranie `IPasswordResetSender` môže ostať ako tenká fasáda, alebo sa nahradí priamo `IEmailSender` v `AuthService`.
- Registrácia cez `IOptions<EmailOptions>` a `TryAdd`; pri `Email:Provider=None` sa použije logging sender, pri `Smtp` sa vyžaduje host a from adresa. Validácia pri štarte (`ValidateOnStart`) zabráni tichému zlyhaniu.

### Outbox

Pre obchodné notifikácie platí outbox vzor: zmena entity a záznam o e-maile sa uložia v jednej transakcii, e-mail sa odošle asynchrónne. Výpadok SMTP tak nezastaví vytvorenie tiketu, pridelenie výjazdu ani zmenu stavu (#130).

Tabuľka `email_outbox` (názov sa môže prispôsobiť BPMN entite `Notification`):

| Stĺpec | Význam |
|---|---|
| `id` | UUID |
| `template_key` | napr. `ticket_created`, `visit_scheduled` |
| `recipient_kind` + `recipient_id` | `user`/`customer` a jeho ID; e-mail sa dohľadá pri odoslaní |
| `entity_id` | ID tiketu, výjazdu, ponuky… |
| `payload` | `jsonb` len s nevyhnutnými hodnotami (číslo, stav, termín), nie celý zákazník |
| `dedup_key` | jedinečný kľúč, napr. `ticket:{id}:created`, aby opakovaná udalosť neposlala duplicitný e-mail |
| `status` | `Pending`, `Sending`, `Sent`, `Failed`, `Dead` |
| `attempts`, `next_attempt_at`, `last_error` | retry s exponenciálnym backoffom (1 min, 5 min, 15 min, 1 h; max. 5 pokusov) |
| `created_at`, `sent_at` | čas v UTC; retencia 30 dní po odoslaní |

`EmailOutboxWorker : BackgroundService` beží každých 10 s, berie dávku 20 záznamov cez `FOR UPDATE SKIP LOCKED`, odosiela a aktualizuje stav. Pri štarte obnoví zaseknuté `Sending` záznamy staršie ako 5 minút. Po prekročení pokusov stav `Dead` a upozornenie v logu/metrike; ručné preposlanie je samostatná operácia.

**Reset hesla je výnimka:** odosiela sa priamo v požiadavke, pretože token má platnosť 1 hodinu a nechceme ho ukladať do outboxu. Ak odoslanie zlyhá, endpoint stále vráti generickú odpoveď (ochrana pred enumeráciou účtov) a chyba sa zaloguje bez tokenu.

### Šablóny

Štruktúra: `Features/Notifications/EmailTemplates/{key}.html` a `{key}.txt`, spoločný `_layout.html` s hlavičkou, pätičkou a inline CSS. Renderer: **Scriban** (MIT) alebo jednoduchý `{{placeholder}}` s HTML escapovaním; pre menší počet šablón stačí druhá možnosť. Všetky hodnoty sa escapujú, odkazy sa validujú, do HTML sa nevkladá neoverený vstup.

| Kľúč | Spúšťač | Príjemca | Predmet (sk) | Premenné |
|---|---|---|---|---|
| `password_reset` | `POST /api/auth/forgot-password` (#134, PR #307) | používateľ | `Obnovenie hesla – SolarOps` | `DisplayName`, `ResetLink`, `ExpiresAt` |
| `welcome` | registrácia alebo vytvorenie účtu | používateľ | `Vitajte v SolarOps` | `DisplayName`, `LoginUrl` |
| `ticket_created` | vytvorenie tiketu (#130) | zákazník | `Nový servisný tiket #{{number}}` | `CustomerName`, `TicketNumber`, `Title`, `Priority`, `Link` |
| `ticket_assigned` | pridelenie tiketu technikovi (#130/#131) | technik | `Bol vám pridelený tiket #{{number}}` | `TechnicianName`, `TicketNumber`, `Title`, `CustomerName`, `Address`, `Link` |
| `ticket_resolved` | vyriešenie/uzavretie tiketu (#129/#130) | zákazník | `Váš tiket #{{number}} bol vyriešený` | `CustomerName`, `TicketNumber`, `Link` |
| `visit_scheduled` | naplánovanie alebo zmena výjazdu (#131) | technik, zákazník | `Naplánovaný výjazd k tiketu #{{number}}` | `Name`, `VisitDate`, `TicketNumber`, `Address`, `Link` |
| `quote_sent` | odoslanie ponuky | zákazník | `Ponuka č. {{number}} je pripravená` | `CustomerName`, `QuoteNumber`, `ValidUntil`, `Link` |
| `contract_ready` | zmluva alebo PDF pripravené | zákazník | `Zmluva č. {{number}}` | `CustomerName`, `ContractNumber`, `Link` |
| `installation_scheduled` | naplánovanie inštalácie | zákazník | `Termín inštalácie` | `CustomerName`, `InstallationDate`, `Address`, `Link` |
| `anomaly_alert` | alarm podvýroby (#251/#252) | operátor/technik | `Alarm: podvýroba na {{installation}}` | `InstallationName`, `Period`, `LostKwh`, `Link` |
| `gdpr_anonymized` | anonymizácia zákazníka (#140) | zákazník (ak má e-mail) alebo admin | `Vaše osobné údaje boli anonymizované` | `Reference`, `Date` |
| `test` | diagnostika | vývojár | `SolarOps test e-mail` | `Timestamp`, `Environment` |

Pravidlá: `CultureInfo("sk-SK")`, dátumy `d. M. yyyy H:mm`, meny `12 345,67 €`, čas v `Europe/Bratislava` (v DB UTC). HTML max. šírka 600 px, bez externých obrázkov a tracking pixelov (GDPR), vždy plain-text alternatíva. Transakčné e-maily (reset, tiket) sa neodhlasujú; marketingové by mali samostatný opt-out, ale v tomto projekte sa neposielajú.

### Konfigurácia

```json
"Email": {
  "Provider": "Smtp",
  "FromEmail": "no-reply@notify.solarops.sk",
  "FromName": "SolarOps",
  "ReplyTo": "info@solarops.sk",
  "Smtp": {
    "Host": "email-smtp.eu-central-1.amazonaws.com",
    "Port": 587,
    "Security": "StartTls",
    "UserName": "",
    "Password": ""
  }
}
```

- Development: `Email:Provider=None` alebo `Smtp` s `Host=localhost`, `Port=1025`, `Security=None`, bez auth (Mailpit).
- Secrets: `Email__Smtp__UserName` a `Email__Smtp__Password` cez env, `dotnet user-secrets` alebo Key Vault; nikdy v gite.
- Docker compose: služba `mailpit` s portmi `1025` a `8025`, pod profilom `mail`, aby sa nespúšťala pri bežnom vývoji.

### Testovanie

- **Unit:** renderer (diakritika, escapovanie, chýbajúca premenná), `EmailOptions` validácia, outbox stavový automat a backoff.
- **Integration:** fake `IEmailSender` v biznis testoch (tiket vytvorí záznam, zlyhanie odoslania nezhodí API); Mailpit cez Testcontainers alebo docker-compose pre overenie SMTP, `multipart/alternative`, UTF-8 a hlavičiek.
- **Manuálne:** odoslanie cez Mailpit a kontrola v UI/API; reálne odoslanie cez SES len v stagingu s testovacou doménou a jedným overeným príjemcom.
- **CI:** žiadne reálne e-maily; `Email:Provider=None` a fake sender.

### Prevádzka a doručiteľnosť

- Overiť doménu `notify.solarops.sk` v SES, nastaviť SPF, DKIM a DMARC (`p=none` na začiatok, potom `quarantine`), `MAIL FROM` subdoménu.
- Po overení požiadať o **production access** (sandbox → production); dovtedy posielať len na overené adresy.
- Zapnúť bounce a complaint eventy (SES configuration set → SNS/EventBridge) a automaticky potláčať adresy; bez toho hrozí zablokovanie účtu.
- Sledovať `Send`/`Delivery`/`Bounce`/`Complaint` metriky, počet `Dead` záznamov a latenciu outboxu.
- Retencia: outbox 30 dní, logy bez tela a tokenov; príjemcov a obsah minimalizovať.

## Overenie odoslania (PoC)

### Lokálne SMTP (vykonané)

Dňa 8. 11. 2026 som spustil Mailpit a poslal vzorovú slovenskú notifikáciu cez **MailKit 4.18.2** na `.NET 10`:

1. `docker run -d --name solarops-mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit:latest`
2. Konzolová aplikácia s `MimeMessage`, `BodyBuilder` (plain + HTML), `SmtpClient.ConnectAsync("localhost", 1025, SecureSocketOptions.None)`.
3. Výsledok: odoslanie prebehlo bez výnimky, Mailpit správu prijal (`multipart/alternative`, `text/plain` aj `text/html` v UTF-8, predmet `Nový servisný tiket #2026-0042`, príjemca `Ján Novák <jan.novak@example.test>`, veľkosť 1 874 B).
4. Diakritika sa overila v oboch častiach: `Nový servisný tiket`, `Dobrý deň, Ján Novák`, `Elektráreň nevyrába, hláška „prehriatie“`, `Ďakujeme`.
5. Zachytená správa je v [`assets/spike-221-sample.eml`](assets/spike-221-sample.eml); [`assets/spike-221-sample.html`](assets/spike-221-sample.html) je návrh výslednej šablóny s tabuľkovým rozložením (nie doslovná kópia tela v `.eml`).

Tým je overený SMTP transport, kódovanie, alternatívne telo a väzba na lokálny vývoj. **Nie je** tým overené doručenie cez konkrétneho poskytovateľa.

### Amazon SES (overená konektivita, neodoslané)

- `openssl s_client -starttls smtp -connect email-smtp.eu-central-1.amazonaws.com:587` → TLS 1.3, `TLS_AES_256_GCM_SHA384`, certifikát `CN=email-smtp.eu-central-1.amazonaws.com`, `Verification: OK`, `250 Ok`.
- To isté pre `email-smtp.eu-west-1.amazonaws.com:587` → TLS 1.3, certifikát sedí, `250 Ok`.
- Autentifikované odoslanie som **nevykonal**: v prostredí nie je AWS účet ani SES SMTP credentials a bez overenej domény by správu aj tak neprijal. Je to prvý krok implementačnej issue, nie spike.

### ACS Email (neodoslané)

- Azure CLI je prihlásené na študentské predplatné, ale MFA relácia vypršala a `az` odmieta operácie; navyše ACS Email je v retirement režime. Žiadny ACS resource som nevytváral a nič neodosielal.

## Odhad nákladov

Ceny k 8. 11. 2026, bez DPH a kurzových rozdielov. Prehľad je orientačný; presnú faktúru určí provider.

| Objem/mesiac | SES Essentials | SES à la carte | SendGrid Essentials | ACS Email | Resend |
|---:|---:|---:|---:|---:|---:|
| 500 | 0,08 USD | 0,05 USD | 19,95 USD | 0,125 USD | 0 USD (free) |
| 5 000 | 0,80 USD | 0,50 USD | 19,95 USD | 1,25 USD | 0 USD do 3 000, inak 20 USD |
| 50 000 | 8,00 USD | 5,00 USD | 19,95 USD | 12,50 USD | 20 USD (Pro) |

Poznámky: SES Essentials je predvolený plán pre nové účty od 21. 7. 2026; à la carte je lacnejšia, ale bez niektorých funkcií [2]. ACS cena nezahŕňa dátový prenos (0,00012 USD/MB), pri textových e-mailoch zanedbateľný. SendGrid Essentials za 19,95 USD obsahuje 50 000 e-mailov. Resend free má 3 000/mesiac a 100/deň [11].

**Záver:** pri 500 e-mailoch/mesiac je SES prakticky zdarma a aj pri 50 000 je to najlacnejšia rozumná voľba. SendGrid sa vyplatí len ak tím nechce AWS účet a akceptuje paušál.

## Dôsledky

- (+) Náklady sú pri našom objeme takmer nulové a rastú lineárne, bez mesačného minima.
- (+) SMTP + MailKit drží kód nezávislý od poskytovateľa; výmena na SendGrid, Resend alebo ACS je len nová implementácia `IEmailSender`.
- (+) Lokálny vývoj a testy sú offline cez Mailpit; do CI netreba žiadne credentials.
- (+) EÚ región a DPA podporujú GDPR; e-maily neobsahujú zbytočné osobné údaje.
- (+) Outbox zabráni strate notifikácií a splní kritérium „chyba odoslania nezastaví biznis operáciu“ (#130).
- (-) Pribudne AWS účet, billing a jednorazové nastavenie domény a production accessu; to je mimo Azure for Students.
- (-) SMTP nemá tak bohaté delivery eventy ako API; pre bounce/complaint treba doplniť SES configuration set alebo neskôr API.
- (-) Šablóny a outbox sú nový kód a nová tabuľka; treba migráciu, worker a testy.
- (-) Ak sa AWS onboarding zdrží, dočasne treba Resend free alebo SendGrid; to je explicitne záložná cesta, nie súčasť rozhodnutia.

## Riziká a neoverené

- **Production access v SES** môže trvať až 96 hodín alebo dlhšie; bez neho je účet v sandboxe (200/24 h, 1/s) a posiela len na overené adresy [5]. Toto je najväčšie riziko harmonogramu.
- **Doručiteľnosť** do Gmailu, Outlooku a slovenských poskytovateľov nie je otestovaná; prvé reálne správy treba poslať na kontrolné schránky a sledovať bounce/complaint.
- **SES ceny a plány** sa môžu zmeniť; tabuľka je z 8. 11. 2026. Rovnako sa môžu zmeniť podmienky free tieru a production accessu.
- **AWS účet** musí vlastniť tím, nie jednotlivec; inak hrozí, že po odchode študenta stratíte prístup. DPA a používateľské podmienky treba overiť pred produkciou.
- **ACS retirement** je oznámený, ale termíny a rozsah sa môžu posunúť; ak by sa ACS Email predsa len použil, treba sledovať oficiálnu retirement stránku a mať migration plán.
- **MailKit verzie:** 4.14.x mali známe zraniteľnosti; použiť aktuálnu 4.18.2 alebo novšiu a sledovať NuGet advisories.
- **Šablóny** sú návrh; vizuálna kontrola v reálnych klientoch (Gmail, Outlook, Apple Mail) a responzivita na mobile nie sú overené.
- **Odoslanie cez SES** nebolo vykonané; overená je len TLS konektivita a lokálny SMTP. Toto je zámerne uvedené ako neoverené, nie ako hotové.

## Zdroje

1. Azure, Troubleshoot outbound SMTP connectivity (port 25 blokovaný, 587 povolený): https://learn.microsoft.com/en-us/azure/virtual-network/troubleshoot-outbound-smtp-connectivity
2. Amazon SES pricing (Essentials, à la carte, free credits): https://aws.amazon.com/ses/pricing/
3. Twilio SendGrid Email API pricing (60-dňový trial, Essentials 19,95 USD): https://www.twilio.com/en-us/products/email-api/pricing
4. Azure Communication Services Email pricing (0,00025 USD/e-mail): https://learn.microsoft.com/en-us/azure/communication-services/concepts/email-pricing
5. Amazon SES quotas a sandbox (200/24 h, 1/s): https://docs.aws.amazon.com/ses/latest/dg/quotas.html a https://docs.aws.amazon.com/ses/latest/dg/request-production-access.html
6. Azure Communication Services service limits (30/min, 100/h; managed 5/min, 10/h): https://learn.microsoft.com/en-us/azure/communication-services/concepts/service-limits
7. Azure.Communication.Email 1.1.0 na NuGet: https://www.nuget.org/packages/Azure.Communication.Email
8. AWS SES SMTP interface a SMTP endpointy: https://docs.aws.amazon.com/ses/latest/dg/send-email-smtp.html
9. Azure Communication Services data residency a EU Data Boundary: https://learn.microsoft.com/en-us/azure/communication-services/concepts/privacy a https://learn.microsoft.com/en-us/azure/communication-services/concepts/european-union-data-boundary
10. ACS retirement and breaking changes guide (ACS Email retirement k 30. 9. 2028, maintenance mode): https://aka.ms/acs-retirement-and-breaking-changes-guide
11. Resend pricing (free 3 000/mesiac, 100/deň): https://resend.com/pricing
12. AWS GDPR Center (DPA a spracovanie údajov): https://aws.amazon.com/compliance/gdpr-center/

## Nadväzujúce issues

- **#130** Odosielať e-mailové notifikácie o tikete: implementovať outbox, šablóny `ticket_created`, `ticket_assigned`, `ticket_resolved` a napojenie na zmeny stavu.
- **#131** Upozorniť technika na nový výjazd: šablóna `visit_scheduled`, preferencie notifikácií a test s fake senderom.
- **#271** Notifikačné centrum: e-mail je samostatný kanál; in-app zvonček ostáva bez zmeny.
- **#134** Reset hesla (PR #307): nahradiť `LoggingPasswordResetSender` reálnym `EmailPasswordResetSender` a šablónou `password_reset`.
- Nová issue **„Nastaviť SES doménu, SPF/DKIM/DMARC a production access“** (Sprint 4): overenie `notify.solarops.sk`, sandbox exit, bounce/complaint eventy, DPA.
- Nová issue **„Mailpit v docker-compose a e-mailové testy“**: lokálny capture, Testcontainers, CI bez credentials.
- Nová issue **„Šablóny a outbox pre e-maily“**: renderer, `email_outbox`, worker, retry a retencia.

Poznámka: výsledok spiku sa má podľa zadania zapísať aj ako komentár k issue #221. V tomto prostredí nie je `gh` prihlásený, preto je zatiaľ zapísaný len v ADR; komentár doplníme po prihlásení alebo cez GitHub UI.
