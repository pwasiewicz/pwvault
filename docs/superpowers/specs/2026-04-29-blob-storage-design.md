# Blob storage — design

**Data:** 2026-04-29
**Status:** spec, oczekuje na plan implementacji
**Scope:** rozszerzenie `pw-vault` o przechowywanie małych zaszyfrowanych plików (notatki, klucze SSH, "pierdoły") obok haseł, w tym samym repo.

## Tło i motywacja

Obecny `pw-vault` przechowuje wyłącznie wpisy haseł (`VaultEntry` w `vault/<path>.json`). Chcemy uzupełnić go o **bloby** — drobne zaszyfrowane pliki (klucze SSH, recovery codes, notatki, drobne konfigi) wsadzane przez `pwvault blob add /path < input` i odczytywane przez `pwvault blob get /path`.

Ograniczenie: **max 16 KB per blob** (twardy cap, konfigurowalny). Powyżej tego rozmiaru `pw-vault` jawnie nie jest właściwym narzędziem — git nie znosi dużych binariów, a emergency recovery zaczyna być niewygodne.

Wszystkie kluczowe założenia projektu pozostają:
- master password w głowie
- zero custom krypto (wyłącznie `age` v1)
- emergency recovery działa bez naszej binarki
- git jest jedynym mechanizmem sync

## Decyzje (z brainstormu 2026-04-29)

1. **Format pliku** — sidecar `<path>.age` w osobnym poddrzewie `vault/blobs/`. Nie inline w JSON-ie obok hasła.
2. **Operacje** — symetria z hasłami: `add`, `get`, `ls`, `rm`, `mv`. Każde z auto-commitem.
3. **Namespace** — osobne poddrzewo `vault/blobs/<path>.age`. Oddzielny `IBlobStorage` w Core. Brak współdzielenia ścieżki z entries.
4. **Plaintext metadane** — brak. Sama ścieżka pliku jest etykietą. Brak sidecar JSON, brak `created`/`updated`/`tags`. Historia = `git log`.
5. **I/O CLI** — stdin/stdout default; `--from-file`, `--to-file`, `-c/--copy` jako alternatywy.
6. **Safety** — `add` na istniejącej ścieżce rzuca błąd; `-f/--force` wymusza overwrite. Hard limit 16 KB konfigurowalny przez `~/.config/pwvault/config.json`.
7. **Integracja z resztą CLI** — żadna. `pwvault search`, `pwvault ls`, `pwvault gui` (TUI), Blazor MVP nie widzą blobów. Pełna izolacja.
8. **Emergency recovery** — osobny `recover-blob.sh` obok istniejącego `recover.sh`, sekcja "Blobs" w `FORMAT.md`. Symetria + discoverability.

## Architektura

### Storage layout

```
vault/
├── banking/mbank.json           ← entries jak teraz
├── dev/github.json
├── blobs/                       ← NOWE poddrzewo
│   ├── ssh/aws-prod.age
│   ├── notes/router-tata.age
│   └── certs/company-ca.age
├── FORMAT.md                    ← sekcja "Blobs" dorzucona
├── recover.sh                   ← bez zmian
├── recover-blob.sh              ← NOWY
└── .vault.json                  ← bez zmian (sentinel)
```

### Warstwy w `PwVault.Core`

```
PwVault.Core/
├── Domain/
│   ├── VaultEntry.cs           (jak teraz)
│   └── BlobPath.cs             ← nowe
├── Storage/
│   ├── IVaultStorage.cs         (jak teraz)
│   ├── VaultStorage.cs          (jak teraz, filtruje już po *.json — bloby niewidzialne)
│   ├── IBlobStorage.cs          ← nowe
│   └── BlobStorage.cs           ← nowe, operuje na vault/blobs/
└── Security/
    ├── ICryptoService.cs        (rozszerzony)
    ├── CryptoService.cs         (rozszerzony)
    └── Age/
        └── AgeV1Gateway.cs      (rozszerzony o EncryptBytes/DecryptBytes)
```

`BlobPath` to osobny typ wartościowy od `EntryPath` — kompilator nie pozwoli pomylić namespace'ów. Te same reguły walidacji (forward-slash, brak `..`, brak control chars, segment ≤ 50 znaków).

