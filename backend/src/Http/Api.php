<?php

declare(strict_types=1);

namespace ChibiFantasy\Http;

use ChibiFantasy\Auth\AccountRepository;
use ChibiFantasy\Auth\Authenticator;
use ChibiFantasy\Auth\AuthFailure;
use ChibiFantasy\Auth\RateLimiter;
use ChibiFantasy\Character\CharacterRepository;
use ChibiFantasy\Character\CharacterStateRepository;
use ChibiFantasy\Party\PartyRepository;
use ChibiFantasy\Directory\DirectoryRepository;
use ChibiFantasy\Session\IdempotencyStore;
use ChibiFantasy\Session\SessionRepository;
use ChibiFantasy\Session\SessionService;
use ChibiFantasy\Session\VersionPolicy;
use ChibiFantasy\Support\Env;
use ChibiFantasy\World\MonsterSpawnRepository;
use PDO;

/**
 * Every endpoint, and the routing that reaches them.
 *
 * One class rather than a controller per route because the whole surface is eight
 * handlers that share one set of collaborators; spreading them over eight files
 * with a container to wire them would be more machinery than the problem has.
 *
 * The shape of every mutating handler is the same and deliberately so:
 *
 *   1. resolve the session from the bearer token -- never from the payload;
 *   2. validate the request's shape;
 *   3. run the work at most once for its request key;
 *   4. return a typed result or a typed problem.
 *
 * `handle()` is the only entry point, it never throws, and it turns anything
 * unexpected into a 500 whose body says nothing about what broke.
 */
final class Api
{
    private readonly AccountRepository $accounts;
    private readonly Authenticator $authenticator;
    private readonly SessionRepository $sessions;
    private readonly SessionService $flow;
    private readonly DirectoryRepository $directory;
    private readonly CharacterRepository $characters;
    private readonly CharacterStateRepository $characterState;
    private readonly PartyRepository $parties;
    private readonly MonsterSpawnRepository $monsterSpawns;
    private readonly IdempotencyStore $idempotency;

    public function __construct(private readonly PDO $pdo)
    {
        $this->accounts = new AccountRepository($pdo);
        $this->authenticator = new Authenticator($this->accounts, new RateLimiter($pdo));
        $this->sessions = new SessionRepository($pdo);
        $this->directory = new DirectoryRepository($pdo);
        $this->characters = new CharacterRepository($pdo);
        $this->characterState = new CharacterStateRepository($pdo);
        $this->parties = new PartyRepository($pdo);
        $this->monsterSpawns = new MonsterSpawnRepository($pdo);
        $this->idempotency = new IdempotencyStore($pdo);
        $this->flow = new SessionService(
            $pdo,
            $this->sessions,
            $this->directory,
            $this->characters,
            $this->characterState
        );
    }

    /**
     * Routes and runs a request. Never throws.
     *
     * An unhandled exception here would otherwise reach PHP's error handler, whose
     * output carries file paths, a stack trace and often a fragment of SQL. It is
     * caught, and the client is told only that something failed.
     */
    public function handle(Request $request): Response
    {
        $requestId = $request->string('request_id', '');

        try {
            return $this->route($request, $requestId);
        } catch (ValidationException $e) {
            return Response::problem(
                ApiProblem::validation('invalid_' . $e->field, $e->messageKey),
                $requestId
            );
        } catch (\Throwable $e) {
            // The real cause belongs in a log the operator reads, never in a
            // response the player receives.
            error_log('[api] ' . $request->method . ' ' . $request->path . ': ' . $e->getMessage());

            return Response::problem(ApiProblem::internal(), $requestId);
        }
    }

