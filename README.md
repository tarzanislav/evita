# EviTA – evidencia IT techniky

Desktopový program pre Windows 10/11 na evidenciu informačných technológií, zariadení a príslušenstva.

## Stiahnutie a inštalácia

1. Otvorte [najnovšiu verziu](https://github.com/tarzanislav/evita/releases/latest).
2. Stiahnite súbor **EviTA_Setup_X.Y.Z.exe** (X.Y.Z je číslo verzie) a spustite ho.
3. Windows môže zobraziť modré upozornenie *Neznámy vydavateľ* (inštalátor zatiaľ nemá platený podpisový certifikát).
   Kliknite na **Ďalšie informácie → Spustiť aj tak**.

Pri prvom spustení si vyberiete, či chcete začať s **ukážkovými dátami** (200 položiek, 30 osôb, 25 miestností)
alebo s **prázdnou databázou**. Ukážkové dáta neskôr vymažete v časti *Servis → Databáza → Vymazať dáta a začať nanovo*.

## Aktualizácie

Od verzie 0.2.7 program pri spustení sám zistí, či je na GitHube nová verzia, ukáže novinky a ponúkne aktualizáciu.
Po kliknutí na *Aktualizovať teraz* sa nová verzia stiahne, databáza sa zálohuje, nainštaluje sa a program sa znova spustí.
Kontrolu možno vypnúť alebo spustiť ručne v časti *Servis → Aktualizácie*.

Každá verzia má na GitHube vlastné vydanie (napr. `v0.2.7`) s inštalátorom `EviTA_Setup_0.2.7.exe`
a zdrojovým kódom `evita-0.2.7.zip` a `evita-0.2.7.tar.gz`.

## Kde sú dáta

| Čo | Kde |
| --- | --- |
| Program | `C:\Program Files\EviTA` |
| Databáza | `C:\ProgramData\EviTA\Data\evita.db` |
| Zálohy | `Dokumenty\EviTA\Zálohy` (dá sa zmeniť v Servis → Zálohy) |

Odinštalovanie programu databázu ani zálohy nemaže.

## Stav vývoja

- [x] **Etapa 1 – Jadro:** evidencia, filtre, karta položky, história zmien, hromadné pridanie a zmena, číselníky,
      ukážková databáza, jednorazová záloha, inštalátor
- [x] **Etapa 2 – Bezpečnosť dát:** pravidelné zálohy (aj cez Plánovač úloh Windows), zálohy s heslom,
      obnova zo zálohy, kontrola a oprava databázy, automatická aktualizácia štruktúry databázy
- [ ] Etapa 3 – Dashboard: grafy, meniny, sviatky
- [ ] Etapa 4 – Import a export
- [ ] Etapa 5 – Tlač a zostavy
- [ ] Etapa 6 – Kalendár
- [ ] Etapa 7 – Kalendárium (citáty, výročia)
- [ ] Etapa 8 – Skenovanie a údaje z internetu
- [ ] Etapa 9 – Dokončenie

## Technické informácie

- C# / .NET 10 (LTS), WPF s Fluent vzhľadom Windows 11
- SQLite cez Entity Framework Core
- Inštalátor: Inno Setup, zostavuje sa automaticky cez GitHub Actions pri každej zmene vetvy `main`

Štruktúra:

```
src/Evita.App/
  Models/       dátové triedy (položka, kategória, miestnosť, osoba, história)
  Data/         databáza (EF Core, SQLite)
  Services/     evidencia, číselníky, servis, ukážkové dáta
  ViewModels/   logika obrazoviek
  Views/        okná a obrazovky (XAML)
installer/      skript inštalátora
```
