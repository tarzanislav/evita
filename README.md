# EviTA – evidencia IT techniky

Desktopový program pre Windows 10/11 na evidenciu informačných technológií, zariadení a príslušenstva.

## Stiahnutie a inštalácia

1. Otvorte [najnovšiu verziu](https://github.com/tarzanislav/evita/releases/latest).
2. Stiahnite súbor **EviTA_Setup_X.Y.Z.exe** (X.Y.Z je číslo verzie) a spustite ho.
3. Windows môže zobraziť modré upozornenie *Neznámy vydavateľ* (inštalátor zatiaľ nemá platený podpisový certifikát).
   Kliknite na **Ďalšie informácie → Spustiť aj tak**.

Pri prvom spustení si vyberiete, či chcete začať s **ukážkovými dátami** (200 položiek, 30 osôb, 25 miestností),
s **prázdnou databázou**, alebo **naimportovať existujúcu evidenciu** zo súboru. Ukážkové dáta neskôr vymažete
v časti *Servis → Databáza → Vymazať dáta a začať nanovo*.

## Jazyky

Program je v **angličtine** (predvolený jazyk), **slovenčine, češtine, poľštine, maďarčine, ruštine a čínštine**.
Jazyk zmeníte v časti *Nastavenia → Jazyk programu*; tlačidlo **Language reset** vráti angličtinu. Zmena sa prejaví
po reštarte programu. Prekladá sa celé rozhranie, správy, tlačové zostavy, PDF a hlavičky exportov; import rozpozná
stĺpce v ktoromkoľvek jazyku. Dáta evidencie (názvy položiek, miestností, osôb) sa neprekladajú – počiatočné
kategórie a typy udalostí, ktoré ste nepremenovali, sa po zmene jazyka premenujú do nového jazyka.

Preklady sú v `src/Evita.App/Preklady/<jazyk>.json` (kľúčom je slovenský text). Skript `nastroje/preklady.py kontrola`
overí, že každý text v programe má preklad vo všetkých jazykoch.

## Dashboard, úlohy a poznámky

- **Dashboard:** dnešný deň s meninami a sviatkami, úlohy (To Do), **počasie** pre zvolené mesto alebo obec
  (Open-Meteo, predpoveď na 5 dní), **kurzový lístok** ECB s vývojom za 30 dní a prepočtom mien, pripnuté poznámky,
  kalendár na 7 dní, počty, upozornenia. Panely sa dajú skryť (*Prispôsobiť dashboard*).
- **Úlohy:** samostatný zoznam úloh s prioritou, termínom, popisom a voliteľným naviazaním na položku evidencie.
- **Poznámky:** poznámkový blok s farbami, pripnutím a vyhľadávaním; poznámku možno prepojiť s dňom v kalendári,
  s udalosťou alebo s položkou evidencie (zobrazí sa v kalendári aj v karte položky).
- **Diktovanie poznámok** (tlačidlo *Diktovať* v poznámke, slovenčina): buď **hlasové písanie Windows** (Win+H –
  píše počas hovorenia, potrebuje internet, rozpoznáva Microsoft; v systéme musí byť slovenská klávesnica), alebo
  **prepis v počítači bez internetu** (Whisper – zvuk ani text neodchádzajú z počítača; model 148 – 574 MB sa raz
  stiahne a overí podľa SHA-256, ukladá sa mimo databázy, aby nezväčšoval zálohy). Spôsob a model sa volia
  v *Nastavenia → Diktovanie poznámok…*. Prepis v počítači potrebuje procesor s AVX2 (približne od roku 2013).

## Citát dňa a výročia

- **Citát dňa** na dashboarde – v jazyku programu (zabudované príslovia a citáty vo všetkých jazykoch programu).
  Ďalší citát, skrytie nechceného jedným kliknutím; vlastné citáty pridáte v časti *Citáty a výročia*.
- **Výročia dňa** – udalosti, narodenia a úmrtia zo stránky dňa vo Wikipédii v jazyku programu (napr. „20. október“
  zo slovenskej Wikipédie). Na dashboarde sú prednostne domáce (slovenské) a okrúhle výročia. Sťahujú sa
  automaticky na dnes a týždeň dopredu; tlačidlom *Stiahnuť celý rok…* sa uložia všetky dni a potom fungujú aj
  bez internetu. Vlastné výročia pridáte ku ktorémukoľvek dňu, nechcené skryjete alebo vymažete.
- Všetko je v **Kalendáriu** (`C:\ProgramData\Kalendarium\kalendarium.db`) spolu s meninami a sviatkami – spoločnej
  databáze pre ďalšie programy. Citáty a výročia sa dajú **exportovať a importovať** v súbore JSON.

## Nastavenia a konfigurácia

V časti *Nastavenia* je jazyk, miesto pre počasie, meny kurzového lístka a **export a import konfigurácie programu**
(súbor JSON bez dát evidencie – jazyk, panely dashboardu, nastavenia kalendára a typy udalostí, pravidelné zálohy,
názov a logo organizácie, tlač a export, skenovanie a štítky). Heslo automatických záloh sa z bezpečnostných dôvodov neprenáša.

## Import a export

- **Export** (menu *Import a export*, alebo tlačidlo *Export* v Evidencii pre vyfiltrované či označené položky):
  Excel, CSV (UTF-8, bodkočiarka), JSON, XML s popisom formátu `evita.xsd`, HTML a PDF, s voľbou stĺpcov.
- **Import** cez sprievodcu v 5 krokoch: výber súboru (Excel, CSV, JSON, XML), priradenie stĺpcov, kontrola
  s opravou priamo v tabuľke, náhľad a import s protokolom. Pred zápisom sa urobí záloha a import sa dá jedným
  kliknutím vrátiť. Duplicity sa hľadajú podľa identifikátora, sériového čísla a zhody názvu, kategórie a miestnosti.
- **Import číselníkov** v tom istom sprievodcovi (*Doplniť kategórie, miestnosti alebo osoby*): z Excelu, CSV alebo zo
  súboru EviTA (JSON, XML). Pridajú sa len záznamy, ktoré ešte neexistujú; meno a priezvisko môžu byť aj v jednom
  stĺpci. V Exceli sa použije hárok s názvom číselníka (Kategórie, Miestnosti, Osoby).
- **Prenos celej databázy** na iný počítač: úplný export JSON alebo XML naimportovaný do prázdnej evidencie
  zachová prírastkové čísla, číselníky aj históriu zmien.
- **Vzorové súbory** sú priamo v programe (*Import a export → Vzorové súbory pre import*, aj v sprievodcovi).
  Kliknutím sa uloží do priečinka Stiahnuté: `EviTA_vzor_Excel.xlsx` (s návodom a výberovými zoznamami),
  `EviTA_vzor_CSV.csv`, `EviTA_vzor_JSON.json`, `EviTA_vzor_XML.xml` (spolu s popisom formátu `evita.xsd`)
  a `EviTA_vzor_ciselniky.xlsx` (kategórie, miestnosti a osoby, každý číselník na vlastnom hárku).

## Aktualizácie

Od verzie 0.2.7 program pri spustení sám zistí, či je na GitHube nová verzia, ukáže novinky a ponúkne aktualizáciu.
Po kliknutí na *Aktualizovať teraz* sa nová verzia stiahne, databáza sa zálohuje, nainštaluje sa a program sa znova spustí.
Kontrolu možno vypnúť alebo spustiť ručne v časti *Servis → Aktualizácie*.

Každá verzia má na GitHube vlastné vydanie (napr. `v0.2.7`) s inštalátorom `EviTA_Setup_0.2.7.exe`
a zdrojovým kódom `evita-0.2.7.zip` a `evita-0.2.7.tar.gz`.

## Pracovný kalendár

Menu *Kalendár* ukazuje mesiac, týždeň alebo zoznam udalostí. Víkendy a dni pracovného pokoja sú farebne odlíšené,
pri každom dni sú meniny a sviatky. Dvojklikom na deň pridáte udalosť alebo úlohu (typ s farbou, čas alebo celý deň
či obdobie cez viac dní, opakovanie, pripomienka, naviazanie na položku, miestnosť alebo osobu, stav). Koniec záruky,
ďalšia revízia, pravidelná záloha a ročná inventúra sa do kalendára dostanú automaticky (dajú sa vypnúť v nastaveniach
kalendára). Pri spustení programu sa zobrazí prehľad dňa s úlohami po termíne a pripomienkami. Kalendár sa dá
vytlačiť (mesiac na strane alebo zoznam) a uložiť ako `.ics` pre Outlook alebo Google Kalendár.

## Tlač a zostavy

V menu *Tlač a zostavy* vyberiete zostavu, nastavíte obdobie, zoskupenie, stĺpce a ich poradie a orientáciu A4.
Každá zostava sa dá pozrieť v náhľade, vytlačiť alebo jedným kliknutím uložiť do PDF. Hlavička obsahuje názov
organizácie a logo (nastavíte na tej istej obrazovke), päta dátum tlače a číslo strany. Zobrazené položky
z Evidencie vytlačíte tlačidlom *Tlačiť…* alebo klávesmi Ctrl+P.

Nastavenú zostavu uložíte tlačidlom **Uložiť zostavu…** pod vlastným názvom – so stĺpcami a ich poradím, filtrami
(miestnosť, osoba, kategória), orientáciou a obdobím. Obdobie môže byť pevné (od – do) alebo napr. *minulý mesiac*
či *tento rok* – vtedy sa pri každom použití prepočíta. Uložené zostavy sú vľavo pod zoznamom zostáv; prenášajú sa
aj s exportom konfigurácie programu.

**Uložiť ako PDF** otvorí okno volieb s náhľadom (rovnaké voľby nájdete aj pod tlačidlom *Nastavenia PDF…* v častiach
*Tlač a zostavy* a *Import a export* – platia pre každé PDF z EviTA): 2, 4 alebo 8 strán na list, brožúra, rámik,
zväčšenie, odtiene sivej (čierny text zostane čierny, farby a obrázky zošednú, biele pozadie zostane biele),
zosvetlenie, bez pozadia (farebné podklady sa netlačia – platí aj pri tlači), okraj na dierovač, šifrovanie AES, heslo na otvorenie (neukladá sa – platí do
zatvorenia programu) a zákazy tlače, kopírovania, úprav a poznámok. Písma sa do PDF vkladajú vždy a text zostáva
skutočným textom – dá sa označiť, kopírovať a vyhľadávať (OCR netreba), ak kopírovanie nie je zakázané.

**Formát súboru:** bežné PDF, **PDF/A-2b** (archívne, ISO 19005-2), **PDF/A-3b** (archívne s vloženou tabuľkou zostavy
v Exceli), **PDF/X-4** (pre tlačiareň, ISO 15930-7, farebný profil pre ofsetovú tlač), **obrázky PNG** (každá strana
zvlášť, 300 dpi) alebo **TIFF** (všetky strany v jednom súbore, 300 dpi). Normy PDF/A a PDF/X nepovoľujú šifrovanie
ani heslo, preto sa vtedy zabezpečenie nepoužije. Farebné profily sa berú zo systému Windows. Nadpisy skupín
(napr. miestnosti) sú v PDF ako **záložky** – v prehliadači PDF sa dá kliknutím skočiť na miestnosť.

**Poslať e-mailom…** (v okne Uložiť ako PDF) pošle zostavu ako prílohu v zvolenom formáte. Spôsob nastavíte
v *Nastavenia → E-mail (posielanie PDF)…*:
- **poštový program počítača** (predvolené, nič sa nenastavuje) – otvorí sa nový e-mail s prílohou v klasickom
  Outlooku, Thunderbirde a pod.; nový Outlook a pošta v prehliadači prílohu od iných programov neprevezmú, vtedy
  EviTA otvorí priečinok so súborom na pretiahnutie do e-mailu,
- **priamo z EviTA cez SMTP** – server, port, zabezpečenie SSL/TLS alebo STARTTLS, odosielateľ a heslo (uložené
  zašifrované pre účet Windows, do exportu konfigurácie sa neprenáša). Príloha ide priamo z pamäte, na disk sa
  neukladá. Tlačidlo *Odoslať skúšobný e-mail* nastavenie overí. Gmail vyžaduje heslo aplikácie, Microsoft 365
  prihlásenie heslom (SMTP AUTH) povoleným správcom.

## Štatistika

Obrazovka *Štatistika* (nad *Tlač a zostavy*): súhrn (počet položiek, hodnota = súčet nákupných cien, priemerný vek,
počet položiek na obnovu, záruky) a bloky **Kategórie** (kusy a hodnota), **Obnova – vek techniky** (podľa dátumu
nákupu, hranica obnovy nastaviteľná, predvolene 5 rokov), **Obnova podľa kategórií**, **Plán obnovy na 5 rokov**
(v ktorom roku položky dosiahnu hranicu – odhad rozpočtu), **Miestnosti** (kusy, hodnota, staré, v oprave, prevládajúca
kategória), **Stav**, **Záruky a revízie**, **Nákupy po rokoch**, **Pridelené osobám** a **Úplnosť údajov**. Kliknutím
na riadok sa v podokne zobrazia položky s cenami a sumami; dajú sa otvoriť v Evidencii alebo vytlačiť. Celá štatistika
sa vytlačí alebo uloží ako PDF (aj PDF/A-3b s dátami v Exceli).

## Skenovanie, štítky a inventúra

- **Čítačka čiarových a QR kódov** (USB alebo Bluetooth, správa sa ako klávesnica) funguje v celom programe: naskenovaný
  kód otvorí kartu položky, kód viacerých položiek ich ukáže v Evidencii, neznámy kód ponúkne pridanie novej položky.
  Rozpozná štítok EviTA (`EVITA-123`), inventárne, sériové a prírastkové číslo a EAN výrobku. Čítačku nastavenú na americkú
  klávesnicu (píše `+ľščť` namiesto číslic) EviTA opraví sama. V textovom poli (napr. sériové číslo v karte položky) kód
  zostane v poli a Enter presunie kurzor ďalej.
- **Obrazovka Skenovanie a inventúra:** pole na skenovanie, posledné skeny s akciami, inventúry, tlač štítkov a nastavenia
  (zvuk, otvorenie karty, port pre mobil, účet Icecat).
- **Štítky s QR alebo čiarovým kódom (Code 128)** na samolepiace hárky A4 (24, 21, 40, 65, 14 štítkov a vlastný formát)
  aj na tlačiarne štítkov (62 × 29, 50 × 30, 50 × 25, 38 × 25 mm). Obsah kódu: štítok EviTA, inventárne alebo sériové
  číslo; text: organizácia, názov, čísla, miestnosť. Začiatok na čiastočne použitom hárku, kópie, posun tlače, náhľad,
  tlač a PDF. Štítky sa tlačia z Evidencie (označené alebo zobrazené položky), z karty položky aj podľa miestnosti.
  Poradie štítkov podľa čísla, **miestností** alebo kategórií, voliteľne každá miestnosť na novom hárku (hárok sa
  odnesie rovno do miestnosti). Nastavenie štítkov sa dá **uložiť pod vlastným názvom** a neskôr vybrať jedným kliknutím.
  **Vytlačiť…** otvorí okno s **náhľadom každej strany**, výberom tlačiarne (pre štítky sa pamätá zvlášť) a rozsahom strán.
- **Inventúra:** po miestnostiach – vyberiete miestnosť a skenujete. EviTA ukazuje, čo chýba, čo je v inej miestnosti
  (jedným klikom sa presunie v evidencii), neznáme kódy a vyradené položky; položku bez štítku označíte ručne.
  Inventúrny protokol (súhrn po miestnostiach, chýbajúce, nájdené inde, podpisy) vytlačíte alebo uložíte do PDF.
- **Kamera počítača** (webkamera, notebook, tablet) číta kódy priamo z obrazu.
- **Mobil ako skener:** EviTA ukáže QR kód s adresou stránky v miestnej sieti; mobil kód na zariadení odfotí alebo ho
  napíšete a počítač ho spracuje ako kód z čítačky. Bez aplikácie v mobile, stačí prehliadač a rovnaká Wi-Fi.
  Stránku otvorte v prehliadači (Chrome) – vnútorný prehliadač aplikácie Skener (MIUI) posiela z fotoaparátu len malý
  náhľad, z ktorého sa kód prečítať nedá; mobil vtedy ponúkne „Otvoriť v prehliadači Chrome“. Fotka sa číta v plnom
  rozlíšení, pri neúspechu aj zaostrená; posledná nečitateľná fotka sa odloží do `Data\Diagnostika` na rozbor.
  Inštalátor povolí program vo Windows Firewall (porty 8765 – 8769, súkromná a doménová sieť).
- **EAN výrobku** v karte položky (aj v exporte a importe): tlačidlo *Doplniť z internetu* vyhľadá výrobcu, model, názov
  a kategóriu vo vlastnej evidencii, v katalógu IT produktov **Open Icecat** a v databáze **UPCitemdb** (bez registrácie,
  UPCitemdb najviac 100 vyhľadaní denne). Naskenovaný neznámy EAN založí novú položku s doplnenými údajmi.

## Kde sú dáta

| Čo | Kde |
| --- | --- |
| Program | `C:\Program Files\EviTA` |
| Databáza | `C:\ProgramData\EviTA\Data\evita.db` |
| Zálohy | `Dokumenty\EviTA\Zálohy` (dá sa zmeniť v Servis → Zálohy) |
| Vzorové súbory pre import | `Dokumenty\EviTA\Vzory` |
| Exporty (predvolene) | `Dokumenty\EviTA\Exporty` |
| Meniny a sviatky (Kalendárium) | `C:\ProgramData\Kalendarium\kalendarium.db` |

Odinštalovanie programu databázu ani zálohy nemaže.

## Stav vývoja

- [x] **Etapa 1 – Jadro:** evidencia, filtre, karta položky, história zmien, hromadné pridanie a zmena, číselníky,
      ukážková databáza, jednorazová záloha, inštalátor
- [x] **Etapa 2 – Bezpečnosť dát:** pravidelné zálohy (aj cez Plánovač úloh Windows), zálohy s heslom,
      obnova zo zálohy, kontrola a oprava databázy, automatická aktualizácia štruktúry databázy
- [x] **Etapa 3 – Dashboard:** počty, graf kategórií, meniny, sviatky a dni pracovného pokoja, upozornenia
- [x] **Etapa 4 – Import a export:** všetky formáty, sprievodca importom, vzorové súbory, prenos celej databázy
- [x] **Etapa 5 – Tlač a zostavy:** súhrnný zoznam, vyfiltrované položky, po miestnostiach (inventúrny zoznam s podpismi),
      po kategóriách, podľa pridelenia (zverená technika s podpisom), prírastkový zoznam, podľa dátumu pridania,
      vyradené položky, história zmien; náhľad, tlač a PDF; hlavička s názvom organizácie a logom
- [x] **Etapa 6 – Pracovný kalendár:** mesiac, týždeň a zoznam so sviatkami, menami a číslom týždňa; udalosti a úlohy
      s typmi a farbami, opakovaním, pripomienkou a naviazaním na položku, miestnosť alebo osobu; automatické termíny
      (záruky, revízie, zálohy, inventúra); prehľad dňa pri spustení; upozornenie pred udalosťou; panel na dashboarde;
      záložka Udalosti v karte položky; tlač kalendára a export `.ics` pre Outlook a Google Kalendár
- [x] **Verzia 0.7 – Dashboard a jazyky:** kurzový lístok, počasie, úlohy (To Do), poznámkový blok prepojený
      s kalendárom a položkami, export a import konfigurácie, jazyky EN, SK, CS, PL, HU, RU, ZH s tlačidlom Language reset
- [x] **Etapa 7 – Kalendárium:** citát dňa (zabudované a vlastné citáty, skrytie, vymazanie), výročia dňa
      z Wikipédie v jazyku programu (aj celý rok naraz, offline), vlastné výročia, export a import JSON
- [x] **Etapa 8 – Skenovanie a údaje z internetu:** čítačka čiarových a QR kódov v celom programe, štítky s QR
      a čiarovým kódom na hárky A4 aj tlačiarne štítkov, inventúra skenovaním po miestnostiach s protokolom, kamera
      počítača a mobil ako skener, EAN výrobku s doplnením údajov z Open Icecat a UPCitemdb
- [ ] Etapa 9 – Dokončenie
  - [ ] vyskúšať čítanie kódov webovou kamerou notebooku (Kamera ako skener) na skutočných krabiciach
  - [ ] vyskúšať ručnú USB čítačku čiarových kódov, najmä príjem nového tovaru, ktorý ešte nie je v evidencii
        (sken EAN z krabice → údaje z internetu → nová položka)
  - [ ] interaktívne PDF: vyplniteľný inventúrny protokol (nájdené / chýba, poznámka) a načítanie vyplnených
        odpovedí späť do evidencie
- [ ] Etapa 10 (len plán, termín neurčený) – Viac používateľov, prístupové práva a sieťová databáza (špecifikácia:
      [docs/Etapa10_viac_pouzivatelov.md](docs/Etapa10_viac_pouzivatelov.md)) – lokálny režim zostáva; navyše prihlasovanie
      10 – 20 používateľov s rolami a právami a voliteľne databáza na serveri (MariaDB/MySQL, SQL Server, PostgreSQL)

## Technické informácie

- C# / .NET 10 (LTS), WPF s Fluent vzhľadom Windows 11
- SQLite cez Entity Framework Core
- Čiarové a QR kódy: [zxing-cpp](https://github.com/zxing-cpp/zxing-cpp) v3.1.1 (Apache-2.0); kamera cez Windows.Media.Capture
- Prepis diktovania bez internetu: [whisper.cpp](https://github.com/ggml-org/whisper.cpp) v1.9.5 (MIT, commit
  d1be6fde…) – zostavuje sa v GitHub Actions zo zdrojového kódu spolu s tenkou vrstvou `nastroje/hlas/evita_hlas.c`
  do jednej knižnice EvitaHlas.dll (bez GPU, bez OpenMP, nič sa počas zostavenia nesťahuje); kontroluje sa, že volá len
  jadro Windows a z advapi32.dll len čítanie názvu procesora z registra (RegOpenKeyExA, RegQueryValueExA, RegCloseKey). Modely Whisper (OpenAI, MIT) v prevedení ggml z huggingface.co/ggerganov/whisper.cpp, overené SHA-256.
  Mikrofón cez rozhranie Windows (winmm), hlasové písanie Windows cez Win+H.
- Ikony kategórií: tvary zo sady [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (Microsoft, MIT) –
  vložené ako dáta kresby (cesty), žiadna knižnica ani spustiteľný kód; licencia je priložená k programu

### Bezpečnosť čítačky kódov

- Knižnica na kódy sa **nepreberá ako hotový súbor**. Pri každom zostavení ju GitHub Actions skompiluje zo zdrojového
  kódu pevne určenej verzie (v3.1.1, commit `287c85df6f961c8efbfb5ffd736cd9457b8b890e` – iný commit zostavenie
  odmietne), bez sťahovania ďalších závislostí. Do záznamu zostavenia sa zapíše kontrolný súčet SHA-256 knižnice.
- Zostavenie skontroluje, z ktorých knižníc Windows `ZXing.dll` volá funkcie. Povolené je len jadro systému a knižnice
  C/C++ – ak by knižnica používala sieť, registre, prehliadač, kryptografiu či spúšťanie programov, zostavenie zlyhá.
- Zdrojový kód zxing-cpp bol preverený: neobsahuje sieťovú komunikáciu, spúšťanie programov ani prístup k registrom;
  zápis súborov je len v ladiacom režime, ktorý sa do programu nekompiluje. .NET obal (`ZXingCpp.cs`) je prevzatý
  bez zmien z tej istej verzie.
- Naskenovaný kód je pre EviTA len text na vyhľadanie v evidencii (parametrický dopyt do databázy): nikdy sa nespúšťa,
  odkazy z QR kódov sa neotvárajú a dlhý text sa skráti na 512 znakov.
- Čítačka sa pripája ako klávesnica (USB/Bluetooth HID) – **nepotrebuje žiadny ovládač ani program od výrobcu**.
  Kupujte čítačky renomovaných značiek a neinštalujte k nim softvér z neznámych zdrojov.
- Inštalátor: Inno Setup, zostavuje sa automaticky cez GitHub Actions pri každej zmene vetvy `main`

Štruktúra:

```
src/Evita.App/
  Models/       dátové triedy (položka, kategória, miestnosť, osoba, história)
  Data/         databáza (EF Core, SQLite)
  Services/     evidencia, číselníky, servis, zálohy, import a export, kalendár, úlohy, poznámky,
                kurzy, počasie, konfigurácia, preklad, ukážkové dáta, skenovanie, štítky, inventúra,
                kamera, mobil ako skener, EAN
  Preklady/     slovníky prekladov (en, cs, pl, hu, ru, zh)
  ViewModels/   logika obrazoviek
  Views/        okná a obrazovky (XAML)
installer/      skript inštalátora
nastroje/       pomocné skripty (kontrola prekladov)
```