    private function route(Request $request, string $requestId): Response
    {
        $path = rtrim($request->path, '/');

        return match (true) {
            $request->method === 'POST' && $path === '/api/auth/login'
                => $this->login($request, $requestId),

            $request->method === 'GET' && $path === '/api/servers'
                => $this->listServers($request, $requestId),

            $request->method === 'GET' && $path === '/api/channels'
                => $this->listChannels($request, $requestId),

            $request->method === 'GET' && $path === '/api/characters'
                => $this->listCharacters($request, $requestId),

            $request->method === 'POST' && $path === '/api/session/select-server'
                => $this->selectServer($request, $requestId),

            $request->method === 'POST' && $path === '/api/session/select-channel'
                => $this->selectChannel($request, $requestId),

            $request->method === 'POST' && $path === '/api/session/select-character'
                => $this->selectCharacter($request, $requestId),

            $request->method === 'POST' && $path === '/api/session/enter-world'
                => $this->enterWorld($request, $requestId),

            $request->method === 'POST' && $path === '/api/session/release'
                => $this->release($request, $requestId),

            $request->method === 'GET' && $path === '/api/session'
                => $this->describeSession($request, $requestId),

            $request->method === 'POST' && $path === '/api/session/world-ready'
                => $this->worldReady($request, $requestId),

            $request->method === 'GET' && $path === '/api/character/state'
                => $this->loadCharacterState($request, $requestId),

            $request->method === 'POST' && $path === '/api/character/state'
                => $this->saveCharacterState($request, $requestId),

            $request->method === 'GET' && $path === '/api/party'
                => $this->loadParty($request, $requestId),

            $request->method === 'POST' && $path === '/api/party'
                => $this->saveParty($request, $requestId),

            $request->method === 'GET' && $path === '/api/world/spawn-configuration'
                => $this->spawnConfiguration($request, $requestId),

            $request->method === 'GET' && $path === '/api/health'
                => Response::ok(['status' => 'ok']),

            default => Response::problem(
                ApiProblem::notFound('unknown_route', 'error.unknown_route'),
                $requestId
            ),
        };
    }

    // ---- authentication ------------------------------------------------------

    /**
     * Signs in and issues a session.
     *
     * The password reaches this method, is handed straight to the authenticator,
     * and is never stored, logged or echoed. It does not appear in the response,
     * in the idempotency record, or in the rate-limit row.
     */
    private function login(Request $request, string $requestId): Response
    {
        $requestId = $request->requireString('request_id', 64);
        $identifier = $request->requireString('login_identifier');
        $password = $request->requireString('password', 512);

        $versions = $request->nested('versions');
        $client = (string) ($versions['client'] ?? '');
        $protocol = (string) ($versions['protocol'] ?? '');
        $content = (string) ($versions['content'] ?? '');

        $outcome = $this->idempotency->once(
            $requestId,
            'login',
            null,
            function () use ($identifier, $password, $request, $client, $protocol, $content): array {
                $result = $this->authenticator->attempt(
                    $identifier,
                    $password,
                    $request->remoteAddress
                );

                if (!$result->succeeded) {
                    return [
                        'recordable' => false,
                        'response'   => ['__problem' => $this->problemFor($result->failure)],
                    ];
                }

                // A second live session is refused rather than silently replacing
                // the first: taking somebody's session away is a policy decision,
                // not a side effect of signing in again.
                if ($this->sessions->hasLiveSession($result->accountId)) {
                    return [
                        'recordable' => false,
                        'response'   => ['__problem' => ApiProblem::conflict(
                            'session_already_active',
                            'error.session.already_active'
                        )],
                    ];
                }

                $issued = $this->sessions->issue(
                    $result->accountId,
                    $client,
                    $protocol,
                    $content,
                    Env::getInt('SESSION_LIFETIME_SECONDS', 86400),
                    Env::getInt('SESSION_ID_BYTES', 32),
                    Env::getInt('SESSION_TOKEN_BYTES', 32)
                );

                $account = $this->accounts->findById($result->accountId);

                return [
                    'recordable' => true,
                    'response'   => [
                        'session_id'   => $issued['session_id'],
                        'token'        => $issued['token'],
                        'account_id'   => $result->accountId,
                        'display_name' => $account['display_name'] ?? '',
                        'expires_at'   => $issued['expires_at'],
                        'state'        => SessionRepository::AUTHENTICATED,
                        'revision'     => 0,
                    ],
                ];
            }
        );

        return $this->respond($outcome, $requestId);
    }

