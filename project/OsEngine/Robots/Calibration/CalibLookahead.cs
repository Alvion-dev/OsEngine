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

It trades knowing the future, which is impossible, so that we can see what
impossible looks like in this engine's own statistics. Without that yardstick
there is nothing to compare a suspiciously good result against later.

Getting the future in took a detour worth recording. OsEngine hands a robot only
the candles up to now: CandleFinishedEvent receives a List<Candle> that ends at
the present one, and there is no way to index past it. The commonest form of
lookahead is therefore not available through the API at all -- which is itself a
finding about the engine, made before any test was run.

So the future is smuggled in over two passes:

  Regime = Record  -- trades nothing, writes the opens it sees to a file.
  Regime = Cheat   -- reads that file back and trades knowing what comes next.

Two passes rather than reading OsEngine's own data files, because that would
require knowing their format and would break whenever the format changed. The
robot writes what it needs itself, in the shape it needs.

It cheats on OPENS, two candles ahead, and that detail is the whole exercise.
An order placed when candle N finishes fills at the open of candle N+1 -- the
engine emits open, high, low and close as trades and only then raises the
candle-finished event, so the current candle's prices are already spent. This
robot closes on the next candle it sees, which fills at the open of N+2.
So what a long trade actually earns is open[N+2] - open[N+1], and that is the
number it has to know in advance.

Peeking at closes instead -- the obvious thing to write -- would foresee a
quantity the robot never trades. It would still make money, because closes and
opens move together, but it would win maybe seven trades in ten. Reading that as
"the engine hides the future from a robot that is openly cheating" would be
exactly backwards, and would have quietly weakened every later check that leans
on this yardstick.

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

        // candle time -> the two opens that a trade opened on that candle will
        // actually be filled at: entry at the first, exit at the second.
        private readonly Dictionary<DateTime, decimal[]> _future = new Dictionary<DateTime, decimal[]>();
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
                + current.Open.ToString(CultureInfo.InvariantCulture));

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

            // Close first and return: the position opened on the previous candle
            // is exited here, and its exit is the second of the two opens that
            // were looked up when it was opened.
            if (_tab.PositionsOpenAll.Count > 0)
            {
                _tab.CloseAllAtMarket();
                return;
            }

            decimal[] fills;
            if (_future.TryGetValue(current.TimeStart, out fills) == false)
            {
                return;
            }

            decimal entry = fills[0];
            decimal exit = fills[1];

            if (exit > entry)
            {
                _tab.BuyAtMarket(_volume.ValueDecimal);
            }
            else if (exit < entry)
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
            var opens = new List<decimal>();

            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(';');
                if (parts.Length != 2)
                {
                    continue;
                }
                DateTime time;
                decimal open;
                if (DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                    && decimal.TryParse(parts[1], NumberStyles.Any,
                        CultureInfo.InvariantCulture, out open))
                {
                    times.Add(time);
                    opens.Add(open);
                }
            }

            // Each candle is mapped to the NEXT TWO opens -- where an order
            // placed now would be filled, and where it would be closed. The
            // mapping is built here rather than at lookup time so a candle
            // without two candles after it simply has no entry, instead of
            // quietly falling back to a price that is not a fill.
            for (int i = 0; i + 2 < times.Count; i++)
            {
                _future[times[i]] = new[] { opens[i + 1], opens[i + 2] };
            }

            _tab.SetNewLogMessage("Calibration: loaded " + _future.Count + " future fills",
                Logging.LogMessageType.System);
        }
    }
}
