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

### Schemat wpisu (v1)

Jeden plik JSON per wpis. Lokalizacja: `{vault_root}/{entry_path}.json`. `entry_path` to logiczna ścieżka z `/` jako separatorem (niezależnie od OS).

```json
{
  "schema_version": 1,
  "title": "mBank",
  "username": "pat@example.com",
  "url": "https://mbank.pl",
  "password_age": "-----BEGIN AGE ENCRYPTED FILE-----\n...\n-----END AGE ENCRYPTED FILE-----",
  "notes_age": "-----BEGIN AGE ENCRYPTED FILE-----\n...\n-----END AGE ENCRYPTED FILE-----",
  "created": "2026-04-22T10:30:00.0000000+00:00",
  "updated": "2026-04-22T10:30:00.0000000+00:00"
}
```

**Plaintext fields (świadomy trade-off):** `schema_version`, `title`, `username`, `url`, `created`, `updated`. Git log per-wpis ujawnia historię zmian. Akceptowane — nikt nie robi targeted reverse engineering osobistego vaulta.

**Zaszyfrowane fields (zawsze ASCII-armored age):**
- `password_age` — właściwe hasło (wymagane, zawsze obecne)
- `notes_age` — notatki (opcjonalne, ale **gdy są, to zawsze szyfrowane**; tu lądują security answers, recovery codes, PINy)

**Nullability:** `username`, `url`, `notes_age` mogą być `null` / nieobecne. `title` i `password_age` są wymagane. `schema_version`, `created`, `updated` wypełniane przez storage.

**Ścieżka logiczna (`EntryPath`):** forward-slash separated, bez wiodącego/końcowego `/`, bez `..`, bez znaków kontrolnych. Przykłady: `banking/mbank`, `dev/github`, `gmail`.

### Rozdział domain vs serializacja

- **Domena** (eksponowana z Core, używana przez CLI/Web): `VaultEntry` — immutable record z typami wartościowymi (`EncryptedField` struct, `EntryPath` struct).
- **Serializacja** (internal): `EntryFileModel` — płaski DTO z `string`-ami, 1:1 z JSON. Mapowanie w `EntryMapper`.

Powód: izolacja zmian formatu pliku (`schema_version`, migracje) od typów używanych w logice aplikacji.

### Szyfrowanie — age passphrase mode

- `age -a -p` (ASCII armor + passphrase)
- KDF: scrypt (standard age)
- Każde pole szyfrowane osobno z tym samym master password
- ~1s scrypt per deszyfracja — akceptowalne przy unlock-one, świadomie odrzucone unlock-all

### Storage API (warstwa `PwVault.Core`)

Interfejs surowy (bez szyfrowania — storage operuje na już-zaszyfrowanych polach):

```csharp
interface IVaultStorage : IDisposable {
    string RootPath { get; }
    IReadOnlyList<StoredEntry> List(EntryPath? underPath = null);
    StoredEntry? TryGet(EntryPath path);
    StoredEntry  Get(EntryPath path);         // throws EntryNotFoundException
    StoredEntry  Add(VaultEntry entry);       // throws EntryAlreadyExistsException
    StoredEntry  Update(VaultEntry entry);    // throws EntryNotFoundException
    void         Remove(EntryPath path);
    IReadOnlyList<StoredEntry> Search(string query, int maxResults = 20);
}
```

**Session pattern:** `using var storage = VaultStorage.Open("/path/to/vault");` — operacje w obrębie scope'u, `Dispose()` zamyka. Na razie session jest lekki (trzyma path + IFileSystem). Miejsce na przyszłe: file locks, in-memory cache, transakcje.

**Factory:**
```csharp
VaultStorage.Open(string rootPath, IFileSystem? fs = null, TimeProvider? time = null);
```

Default `IFileSystem` = `RealFileSystem`. Wstrzykiwany dla testów / in-memory sandboxu.

**Gdzie trzymać referencję do vault directory:** na razie CLI dostaje path jako argument i przekazuje do `Open`. Przyszłe iteracje: `~/.config/pwvault/config.json` z `vault_path` + opcjonalnie vault marker file (`.pwvault`) żeby odróżnić katalog vaulta od losowego folderu.

### Abstrakcja filesystemu

`IFileSystem` — minimum co storage potrzebuje:
- `FileExists`, `DirectoryExists`, `CreateDirectory`
- `ReadAllText`, `WriteAllTextAtomic` (temp-then-rename, atomic na tym samym FS)
- `DeleteFile`, `EnumerateFiles(dir, pattern, recursive)`
- `GetFileInfo` → `FileInfoSnapshot(FullPath, SizeBytes, CreatedUtc, ModifiedUtc)`

`RealFileSystem` używa `System.IO`. Testy używają tmp dir + `RealFileSystem` (prostsze, testuje realne ścieżki kodu). `InMemoryFileSystem` do dodania gdy będzie potrzebny dla wydajności testów.

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