    // ---- directory -----------------------------------------------------------

    private function listServers(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $servers = array_map(
            fn (array $s): array => $this->presentServer($s, $session),
            $this->directory->listServers()
        );

        return Response::ok(['servers' => $servers]);
    }

    private function listChannels(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        // The server comes from the query, but only a server that exists produces
        // channels, and the channel rows are scoped by it in SQL.
        $serverId = (string) $request->query('server_id', (string) $session['selected_server_id']);

        if ($serverId === '') {
            return Response::problem(
                ApiProblem::validation('invalid_server_id', 'error.field_required'),
                $requestId
            );
        }

        $channels = array_map(
            static fn (array $c): array => [
                'channel_id' => $c['channel_id'],
                'server_id'  => $c['server_id'],
                'name_key'   => $c['name_key'],
                'status'     => $c['status'],
                'enabled'    => $c['enabled'],
                'capacity'   => $c['capacity'],
                'population' => $c['population'],
                'population_known' => $c['population_known'],
                'pk_enabled' => $c['pk_enabled'],
                'selectable' => $c['selectable'] && !$c['is_full'],
                'revision'   => $c['revision'],
            ],
            $this->directory->listChannels($serverId)
        );

        return Response::ok(['channels' => $channels]);
    }

    /**
     * This account's characters.
     *
     * The account is taken from the session, never from the request. There is no
     * `account_id` parameter to supply, which is why one cannot be forged.
     */
    private function listCharacters(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $serverId = (string) $request->query('server_id', (string) $session['selected_server_id']);

        if ($serverId === '') {
            return Response::problem(
                ApiProblem::validation('invalid_server_id', 'error.field_required'),
                $requestId
            );
        }

        $characters = $this->characters->listForAccount($session['account_id'], $serverId);

        return Response::ok(['characters' => $characters]);
    }

    // ---- selection -----------------------------------------------------------

    private function selectServer(Request $request, string $requestId): Response
    {
        return $this->selection($request, 'select_server', 'server_id',
            fn (array $session, string $id): array => $this->flow->selectServer($session, $id));
    }

    private function selectChannel(Request $request, string $requestId): Response
    {
        return $this->selection($request, 'select_channel', 'channel_id',
            fn (array $session, string $id): array => $this->flow->selectChannel($session, $id));
    }

    private function selectCharacter(Request $request, string $requestId): Response
    {
        return $this->selection($request, 'select_character', 'character_id',
            fn (array $session, string $id): array => $this->flow->selectCharacter($session, $id));
    }

    /**
     * The shape all three selections share.
     *
     * Written once because the three differ only in which field they read and
     * which service method they call. Three copies would be three places for the
     * idempotency handling or the session resolution to drift.
     *
     * @param callable(array<string,mixed>,string):array<string,mixed> $apply
     */
    private function selection(
        Request $request,
        string $scope,
        string $field,
        callable $apply
    ): Response {
        $requestId = $request->requireString('request_id', 64);
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $targetId = $request->requireString($field, 64);

        $outcome = $this->idempotency->once(
            $requestId,
            $scope,
            $session['account_id'],
            static function () use ($apply, $session, $targetId): array {
                $result = $apply($session, $targetId);

                if (!($result['ok'] ?? false)) {
                    return [
                        'recordable' => false,
                        'response'   => ['__problem' => $result['problem']],
                    ];
                }

                $updated = $result['session'];

                return [
                    'recordable' => true,
                    'response'   => [
                        'session_id'   => $updated['session_id'],
                        'state'        => $updated['state'],
                        'revision'     => $updated['revision'],
                        'server_id'    => $updated['selected_server_id'],
                        'channel_id'   => $updated['selected_channel_id'],
                        'character_id' => $updated['selected_character_id'],
                    ],
                ];
            }
        );

        return $this->respond($outcome, $requestId);
    }

    // ---- enter world ---------------------------------------------------------

