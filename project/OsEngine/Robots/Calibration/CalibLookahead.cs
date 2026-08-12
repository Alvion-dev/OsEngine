using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

/* Description
Calibration robot. Not a strategy -- a measuring instrument, and a deliberately
broken one.

It buys when tomorrow closes higher than today and sells when it closes lower.
That is impossible, and the point is to see what impossible looks like in this
engine's own statistics. Without that yardstick there is nothing to compare a
suspiciously good result against later.

Getting the future in took a detour worth recording. OsEngine hands a robot only
the candles up to now: CandleFinishedEvent receives a List<Candle> that ends at
the present one, and there is no way to index past it. The commonest form of
lookahead is therefore not available through the API at all -- which is itself a
finding about the engine, made before any test was run.

So the future is smuggled in over two passes:

  Regime = Record  -- trades nothing, writes the closes it sees to a file.
  Regime = Cheat   -- reads that file back and trades knowing what comes next.

Two passes rather than reading OsEngine's own data files, because that would
require knowing their format and would break whenever the format changed. The
robot writes what it needs itself, in the shape it needs.

If the Cheat pass does NOT produce absurd numbers, something is wrong with the
experiment or with the engine, and either way it has to be understood before any
ordinary strategy result from this engine is believed.
*/

namespace OsEngine.Robots.Calibration
{
    [Bot("CalibLookahead")]
    public class CalibLookahead : BotPanel
    {
        private BotTabSimple _tab;

        private StrategyParameterString _regime;
        private StrategyParameterDecimal _volume;
        private StrategyParameterString _fileName;

        private readonly Dictionary<DateTime, decimal> _future = new Dictionary<DateTime, decimal>();
        private readonly List<string> _recorded = new List<string>();
        private bool _futureLoaded;

        public CalibLookahead(string name, StartProgram startProgram)
            : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            _regime = CreateParameter("Regime", "Record", new[] { "Record", "Cheat", "Off" });
            _volume = CreateParameter("Volume", 1m, 1m, 100m, 1m);
            _fileName = CreateParameter("File", "calib-future.csv", new[] { "calib-future.csv" });

            _tab.CandleFinishedEvent += CandleFinished;

            Description = "Calibration: pass one records the future, pass two trades on it.";
        }

        public override string GetNameStrategyType()
        {
            return "CalibLookahead";
        }

        public override void ShowIndividualSettingsDialog()
        {
        }

        private string FilePath
        {
            get { return Path.Combine(Directory.GetCurrentDirectory(), _fileName.ValueString); }
        }

        private void CandleFinished(List<Candle> candles)
        {
            if (_regime.ValueString == "Off" || candles == null || candles.Count == 0)
            {
                return;
            }

            Candle current = candles[candles.Count - 1];

            if (_regime.ValueString == "Record")
            {
                Record(current);
                return;
            }

            Cheat(current);
        }

        private void Record(Candle current)
        {
            _recorded.Add(
                current.TimeStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + ";"
                + current.Close.ToString(CultureInfo.InvariantCulture));

            // Written on every candle rather than at the end: a backtest gives
            // no "we are finished" moment, and a file that is only flushed on
            // shutdown is a file that is sometimes empty.
            try
            {
                File.WriteAllLines(FilePath, _recorded);
            }
            catch (Exception error)
            {
                _tab.SetNewLogMessage("Calibration: cannot write " + FilePath + " -- " + error.Message,
                    Logging.LogMessageType.Error);
            }
        }

        private void Cheat(Candle current)
        {
            LoadFutureOnce();

            if (_future.Count == 0)
            {
                return;
            }

            decimal tomorrow;
            if (_future.TryGetValue(current.TimeStart, out tomorrow) == false)
            {
                return;
            }

            if (_tab.PositionsOpenAll.Count > 0)
            {
                _tab.CloseAllAtMarket();
                return;
            }

            if (tomorrow > current.Close)
            {
                _tab.BuyAtMarket(_volume.ValueDecimal);
            }
            else if (tomorrow < current.Close)
            {
                _tab.SellAtMarket(_volume.ValueDecimal);
            }
        }

        private void LoadFutureOnce()
        {
            if (_futureLoaded)
            {
                return;
            }
            _futureLoaded = true;

            if (File.Exists(FilePath) == false)
            {
                _tab.SetNewLogMessage("Calibration: " + FilePath + " missing -- run Record first",
                    Logging.LogMessageType.Error);
                return;
            }

            string[] lines = File.ReadAllLines(FilePath);
            var times = new List<DateTime>();
            var closes = new List<decimal>();

            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(';');
                if (parts.Length != 2)
                {
                    continue;
                }
                DateTime time;
                decimal close;
                if (DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                    && decimal.TryParse(parts[1], NumberStyles.Any,
                        CultureInfo.InvariantCulture, out close))
                {
                    times.Add(time);
                    closes.Add(close);
                }
            }

            // Each candle is mapped to the NEXT candle's close. That mapping is
            // the whole cheat, and it is built here rather than at lookup time
            // so a missing tomorrow simply has no entry instead of quietly
            // returning today's own close.
            for (int i = 0; i + 1 < times.Count; i++)
            {
                _future[times[i]] = closes[i + 1];
            }

            _tab.SetNewLogMessage("Calibration: loaded " + _future.Count + " future closes",
                Logging.LogMessageType.System);
        }
    }
}
