# pw-vault — architektura

Osobisty menedżer haseł. Self-hosted przez git, zero cudzej chmury, master password w głowie.

## Cele i filozofia

- **Git to top priority.** Baza musi być w repo i rozproszona — jeśli padnie VPS/serwer, hasła są bezpieczne w klonach.
- **Master password w głowie, zero plików-kluczy.** Nowy komp / u ziomka: `git clone` + hasło = działa.
- **Emergency recovery bez własnego toola.** W krytycznej sytuacji kopiuj zaszyfrowany string z github.com w przeglądarce → `age -d` → masz hasło. Nawet bez klonowania repo, nawet bez swojej binarki.
- **Nie piszemy własnego krypto.** Wszystko stoi na `age` (spec Filippo Valsordy, universal CLI decryptor).
- **Scope discipline.** CLI first, web second. Nie robimy OTP, TOTP, autofill, mobile, browser extension, sharing.

## Format storage

Drzewo plików JSON, jeden plik na wpis. Git śledzi zmiany per-wpis (czytelny `git log`, minimalne konflikty merge).

```
vault/
├── banking/
│   ├── mbank.json
│   └── revolut.json
├── dev/
│   ├── github.json
│   └── aws-prod.json
├── personal/
│   └── gmail.json
├── FORMAT.md          # opis schematu, do recovery bez naszego toola
├── recover.sh         # jednolinijkowy skrypt: age -d pola password_age
└── README.md          # jak odpalić recovery
```

### Schemat wpisu

```json
{
  "title": "mBank",
  "url": "https://mbank.pl",
  "username": "pat@example.com",
  "tags": ["banking", "pl"],
  "password_age": "-----BEGIN AGE ENCRYPTED FILE-----\n...\n-----END AGE ENCRYPTED FILE-----",
  "notes_age": "-----BEGIN AGE ENCRYPTED FILE-----\n...\n-----END AGE ENCRYPTED FILE-----",
  "updated": "2026-04-22"
}
```

**Plaintext fields (świadomy trade-off):** `title`, `url`, `username`, `tags`, `updated`. Git log dla danego pliku ujawnia historię zmian. Akceptowane — nikt nie robi targeted reverse engineering naszego osobistego vaulta.

**Zaszyfrowane fields:**
- `password_age` — właściwe hasło
- `notes_age` — notatki (opcjonalne, ale gdy są to **zawsze szyfrowane** — tu lądują security answers, recovery codes, PINy)

### Szyfrowanie — age passphrase mode

- `age -a -p` (ASCII armor + passphrase)
- KDF: scrypt (standard age)
- Każde pole szyfrowane osobno z tym samym master password
- ~1s scrypt per deszyfracja — akceptowalne przy unlock-one, świadomie odrzucone unlock-all

### Dlaczego nie intermediate key

Rozważono: jeden age keypair w repo (private key sam zaszyfrowany `age -p`), hasła szyfrowane publicznym kluczem. Szybsze (jeden scrypt per sesja). **Odrzucone** — zabija emergency flow "kopiuj string z githuba → age -d" bo wymagałoby też identity file.

## Emergency recovery flow (must work bez naszej binarki)

1. github.com w przeglądarce, login
2. Prywatne repo `vault` → `banking/mbank.json` → "Raw" albo po prostu widoczny JSON
3. Copy wartość `password_age` (od `-----BEGIN` do `-----END`)
4. Na dowolnym komputerze z `age`:
   ```bash
   pbpaste | age -d       # mac
   xclip -o | age -d      # linux
   # prompt o passphrase → wypluwa hasło
   ```

Dependencies: tylko binarka `age` (apt/brew/winget/scoop/github releases — wszędzie dostępna, ~30s instalki).

## Session management

Master password odblokowany raz na sesję, cache'owany z TTL. Implementacja per-OS przez `ISessionStore`.

### Linux / WSL2 / macOS — plik w tmpfs