    private function enterWorld(Request $request, string $requestId): Response
    {
        $requestId = $request->requireString('request_id', 64);
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $claims = [
            'account_id'   => $request->string('account_id'),
            'character_id' => $request->string('character_id'),
            'server_id'    => $request->string('server_id'),
            'channel_id'   => $request->string('channel_id'),
        ];

        $versionBlock = $request->nested('versions');
        $versions = [
            'client'   => (string) ($versionBlock['client'] ?? $session['client_version']),
            'protocol' => (string) ($versionBlock['protocol'] ?? $session['protocol_version']),
            'content'  => (string) ($versionBlock['content'] ?? $session['content_version']),
        ];

        $outcome = $this->idempotency->once(
            $requestId,
            'enter_world',
            $session['account_id'],
            function () use ($session, $claims, $versions): array {
                $result = $this->flow->enterWorld($session, $claims, $versions);

                if (!($result['ok'] ?? false)) {
                    return [
                        'recordable' => false,
                        'response'   => ['__problem' => $result['problem']],
                    ];
                }

                return ['recordable' => true, 'response' => $result['result']];
            }
        );

        return $this->respond($outcome, $requestId);
    }

    /**
     * Everything a world server needs to instantiate a character.
     *
     * **The character is the session's, not the request's.** There is no
     * `character_id` parameter: it is read from the session the bearer token
     * resolves to. A world server cannot ask for a character the player did not
     * select, and a player cannot ask for one they do not own, because neither has
     * anywhere to say which character they mean.
     *
     * Refused unless the session has actually reached the world. A character's full
     * state -- stats, skills, resources -- is not something character select is
     * entitled to, and a session that has not been authorised has no business
     * loading one.
     */
    /**
     * The party the session's own character belongs to.
     *
     * Scoped to the session's character, never to one the body names: a client that
     * asked about somebody else's party would be reading a membership list it has no
     * business seeing. An answer of "no party" and "that party is not yours" are the
     * same response for the same reason.
     */
    private function loadParty(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $characterId = (string) ($session['selected_character_id'] ?? '');

        if ($characterId === '') {
            return Response::problem(
                ApiProblem::notFound('unknown_character', 'error.character.unknown'),
                $requestId
            );
        }

        $party = $this->parties->loadByCharacter($characterId);

        return Response::ok($party ?? ['party_id' => '']);
    }

    /**
     * Party refusals that describe the request itself rather than a lost race.
     *
     * These are 422: the body is wrong and will still be wrong next time. Everything
     * else the repository can refuse -- a stale revision, a member another world
     * claimed first -- is a 409, because re-reading and retrying can fix it.
     */
    private const PARTY_MALFORMED = [
        'invalid_party',
        'invalid_loot_policy',
        'invalid_round_robin_cursor',
        'leader_not_a_member',
    ];

    /**
     * Writes a party back.
     *
     * The world server is the only thing that calls this, and it calls it with the whole
     * party as it now stands. The session's character must be in the member list, so a
     * client that reached this endpoint cannot write a party it does not belong to.
     */
    private function saveParty(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $characterId = (string) ($session['selected_character_id'] ?? '');

        $partyId = trim((string) $request->string('party_id'));

        if ($partyId === '') {
            return Response::problem(
                ApiProblem::validation('invalid_party', 'error.party.invalid'),
                $requestId
            );
        }

        $members = [];

        foreach ($request->nested('members') as $member) {
            $id = trim((string) $member);

            if ($id !== '') {
                $members[] = $id;
            }
        }

        // Disband arrives as an empty member list, and is the one shape that does not
        // require the caller to still be a member -- somebody has to be able to end a
        // party they have just left.
        if ($members !== [] && !in_array($characterId, $members, true)) {
            return Response::problem(
                ApiProblem::forbidden('not_a_member', 'error.party.not_a_member'),
                $requestId
            );
        }

        $result = $members === []
            ? $this->parties->disband($partyId)
            : $this->parties->save(
                $partyId,
                trim((string) $request->string('leader_character_id')),
                $request->int('loot_policy'),
                $members,
                // Zero means "this world has never read this party", not "I expect
                // revision zero" -- a freshly formed party has no revision to have read,
                // and a party id reused after a disband would otherwise look stale
                // forever. A caller that did read one sends it and is checked against it.
                $request->int('revision') > 0 ? $request->int('revision') : null,
                $request->int('round_robin_cursor')
            );

        if (!($result['ok'] ?? false)) {
            $reason = (string) ($result['reason'] ?? 'party_save_failed');

            // A refusal the caller can never satisfy by re-reading is not a conflict.
            // Reporting a malformed policy or cursor as 409 would tell the world it lost
            // a race and should try again, and it would try again forever.
            $problem = in_array($reason, self::PARTY_MALFORMED, true)
                ? ApiProblem::validation($reason, 'error.party.' . $reason)
                : ApiProblem::conflict($reason, 'error.party.' . $reason);

            return Response::problem($problem, $requestId);
        }

        return Response::ok([
            'party_id'  => $partyId,
            'revision'  => $result['revision'] ?? 0,
            'request_id' => $requestId,
        ]);
    }

