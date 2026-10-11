using System;
using System.Collections.Generic;

namespace LanternLounge.Core
{
    /// <summary>
    /// The contract every game implements. The lounge, rooms and the server's match host only
    /// ever talk to a game through this interface, so adding a game never touches another one.
    ///
    /// Seats are numbered 0 .. PlayerCount-1. States must be treated as immutable: Apply returns
    /// a new state and never changes the one passed in.
    ///
    /// Most games should derive from <see cref="GameModule{TState,TAction,TView}"/> rather than
    /// implementing this untyped interface directly.
    /// </summary>
    public interface IGameModule
    {
        GameInfo Info { get; }

        Type StateType { get; }
        Type ActionType { get; }
        Type ViewType { get; }

        /// <summary>Builds the starting state: deal, shuffle, pick who goes first.</summary>
        object Setup(MatchConfig config);

        /// <summary>Is this action legal for this seat right now? Never throws for bad input.</summary>
        ValidationResult Validate(object state, int seat, object action);

        /// <summary>Validates, then returns the next state. Throws if the action is illegal.</summary>
        object Apply(object state, int seat, object action);

        /// <summary>
        /// What one seat is allowed to see. The server sends only this to each player, so hidden
        /// information (hands, fleets, the deck order) must be removed here.
        /// </summary>
        object ViewFor(object state, int seat);

        MatchResult Result(object state);

        /// <summary>Seats that may act now. Empty when the match is over.</summary>
        IReadOnlyList<int> SeatsToAct(object state);

        /// <summary>The action the server takes for a seat whose turn timer ran out, if the game has one.</summary>
        bool TryGetTimeoutAction(object state, int seat, out object action);
    }
}
