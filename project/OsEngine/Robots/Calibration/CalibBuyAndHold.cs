using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

/* Description
Calibration robot. Not a strategy -- a measuring instrument.

Buys once on the first finished candle and closes on a date given as a
parameter. The result is fixed by arithmetic before the tester is asked:
the price move times the volume, minus exactly one round trip of costs.

Any disagreement is worth reading twice. It is usually not the engine but the
price series -- an unadjusted dividend or split puts a gap in it that no trade
caused, and that gap lands here in full.

The exit is a date rather than "the last candle" because a robot inside a
backtest is never told which candle is last. Leaving the position open would be
worse than wrong: the replay refuses to value an open position, so the run would
report nothing at all rather than something suspicious.
*/

namespace OsEngine.Robots.Calibration
{
    [Bot("CalibBuyAndHold")]
    public class CalibBuyAndHold : BotPanel
    {
        private BotTabSimple _tab;

        private StrategyParameterString _regime;
        private StrategyParameterDecimal _volume;
        private StrategyParameterString _exitDate;

        private bool _entered;
        private bool _exited;

        public CalibBuyAndHold(string name, StartProgram startProgram)
            : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            _regime = CreateParameter("Regime", "On", new[] { "On", "Off" });
            _volume = CreateParameter("Volume", 1m, 1m, 100m, 1m);
            _exitDate = CreateParameter("Exit date yyyy-MM-dd", "2026-06-30", new[] { "2026-06-30" });

            _tab.CandleFinishedEvent += CandleFinished;

            Description = "Calibration: buy on the first candle, close on a fixed date.";
        }

        public override string GetNameStrategyType()
        {
            return "CalibBuyAndHold";
        }

        public override void ShowIndividualSettingsDialog()
        {
        }

        private void CandleFinished(List<Candle> candles)
        {
            if (_regime.ValueString == "Off" || _exited)
            {
                return;
            }

            if (candles == null || candles.Count == 0)
            {
                return;
            }

            DateTime now = candles[candles.Count - 1].TimeStart.Date;

            if (_entered == false)
            {
                _tab.BuyAtMarket(_volume.ValueDecimal);
                _entered = true;
                return;
            }

            DateTime exit;
            if (DateTime.TryParse(_exitDate.ValueString, out exit) == false)
            {
                return;
            }

            if (now >= exit.Date && _tab.PositionsOpenAll.Count > 0)
            {
                _tab.CloseAllAtMarket();
                _exited = true;
            }
        }
    }
}