    private function loadCharacterState(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $state = $this->flow->loadCharacterState($session);

        if (!($state['ok'] ?? false)) {
            return Response::problem($state['problem'], $requestId);
        }

        return Response::ok($state['result']);
    }

    /**
     * Writes a character back.
     *
     * The account and character both come from the session. The body carries only
     * what changed and the save revision the caller loaded -- so a client that
     * edited an account id into the payload changes nothing, and one that omitted
     * the revision is refused rather than allowed to overwrite blindly.
     */
    private function saveCharacterState(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $result = $this->flow->saveCharacterState($session, $request->nested('state'),
            $request->has('save_revision') ? $request->int('save_revision') : null);

        if (!($result['ok'] ?? false)) {
            return Response::problem($result['problem'], $requestId);
        }

        $body = $result['result'];
        $body['request_id'] = $requestId;

        return Response::ok($body);
    }

    /**
     * The monster spawn and AI configuration for one map.
     *
     * **Read-only, and deliberately unauthenticated.** A world server has no player
     * session and no credential of its own -- by design, since Phase 16 kept every
     * secret out of Unity -- so it cannot present a bearer token for its own startup
     * read. The alternatives were to invent a server credential and ship it with the
     * build, or to make this endpoint readable.
     *
     * Readable is the safer of the two. Spawn configuration is level design, not a
     * secret: any player learns where the monsters are by walking the map. A
     * credential in a client build, by contrast, is a credential in every player's
     * hands. This carries no account data, no character data and no token, and there
     * is no write path -- a client that calls it learns where monsters spawn, which
     * it was going to find out anyway, and can change nothing.
     *
     * Rejected rows are reported alongside the accepted ones rather than dropped, so
     * a misconfigured nest appears in an operator's log instead of silently failing
     * to populate.
     */
    private function spawnConfiguration(Request $request, string $requestId): Response
    {
        $mapId = (string) $request->query('map_id', '');

        if ($mapId === '') {
            return Response::problem(
                ApiProblem::validation('invalid_map_id', 'error.field_required'),
                $requestId
            );
        }

        $spawns = $this->monsterSpawns->loadSpawnPoints($mapId);
        $ai = $this->monsterSpawns->loadAiConfiguration();

        return Response::ok([
            'map_id'         => $mapId,
            'spawn_points'   => $spawns['points'],
            'ai_configurations' => $ai['configurations'],
            'rejected_spawn_points' => $spawns['rejected'],
            'rejected_ai_configurations' => $ai['rejected'],
        ]);
    }

    /**
     * What the session behind this token actually is.
     *
     * The call a dedicated game server makes about a connecting client. Everything
     * spoofable -- account, character, server, channel -- comes from the session row
     * rather than from the connecting client, which is why spoofing them is not
     * something this has to detect.
     */
    private function describeSession(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        return Response::ok($this->flow->describe($session)['result']);
    }