### Abstrakcja (`PwVault.Core.Security`)

```csharp
interface ISessionStore {
    void Save(string masterPassword);     // initial TTL 1h
    string? TryGetAndExtend();            // null jeśli brak/wygasł; wydłuża sesję
    void Clear();
}

static class SessionStoreFactory {
    public static ISessionStore Create(TimeProvider? time = null);
}
```

Implementacje (obie w `Core`):
- `FileSessionStore` — Linux (WSL2) i macOS. JSON plaintext, permissions `0600` wymuszone przez `FileStreamOptions.UnixCreateMode` przy tworzeniu pliku tymczasowego, atomic write przez temp-then-rename.
- `WindowsSessionStore` — `ProtectedData.Protect` (DPAPI, `DataProtectionScope.CurrentUser`) nad JSON w UTF-8, zapis jako binary blob. Typ oznaczony `[SupportedOSPlatform("windows")]`.

### Semantyka TTL

- **Initial TTL:** 1h od `Save`.
- **Sliding extend:** każde udane `TryGetAndExtend` gwarantuje że do wygaśnięcia zostaje **co najmniej 30 minut** od `now`. Formalnie: `new_expires = max(current_expires, now + 30min)`. Nigdy nie skraca; nie rośnie w nieskończoność; pozwala aktywnej sesji nie wygasnąć w środku pracy.
- **Expiration:** gdy `expires_at <= now`, plik zostaje skasowany, `TryGetAndExtend` zwraca `null`.
- **Korupcja pliku** (JSON / DPAPI): traktujemy jako brak sesji (kasujemy plik, zwracamy `null`).

Stałe `SessionTtl.Initial` (1h) i `SessionTtl.MinAfterUse` (30min) są publiczne. Oba stores przyjmują też nadpisane wartości w konstruktorze — używane w testach z `FakeTimeProvider`.

### Świadoma asymetria

Windows dostaje darmowy OS-level key wrapping (DPAPI). Linux poprzestaje na `0600` + tmpfs + TTL. Na Linuksie nie ma natywnego odpowiednika DPAPI w BCL, a libsecret/keyring daemon są zawodne (szczególnie w WSL2). Dla threat modelu osobistego narzędzia — wystarczy.

## CryptoService i IAgeGateway

Warstwa `PwVault.Core.Security` udostępnia też kontrakt szyfrowania:

```csharp
enum DecryptionStatus { Success, MasterNeeded }

sealed record DecryptionResult(DecryptionStatus Status, string? PlainText);

interface ICryptoService {
    DecryptionResult DecryptPassword(VaultEntry entry, string? masterPassword = null);
    DecryptionResult DecryptNotes(VaultEntry entry, string? masterPassword = null);
}
```

**Algorytm:** jeśli `masterPassword` podany → używamy go (bez dotykania session store). Jeśli `null` → `_sessionStore.TryGetAndExtend()`. Gdy session też zwróci `null` → `DecryptionResult.MasterNeeded`.

**Dlaczego podany master nie odświeża sesji:** session refresh jest efektem ubocznym komendy `unlock`, nie deszyfracji ad-hoc z jawnie podanym hasłem (np. jednorazowe wywołanie z CI lub pipe). Rozdział świadomy — `unlock` i `decrypt` mają różne kontrakty.

**`DecryptNotes` z `entry.NotesEncrypted = null`:** zwraca `Success(null)` — brak notatek nie jest błędem.

### IAgeGateway — native C# implementacja age v1

```csharp
interface IAgeGateway {
    string Decrypt(string asciiArmor, string passphrase);
    string Encrypt(string plaintext, string passphrase);
}
```