`BlobStorage` operuje wyłącznie na `vault/blobs/`. `VaultStorage` operuje na pozostałej części `vault/` — już dziś filtruje po `*.json`, więc pliki `.age` w `blobs/` nie są wykrywane jako entries. W planie implementacji warto dodać optymalizację: `VaultStorage.List` skipuje katalog `blobs/` przy enumeracji rekursywnej, żeby nie schodzić bez potrzeby przez poddrzewo blobów (zwłaszcza gdy ich będzie sporo).

## Kontrakt

### Domena

```csharp
public readonly struct BlobPath {
    public string Value { get; }
    public static BlobPath Parse(string raw);    // throws ArgumentException
    public override string ToString() => Value;
}
```

Bez wrappera dla ciphertextu — `byte[]` w storage i kontrakcie krypto jest wystarczające. Dodatkowy typ to ceremonia bez wartości w tym kontekście.

### Storage

```csharp
public interface IBlobStorage : IDisposable {
    string RootPath { get; }                                  // = vault/blobs
    IReadOnlyList<BlobPath> List(BlobPath? underPath = null);
    bool Exists(BlobPath path);
    byte[] ReadCiphertext(BlobPath path);                     // throws BlobNotFoundException
    void Add(BlobPath path, byte[] ciphertext);               // throws BlobAlreadyExistsException
    void Update(BlobPath path, byte[] ciphertext);            // throws BlobNotFoundException
    void Remove(BlobPath path);                               // throws BlobNotFoundException
    void Move(BlobPath src, BlobPath dst, bool overwrite);
}

public static class BlobStorageFactory {
    public static IBlobStorage Open(string vaultRoot, IFileSystem? fs = null);
}
```

Atomic write (temp+rename), spójnie z `VaultStorage`. Storage nic nie wie o `age` ani o masterze — operuje na surowych bajtach.

### Krypto — `IAgeGateway`

Dwie nowe metody bytes-based (bez ASCII armor):

```csharp
public interface IAgeGateway {
    // istniejące
    string Encrypt(string plaintext, string passphrase);
    string Decrypt(string asciiArmor, string passphrase);

    // nowe — binary age v1
    byte[] EncryptBytes(byte[] plaintext, string passphrase);
    byte[] DecryptBytes(byte[] ageFile, string passphrase);
}
```

**Dlaczego binary, nie ASCII armor:** ASCII armor istnieje dla copy-paste z github web UI w emergency flow entries (300-bajtowy `password_age` mieści się w schowku). Bloby w emergency wymagają `git clone` tak czy inaczej (16 KB w jednym `<textarea>` to nie jest copy-paste flow). Bez armoru → ~33% mniejsze pliki, prostszy `recover-blob.sh` (`age -d < blobs/x.age` bez wstępnego decode).

`AgeV1Gateway` od strony implementacji: dziś używa `AgeArmor.Encode/Decode` jako outer layer. Dla bytes pomijamy ten krok, reszta (`AgeHeaderCodec`, `AgePayload`, `ScryptKdf`) operuje już natywnie na bajtach. Zero zmian w pozostałych modułach.

### Krypto — `ICryptoService`

```csharp
public sealed record BlobDecryptionResult(DecryptionStatus Status, byte[]? Plaintext);

public interface ICryptoService {
    // istniejące — bez zmian
    DecryptionResult DecryptPassword(VaultEntry entry, string? masterPassword = null);
    DecryptionResult DecryptNotes(VaultEntry entry, string? masterPassword = null);

    // nowe
    byte[] EncryptBlob(byte[] plaintext, string masterPassword);
    BlobDecryptionResult DecryptBlob(byte[] ciphertext, string? masterPassword = null);
}
```

Semantyka spójna z password/notes:
- `EncryptBlob` wymaga jawnego mastera (encrypt jest świadomy, brak shortcutu sesji).
- `DecryptBlob`: jeśli `masterPassword` podany → używamy go (bez dotykania session store). Jeśli `null` → `_sessionStore.TryGetAndExtend()`. Sesja brak → `MasterNeeded` z `Plaintext = null`.

### Sentinel check

