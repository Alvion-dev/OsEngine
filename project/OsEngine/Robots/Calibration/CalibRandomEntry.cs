using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

/* Description
Calibration robot. Not a strategy -- a measuring instrument.

Enters on a coin flip and leaves after a fixed number of candles. It has no
edge by construction, so over many trades its result must be the cost of the
turnover and nothing else. An engine that reports a profit here is not charging
what its settings say it charges.

Two details decide whether the measurement means anything.

The seed is a parameter, so a run repeats exactly. Without it two runs give two
numbers and there is nothing to compare. The generator is built on the first
candle rather than in the constructor: parameters are set over the API after the
robot exists, so a generator built at construction time would always use the
default and the parameter would be decoration.

The trade count has to be in the hundreds. With a dozen trades the costs drown
in the price noise and the check says nothing either way -- which is worse than
failing, because it looks like a pass.

Even hundreds may not be enough to state the statistical claim sharply. On daily
candles a one-day hold carries about two percent of price noise, so the average
over N trades is uncertain by roughly 2%/sqrt(N) -- at 300 trades that is about
0.11%, and the cost being measured is 0.1%. The exact claim is therefore checked
per trade -- every position must be charged (entry + close) * rate -- and the
statistical one is reported with its error band rather than asserted.
*/

namespace OsEngine.Robots.Calibration
{
    [Bot("CalibRandomEntry")]
    public class CalibRandomEntry : BotPanel
    {
        private BotTabSimple _tab;

        private StrategyParameterString _regime;
        private StrategyParameterDecimal _volume;
        private StrategyParameterInt _seed;
        private StrategyParameterInt _holdCandles;
        private StrategyParameterInt _entryEveryCandles;
        private StrategyParameterString _closeOnce;

        private Random _random;
        private int _candlesSeen;
        private int _candleOfEntry;
        private bool _closeAsked;

        public CalibRandomEntry(string name, StartProgram startProgram)
            : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            _regime = CreateParameter("Regime", "On", new[] { "On", "Off" });
            _volume = CreateParameter("Volume", 1m, 1m, 100m, 1m);
            _seed = CreateParameter("Seed", 20260812, 1, 99999999, 1);
            _holdCandles = CreateParameter("Hold candles", 3, 1, 50, 1);
            _entryEveryCandles = CreateParameter("Try entry every N candles", 5, 1, 50, 1);

            // An experiment, not a strategy choice. With "No" the robot asks to
            // close on every bar while a position is open, which is the obvious
            // way to write it and what every earlier run did. With "Yes" it asks
            // exactly once per position.
            //
            // If that single difference makes positions close on continuous
            // minute bars, then the tester was never refusing the order -- the
            // order was being replaced faster than it could be matched, and
            // TesterServer.cs:1331 (`order.TimeCreate >= lastCandle.TimeStart`)
            // rejected it every time because TimeCreate had just been reset to
            // the current bar.
            _closeOnce = CreateParameter("Ask to close once", "Yes", new[] { "Yes", "No" });

            _tab.CandleFinishedEvent += CandleFinished;

            Description = "Calibration: coin-flip entries, fixed holding time. Must lose the costs.";
        }

        public override string GetNameStrategyType()
        {
            return "CalibRandomEntry";
        }

        public override void ShowIndividualSettingsDialog()
        {
        }

        private void CandleFinished(List<Candle> candles)
        {
            if (_regime.ValueString == "Off")
            {
                return;
            }

            if (candles == null || candles.Count == 0)
            {
                return;
            }

            if (_random == null)
            {
                _random = new Random(_seed.ValueInt);
            }

            _candlesSeen++;

            if (_tab.PositionsOpenAll.Count > 0)
            {
                if (_candlesSeen - _candleOfEntry < _holdCandles.ValueInt)
                {
                    return;
                }

                if (_closeOnce.ValueString == "No")
                {
                    // The obvious way to write an exit, and the way that does
                    // not work. Kept so the defect can be reproduced on demand.
                    _tab.CloseAllAtMarket();
                    return;
                }

                if (_closeAsked)
                {
                    return;
                }

                // Two rules this engine enforces silently, both established by
                // instrumenting it rather than by reading it -- see ENGINES.md.
                //
                // A position that merely exists is not yet a position that can
                // be closed. Inside one tester step the robot runs BEFORE order
                // matching, so on the bar after the entry the position is still
                // Opening with no volume, and CloseAtMarket returns without a
                // word.
                //
                // And a second request destroys the first order and files a new
                // one, which the tester rejects on the bar it was created. Ask
                // once, then wait.
                Position position = _tab.PositionsOpenAll[0];

                if (position.State != PositionStateType.Open
                    || position.OpenVolume <= 0)
                {
                    return;
                }

                _tab.CloseAllAtMarket();
                _closeAsked = true;
                return;
            }

            if (_candlesSeen % _entryEveryCandles.ValueInt != 0)
            {
                return;
            }

            // The coin is drawn every time an entry is possible, not only when
            // it comes up heads. Drawing conditionally would let the sequence
            // depend on the price path and stop being reproducible.
            bool goLong = _random.Next(2) == 0;

            if (goLong)
            {
                _tab.BuyAtMarket(_volume.ValueDecimal);
            }
            else
            {
                _tab.SellAtMarket(_volume.ValueDecimal);
            }

            _candleOfEntry = _candlesSeen;
            _closeAsked = false;
        }
    }
}
