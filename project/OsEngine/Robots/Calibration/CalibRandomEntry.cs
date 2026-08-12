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
numbers and there is nothing to compare.

The trade count has to be in the hundreds. With a dozen trades the costs drown
in the price noise and the check says nothing either way -- which is worse than
failing, because it looks like a pass.
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

        private Random _random;
        private int _candlesSeen;
        private int _candleOfEntry;

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

            _random = new Random(_seed.ValueInt);

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

            _candlesSeen++;

            if (_tab.PositionsOpenAll.Count > 0)
            {
                if (_candlesSeen - _candleOfEntry >= _holdCandles.ValueInt)
                {
                    _tab.CloseAllAtMarket();
                }
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
        }
    }
}
