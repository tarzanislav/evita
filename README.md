# EviTA – evidencia IT techniky

Desktopový program pre Windows 10/11 na evidenciu informačných technológií, zariadení a príslušenstva.

## Stiahnutie a inštalácia

1. V repozitári otvorte vpravo časť **Releases** a vyberte **EviTA … (najnovšia)**.
2. Stiahnite súbor **EviTA_Setup.exe** a spustite ho.
3. Windows môže zobraziť modré upozornenie *Neznámy vydavateľ* (inštalátor zatiaľ nemá platený podpisový certifikát).
   Kliknite na **Ďalšie informácie → Spustiť aj tak**.

Pri prvom spustení si vyberiete, či chcete začať s **ukážkovými dátami** (200 položiek, 30 osôb, 25 miestností)
alebo s **prázdnou databázou**. Ukážkové dáta neskôr vymažete v časti *Servis → Vymazať dáta a začať nanovo*.

## Kde sú dáta

| Čo | Kde |
| --- | --- |
| Program | `C:\Program Files\EviTA` |
| Databáza | `C:\ProgramData\EviTA\Data\evita.db` |
| Zálohy | `Dokumenty\EviTA\Zálohy` |

Odinštalovanie programu databázu ani zálohy nemaže.

## Stav vývoja

- [x] **Etapa 1 – Jadro:** evidencia, filtre, karta položky, história zmien, hromadné pridanie a zmena, číselníky,
      ukážková databáza, jednorazová záloha, inštalátor
- [ ] Etapa 2 – Bezpečnosť dát: pravidelné zálohy, obnova, nástroje na ladenie
- [ ] Etapa 3 – Dashboard: grafy, meniny, sviatky
- [ ] Etapa 4 – Import a export
- [ ] Etapa 5 – Tlač a zostavy
- [ ] Etapa 6 – Kalendár
- [ ] Etapa 7 – Kalendárium (citáty, výročia)
- [ ] Etapa 8 – Dokončenie

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
