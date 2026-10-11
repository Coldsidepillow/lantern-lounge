using System;
using LanternLounge.Core;

namespace LanternLounge.Games.TreehouseHoldem
{
    /// <summary>
    /// A friendly computer player. It decides only from its own <see cref="HoldemView"/> (the same
    /// things a player can see) by estimating how often its hand would win, with a little
    /// randomness so it isn't predictable. It never presses "Next round".
    /// </summary>
    public sealed class HoldemBot
    {
        private readonly SeededRandom random;
        private readonly int trials;

        /// <param name="boldness">0 = careful, 1 = bold. Bold bots raise and call more often.</param>
        public HoldemBot(ulong seed, float boldness = 0.5f, int trials = 160)
        {
            random = new SeededRandom(seed);
            Boldness = Math.Max(0f, Math.Min(1f, boldness));
            this.trials = Math.Max(20, trials);
        }

        public float Boldness { get; }

        public HoldemMove ChooseMove(HoldemView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!view.IsMyTurn) throw new InvalidOperationException("It isn't the bot's turn.");

            int opponents = Math.Max(1, view.LiveOpponents);
            float chance = HoldemOdds.WinChance(view.MyCards, view.Board, opponents, random, trials);
            // How much better than an even share this hand is: 1 = average, 2 = twice as likely as average.
            float edge = chance * (opponents + 1);
            float wobble = (random.NextInt(1000) / 1000f - 0.5f) * 0.3f;
            float bold = Boldness - 0.5f;

            if (view.CostToCall == 0)
            {
                if (view.CanRaise && edge + wobble + bold * 0.4f > 1.45f) return HoldemMove.Raise;
                return HoldemMove.Check;
            }

            float price = view.CostToCall / (float)(view.Pot + view.CostToCall);
            if (view.CanRaise && edge + wobble + bold * 0.4f > 1.8f) return HoldemMove.Raise;
            if (chance + wobble * 0.3f + bold * 0.1f >= price * 0.9f || view.CostToCall <= HoldemModule.Ante) return HoldemMove.Call;
            return HoldemMove.Fold;
        }
    }
}
