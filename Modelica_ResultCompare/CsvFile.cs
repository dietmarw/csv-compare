﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CurveCompare;

namespace CsvCompare
{
    /// This class parses CSV files and holds results in a dictionary
    public class CsvFile:IDisposable
    {
        private double _dRangeDelta = 0.002;
        private string _fileName = string.Empty;
        private List<double> _xAxis = new List<double>();
        private Dictionary<string, List<double>> _values = new Dictionary<string, List<double>>();
        private bool _bShowRelativeErrors = true;
        private bool _bDisposed = false;

        /// Holds values for x axis (time)
        public List<double> XAxis { get { return _xAxis; } }
        /// Holds values for the results in a dictionary.
        /// 
        /// The key is the result identifier.
        public Dictionary<string, List<double>> Results { get { return _values; } }
        /// This value can be used to produce a offset between base and comparison values
        public double RangeDelta { get { return _dRangeDelta; } set { _dRangeDelta = value; } }
        /// This value enables/disables relative error differences in the error graph
        public bool ShowRelativeErrors
        {
            get { return _bShowRelativeErrors; }
            set { _bShowRelativeErrors = value; }
        }

        /// The constructor reads the csv file to this object
        /// @para filename The full path of the csv file
        /// @para dTolerance Double value containing the delta for the tube generation
        public CsvFile(string fileName, Options options, Log log)
        {
            //Parse tolerance from commandline
            NumberFormatInfo toleranceProvider = new NumberFormatInfo();
            toleranceProvider.NumberDecimalSeparator = ".";

            //understand 0.002
            if (!Double.TryParse(options.Tolerance, NumberStyles.AllowDecimalPoint, toleranceProvider, out _dRangeDelta))
            {
                //understand 0,002
                toleranceProvider.NumberDecimalSeparator = ",";
                if (!Double.TryParse(options.Tolerance, NumberStyles.AllowDecimalPoint, toleranceProvider, out _dRangeDelta))
                    //understand 2e-2 etc.
                    if (!Double.TryParse(options.Tolerance, out _dRangeDelta))
                        log.WriteLine(LogLevel.Warning, "could not parse given tolerance argument: \"{0}\", using default \"{1}\".", options.Tolerance, _dRangeDelta);
            }

            if (File.Exists(fileName))
            {
                _fileName = Path.GetFullPath(fileName);
                using (TextReader reader = new StreamReader(fileName))
                {
                    string sLine = reader.ReadLine();
                    if (null == sLine)
                        throw new ArgumentNullException(string.Format("\"{0}\" is empty, nothing to parse here.", fileName));

#if DEBUG           //Do some benchmarking in DEBUG mode
                    Stopwatch timer = new Stopwatch();
                    timer.Start();
#endif
                    List<string> map = new List<string>();

                    //skip comments
                    while (!string.IsNullOrEmpty(sLine) && sLine.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                        sLine = reader.ReadLine();

                    Regex reg = new Regex(string.Format(CultureInfo.CurrentCulture, "{0}(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))", options.Delimiter));

                    //read the columns from the first line
                    string[] values = reg.Split(sLine);

                    //one element means the line has not been parsed correctly
                    if (null == values || values.Length == 1)
                        throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "The file {0} could not be parsed. Maybe the wrong delimiter is set? It has been set to \"{1}\".", fileName, options.Delimiter));

                    foreach (string sCol in values)
                        if (!string.IsNullOrEmpty(sCol))
                        {
                            string sTemp = sCol.Trim(' ', '"', '\t', '\'');
                            if (sTemp != "t" && sTemp != "time" && sTemp != "Time")//Skip time values
                            {
                                try
                                {
                                    _values.Add(sTemp, new List<double>());
                                    map.Add(sTemp);
                                }
                                catch (ArgumentException)
                                {
                                    throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, "Error parsing the csv file \"{0}\". The result {1} is already in the list (maybe you set no or a wrong delimiter for the parser? I used \"{2}\").",
                                        fileName, sTemp, options.Delimiter));
                                }
                            }
                        }

#if DEBUG 
                    log.WriteLine(LogLevel.Debug, "Parsed header in {0}ms", timer.ElapsedMilliseconds);
                    timer.Restart();
#endif
                    CheckHeaderForNumbers(log, map);
#if DEBUG 
                    log.WriteLine(LogLevel.Debug, "Checked header in {0}ms", timer.ElapsedMilliseconds);
                    timer.Restart();
#endif
                    //read the rest of the csv file
                    while ((sLine = reader.ReadLine()) != null)
                    {
                        //Skip comments
                        if (sLine.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                            continue;

                        //values = reg.Split(sLine); //splitting using regular expressions is slow
                        IEnumerable<string> dataValues;
                        if (options.Delimiter.Equals(options.Separator))
                            dataValues = Tokenize(sLine, options.Delimiter); //use custom tokenizer for improved performance
                        else
                            dataValues = sLine.Split(options.Delimiter); //use ordinary Split function for simple cases
                        
                        int iCol = 0;

                        NumberFormatInfo provider = new NumberFormatInfo();
                        provider.NumberDecimalSeparator = options.Separator.ToString();

                        //read values to the dictionary
                        foreach (string sCol in dataValues)
                        {
                            double dValue;
                            if (!Double.TryParse(sCol.Trim('"'), NumberStyles.Any, provider, out dValue))
                            {
                                if (!string.IsNullOrEmpty(sCol))
                                    log.WriteLine(LogLevel.Warning, "Could not parse string \"{0}\" as double value, skipping.", sCol);
                                iCol++;
                                continue;
                            }

                            if (iCol == 0)
                                _xAxis.Add(dValue);
                            else
                                try
                                {
                                    _values[map[iCol - 1]].Add(dValue);
                                }
                                catch (KeyNotFoundException)
                                {
                                    break;
                                }

                            iCol++;
                        }
                    }
#if DEBUG 
                    timer.Stop();
                    log.WriteLine(LogLevel.Debug, "Time to parse: {0}", timer.Elapsed);
#endif

                    if (_xAxis.Count <= 1)
                        throw new ArgumentNullException(string.Format(CultureInfo.CurrentCulture, "{0} could not be parsed and might be an invalid csv file.", fileName));
                }
            }
            else
                throw new FileNotFoundException();
        }

        private static void CheckHeaderForNumbers(Log log, List<string> map)
        {
            //Check map for numbers to throw a warning if no header has been set
            foreach (string sCol in map)
            {
                double dTemp;
                if (double.TryParse(sCol, out dTemp))
                    log.WriteLine(LogLevel.Warning, "Column \"{0}\" seems to be a number and should be a column title. Maybe you forgot to add a header line?", sCol);
                else
                    log.WriteLine(LogLevel.Debug, "Column \"{0}\" seems to be text, this is good.", sCol);
            }
        }

        private List<string> Tokenize(string str, char delimiter)
        {
            List<string> tokens = new List<string>();

            int pos = 0;
            int end = str.Length;
            bool withinQuotes = false;

            int lpos=pos;
            int length=0;

            while (pos < end) {
                char c = str[pos];
                
                if (c == '"')
                    withinQuotes = !withinQuotes;

                if (c == delimiter && !withinQuotes)
                {
                    string token = str.Substring(lpos, length);
                    tokens.Add(token);
                    lpos = pos+1;
                    length = 0;
                }
                else {
                    length++;
                }

                pos++;
            }

            //special treatment for lines which are not terminated by the delimiter
            if (length > 0)
            {
                string token = str.Substring(lpos, length);
                tokens.Add(token);
            }

            return tokens;
        }

        public override string ToString()
        {
            return _fileName;
        }

        public Report PlotCsvFile(string sReportPath, Log log)
        {
            Report r = new Report(sReportPath);
            log.WriteLine("Generating plot for report");

            foreach (KeyValuePair<string, List<double>> res in _values)
            {
                Curve compare = new Curve(res.Key, this.XAxis.ToArray<double>(), res.Value.ToArray<double>());
                PrepareCharts(r, compare);
            }

            return r;
        }
        public Report CompareFiles(Log log, CsvFile csvBase, ref Options options)
        {
            if (null != _fileName)
                if (Path.GetDirectoryName(_fileName).Length > 0)
                    return CompareFiles(log, csvBase, string.Format(CultureInfo.CurrentCulture, "{0}{1}{2}_report.html", Path.GetDirectoryName(_fileName), Path.DirectorySeparatorChar, Path.GetFileNameWithoutExtension(csvBase.ToString())), ref options);
                else
                    return CompareFiles(log, csvBase, Path.GetFileNameWithoutExtension(_fileName) + ".html", ref options);
            else
                return CompareFiles(log, csvBase, null, ref options);
        }
        
        public Report CompareFiles(Log log, CsvFile csvBase, string sReportPath, ref Options options)
        {
            int iInvalids = 0;

            Report rep = new Report(sReportPath);
            log.WriteLine("Comparing \"{0}\" to \"{1}\"", _fileName, csvBase.ToString());

            rep.BaseFile = csvBase.ToString();
            rep.CompareFile = _fileName;

            Curve reference = new Curve();
            Curve compareCurve = new Curve();
            TubeReport tubeReport = new TubeReport();
            TubeSize size = null;            
            Tube tube = new Tube(size);
            IOptions tubeOptions = new Options1(Convert.ToDouble(options.Tolerance), Axes.X);

            foreach (KeyValuePair<string, List<double>> res in csvBase.Results)
            {
                if (!this.Results.ContainsKey(res.Key))
                    log.WriteLine(LogLevel.Warning, "{0} not found in \"{1}\", skipping checks.", res.Key, this._fileName);
                else
                {
                    compareCurve = new Curve(res.Key, this.XAxis.ToArray<double>(), this.Results[res.Key].ToArray<double>());

                    if (res.Value.Count == 0)
                    {
                        log.Error("{0} has no y-Values! Maybe error during parsing? Skipping", res.Key);
                        continue;
                    }
                    reference = new Curve("Reference ", csvBase.XAxis.ToArray(), csvBase.Results[res.Key].ToArray());
                    if (!reference.ImportSuccessful)
                    {
                        log.Error("Error in the calculation of the tubes. Skipping {0}", res.Key);
                        rep.Chart.Add(new Chart() { Title = res.Key, Errors = 1 });
                        continue;
                    }

                    if (reference.X.Length < compareCurve.X.Length)
                        log.WriteLine(LogLevel.Warning, "The resolution of the base x-axis is smaller than the compare x-axis. The better the base resolution is, the better the validation result will be!");
                    else
                        log.WriteLine(LogLevel.Debug, "The resolution of the base x-axis is good.");

                    size = new TubeSize(reference, true);
                    size.Calculate(_dRangeDelta, Axes.X, Relativity.Relative);
                    tube = new Tube(size);
                    tubeReport = tube.Calculate(reference);
                    tube.Validate(compareCurve);

                    if (tubeReport.Valid == Validity.Valid)
                        log.WriteLine(res.Key + " is valid");
                    else
                    {
                        log.WriteLine(LogLevel.Warning, "{0} is invalid! {1} errors have been found during validation.", res.Key,
                            (null != tube.Report.Errors && null != tube.Report.Errors.X) ? tube.Report.Errors.X.Length : 0);
                        iInvalids++;
                        Environment.ExitCode = 1;
                    }
                }
                if (null != tube.Report)//No charts for missing reports
                    PrepareCharts(reference, compareCurve, tube.Report.Errors, rep, tubeReport, res);
            }
            rep.Tolerance = _dRangeDelta;

            string sResult = "na";

            if (rep.TotalErrors == 0)
                sResult = "passed";
            else
                sResult = "failed";

            if (options.ComparisonFlag)
                using (TextWriter writer = File.CreateText(string.Format("{0}{1}compare_{2}.log", Path.GetDirectoryName(_fileName), Path.DirectorySeparatorChar, sResult)))
                {
                    //Content needs to be defined
                    writer.WriteLine("CSV Compare Version {0} ({1})", Info.AssemblyVersion, Assembly.GetExecutingAssembly().GetName().ProcessorArchitecture);
                    writer.WriteLine("Comparison result file for {0}", _fileName);
                    writer.WriteLine(". Time:        {0:o}", DateTime.Now);
                    writer.WriteLine(". Operation:   {0}", options.Mode);
                    writer.WriteLine(". Tolerance:   {0}", options.Tolerance);
                    writer.WriteLine(". Result:      {0}", sResult);

                    if (rep.TotalErrors > 0)
                    {
                        Chart pairMax = rep.Chart.Aggregate((l, r) => l.DeltaError > r.DeltaError ? l : r);
                        writer.WriteLine(". Biggest error: {0}=>{1}", pairMax.Title, pairMax.DeltaError);
                        writer.WriteLine(". Failed values:");

                        foreach (Chart c in (from r in rep.Chart where r.DeltaError > 0 select r).OrderByDescending(er => er.DeltaError))
                            writer.WriteLine("{0}=>{1}", c.Title, c.DeltaError);
                    }
                }

            return rep;
        }

        private void PrepareCharts(Report rep, Curve compare)//Draw result only
        {
            PrepareCharts(compare, null, null, rep, null, new KeyValuePair<string, List<double>>());
        }

        private static void PrepareCharts(Curve reference, Curve compare, Curve error, Report rep, TubeReport tubeReport, KeyValuePair<string, List<double>> res)
        {
            Chart ch = new Chart()
            {
                LabelX = "Time",
                LabelY = res.Key,
                Errors = (null != error && null != error.X) ? error.X.Length : 0,
                Title = res.Key
            };

            if (null != compare)
            {
                ch.Series.Add(new Series()
                {
                    Color = Color.Orange,
                    ArrayString = Series.GetArrayString(reference.X, reference.Y),
                    Title = "Base (to compare with)"
                });
                ch.Series.Add(new Series()
                {
                    Color = Color.Green,
                    ArrayString = Series.GetArrayString(compare.X, compare.Y),
                    Title = "Result"
                });
                ch.Series.Add(new Series()
                {
                    Color = Color.LightBlue,
                    ArrayString = Series.GetArrayString(tubeReport.Lower.X, tubeReport.Lower.Y),
                    Title = "Low Tube"
                });
                ch.Series.Add(new Series()
                {
                    Color = Color.LightGreen,
                    ArrayString = Series.GetArrayString(tubeReport.Upper.X, tubeReport.Upper.Y),
                    Title = "High Tube"
                });
            }
            else
            {
                ch.Series.Add(new Series()
                {
                    Color = Color.Green,
                    ArrayString = Series.GetArrayString(reference.X, reference.Y),
                    Title = "Compare"
                });
            }
            if (null != error && null != error.X && error.X.Length > 0)
            {
                ch.Series.Add(new Series()
                {
                    Color = Color.DarkGoldenrod,
                    ArrayString = Series.GetArrayString(error.X, error.Y),
                    Title = "ERRORS"
                });

                //List<double> lDeltas = new List<double>();
                //for (int i = 1; i < compare.X.Length - 1; i++)
                //    lDeltas.Add((Math.Abs(error.Y[i]) *
                //            (
                //                (Math.Abs(compare.X[i] - compare.X[i - 1])) +
                //                (Math.Abs(compare.X[i + 1] - compare.X[i]))
                //            )) / 2);

                //ch.DeltaError = lDeltas.Sum() / (1e-3 + compare.Y.Max(x => Math.Abs(x)));
            }
            if (tubeReport.Lower.X.ToList<double>().Count > 2)//Remember Start and Stop values for graph scaling
            {
                ch.MinValue = tubeReport.Lower.X[0];
                ch.MaxValue = tubeReport.Lower.X.Last();
            }
            rep.Chart.Add(ch);
        }

        public void Save(Options options)
        {
            Save(Path.GetDirectoryName(_fileName), options);
        }
        public void Save(string sFolderName, Options options)
        {
            string sFilename = string.Format(CultureInfo.CurrentCulture, "{0}{1}.generated.csv", sFolderName, Path.GetFileNameWithoutExtension(_fileName));
            NumberFormatInfo provider = new NumberFormatInfo();

            provider.NumberDecimalSeparator = ".";

            using (TextWriter writer = new StreamWriter(sFilename))
            {
                writer.Write(string.Format(provider, "\"time\"{0}", options.Delimiter));

                foreach (KeyValuePair<string, List<double>> res in _values)
                    writer.Write(string.Format(provider, "\"{0}\"{1}", res.Key, options.Delimiter));

                writer.WriteLine();

                int iCount = 0;
                foreach (double dTime in _xAxis)
                {
                    writer.Write(string.Format(provider, "\"{0}\"{1}", dTime, options.Delimiter));
                    foreach (KeyValuePair<string, List<double>> res in _values)
                    {
                        try
                        {
                            if (res.Value.Count == 0)
                                continue;

                            writer.Write(string.Format(provider, "\"{0}\"{1}", res.Value[iCount], options.Delimiter));
                        }
                        catch (ArgumentException) { }
                    }
                    
                    writer.WriteLine();
                    iCount++;
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_bDisposed)
                return;

            if (disposing)
            {
                this._xAxis.Clear();
                this._values.Clear();
            }
            _bDisposed = true;
        }
    }
}