- Lokalizacja: `$XDG_RUNTIME_DIR/pwvault/session` (Linux), `~/Library/Caches/pwvault/session` (macOS fallback)
- Permissions: `0600` (tylko current user)
- Zawartość: JSON z `expires_at` + `master_password` (plaintext w pliku, filesystem perms = security)
- `$XDG_RUNTIME_DIR` to tmpfs na Linuksie — w RAM, auto-clean przy wylogowaniu/reboocie

### Windows — DPAPI

- `System.Security.Cryptography.ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`
- Plik w `%LOCALAPPDATA%\pwvault\session`
- Klucz deszyfrujący trzyma OS (związany z loginem Windows)
- Kopia pliku na inną maszynę / innego usera = bezużyteczna

### Abstrakcja

```csharp
interface ISessionStore {
    void Save(string masterPassword, DateTime expiresAt);
    string? TryLoad();   // null jeśli brak/wygasł
    void Clear();
}

static ISessionStore Create() =>
    OperatingSystem.IsWindows() ? new WindowsDpapiStore()
    : OperatingSystem.IsMacOS() ? new MacFileStore()
    : new LinuxTmpfsStore();
```

### Świadoma asymetria

Windows dostaje darmowy OS-level key wrapping (DPAPI). Linux poprzestaje na `0600` + tmpfs + TTL. Na Linuksie nie ma natywnego odpowiednika DPAPI w BCL, a libsecret/keyring daemon są zawodne (szczególnie w WSL2). Dla threat modelu osobistego narzędzia — wystarczy.

## TTL i fallback

- Domyślny TTL sesji: 1h (konfigurowalne)
- `pwvault unlock` — prompt o master password, zapis do session store
- `pwvault get <name>` — jeśli sesja jest i niewygasła, użyj; jeśli nie, zapytaj o master password (ad-hoc, bez zapisu)
- `pwvault lock` — wyczyść session store
- Zmiana środowiska/device nie psuje nic — fallback do promptu zawsze działa

## Git workflow

- Repo prywatne (GitHub / Gitea / cokolwiek)
- Każda modyfikacja vaulta → auto-commit + push (konfigurowalne)
- Merge conflicts: rzadkie (per-file, a sam edytujesz na jednym urządzeniu na raz). Przy konflikcie bierzesz nowszą wersję pliku — tracisz starszą edycję, akceptowalny koszt.
- Brak własnego sledzenia historii — `git log <path>` załatwia sprawę.

## Tech stack

