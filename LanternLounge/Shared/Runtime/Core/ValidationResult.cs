namespace LanternLounge.Core
{
    /// <summary>Error codes every game shares. Games add their own (e.g. "column_full").</summary>
    public static class ErrorCodes
    {
        public const string GameOver = "game_over";
        public const string NotYourTurn = "not_your_turn";
        public const string BadSeat = "bad_seat";
        public const string InvalidAction = "invalid_action";
    }

    /// <summary>
    /// The answer to "is this move legal?". Code is for programs (and tests); Message is plain
    /// language the client can show to the player.
    /// </summary>
    public readonly struct ValidationResult
    {
        private ValidationResult(bool ok, string code, string message)
        {
            Ok = ok;
            Code = code;
            Message = message;
        }

        public bool Ok { get; }
        public string Code { get; }
        public string Message { get; }

        public static ValidationResult Success => new ValidationResult(true, null, null);

        public static ValidationResult Fail(string code, string message) => new ValidationResult(false, code, message);

        public override string ToString() => Ok ? "ok" : $"{Code}: {Message}";
    }
}
