# pw-vault

Osobisty password manager. Git-backed, master password w głowie, `age` passphrase mode dla pól wrażliwych.

Architektura i threat model: zobacz [ARCH.md](./ARCH.md).

## Instalacja

### Self-contained binarka (zalecane)

Jeden plik ~78 MB ze wszystkim (runtime .NET + libsodium + kod), działa bez zainstalowanego .NET:

```bash
git clone <repo-url> pw-vault
cd pw-vault
./install.sh                    # → ~/.local/bin/pwvault (bez sudo)
./install.sh --system           # → /usr/local/bin/pwvault (sudo, system-wide)
./install.sh --prefix /opt/bin  # → /opt/bin/pwvault (custom)
./install.sh --build-only       # tylko build do ./artifacts, bez instalacji
```

Skrypt wywołuje `dotnet publish` (wymaga SDK na maszynie buildującej), produkuje single-file binary i kopiuje pod nazwą `pwvault`. Runtime auto-detect: linux-x64 / osx-x64 / osx-arm64.

**Upgrade:** po `git pull` po prostu odpal `./install.sh` ponownie — nadpisze poprzednią wersję.

**PATH:** `~/.local/bin` jest w PATH w większości nowoczesnych dystrybucji (Debian/Ubuntu od `~/.profile`). Jeśli `which pwvault` po instalacji nic nie zwraca:

```bash
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

### Dev mode (bez publishu)

```bash
dotnet build -c Release
alias pwvault='dotnet run --project src/PwVault.Cli --'
```

Wymaga .NET 10 SDK. `age` binary nie jest wymagane do działania (mamy natywną implementację v1), ale przyda się do emergency recovery.

## Quickstart

```bash
# Ustaw ścieżkę vaulta raz (albo przekazuj --vault <path> przy każdej komendzie)
export PWVAULT_PATH=~/vault

# 1. Inicjalizacja — tworzy katalog, .vault.json z sentinelem, README/FORMAT/recover.sh, git init
pwvault init ~/vault

# 2. Odblokuj sesję (master cache'owany 1h, sliding extend)
pwvault unlock

# 3. Dodaj wpis z generowanym hasłem
pwvault add banking/mbank --generate --tag banking --tag 2fa

# 4. Pobierz (domyślnie do clipboardu z auto-clear)
pwvault get banking/mbank

# 5. Zakończ sesję
pwvault lock
```

## Przykłady

### Dodawanie wpisów

```bash
# Interaktywnie — prompt o tytuł, username, URL, password, notes
pwvault add dev/github

# Z flagami (brak promptu)
pwvault add dev/github \
  --title "GitHub" \
  --username pat@example.com \
  --url https://github.com \
  --generate --length 32 \
  --tag dev --tag 2fa

# Z notatkami (szyfrowane)
pwvault add banking/revolut --notes "PIN: 1234 / security Q: pet name"
```

Tagi są normalizowane automatycznie: `--tag Banking` == `--tag banking` == `--tag BANKING` (lowercase, dedup, sort ordinal). Bez spacji, bez `/`, max 50 znaków.

### Edycja wpisów

```bash
# Nie pamiętasz path — interactive picker najpierw, potem prompt po każdym polu
pwvault edit -i

# Interaktywnie — prompt o każde pole z obecną wartością jako default
pwvault edit dev/github

# Zmień tylko jedno pole (bez promptu o master, jeśli nie dotyka password/notes)
pwvault edit dev/github --title "GitHub Enterprise"
pwvault edit dev/github --username pat+new@example.com
pwvault edit dev/github --url https://github.example.com

# Wyczyść opcjonalne pole
pwvault edit dev/github --clear-url
pwvault edit dev/github --clear-notes

# Tagi — dwa style (nie można mieszać)
pwvault edit dev/github --tag dev --tag 2fa             # REPLACE całą listą
pwvault edit dev/github --add-tag work --remove-tag old  # diff względem obecnych
pwvault edit dev/github --clear-tags