- **.NET** (C#, .NET 9+)
- **age** — jako binary wywoływany subprocessem (najprościej, najbardziej odporne na zmiany spec) LUB .NET libka gdy dojrzała. Default: subprocess.
- **Spectre.Console** — TUI (prompty, tabele, drzewa, kolor)
- **FuzzySharp** — fuzzy search po title/tags
- **TextCopy** — cross-platform clipboard z auto-clear przez `Task.Delay`
- **System.Text.Json** — serializacja wpisów
- **LibGit2Sharp** albo `git` binary — zarządzanie repo. Default: `git` binary (mniej zależności).

## Scope

### MVP (tydzień 1)

- [ ] `pwvault init <path>` — inicjalizacja vaulta (katalog + README + FORMAT.md + recover.sh + .gitignore)
- [ ] `pwvault unlock` — prompt o master password, zapis do session store z TTL
- [ ] `pwvault lock` — wyczyść session
- [ ] `pwvault add <path>` — dodaj wpis (prompty o title, url, username, password, notes)
- [ ] `pwvault get <path> [-c]` — pokaż wpis, `-c` kopiuj hasło do schowka (auto-clear 15s)
- [ ] `pwvault ls [path]` — drzewo wpisów
- [ ] `pwvault rm <path>` — usuń wpis
- [ ] `pwvault gen [length]` — generator haseł (CSPRNG)
- [ ] Auto-commit + push po każdej mutacji

### Iteracja 2 (tydzień 2)

- [ ] `pwvault search <query>` — fuzzy po plaintext metadata
- [ ] `pwvault edit <path>` — edycja istniejącego wpisu
- [ ] `pwvault mv <src> <dst>` — przenoszenie
- [ ] `pwvault import bitwarden <json>` — import z Bitwardena
- [ ] Interaktywny TUI picker (`pwvault` bez argumentów → Spectre.Console lista z fuzzy)
- [ ] `pwvault rotate-master` — rotacja master password (re-encrypt wszystkich pól `*_age`)

### Iteracja 3 (web)

- [ ] Lekki web UI (ASP.NET Minimal API + HTML)
- [ ] Czyta z lokalnego klona repo (pull on demand)
- [ ] Deszyfracja po stronie serwera (prompt o master password, trzymanie w pamięci sesji)
- [ ] Self-hosted, tylko dla siebie (basic auth albo za Tailscale)

### Explicitly nie robimy

- OTP / TOTP
- Autofill
- Browser extension
- Mobile client
- Password sharing / multi-user
- Sync engine (git to sync)
- Własny format storage (age standard)
- Własne krypto (libsodium / System.Security.Cryptography)

## Safety nets

1. **Roundtrip test po każdym Save.** Po zapisie wpisu, natychmiastowe odczytanie i porównanie — catch bugów serializacji zanim uderzą.
2. **Backup vaulta przed pierwszym write w nowej wersji toola.** Kopia całego katalogu do `vault.backup.<timestamp>/`.
3. **Recovery test na starcie.** Przed wrzuceniem realnych haseł: klonowanie repo na drugim urządzeniu, odczyt. Zweryfikować że `recover.sh` też działa bez naszej binarki.
4. **Dual-run przez miesiąc.** Trzymać równoległy vault w Bitwarden/KeePassXC. Dopiero po miesiącu bez problemów → porzucić drugi.
5. **FORMAT.md commit'owany do repo.** Dokładny opis schematu JSON + jak deszyfrować ręcznie. Future-you za 5 lat ci podziękuje.
6. **Wersjonowanie schematu od dnia 1.** Pole `schema_version` w każdym wpisie, magic/version na początku plików pomocniczych jeśli będą.

## Threat model — co chronimy, czego nie

### Chronimy przed

- Wyciek repo / GitHub compromise (hasła zaszyfrowane)
- Utrata pojedynczego urządzenia (git pull na nowym)
- Padnięcie serwera / infrastruktury (rozproszone klony)
- Ktoś widzi twój ekran na chwilę (auto-clear clipboard, TTL sesji)
- Inny user na tej samej maszynie (filesystem perms, DPAPI user scope)

### Nie chronimy przed

- **Keylogger / compromised endpoint** — master password wpisywany na zkompromitowanej maszynie = game over. Nic nie obroni.
- **Wyciek metadanych z repo** — widać ile haseł, do jakich serwisów, kiedy zmieniane. Świadomy trade-off.
- **Rainbow table attack na słaby master password** — scrypt pomaga, ale słabe hasło to słabe hasło. Master password musi być mocny (>= 6 słów diceware albo 20+ znaków losowych).
- **Fizyczny dostęp + cold boot attack** — master password w session file / DPAPI blob w pamięci. Akceptowalne.

## Otwarte pytania / przyszłe decyzje

- **Atomic writes** — czy używać write-temp-then-rename żeby uniknąć częściowych zapisów przy crashu? Prawdopodobnie tak.
- **`.gitignore` dla session file** — musi być, ale session jest poza vault dir (w `$XDG_RUNTIME_DIR`), więc nieistotne. Ale może użytkownik chce mieć session w samym vault dir? Decyzja: NIE, session zawsze w runtime dir, poza repo.
- **Config format** — `~/.config/pwvault/config.json` z polami: `vault_path`, `session_ttl_minutes`, `clipboard_clear_seconds`, `auto_commit`, `auto_push`.
- **Multi-vault support** — teraz nie, ale projektujemy tak żeby dało się dodać bez przepisywania.
