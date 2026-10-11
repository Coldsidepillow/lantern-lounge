using System;
using System.Collections.Generic;

namespace LanternLounge.Core
{
    /// <summary>One move as it was played.</summary>
    public readonly struct PlayedMove<TAction>
    {
        public PlayedMove(int seat, TAction action)
        {
            Seat = seat;
            Action = action;
        }

        public int Seat { get; }
        public TAction Action { get; }
    }

    /// <summary>
    /// Runs one match on this device: holds the current state, refuses illegal moves, and keeps
    /// the move history. The client's screens drive a game only through this, so the same flow
    /// can later be swapped for an online match without touching the game's rules.
    /// </summary>
    public sealed class LocalMatch<TState, TAction, TView>
        where TState : class
    {
        private readonly GameModule<TState, TAction, TView> module;
        private readonly List<PlayedMove<TAction>> history = new List<PlayedMove<TAction>>();

        public LocalMatch(GameModule<TState, TAction, TView> module, MatchConfig config)
        {
            this.module = module ?? throw new ArgumentNullException(nameof(module));
            State = module.Setup(config);
        }

        /// <summary>Raised after every accepted move and after <see cref="Reset"/>.</summary>
        public event Action<TState> Changed;

        public TState State { get; private set; }

        public IReadOnlyList<PlayedMove<TAction>> History => history;

        public MatchResult Result => module.Result(State);

        public bool IsOver => module.Result(State).IsOver;

        public IReadOnlyList<int> SeatsToAct => module.SeatsToAct(State);

        /// <summary>True if this seat may move right now.</summary>
        public bool CanAct(int seat)
        {
            var seats = module.SeatsToAct(State);
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i] == seat) return true;
            }
            return false;
        }

        /// <summary>Plays the move if it is legal. On failure the state is unchanged.</summary>
        public ValidationResult TryPlay(int seat, TAction action)
        {
            if (!module.TryApply(State, seat, action, out var next, out var check)) return check;
            State = next;
            history.Add(new PlayedMove<TAction>(seat, action));
            Changed?.Invoke(State);
            return check;
        }

        /// <summary>Starts over from a given state, such as a rematch board.</summary>
        public void Reset(TState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            history.Clear();
            Changed?.Invoke(State);
        }
    }
}
