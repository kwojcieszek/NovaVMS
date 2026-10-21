# Weryfikacja stabilności Release — 2026-10-21

## Potwierdzone problemy i poprawki

1. **Natywna awaria bez komunikatu.** Podczas odtwarzania rzeczywistego nagrania
   i zatrzymania po pauzie odtworzono awarię procesu diagnostycznego `Probe.exe`.
   Dziennik Application, 22:05:51: `libd3d11va_plugin.dll` 3.0.24.0,
   `0xc0000005`, przesunięcie `0x25b0e` (zdarzenia 1000/1026).
   Ustawienie `--avcodec-hw=none` na instancji LibVLC nie wystarczało:
   LibVLCSharp nadpisuje je ustawieniem odtwarzacza przy Play.
   Wszystkie odtwarzacze NovaVMS ustawiają teraz jawnie
   `EnableHardwareDecoding=false` w Windows. Próba zatrzymania z tym ustawieniem
   zakończyła się poprawnie. Nie dowodzi to przyczyny każdej historycznej awarii.
   Koszt: większe obciążenie CPU, szczególnie przy wielu kamerach wysokiej rozdzielczości.

2. **Wyścigi podczas zamykania.** Synchronizacja, komendy PTZ oraz kolejka
   czyszczenia połączeń są teraz opróżniane przed zwolnieniem SDK.
   Strumień SDK nie wraca z Dispose przed zakończeniem otwierania i natywnego Stop.
   Równoległe Dispose czekają na tę samą operację; dekoder i rejestr callbacków
   są zwalniane na końcu. SDK nie trzyma blokady inicjalizacji, oczekując na strumienie.

3. **Nagranie przesuwające się za szybko.** Rejestrator dostarcza dane szybciej
   niż czas rzeczywisty. Wejście callbacków VLC 3 nie synchronizuje takich danych
   z zegarem odtwarzania, jeśli nie otrzyma callbacku Seek.
   RecordingMediaInput umożliwia synchronizację i pauzę, odrzucając rzeczywiste
   przesunięcia bajtowe na strumieniu SDK. Przeskok po czasie nadal otwiera nowy strumień.
   Poprawka obejmuje odtwarzanie i eksport, bez zmiany wejścia kamer na żywo.

4. **Nieruchoma pierwsza klatka.** Potwierdzona przez użytkownika w R3 oraz przez
   porównanie zdekodowanych obrazów w niezależnej próbie. VLC odrzucał kolejne
   klatki jako spóźnione. Osobna instancja dla nagrań wyłącza hurry-up,
   drop-late-frames i skip-frames. Same opcje Media nie wystarczały.
   Instancja podglądu na żywo zachowuje swoje ustawienia opóźnienia.

5. **Czarny ekran przy przewijaniu.** W R4 podgląd 2× był zastępowany następnym
   strumieniem tuż po buforowaniu. R5 liczy minimalne 900 ms podglądu od pierwszej
   wyświetlonej klatki, zamiast od wywołania Play.
   Przewijanie pozostaje przeskakiwaniem między fragmentami; podczas otwierania
   kolejnego fragmentu może wystąpić krótka przerwa w obrazie.

6. **Brak informacji o błędzie zarządzanym.** Błąd startu lub głównej pętli
   przechwycony w punkcie wejścia wyświetla w Windows natywny komunikat
   z lokalizacją logów. Nie przechwytuje naruszeń dostępu w kodzie natywnym.
   Celowo zablokowany plik aktualizacji potwierdził zapis błędu i obecność okna
   „NovaVMS — błąd aplikacji”. Pełna kontrola treści okna była wtedy niedostępna
   z powodu odmowy dostępu narzędzia do pulpitu.

Historyczne wyjątki `Control does not belong to a visual tree` dotyczyły nakładek.
Zabezpieczenie było już w kodzie; regresję rozszerzono do 100 odłączeń i podłączeń.

## Testy automatyczne

**289 zaliczonych, 0 błędów, 0 pominięć:** 152 aplikacji, 74 protokołów,
63 infrastruktury. Po ostatniej poprawce przewijania ponowiono 152 testy aplikacji
— wszystkie zaliczone. Budowa i publikacja Windows x64 zakończone poprawnie.

