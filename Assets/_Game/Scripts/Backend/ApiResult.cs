namespace ChibiFantasy.Backend
{
    /// <summary>Why a call to the account authority did not return data.</summary>
    /// <remarks>
    /// <b>Transport failures, not domain answers.</b> "The account is banned" is a
    /// <c>LoginRejection</c> the authority deliberately returned; "the authority could not be
    /// reached" is this. Keeping them apart is what stops a network outage being reported to
    /// a player as a ban, and what lets a client retry one and not the other.
    /// </remarks>
    public enum ApiErrorKind
    {
        None = 0,

        /// <summary>Could not reach the authority at all.</summary>
        Unreachable = 1,

        /// <summary>Reached it, and it took too long.</summary>
        Timeout = 2,

        /// <summary>The caller is not permitted to ask.</summary>
        Unauthorized = 3,

        /// <summary>The authority refused the shape of the request.</summary>
        BadRequest = 4,

        /// <summary>The authority failed on its own account.</summary>
        ServerError = 5,

        /// <summary>Asked too often.</summary>
        RateLimited = 6,

        /// <summary>The reply arrived and could not be understood.</summary>
        MalformedResponse = 7,

        /// <summary>The caller gave up first.</summary>
        Cancelled = 8,

        Unknown = 9
    }

    /// <summary>
    /// A transport failure, described without naming a transport.
    /// </summary>
    /// <remarks>
    /// No status code, no header, no URL and no exception. Those are HTTP's vocabulary, and
    /// putting them here would make every caller of this interface an HTTP caller -- which is
    /// exactly what a transport-neutral seam exists to avoid. An implementation maps its own
    /// failures onto these kinds.
    ///
    /// <see cref="Detail"/> is for a log, never for a player and never parsed.
    /// </remarks>
    public readonly struct ApiError
    {
        public ApiError(ApiErrorKind kind, string detail = null)
        {
            Kind = kind;
            Detail = detail;
        }

        public ApiErrorKind Kind { get; }

        /// <summary>Diagnostic text. Not shown to a player and not branched on.</summary>
        public string Detail { get; }

        public bool IsError => Kind != ApiErrorKind.None;

        /// <summary>Whether trying again could plausibly work.</summary>
        /// <remarks>What a client uses to decide between a retry button and a dead end.</remarks>
        public bool IsTransient => Kind == ApiErrorKind.Unreachable
            || Kind == ApiErrorKind.Timeout
            || Kind == ApiErrorKind.ServerError;

        public static ApiError None => default;

        public override string ToString()
        {
            return IsError ? Kind + (Detail == null ? string.Empty : ": " + Detail) : "ok";
        }
    }

    /// <summary>
    /// What an authority call returned, or why it did not.
    /// </summary>
    /// <remarks>
    /// <b>Two layers, kept apart.</b> This says whether the authority answered. What it
    /// answered -- accepted, banned, full, out of date -- is the domain result inside
    /// <see cref="Value"/>. A call can succeed at this layer and still carry a refusal, and a
    /// caller that conflated the two would show "connection failed" to a banned player.
    /// </remarks>
    public readonly struct ApiResult<T>
    {
        private ApiResult(bool ok, T value, ApiError error)
        {
            IsOk = ok;
            Value = value;
            Error = error;
        }

        public bool IsOk { get; }

        /// <summary>What came back. Meaningless unless <see cref="IsOk"/>.</summary>
        public T Value { get; }

        public ApiError Error { get; }

        public static ApiResult<T> Ok(T value)
        {
            return new ApiResult<T>(true, value, ApiError.None);
        }

        public static ApiResult<T> Failed(ApiErrorKind kind, string detail = null)
        {
            return new ApiResult<T>(false, default, new ApiError(kind, detail));
        }

        public static ApiResult<T> Failed(ApiError error)
        {
            return new ApiResult<T>(false, default, error);
        }

        public override string ToString()
        {
            return IsOk ? "ok" : "failed: " + Error;
        }
    }
}
