# Networking — account API, world server, and the line between them

Phase 16. What is authoritative, where it runs, and how to start it.

---

## Two connections, not one

```
Unity client ──HTTP──▶ PHP API ──▶ MySQL 8.4          who you are
Unity client ─FishNet─▶ World server ──▶ PHP API      where you are
```

They are separate on purpose. The account API decides identity and never moves; the
world server decides gameplay and holds no credential. A world server that could read
the accounts table would be one compromised game process away from every password
hash in the game.

The world server is a **client of the account API**, exactly like the player is. It
presents the player's own token and is told what that token means. It has no
privileged endpoint, no service account and no database connection.

---

## Who decides what

| Decision | Decided by | Never decided by |
|---|---|---|
| Account identity | PHP, from the session token | the client, the world server |
| Character ownership | PHP, scoped by account in SQL | the client |
| Server / channel | PHP, from the session row | the client |
| Version compatibility | world server, against configured requirement | the client |
| Who may enter the world | world server, on the authority's answer | the client |
| Where a character stands | world server, from `SpawnPointDefinition` | the client |
| Whether a connection may act | world server's connection registry | the client |

The client sends a token and a set of claims. **The claims are compared, never read.**
Deleting every claim from a join request produces an identical admission — which is
the property that makes account, character, server and channel spoofing
unrepresentable rather than merely refused.

---

## Assemblies

```
Core ─ Data ─ Contracts ─ Backend ─ Network ─ Gameplay ─ Server ─ Client ─ UI
```

| Assembly | Owns | Must never contain |
|---|---|---|
| `Contracts` | `IWorldSessionAuthority`, `WorldJoinClaim`, `WorldAdmission`, `WorldPresence` | anything concrete |
| `Backend` | the only HTTP transport in the project; `HttpWorldSessionAuthority` | SQL, PHP, FishNet |
| `Network` | FishNet broadcast contracts, `WorldConnectionRegistry` | HTTP, SQL, `UnityEngine` beyond the messages |
| `Gameplay` | rules | `UnityEngine`, FishNet, HTTP, SQL |
| `Server` | `WorldEntryCoordinator`, the FishNet glue | `UnityWebRequest`, PDO, MySQL, `.php` |

Enforced by `WorldArchitectureTests`, not by convention. The Server assembly named a
transport once; that is what the tests exist to stop happening again.

---

## Development startup

Four things, in order. Placeholders only — no real value appears in this file.

**1. MySQL** (development instance, port `3307` locally; `3306` is the default)

```bash
# already running as a service in a normal setup
mysql --port=<DB_PORT> -u <DB_USERNAME> -p
```

**2. Schema and content**

```bash
cd backend
cp .env.example .env        # then fill in real values; .env is gitignored
php bin/migrate.php
php bin/seed.php            # content only, no accounts, no passwords
```

**3. The account API**

```bash
php -S 127.0.0.1:8080 -t backend/public
```

`backend/public` is the only directory a web server may expose. Source, migrations and
`.env` live above it.

**4. The world server**

A scene with a `NetworkManager`, a `Tugboat` transport, and `WorldServerBootstrap`.
Set its `Server Id`, `Channel Id`, `Api Base Address` and `Port` in the inspector —
none of them is hard-coded — and call `UseContent(...)` with the spawn-point registry
from the composition root.

Default listen port is `7770`. Nothing about it is fixed; it is a serialized field.

---

## Login to world, end to end

```
1.  POST /api/auth/login            → session id + bearer token
2.  GET  /api/servers               → what this account may see
3.  POST /api/session/select-server
4.  GET  /api/channels?server_id=
5.  POST /api/session/select-channel
6.  GET  /api/characters?server_id= → scoped by account in SQL
7.  POST /api/session/select-character
8.  POST /api/session/enter-world   → state 5, world_entry_state 1 (Authorised)

9.  FishNet connect to the world server
10. WorldJoinRequestMessage { token, claims, versions }
11. server: GET /api/session with that token
      → account, character, server, channel, map — the authority's, not the client's
12. version check, claim comparison, connection registry
13. WorldJoinResponseMessage { admitted, identities }   → Connecting
14. spawn resolved from SpawnPointDefinition
15. WorldSpawnMessage { map, spawn point, x, y, z }
16. server: POST /api/session/world-ready               → state 6, Active
```

Phase 14 stopped at step 8. Steps 9–16 are Phase 16.

If the connection dies between 8 and 16, the session stays in `EnteringWorld` — which
is the correct record of a handoff that did not complete, not a bug.

---

## Endpoints

| Method | Path | Auth | Added |
|---|---|---|---|
| `POST` | `/api/auth/login` | — | 15 |
| `GET` | `/api/servers` | Bearer | 15 |
| `GET` | `/api/channels?server_id=` | Bearer | 15 |
| `GET` | `/api/characters?server_id=` | Bearer | 15 |
| `POST` | `/api/session/select-server` | Bearer | 15 |
| `POST` | `/api/session/select-channel` | Bearer | 15 |
| `POST` | `/api/session/select-character` | Bearer | 15 |
| `POST` | `/api/session/enter-world` | Bearer | 15 |
| **`GET`** | **`/api/session`** | Bearer | **16** |
| **`POST`** | **`/api/session/world-ready`** | Bearer | **16** |
| **`POST`** | **`/api/session/release`** | Bearer | **16** |
| `GET` | `/api/health` | — | 15 |

