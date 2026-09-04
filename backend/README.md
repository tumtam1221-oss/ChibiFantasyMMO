# ChibiFantasyMMO Backend

Account, session, server/channel and character-select API. PHP + MySQL, behind the
transport-neutral `IAccountApi` seam Phase 14 defined in Unity.

This is the authority. The Unity client's copy of the session state machine exists so a
screen can grey out a button without a round trip; **this** decides, because a client that
concluded it was allowed would otherwise simply be believed.

---

## Requirements

| Component | Version | Why |
|---|---|---|
| PHP | **8.2+** (developed on 8.4.24) | `readonly` properties, enums, `match`, typed constants |
| ext-pdo, ext-pdo_mysql | — | the only database access path |
| ext-mbstring | — | correct length checks on multi-byte names |
| ext-openssl | — | not used directly; `random_bytes` requires a working CSPRNG |
| MySQL | **8.0+** (developed on 8.4.9) | `CHECK` constraints (8.0.16+), modern `utf8mb4` collations |
| Composer | 2.x | test framework only — the application runs without `vendor/` |

MariaDB 10.6+ works as a drop-in: nothing here uses a MySQL-only feature beyond InnoDB,
foreign keys, `CHECK` and `SELECT … FOR UPDATE`.

**MySQL 5.x will not work.** 5.5 allows only one `TIMESTAMP` column per table with
`DEFAULT CURRENT_TIMESTAMP`, and several tables here carry both `created_at` and
`updated_at`. `CHECK` constraints are parsed and ignored before 8.0.16, which would silently
drop the non-negative-balance guarantee.

---

## Setup

```bash
cp .env.example .env          # then fill in real values — .env is gitignored
composer install              # test framework only

php bin/migrate.php           # create the schema
php bin/seed.php              # development content (no accounts, no passwords)
```

Verify:

```bash
php bin/migrate.php --status
php vendor/phpunit/phpunit/phpunit
```

### Configuration

All configuration is environment-first: a real environment variable beats the `.env` file,
so a deployed server needs no file at all. See `.env.example` for the full list.

**Never commit `.env`.** `git add backend/.env` is refused by `.gitignore`; `.env.example`
is explicitly re-included so the template stays in version control.

---

## Running

`public/` is the **only** directory a web server may expose. Source, migrations,
configuration and `.env` all live above it, so a misconfigured server cannot serve them as
text.

```bash
php -S 127.0.0.1:8080 -t public      # development only
```

Production: point the document root at `backend/public`, route everything to `index.php`.

---

## Database

### Migrations

Ordered, tracked and **forward-only**.

```bash
php bin/migrate.php --status     # what is applied, what is pending
php bin/migrate.php              # apply everything outstanding
php bin/migrate.php --test       # apply to DB_TEST_DATABASE
```

Files are `NNNN_description.sql`; the numeric prefix is the version and duplicates are
refused. Applied versions are recorded in `schema_migration`, so re-running applies nothing
and it is safe on every deploy.

**There is no `down()`.** A rollback that has never been executed is a rollback that does not
work, and one that drops a column destroys data an operator may still need. Undoing a
migration is a new migration, written deliberately, with the data question answered.

**Caveat — DDL is not transactional in MySQL.** A migration containing several
`CREATE TABLE` statements that fails halfway leaves the earlier ones behind. That is a
property of the engine, not a claim this migrator makes. Mitigation: one concern per file,
so a failure leaves a boundary you can reason about. In development, `php bin/db-reset.php
--force` drops every table and re-migrates from zero. It refuses unless `APP_ENV` is a
development environment, refuses a database whose name looks production-ish, and refuses
without `--force`.

### Conventions

- **Identity columns** are `VARCHAR(64) COLLATE utf8mb4_bin` — binary, because an identifier
  is opaque and `A1B2` must not equal `a1b2`. 64 holds a GUID in "N" form with room to spare.
- **Display text** is `utf8mb4_unicode_ci`, so names sort as a human expects and survive
  characters outside the BMP.
- **Timestamps** are `DATETIME(3)`, not `TIMESTAMP` — the 2038 limit is inside this game's
  plausible lifetime.
- **Currency** is `BIGINT`. Never `DECIMAL`, never a float: a float balance makes
  `a + b - b != a` for ordinary values, which in an economy is a duplication exploit.
- **`revision`** on every row that changes under optimistic concurrency, matching the
  `Revision` type used in Unity since Phase 08.

### Invariants the database enforces

These are guarantees, not conventions — no application bug can violate them:

| Constraint | Guarantees |
|---|---|
| `container_slot.instance_id` UNIQUE | one item is in at most one slot of one container, anywhere |
| `character_equipment.instance_id` UNIQUE | one piece is worn in one place by one person |
| `equipment_card_socket.card_instance_id` UNIQUE | a socketed card is in exactly one piece |
| `character_devil_fruit` PK on `owner_id` | one active Devil Fruit per character |
| `party_member.character_id` UNIQUE | one character, one party |
| `guild_member.character_id` UNIQUE | one character, one guild |
| `guild.name` UNIQUE (ci) | two guilds cannot share a name, even in different case |
| `character_currency` CHECK `balance >= 0` | a balance can never go negative |
| `economy_transaction.request_id` UNIQUE | one request produces at most one transaction |
| `request_result` PK `(request_id, scope)` | a retry cannot commit twice |
| `character.account_id` FK NOT NULL | every character has an owner |