Każda komenda zapisująca blob (`blob add`, `blob add -f`) **przed** `EncryptBlob` weryfikuje master przeciw sentinelowi `.vault.json`. Gwarantuje, że typo w masterze nie zaszyfruje bloba nieodzyskiwalnym hasłem. Spójne z `add`/`edit` dla haseł. `rm`, `mv`, `get` nie szyfrują niczego nowego → nie sprawdzają sentinela.

### Roundtrip po Save

`BlobStorage.Add`/`Update` po atomic write robi natychmiastowy `ReadCiphertext` i porównuje bajty (`SequenceEqual`). Niezgodność → exception, brak auto-commitu, plik usunięty (rollback). Tak samo jak `VaultStorage` dziś.

## CLI surface

Wszystko zagnieżdżone pod `pwvault blob ...` (Spectre.Console.Cli `AddBranch("blob")`).

### `pwvault blob add <path> [--from-file <p>] [-f|--force]`

- Czyta plaintext: stdin (default) albo plik z `--from-file`. Jeśli oba dostępne (np. `--from-file` + redirect stdina) → `--from-file` wygrywa, stdin ignorowany.
- Walidacja rozmiaru przeciw `config.blob_max_bytes` (default 16384) **przed** szyfrowaniem. Powyżej → exit code 2, komunikat `blob size X bytes exceeds limit Y; raise blob_max_bytes in config if intentional`.
- Pozyskuje master z sesji albo prompt (`unlock`-style), weryfikuje sentinel.
- `EncryptBlob` → zapis do storage:
  - bez `-f` i ścieżka nie istnieje → `BlobStorage.Add`, auto-commit `pwvault: blob add <path>`
  - bez `-f` i ścieżka istnieje → `BlobAlreadyExistsException` → exit code 2
  - `-f` i ścieżka istnieje → `BlobStorage.Update`, auto-commit `pwvault: blob update <path>`
  - `-f` i ścieżka nie istnieje → `BlobStorage.Add` (jak normalny add), auto-commit `pwvault: blob add <path>`
- Roundtrip read+compare po Save.

### `pwvault blob get <path> [--to-file <p>] [-c|--copy]`

- `BlobStorage.ReadCiphertext` → `DecryptBlob`. Master z sesji albo prompt fallback (jak `get` dla haseł).
- Output (mutually exclusive flagi):
  - default → stdout (`Console.OpenStandardOutput().Write(bytes)`, binary-safe)
  - `--to-file <p>` → atomic write do pliku (temp+rename), tworzy parent dirs jak trzeba
  - `-c/--copy` → próba dekoduj UTF-8; sukces → `TextCopy.SetText` + auto-clear po `clipboard_clear_seconds`; failure (non-text bytes) → exit code 2, komunikat `blob is not valid UTF-8, use --to-file`
- Brak auto-commitu (read-only).

### `pwvault blob ls [path] [--flat]`

- `BlobStorage.List(prefix)`, render Spectre tree (default) albo flat.
- Bez deszyfracji, bez mastera. Pokazuje wyłącznie ścieżki — żadnych plaintext metadanych poza ścieżką.

### `pwvault blob rm <path> [-y]`

- Confirm prompt `Remove blob <path>? [y/N]`, `-y` skipuje.
- `BlobStorage.Remove`, auto-commit `pwvault: blob rm <path>`.

### `pwvault blob mv <src> <dst> [-f]`

- `BlobStorage.Move(src, dst, overwrite: -f)`. **Bez re-encrypt** — tylko rename pliku.
- `-f` nadpisuje istniejący `dst`. Bez `-f` → `BlobAlreadyExistsException`.
- Auto-commit `pwvault: blob mv <src> -> <dst>`.

### Konfiguracja

`PwVaultConfig` rośnie o jedno pole:

```csharp
public int BlobMaxBytes { get; set; } = 16 * 1024;        // 16 KB hard cap
```

Reflection w `ConfigCommand` to wykryje automatycznie (`pwvault config show` / `set blob_max_bytes 32768` działa bez dodatkowego kodu).

## Emergency recovery

### `vault/recover-blob.sh` (nowy)

