using System;
using System.Collections.Generic;

namespace LanternLounge.Core
{
    /// <summary>
    /// Typed base class for games. Implement the protected and abstract members; the untyped
    /// <see cref="IGameModule"/> members are handled here, including type checks, so a wrong
    /// state or action type is reported instead of crashing the match host.
    /// </summary>
    public abstract class GameModule<TState, TAction, TView> : IGameModule
        where TState : class
    {
        public abstract GameInfo Info { get; }

        public Type StateType => typeof(TState);
        public Type ActionType => typeof(TAction);
        public Type ViewType => typeof(TView);

        /// <summary>Checks the player count against <see cref="Info"/>, then builds the starting state.</summary>
        public TState Setup(MatchConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!Info.SupportsPlayerCount(config.PlayerCount))
            {
                throw new ArgumentException(
                    $"{Info.DisplayName} supports {Info.MinPlayers}-{Info.MaxPlayers} players, not {config.PlayerCount}.",
                    nameof(config));
            }
            return CreateInitialState(config);
        }

        protected abstract TState CreateInitialState(MatchConfig config);

        public abstract ValidationResult Validate(TState state, int seat, TAction action);

        /// <summary>Called only after <see cref="Validate"/> has passed.</summary>
        protected abstract TState ApplyValidated(TState state, int seat, TAction action);

        public TState Apply(TState state, int seat, TAction action)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var check = Validate(state, seat, action);
            if (!check.Ok) throw new InvalidOperationException($"Illegal action ({check.Code}): {check.Message}");
            return ApplyValidated(state, seat, action);
        }

        /// <summary>Applies the action if it is legal. Returns false and leaves newState null otherwise.</summary>
        public bool TryApply(TState state, int seat, TAction action, out TState newState, out ValidationResult check)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            check = Validate(state, seat, action);
            newState = check.Ok ? ApplyValidated(state, seat, action) : null;
            return check.Ok;
        }

        public abstract TView ViewFor(TState state, int seat);
        public abstract MatchResult Result(TState state);
        public abstract IReadOnlyList<int> SeatsToAct(TState state);

        public virtual bool TryGetTimeoutAction(TState state, int seat, out TAction action)
        {
            action = default;
            return false;
        }

        // ---- Untyped bridge used by the registry, rooms and the server ----

        object IGameModule.Setup(MatchConfig config) => Setup(config);

        ValidationResult IGameModule.Validate(object state, int seat, object action)
        {
            if (!(state is TState typedState))
                return ValidationResult.Fail(ErrorCodes.InvalidAction, "That state does not belong to this game.");
            if (!(action is TAction typedAction))
                return ValidationResult.Fail(ErrorCodes.InvalidAction, "That move does not belong to this game.");
            return Validate(typedState, seat, typedAction);
        }

        object IGameModule.Apply(object state, int seat, object action) => Apply(CastState(state), seat, CastAction(action));

        object IGameModule.ViewFor(object state, int seat) => ViewFor(CastState(state), seat);

        MatchResult IGameModule.Result(object state) => Result(CastState(state));

        IReadOnlyList<int> IGameModule.SeatsToAct(object state) => SeatsToAct(CastState(state));

        bool IGameModule.TryGetTimeoutAction(object state, int seat, out object action)
        {
            bool has = TryGetTimeoutAction(CastState(state), seat, out var typed);
            action = has ? (object)typed : null;
            return has;
        }

        private TState CastState(object state)
        {
            if (state is TState typed) return typed;
            throw new ArgumentException($"Expected a {typeof(TState).Name} for {Info.DisplayName}.", nameof(state));
        }

        private static TAction CastAction(object action)
        {
            if (action is TAction typed) return typed;
            throw new ArgumentException($"Expected a {typeof(TAction).Name}.", nameof(action));
        }
    }
}