```powershell
dotnet test src/NovaVMS.sln -c Release --artifacts-path src/obj/crash-validation --nologo --verbosity minimal --blame-crash --blame-hang-timeout 40s
```

Nowe i rozszerzone testy obejmują: zamknięcie podczas zablokowanej operacji
urządzenia; 25 cykli anulowania dla 1/8/64 urządzeń; zamknięcie PTZ podczas komendy;
opróżnianie kolejki; wyścigi otwierania i zamykania strumienia natywnego;
zwalnianie kafelka podczas otwierania LibVLC; 30 cykli dekodowania/Stop;
synchronizację i pauzę nieprzewijalnego nagrania syntetycznego.
Testy syntetyczne nie zastępują weryfikacji obrazu rzeczywistej kamery.

## Próby rzeczywistego nagrania i GUI

- Zmiany układów 9 → 64 → 1, powiększenie kafelka i przywrócenie widoku.
- Ustawienia grup: wybór grupy, podgląd dwóch kamer, przełączanie stron ustawień,
  zamknięcie ustawień z aktywnym podglądem.
- Wybór kamery, wyszukanie nagrań, uruchomienie z osi czasu, pauza i wznowienie,
  przeskok do późniejszej godziny, przewijanie.
- W R4 potwierdzono kolejne czasy na obrazie kamery przy 1× i zatrzymanie obrazu
  podczas pauzy. W R5 potwierdzono widoczne kolejne fragmenty przy 2× i −2×,
  cofanie pozycji oraz poprawny Stop podczas przewijania.
- Eksport z GUI: zakres 12:07:32–12:07:41, wynik 8 552 365 bajtów, czas MP4
  8401 ms, jedna ścieżka wideo. Niezależne odtworzenie: po 2 s 65 zdekodowanych
  klatek/31 wyświetlonych, po 4 s 113/70. Brak pozostawionego pliku partial.
  Plik jest w `src/obj/release-export-test` i zawiera rzeczywisty obraz kamery.
- Niezależna próba przed poprawką synchronizacji: około 1272 zdekodowane klatki
  w 4 s. Po poprawkach: 114 klatek/85 wyświetlonych w 6 s, pauza przez 1 s
  pozostawia 114 klatek. Obrazy zapisane po 2 i 4 s mają różne sumy SHA256
  i zmieniającą się godzinę kamery.
- Normalne zamknięcia R2, R3, R4 i R5 z rzeczywistymi zasobami zakończyły proces.
- Od uruchomienia R4 (22:18) do końca prób R5 brak znalezionych zdarzeń awarii
  lub zawieszenia NovaVMS/Probe/testhost w dzienniku Application (1000/1001/1002/1026).

## Paczka i ograniczenia

Najnowsza samodzielna paczka z helperem aktualizacji:
`src/obj/crash-validation/published-win-x64-r5/NovaVMS.exe`.
Starsze R2–R4 służą wyłącznie porównaniu diagnostycznemu.

Testy używają osobnego katalogu `%TEMP%/NovaVMS-release-investigation-20261021`,
kopii zaszyfrowanej konfiguracji, bez kopiowania konfiguracji bazy synchronizacji.
Użytkownik zalogował się ręcznie. Oryginalne pliki konfiguracji i logi nie były edytowane.

W ostatnich próbach pozostają odmowy połączenia SDK z jednym rejestratorem
oraz SocketException 10061 przy sprawdzaniu dostępności. To nadal problem dostępu
sieciowego lub urządzenia; nie został ukryty ani uznany za naprawiony.
Nie testowano fizycznych ruchów PTZ ani zmian ustawień bezpieczeństwa urządzeń.
Wcześniejsze błędy przechwytywania pulpitu (GetCursorPos 0x80070005,
FrameArrived timeout) przerwały część prób; po przywróceniu pulpitu testy kontynuowano.
Wynik tej sesji nie jest gwarancją wyeliminowania wszystkich możliwych awarii.