**Implementacja: `AgeV1Gateway` w `PwVault.Core.Security.Age`.** Natywny port specyfikacji [age v1](https://age-encryption.org/v1), zero subprocessów, zero PTY magic. Moduły:

- `AgeArmor` — PEM encode/decode (standard base64 z paddingiem, wrap 64 kol., markery `BEGIN/END AGE ENCRYPTED FILE`)
- `AgeHeaderCodec` — build/parse nagłówka (linia wersji, scrypt stanza, MAC), odtwarza dokładne bajty dla HMAC
- `AgePayload` — chunked AEAD (64 KiB, ChaCha20-Poly1305, 12-bajtowy nonce = 11B big-endian counter + 1B last-flag)
- `ScryptKdf` — wrapper nad BouncyCastle scrypt (N=2^WF, r=8, p=1, dkLen=32, salt prefiksowany `"age-encryption.org/v1/scrypt"`)
- `AgeV1Gateway` — orchestrator (generuje file-key + nonce, wrap file-key przez ChaCha20-Poly1305 z zero nonce, HKDF-SHA256 dla MAC key i payload key, HMAC-SHA256 nad nagłówkiem)

**Zależności krypto:**
- `Geralt` — ChaCha20-Poly1305 (wrapper nad libsodium)
- `BouncyCastle.Cryptography` — scrypt (brak w built-in .NET ani w Geralt)
- Built-in `System.Security.Cryptography` — HKDF-SHA256, HMAC-SHA256, RandomNumberGenerator, `CryptographicOperations.FixedTimeEquals` dla MAC comparison, `CryptographicOperations.ZeroMemory` dla kasowania wrażliwych buforów

**Parametry:**
- Default encrypt work factor: **18** (zgodnie z age default, ~1s scrypt na współczesnym CPU)
- Minimum akceptowany przy encrypt: **10** (zabezpieczenie przed słabym KDF w kodzie)
- Maximum akceptowany przy decrypt: **22** (zabezpieczenie DoS — preparowany plik z WF=30 zająłby minuty)
- Test WF: **10** (N=1024, deszyfracja w ms zamiast sekundy)

**Obsługa błędów:**
- `InvalidPassphraseException : AgeDecryptionException` — ChaCha20-Poly1305 auth fail przy unwrapie file-key (jednoznaczny sygnał złego hasła)
- `AgeDecryptionException` — pozostałe błędy deszyfracji (MAC mismatch, tampering, zbyt wysokie WF)
- `FormatException` — uszkodzony nagłówek / armor

**Weryfikacja interop:**
- Testy unit: 32 testy per moduł + end-to-end roundtrip, multi-chunk, unicode
- Testy integracyjne (`AgeBinaryInteropTests`) — kierunek obu stron przeciwko prawdziwemu `age` binary:
  - `Our encrypt → age -d decrypt` (byte-exact)
  - `age -p encrypt → Our decrypt` (byte-exact)
  - Multi-chunk 200KB, unicode, empty payload
- Automatyczne skipowanie jeśli `age` lub `script` nie są w PATH (np. Windows)
- PTY dla TTY wymagania age — via Linux `script -qec` z stdin fed passphrase, obchodzi /dev/tty requirement nie dotykając ConPTY/forkpty bezpośrednio

**Zero custom krypto.** Prymitywy pochodzą z dojrzałych libek (libsodium via Geralt, BouncyCastle scrypt, built-in .NET HKDF/HMAC). Nasz kod to jedynie format assembly/parsing — byte layout weryfikowany przez interop tests.

## TTL i fallback (CLI workflow)

- `pwvault unlock` — prompt o master password, zapis do session store (TTL 1h)
- `pwvault get <name>` — `CryptoService.DecryptPassword(entry)` → jeśli `MasterNeeded`, prompt i retry z `masterPassword` argumentem (bez zapisu do sesji, chyba że CLI zdecyduje)
- `pwvault lock` — `sessionStore.Clear()`
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

### MVP (zrobione)

- [x] `pwvault init <path>` — inicjalizacja vaulta (katalog + README + FORMAT.md + recover.sh + `.vault.json` z sentinel + git init + initial commit)
- [x] `pwvault unlock` — prompt o master password, weryfikacja sentinel, zapis do session store z TTL
- [x] `pwvault lock` — wyczyść session
- [x] `pwvault add <path>` — dodaj wpis (prompty albo flagi: title, url, username, password/generate, notes), szyfruje, zapisuje, auto-commit
- [x] `pwvault get <path> [-c|--show|--notes]` — deszyfruje, default clipboard z auto-clear, `--show` stdout
- [x] `pwvault ls [path] [--flat]` — drzewo wpisów
- [x] `pwvault rm <path> [-y]` — usuń wpis (confirm prompt, bypass `-y`), auto-commit
- [x] `pwvault search <query>` — fuzzy po plaintext metadata (FuzzySharp)
- [x] `pwvault gen [length] [--no-symbols] [-c]` — generator haseł (CSPRNG)
- [x] Auto-commit po każdej mutacji (konfigurowalne; auto-push opcjonalne)

**Weryfikacja master:** `.vault.json` z encrypted sentinel (stały plaintext `pwvault-sentinel-v1` zaszyfrowany master passwordem). Weryfikacja przed każdym write — zapobiega zapisom z typo w master password, które byłyby potem nieodzyskiwalne.

**Stack:** `Spectre.Console.Cli` (routing + settings) + `Spectre.Console` (TUI prompts, tree, tables, progress) + `Microsoft.Extensions.DependencyInjection` (DI bridge przez `TypeRegistrar`/`TypeResolver`). `TextCopy` dla clipboard.

### Iteracja 2 (planowane)

- [ ] `pwvault edit <path>` — edycja istniejącego wpisu
- [ ] `pwvault mv <src> <dst>` — przenoszenie
- [ ] `pwvault import bitwarden <json>` — import z Bitwardena
- [ ] Interaktywny TUI picker (`pwvault` bez argumentów → Spectre.Console lista z live fuzzy)
- [ ] `pwvault rotate-master` — rotacja master password (re-encrypt wszystkich pól `*_age` + sentinel)

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