```bash
#!/usr/bin/env bash
# pwvault blob recovery — works without pwvault binary, only needs `age`.
set -euo pipefail
if [ $# -lt 1 ]; then
    echo "Usage: $0 <blob-path>" >&2
    echo "Example: $0 ssh/aws-prod" >&2
    exit 1
fi
VAULT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BLOB="$VAULT_ROOT/blobs/$1.age"
if [ ! -f "$BLOB" ]; then
    echo "Blob not found: $BLOB" >&2
    exit 1
fi
age -d "$BLOB"
```

Executable (`chmod +x`), output do stdout. User pipuje do pliku jeśli binary.

### `vault/FORMAT.md` — nowa sekcja

```markdown
## Blobs

Lokalizacja: `vault/blobs/<path>.age`. Pliki to binary age v1 (bez ASCII armor),
passphrase mode (scrypt KDF, work factor 18). Zawartość to dowolne bajty
(do `blob_max_bytes` z konfiguracji, default 16384).

Brak plaintext metadanych — sama ścieżka pliku jest jedyną etykietą.

Recovery:
```bash
age -d < blobs/<path>.age          # ręcznie
./recover-blob.sh <path>            # skrypt
```
```

### `init` (nowe vaulty)

`InitCommand` od teraz dorzuca:
- `vault/blobs/` (pusty katalog z `.gitkeep`)
- `vault/recover-blob.sh` z `chmod +x`
- Sekcję "Blobs" w `FORMAT.md`

Initial commit obejmuje całość. Bez bumpa `schema_version` — entry schema się nie zmieniło.

### Lazy upgrade istniejących vaultów

`BlobStorage` udostępnia metodę `EnsurePrepared()` która przy pierwszym wywołaniu w vaulcie bez `blobs/`:

1. Tworzy `vault/blobs/` z `.gitkeep`.
2. Sprawdza obecność `vault/recover-blob.sh` — brak → tworzy z `chmod +x`.
3. Sprawdza `vault/FORMAT.md` — brak sekcji `## Blobs` → appendowuje.

Każdy z trzech kroków idempotentny niezależnie. Metoda zwraca `bool wasUpgraded` — `true` jeśli którykolwiek krok faktycznie coś zmienił.

CLI commands (`blob add`, `blob mv`) wołają `EnsurePrepared()` przed mutacjami i jeśli zwróciło `true`, emitują info-line do usera (`First blob in this vault — adding recover-blob.sh and FORMAT.md section.`). **Storage nie pisze nic do stdout** — zostaje czystym library code.

Wszystko trafia do tego samego auto-commitu (`pwvault: blob add <path>` ze zmianami w `recover-blob.sh`/`FORMAT.md`/`blobs/.gitkeep`).

**Dlaczego lazy zamiast explicit `pwvault blob init`:** spójność — `pwvault init` jest jedyną komendą "initialize vault", nie chcemy drugiej. Lazy upgrade jest idempotentny i widoczny w git diff.

## Testy

Priorytet: roundtrip (CLAUDE.md) + emergency recovery interop. Wszystkie w `tests/PwVault.Core.Tests/`.

### `BlobPathTests`
- Walidacja: parse poprawne ścieżki, odrzuca `..`, leading/trailing `/`, control chars, segment > 50 znaków.
- Equality, ToString round-trip.

### `BlobStorageTests` (real `IFileSystem` na tmp dir)
- `Add` + `ReadCiphertext` round-trip bytes.
- `Add` na istniejącej ścieżce → `BlobAlreadyExistsException`.
- `Update` na nieistniejącej → `BlobNotFoundException`.
- `Remove` + `Exists` zachowanie.
- `Move` bez `overwrite` na istniejącym dst → exception, source nie ruszony.
- `Move` z `overwrite=true` nadpisuje, source znika.
- `List` rekursywnie zwraca `BlobPath`-y w porządku deterministycznym (sort).
- **Lazy upgrade:** pierwszy `Add` w vaulcie bez `blobs/` tworzy katalog + `recover-blob.sh` + dorzuca sekcję do `FORMAT.md`. Idempotentny — drugi `Add` nic nie zmienia.
- **Atomic write korupcji:** symuluj exception w trakcie zapisu temp → `*.age` nie istnieje, brak osieroconego temp file.