# Hasło — żąda master (sentinel check + re-encrypt)
pwvault edit dev/github --password                      # prompt z confirm
pwvault edit dev/github --regenerate                    # CSPRNG
pwvault edit dev/github --regenerate --length 40

# Notatki (szyfrowane)
pwvault edit dev/github --notes "new security Q: mother's maiden name"
```

Edit jest **lazy auth**: metadata-only (title, username, url, tags) nie pyta o master. Dopiero `--password`, `--regenerate`, `--notes` lub `--clear-notes` wymagają master (sentinel check + re-encrypt).

Path jest immutable — zmiana ścieżki to `pwvault mv` (iteracja 2).

### Pobieranie hasła

```bash
# Do clipboardu z progress barem i auto-clear (domyślne)
pwvault get banking/mbank

# Na stdout (do pipe)
pwvault get banking/mbank --show

# Razem z notatkami (prompt o master jeśli sesja wygasła)
pwvault get banking/mbank --notes

# Interactive picker — type to filter po path / title / username / tagach
pwvault get -i                # wybór z listy live-filtrowanej, potem clipboard
pwvault get -i --show         # wybór z listy, potem stdout
pwvault get -i --notes        # wybór z listy, plus odszyfrowane notatki
```

Picker ładuje wszystkie wpisy, sortuje po path, a Ty piszesz żeby odfiltrować — `Spectre.Console` robi substring match (piszesz `mba` → widzisz `banking/mbank`, `personal/mba-test`). Działa też na tagach (`2fa` filtruje do wpisów z tagiem 2fa). Enter wybiera, ↑/↓ scroll.

### Listowanie i wyszukiwanie

```bash
# Drzewo wszystkich wpisów
pwvault ls

# Tylko poddrzewo
pwvault ls banking

# Flat list
pwvault ls --flat

# Filtr po tagu (można łączyć — AND)
pwvault ls --tag 2fa
pwvault ls --tag work --tag 2fa

# Fuzzy search (po path/title/username/url)
pwvault search mbank
pwvault search "mb pol"

# Search + tag filter
pwvault search mbank --tag 2fa

# Sam filtr po tagach (query opcjonalny)
pwvault search --tag banking

# Co mam w ogóle za tagi
pwvault tags
```

### Usuwanie

```bash
pwvault rm personal/old-service         # z confirm promptem
pwvault rm personal/old-service -y      # bypass promptu
```

### Generator haseł

```bash
pwvault gen                # 24 znaki ze symbolami, do stdout (default z configu)
pwvault gen 32             # 32 znaki
pwvault gen --no-symbols   # tylko alfanumeryczne
pwvault gen -c             # do clipboardu z auto-clear
```

## Emergency recovery (bez naszej binarki)

Jeśli z jakiegoś powodu nie masz pwvault pod ręką — wpisy to czytelny JSON, a `password_age` to standardowy age ASCII-armor:

```bash
# W repo klonie
cat ~/vault/banking/mbank.json
# Skopiuj zawartość pola "password_age" (od -----BEGIN do -----END)

# Linux / macOS
pbpaste | age -d           # macOS
xclip -o | age -d          # Linux
# → prompt o master password → wypada plaintext hasło
```

Albo odpal `~/vault/recover.sh banking/mbank` (skrypt committed do repo w `init`).

Działa na dowolnej maszynie z `age` (apt/brew/winget/scoop/GitHub releases) i `jq`.

## Konfiguracja

`~/.config/pwvault/config.json`:

```json
{
  "vault_path": "/home/you/vault",
  "generated_password_length": 24,
  "clipboard_clear_seconds": 15,
  "auto_commit": true,
  "auto_push": false,
  "work_factor": 18,
  "max_decrypt_work_factor": 22
}
```

Override przez env: `PWVAULT_PATH`, flaga `--vault <path>` na każdej komendzie.

## Scope

Świadomie wąski: CLI-first, git jako sync, brak OTP/autofill/mobile/browser extension/sharing. Pełna lista trade-offów: [ARCH.md](./ARCH.md).