### `POST /api/session/release` — why it exists

The API refuses a second live session, deliberately: taking somebody's session away is
a policy decision, not a side effect of signing in again. Until Phase 16 nothing could
give one up, so **a player who closed the game was locked out of their own account for
the full session lifetime**. The first live integration run found it on its second test.

Releasing ends the session and, in the same transaction, hands back any character it
left marked `InWorld`. A character stranded `InWorld` with no session is permanently
unplayable and nothing but expiry would ever fix it.

It is idempotent by nature rather than by an idempotency key, because a disconnect can
be observed more than once and must not consume a request id to be safe.

---

## Disconnect and reconnect

**Disconnect** releases the session and returns the character to `Playable`. It is
idempotent: a callback, a timeout and a shutdown may all fire for one socket, and only
the first does anything.

**Duplicate connection** — the same session connecting twice — is **replacement**, not
refusal. The newer socket wins and the older is disconnected. Refusing instead would
lock a player out of their own character every time their network dropped without a
clean close, which is the common case rather than the rare one.

Refusal is reserved for the genuinely dangerous collision: a *different* session
wanting a character somebody is holding. That would produce two authoritative copies of
one character, so it is refused outright.

**Stale connections** are remembered, not forgotten. A displaced socket cannot act, and
its eventual disconnect releases nothing — its session belongs to the socket that
replaced it, and releasing would disconnect the player who just successfully
reconnected.

**Server shutdown** releases every session it held. Without that, everyone who was
playing is locked out until expiry and every character stays `InWorld` in a world that
no longer exists.

---

## Presence

`WorldPresence` is `Offline`, `Connecting`, `InWorld`. All three are derived from an
actual connection. There is deliberately no `Online`: it would be a claim nothing here
can substantiate.

`Connecting` matters — a connection that is admitted and then fails to load must never
read as present.

---

## Versions

Phase 14's `VersionSet` and `VersionPolicy`, reused unchanged. The world server checks
**before** asking the authority: a client that cannot be spoken to cannot be told
anything useful about its session. Protocol is exact; client and content have floors.

An unparseable version reads as `0.0.0` and fails any requirement with a floor. A
client whose version cannot be read is not given the benefit of the doubt.

The server never invents a client version.

---

## Tests

```bash
# PHP — needs MySQL, uses DB_TEST_DATABASE
cd backend && php vendor/phpunit/phpunit/phpunit

# Unity EditMode — the live tests need the two commands below
php backend/bin/integration-fixture.php
DB_DATABASE=chibifantasy_test php -S 127.0.0.1:8099 -t backend/public

# Unity PlayMode — needs nothing; opens a loopback socket
```

| Suite | Crosses | Needs |
|---|---|---|
| `WorldConnectionRegistryTests` | nothing — pure | nothing |
| `WorldEntryCoordinatorTests` | nothing — fake authority | nothing |
| `HttpWorldSessionAuthorityTests` | scripted transport | nothing |
| `LiveBackendIntegrationTests` | **real HTTP → PHP → MySQL** | the two commands above |
| `FishNetWorldEntryTests` (PlayMode) | **a real FishNet socket** | nothing |

`LiveBackendIntegrationTests` **skips with a reason** when the backend is absent, so a
skip is never mistaken for a pass. A run reporting them as ignored has proven nothing
about integration.

`integration-fixture.php` invents a password per run and writes it to
`backend/storage/` (gitignored). Neither program contains a credential, because the
alternative was one living in the repository for good.

---

## Known limitations

- **Nothing is replicated.** No movement, combat, inventory, equipment, trade, shop,
  party or guild crosses the wire. Phase 16 establishes the authenticated connection
  and the world-entry boundary, and stops there.
- **No character state is loaded beyond identity and placement.** `WorldCharacter`
  holds level, class, job, gender, appearance and a location. Stats, experience,
  inventory and equipment are absent because the API does not serve them — an invented
  stat block is indistinguishable from a real one until it decides a fight.
- **No capacity enforcement in the world server.** Channel population is a cached
  column the API reports; the world server does not count its own connections against
  it.
- **The API is asked once per join.** There is no cache and no invalidation, which is
  correct but means a join costs a round trip.
- **HTTP only.** No TLS configuration, no certificate handling, no CORS policy.
- **No launcher, no patch CDN.** `VersionRequirement` is read from configuration; what
  a player does about a required update is not implemented.
- **The blocking transport is not for the main thread.** `UnityWebRequestTransport.Send`
  occupies its caller. A dedicated server or a worker is fine; calling it inside a frame
  a client wants to finish is not.
- **`ProtoInventoryHarness` hard-codes a character id** (`proto:character`). Pre-existing
  Phase 08 prototype-scene code, untouched by this phase.
