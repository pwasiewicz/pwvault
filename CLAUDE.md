# pw-vault — instrukcje dla Claude

Osobisty password manager. Git-backed, master password w głowie, age passphrase mode dla pól wrażliwych. Stack: .NET + age binary + Spectre.Console.

## Zawsze

- **ARCH.md to źródło prawdy architektonicznej.** Przed istotną zmianą zerknij do niego. Jeśli decyzja z rozmowy zmienia architekturę (format storage, session management, threat model, scope, stack) — **zaktualizuj ARCH.md w tym samym cyklu zmian**. Nie pozwól mu zgnić.
- **Scope discipline.** W ARCH.md jest sekcja "Explicitly nie robimy" (OTP, autofill, mobile, browser extension, sharing, własny format, własne krypto). Nie wchodzimy tam bez jawnej decyzji użytkownika.
- **Zero custom krypto.** Szyfrowanie idzie przez `age` (binary przez subprocess, chyba że pojawi się dojrzała .NET libka age). Nigdy nie piszemy własnego AEAD, KDF, formatu pliku sekretów.
- **Emergency recovery musi działać bez naszej binarki.** Każda zmiana formatu musi pozwalać na `age -d` ręcznie + prosty skrypt recovery committed do repo. Jeśli zmiana to łamie — to red flag, przemyśl ponownie.
- **Roundtrip test po każdym Save.** Każda ścieżka zapisu wpisu: natychmiast załaduj z powrotem i porównaj. Bug w serializacji = utracone hasła.
- **Filesystem perms 0600 dla session file** (Linux/macOS), DPAPI scope CurrentUser (Windows). Nigdy nie zapisujemy master password ani session blob bez tej ochrony.

## Gdy użytkownik prosi o zmianę

1. Sprawdź czy mieści się w aktualnym scope (ARCH.md "Scope"). Jeśli tak — działaj.
2. Jeśli zmienia architekturę (threat model, format, stack, session mechanism, emergency flow) — najpierw zaproponuj update ARCH.md, potem implementacja.
3. Jeśli wchodzi w "Explicitly nie robimy" — zapytaj wprost czy rozszerzamy scope, nie zakładaj że tak.

## Struktura projektu

- `src/PwVault.Core/` — domena, szyfrowanie (wrapper nad age), storage (JSON tree), session store (per-OS)
- `src/PwVault.Cli/` — entry point CLI, komendy, TUI (Spectre.Console)
- `tests/PwVault.Core.Tests/` — xunit
- Web będzie osobnym projektem w iteracji 3, nie dodawaj wcześniej

## Konwencje

- Target: `net10.0`
- Nullable reference types: enabled
- Implicit usings: enabled
- Format: plik per klasa, file-scoped namespaces
- Testy: per publiczne API `Core`, priorytet na testy roundtrip szyfrowania i storage

## Kluczowe pliki referencyjne

- `ARCH.md` — decyzje architektoniczne, threat model, scope. Pierwsze miejsce do sprawdzenia.
- `vault/FORMAT.md` (utworzone przez `pwvault init`) — schemat wpisu, specyfikacja pól `*_age`. Musi być aktualny żeby emergency recovery działało.
- `vault/recover.sh` (utworzone przez `pwvault init`) — skrypt bash do ręcznej deszyfracji. Nie dotykać bez powodu, działa bez naszej binarki.

## Co NIE jest zadaniem tego projektu

- Przechowywać cokolwiek poza hasłami i notatkami użytkownika
- Być wieloużytkownikowe
- Synchronizować przez cokolwiek innego niż git
- Działać offline bez `age` binary (age jest twardą zależnością)
- Mieć klientów mobilnych / przeglądarkowych extensionów