    /**
     * The world server reporting that the character is in.
     *
     * Not idempotency-keyed: an already-Active session answers success, so a retry is
     * harmless without consuming a request key. The revision guard is what stops two
     * concurrent callers both advancing the session.
     */
    private function worldReady(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            return Response::problem($session, $requestId);
        }

        $result = $this->flow->completeWorldEntry($session);

        if (!($result['ok'] ?? false)) {
            return Response::problem($result['problem'], $requestId);
        }

        $body = $result['result'];
        $body['request_id'] = $requestId;

        return Response::ok($body);
    }

    /**
     * Hands a session back.
     *
     * Deliberately not idempotency-keyed. The store records a committed outcome so a
     * retry replays it, but releasing is already idempotent by its own nature and
     * recording it would mean a disconnect callback firing twice consumed a request
     * key for nothing. Retrying this call is safe because the second one finds the
     * work done, not because a store remembered the first.
     */
    private function release(Request $request, string $requestId): Response
    {
        $session = $this->requireSession($request);

        if ($session instanceof ApiProblem) {
            // A token that is already invalid, expired or revoked means the session is
            // gone, which is the outcome the caller wanted. Reporting a failure would
            // make a client retry something that has already happened.
            return Response::ok([
                'session_ended'      => false,
                'character_released' => false,
                'request_id'         => $requestId,
            ]);
        }

        $result = $this->flow->release($session);

        $body = $result['result'];
        $body['request_id'] = $requestId;

        return Response::ok($body);
    }

    // ---- helpers -------------------------------------------------------------

    /**
     * The session behind the bearer token, or the problem explaining why not.
     *
     * Returns a union rather than throwing so each handler decides how to report
     * it, and so the type makes it impossible to forget the failure case.
     *
     * @return array<string,mixed>|ApiProblem
     */
    private function requireSession(Request $request): array|ApiProblem
    {
        $outcome = $this->flow->authorize($request->bearerToken());

        return $outcome['session'] ?? $outcome['problem'];
    }

    /**
     * @param array{response:array<string,mixed>,replayed:bool} $outcome
     */
    private function respond(array $outcome, string $requestId): Response
    {
        $body = $outcome['response'];

        if (isset($body['__problem']) && $body['__problem'] instanceof ApiProblem) {
            return Response::problem($body['__problem'], $requestId);
        }

        // A replay is reported so a client can tell "it worked" from "it had
        // already worked" -- the same outcome for a player, different information
        // for whoever is debugging.
        $body['replayed'] = $outcome['replayed'];
        $body['request_id'] = $requestId;

        return Response::ok($body);
    }

    /** @param array<string,mixed> $session */
    private function presentServer(array $server, array $session): array
    {
        $evaluation = VersionPolicy::evaluate(
            $session['client_version'],
            $session['protocol_version'],
            $session['content_version'],
            $server['versions']
        );

        return [
            'server_id'  => $server['server_id'],
            'name_key'   => $server['name_key'],
            'region'     => $server['region'],
            'status'     => $server['status'],
            'enabled'    => $server['enabled'],
            'capacity'   => $server['capacity'],
            'population' => $server['population'],
            'population_known' => $server['population_known'],
            'revision'   => $server['revision'],
            'compatibility' => $evaluation['compatibility'],
            'selectable' => $server['selectable']
                && !$server['is_full']
                && VersionPolicy::isPlayable($evaluation),
        ];
    }

    private function problemFor(?AuthFailure $failure): ApiProblem
    {
        return match ($failure) {
            AuthFailure::RateLimited       => ApiProblem::rateLimited(),
            AuthFailure::AccountDisabled   => ApiProblem::forbidden('account_disabled', 'error.account.disabled'),
            AuthFailure::AccountBanned     => ApiProblem::forbidden('account_banned', 'error.account.banned'),
            AuthFailure::AccountSuspended  => ApiProblem::forbidden('account_suspended', 'error.account.suspended'),
            default                        => ApiProblem::unauthenticated(
                'invalid_credentials',
                'error.auth.invalid_credentials'
            ),
        };
    }
}