### `AgeV1GatewayBytesTests`
- `EncryptBytes` + `DecryptBytes` round-trip dla: pustego, 1 byte, 16 KB.
- `DecryptBytes` ze złym hasłem → `InvalidPassphraseException`.
- `DecryptBytes` ze stamperem (zmiana 1 bajtu w środku payload) → `AgeDecryptionException`.

### `CryptoServiceBlobTests`
- `EncryptBlob` z masterem → ciphertext deszyfrowalny przez `DecryptBlob` z tym samym masterem.
- `DecryptBlob` bez argumentu, sesja → `Success`.
- `DecryptBlob` bez argumentu, brak sesji → `MasterNeeded` z `Plaintext = null`.
- `DecryptBlob` z masterem **nie** dotyka session store (parity z `DecryptPassword`).

### `AgeBinaryInteropTests` (rozszerzenie istniejących)
- `Our EncryptBytes → age -d` byte-exact.
- `age -p encrypt → Our DecryptBytes` byte-exact.
- Test z plikiem zawierającym null bytes i UTF-8 mix.
- Sanity test 16 KB włączony; multi-chunk wyłączony (prod cap < 64 KiB chunk size).

### Co świadomie NIE testujemy
- Auto-commit message format (UI detail).
- Lazy upgrade `recover-blob.sh` content byte-exact (sprawdzamy: plik istnieje + ma shebang + jest executable; treść pokryta przez interop test).

## Co NIE jest w scope tej zmiany

- **Reveal/show w TUI/Web/search** — bloby tylko przez `pwvault blob *`.
- **Plaintext metadane** (title, tags, content_type, created/updated). Sama ścieżka pliku jest etykietą.
- **Streaming dla większych plików** — cap 16 KB, całość mieści się w pamięci.
- **Versioning blobów** — git daje historię (`git log vault/blobs/<path>.age`).
- **Encrypted directory listing** — `pwvault blob ls` widzi ścieżki w plaintext (spójnie z `pwvault ls`).
- **Bulk operacje** (`blob add-many`, `blob export-all`, `blob import-dir`) — pojedyncze `add` w pętli wystarczy.
- **Symlinki / dedup** — każdy blob to osobny `.age`.
- **Dedicated `blob update` / `blob edit`** — `blob add -f` służy za update path.

## Aktualizacja `ARCH.md` (część tego samego cyklu)

CLAUDE.md wymaga: jeśli decyzja zmienia architekturę (scope, format storage, recovery flow) — uaktualniamy `ARCH.md` w tym samym cyklu. Konkretne zmiany:

1. **Sekcja "Format storage"** — drzewo plików w przykładzie wzbogacone o `blobs/` i `recover-blob.sh`. Nowa pod-sekcja "### Blobs" opisująca: `vault/blobs/<path>.age`, binary age (no armor), brak plaintext metadanych, cap z konfigu.
2. **Sekcja "Storage API"** — dodany `IBlobStorage` obok `IVaultStorage`, krótka tabelka różnic.
3. **Sekcja "Emergency recovery flow"** — dodany krok dla blobów (`age -d < blobs/<path>.age` lub `recover-blob.sh`).
4. **Sekcja "CryptoService i IAgeGateway"** — dorzucone `EncryptBytes`/`DecryptBytes` w gateway, `EncryptBlob`/`DecryptBlob` w cryptoservice.
5. **Sekcja "Scope" → "Iteracja 4 (blobs)"** — nowa lista TODO z komendami `blob add/get/ls/rm/mv` + lazy upgrade istniejących vaultów + cap rozmiaru.
6. **Sekcja "Co NIE jest zadaniem tego projektu"** — przeformułowanie pierwszej linii: zamiast *"Przechowywać cokolwiek poza hasłami i notatkami użytkownika"* → *"Przechowywać duże pliki, media, dokumenty (cap blobów = 16 KB konfigurowalny; powyżej to nie ten tool)"*.
7. **Sekcje "Cele i filozofia"** oraz **"Threat model"** — bez zmian. Bloby nie zmieniają top-level filozofii ani gwarancji bezpieczeństwa.

## Rolloutowanie

Spec → plan implementacji (`superpowers:writing-plans`) → implementacja w ramach jednego brancha. ARCH.md update, kod, testy, dokumentacja w jednym PR — żeby pojedynczy commit pokazywał spójną zmianę.