Together these are why an active fruit cannot be traded, a socketed card cannot be sold and
a listed item cannot be equipped: the item is in no container, and there is nothing for
another system to find.

### Indexes worth knowing

- `character (account_id, server_id, availability)` — the character-select query. Every
  character lookup is scoped by account **in SQL**; nothing fetches all and filters after.
- `account_credential (login_identifier)` UNIQUE — the login lookup.
- `account_session_token (token_hash)` UNIQUE — token resolution is an index hit, not a
  scan-and-compare, so there is no per-row timing to measure.
- `login_attempt (login_identifier, attempted_at)` and `(remote_address, attempted_at)` —
  the two rate-limit counters.

### Lock order

Trade and shop commits take locks in this order to avoid deadlocking against each other:

1. the session or listing row (`SELECT … FOR UPDATE`)
2. wallet rows, ordered by `owner_id` ascending
3. item rows, ordered by `instance_id` ascending

Ordering by identifier rather than by "mine then theirs" is what makes two simultaneous
trades between the same pair take the same locks in the same sequence.

---

## API

All responses are JSON. All errors share one shape:

```json
{ "code": "...", "message_key": "...", "request_id": "..." }
```

`code` is stable and machine-readable; `message_key` is a localisation key, because the
server does not know what language the player reads. No SQL, stack trace, file path or
exception message ever reaches a client.

| Method | Path | Auth | Idempotent |
|---|---|---|---|
| `POST` | `/api/auth/login` | — | yes (`request_id`) |
| `GET` | `/api/servers` | Bearer | read-only |
| `GET` | `/api/channels?server_id=` | Bearer | read-only |
| `GET` | `/api/characters?server_id=` | Bearer | read-only |
| `POST` | `/api/session/select-server` | Bearer | yes |
| `POST` | `/api/session/select-channel` | Bearer | yes |
| `POST` | `/api/session/select-character` | Bearer | yes |
| `POST` | `/api/session/enter-world` | Bearer | yes |
| `GET` | `/api/health` | — | read-only |

HTTP status carries the category (401 re-authenticate, 409 the world moved, 503 retry) so a
proxy or monitor can act without parsing the body; `code` carries the precise reason.

### Idempotency

Every mutating call takes a `request_id`. A committed request replays its original result;
`replayed: true` marks the replay. **A rejected request is deliberately not cached** — it
wrote nothing, so re-sending it is re-judged, and a player who fixes the cause succeeds.

The `UNIQUE (request_id, scope)` index is the real protection: two concurrent retries both
find nothing and both do the work, and the index makes one lose. A check-then-act in PHP
alone would let both commit.

### Authentication

- Passwords: `password_hash`/`password_verify` with `PASSWORD_DEFAULT`, upgraded on login via
  `password_needs_rehash`. Never MD5, SHA1, a custom scheme or a hard-coded salt.
- **Unknown account and wrong password are indistinguishable**, and a dummy verification runs
  when no account is found so both paths take comparable time (measured at ~1.00 ratio).
- A disabled or banned account still verifies its password first, so a guessed identifier
  cannot confirm an account exists.
- Session ids and bearer tokens are `random_bytes` — never derived from an account id, a
  username, a timestamp or a counter.
- Only the **SHA-256 of a token** is stored. A leaked table yields nothing usable. SHA-256
  and not a slow KDF, because a token already carries 256 bits of CSPRNG entropy.

---

## Tests

```bash
php vendor/phpunit/phpunit/phpunit
```

Tests run against **`DB_TEST_DATABASE`**, which `Connection::forTests()` refuses to open if
it is unset or equal to `DB_DATABASE`. They truncate freely, so that guard matters.

They run against real MySQL, not a mock: row locking, transaction rollback and unique
constraints under contention are properties of the database, and a mock would only assert
that the mock behaves as its author imagined. `ConcurrencyTest` opens a genuine **second
connection** to observe contention — a single connection sees its own uncommitted work and
would pass for the wrong reason.

No fixture ships a credential. Accounts are created by tests with passwords invented at
runtime; the seeder creates content only.

---

## Boundaries

Unity contains no DB host, password, connection string, SQL, PHP or signing secret — asserted
by tests in the EditMode suite. Gameplay stays engine-free and transport-free; only
`ChibiFantasy.Backend` knows HTTP exists, and only this backend knows SQL exists.

Phase 16 will add the FishNet connection. `enter-world` stops at `world_entry_state: 1`
(*Authorised*) — the authority has agreed and named everything the world needs, and nothing
has connected.
