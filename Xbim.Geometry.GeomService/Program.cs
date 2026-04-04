using Microsoft.Extensions.Logging;
using Serilog;
using System.Diagnostics;
using Xbim.Common.Configuration;
using Xbim.Common.Enumerations;
using Xbim.Common.ExpressValidation;
using Xbim.Geometry.Abstractions;
using Xbim.Ifc;
using Xbim.ModelGeometry.Scene;

namespace Xbim.Geometry.GeomService
{
    public enum ExitCodes
    {
        ExitCodeOK = 0,
        ExitCodeParamError = 1,
        ExitCodeNotFoundError = 2,
        ExitCodeXbimAlreadyFound = 3,
        ExitCodeErrorCopying = 4,
        ExitCodeUndefinedError = 5,
        ExitCodeInvalidIFC = 6,
        ExitCodeExceptionValidating = 7,
    }

    public enum ExpressValidation 
    {
        None = 0,
        ValidateOnly = 1,
        ValidateAndLog = 2
    }

    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 1 && 
                    (
                        args[0] == "/help" ||
                        args[0] == "/?"
                    )
                )
                return PrintHelp();
            FileInfo? ifcfile = null;
            XGeometryEngineVersion engineVer = XGeometryEngineVersion.V6;
            bool singleThread = false;
            bool adjustWcs = false;
            ExpressValidation validateExpress = ExpressValidation.None;
            bool skipGeometry = false;
            bool progress = false;
            bool doLog = false;
            bool overwrite = false;
            LogLevel ll = LogLevel.Debug;
            try
            {
                foreach (string arg in args)
                {
                    if (arg.StartsWith("/in:", StringComparison.OrdinalIgnoreCase))
                    {
                        var tmp = arg.Substring(4);
                        ifcfile = new FileInfo(tmp);
                    }
                    else if (arg.StartsWith("/ll:", StringComparison.OrdinalIgnoreCase))
                    {
                        var tmp = arg.Substring(4);
                        if (tmp.Equals("Debug", StringComparison.OrdinalIgnoreCase))
                            ll = LogLevel.Debug;
                        else if (tmp.Equals("Information", StringComparison.OrdinalIgnoreCase))
                            ll = LogLevel.Information;
                        else if (tmp.Equals("Warning", StringComparison.OrdinalIgnoreCase))
                            ll = LogLevel.Warning;
                        else if (tmp.Equals("Error", StringComparison.OrdinalIgnoreCase))
                            ll = LogLevel.Error;
                        else if (tmp.Equals("Critical", StringComparison.OrdinalIgnoreCase))
                            ll = LogLevel.Critical;
                        else if (tmp.Equals("Trace", StringComparison.OrdinalIgnoreCase))
                            ll = LogLevel.Trace;
                    }
                    else if (arg.StartsWith("/eng:", StringComparison.OrdinalIgnoreCase))
                    {
                        var tmp = arg.Substring(5);
                        if (tmp == "V5")
                            engineVer = XGeometryEngineVersion.V5;
                        else if (tmp == "V6")
                            engineVer = XGeometryEngineVersion.V6;
                    }
                    else if (arg.StartsWith("/validate:", StringComparison.OrdinalIgnoreCase))
                    {
                        var tmp = arg.Substring(10);
                        if (tmp.Equals("ValidateOnly", StringComparison.OrdinalIgnoreCase))
                            validateExpress = ExpressValidation.ValidateOnly;
                        else if (tmp.Equals("ValidateAndLog", StringComparison.OrdinalIgnoreCase))
                            validateExpress = ExpressValidation.ValidateAndLog;
                        else if (tmp.Equals("None", StringComparison.OrdinalIgnoreCase))
                            validateExpress = ExpressValidation.None;
                    }
                    else if (IsBoolParam(arg, "st", out bool thisParamSt))
                    {
                        singleThread = thisParamSt;
                    }
                    else if (IsBoolParam(arg, "adjust", out bool thisParamAdjust))
                    {
                        adjustWcs = thisParamAdjust;
                    }
                    else if (IsBoolParam(arg, "skipgeometry", out bool skipGeomParam))
                    {
                        skipGeometry = skipGeomParam;
                    }
                    else if (IsBoolParam(arg, "progress", out bool progressPar))
                    {
                        progress = progressPar;
                    }
                    else if (IsBoolParam(arg, "log", out bool thisParamLog))
                    {
                        doLog = thisParamLog;
                    }
                    // e.g.: /overwrite:true
                    else if (IsBoolParam(arg, "overwrite", out bool thisParamOverW))
                    {
                        overwrite = thisParamOverW;
                    }
                }
            }
            catch (Exception)
            {
                return (int)ExitCodes.ExitCodeParamError;
            }

            StreamWriter? tlog = null;
            if (doLog)
                tlog = File.AppendText("_MeshLog.txt");

            tlog?.WriteLine($"== Starting at {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}");
            tlog?.WriteLine($"Xbim.Sunshine.GeomService version 2");
            tlog?.WriteLine($"params {string.Join(" ", args)}");

            if (ifcfile is null)
                return CloseAndReturn(tlog, ExitCodes.ExitCodeParamError);
            if (!ifcfile.Exists)
                return CloseAndReturn(tlog, ExitCodes.ExitCodeNotFoundError);
            LoggerFactory? loggerFactory = null;
            if (doLog)
            {
                LoggerConfiguration? cnf = null;
                switch (ll)
                {
                    case LogLevel.Trace:
                        cnf = new LoggerConfiguration().MinimumLevel.Verbose();
                        break;
                    case LogLevel.Debug:
                        cnf = new LoggerConfiguration().MinimumLevel.Debug();
                        break;
                    case LogLevel.Information:
                        cnf = new LoggerConfiguration().MinimumLevel.Information();
                        break;
                    case LogLevel.Warning:
                        cnf = new LoggerConfiguration().MinimumLevel.Warning();
                        break;
                    case LogLevel.Error:
                        cnf = new LoggerConfiguration().MinimumLevel.Error();
                        break;
                    case LogLevel.Critical:
                        cnf = new LoggerConfiguration().MinimumLevel.Fatal();
                        break;
                    default:
                        cnf = new LoggerConfiguration().MinimumLevel.Debug();
                        break;
                }
                cnf.WriteTo.File(Path.ChangeExtension(ifcfile.FullName, ".geom.log"), rollingInterval: RollingInterval.Day);
                Log.Logger = cnf.CreateLogger();
                loggerFactory = new LoggerFactory();
                loggerFactory.AddSerilog(Log.Logger);
                // loggerFactory.AddSerilog();
                Log.Logger.Information("============== Starting meshing in GeomService executable ==============");
                Log.Logger.Information($"params: \"{string.Join("\" \"", args)}\"");
            }

            // everything else can be used by default
            //
            var xbimFileName = new FileInfo(Path.ChangeExtension(ifcfile.FullName, "xbim"));
            var attempt = 0;
            while (overwrite && xbimFileName.Exists && attempt++ < 10)
            {
                xbimFileName.Delete();
            }
            if (xbimFileName.Exists)
                return CloseAndReturn(tlog, ExitCodes.ExitCodeXbimAlreadyFound);

            if (doLog)
            {
                XbimServices.Current.ConfigureServices(services => services
                    .AddXbimToolkit(opt => opt
                        .AddLoggerFactory(loggerFactory)
                        .AddEsentModel()
                        .AddGeometryServices(builder => builder.Configure(c => c.GeometryEngineVersion = engineVer))));
            }
            else
            {
                XbimServices.Current.ConfigureServices(services => services
                    .AddXbimToolkit(opt => opt
                        .AddEsentModel()
                        .AddGeometryServices(builder => builder.Configure(c => c.GeometryEngineVersion = engineVer))));
            }
            // save on a temp file, then rename to the xbimfilename
            string tempFileName = "";
            do
            {
                tempFileName = Path.ChangeExtension(Path.GetTempFileName(), "xbim");
            } while (File.Exists(tempFileName)); // the temp file must not exist at the start of the process

            tlog?.WriteLine($"Ifc: {ifcfile.FullName}");
            tlog?.WriteLine($"temp: {tempFileName}");
            tlog?.WriteLine($"xbim: {xbimFileName.FullName}");
            tlog?.Flush();

            var sCopy = Stopwatch.StartNew();
            IfcStore? model = null;
            try
            {
                Debug.WriteLine($"Opening model ```{ifcfile.FullName}```");
                model = IfcStore.Open(ifcfile.FullName, null, null, ReportProgress);
            }
            catch (Exception)
            {
                // if the model cannot be parsed, we return a different error code,
                // so that the caller can decide what to do (e.g. retry, skip, etc.)
                return CloseAndReturn(tlog, ExitCodes.ExitCodeInvalidIFC);
            }
            using (model)
            {
                if (validateExpress != ExpressValidation.None)
                {
                    try
                    {
                        var validator = new Validator()
                        {
                            ValidateLevel = ValidationFlags.All,
                            CreateEntityHierarchy = true
                        };

                        List<ValidationResult> validationResult = [];
                        if (progress)
                        {
                            var c = model.Instances.Count();
                            int i = 0;
                            var lastReport = -1;
                            foreach (var item in model.Instances)
                            {
                                validationResult.AddRange(validator.Validate(item));
                                var perc = i * 100 / c;
                                if (lastReport != perc)
                                {
                                    lastReport = perc;
                                    ReportProgress(perc, "Validating");
                                }
                                i++;
                            }
                            ReportProgress(100, "Validating");
                        }
                        else
                        {
                            validationResult = validator.Validate(model).ToList();
                        }

                        tlog?.WriteLine("Express validation completed with {0} errors.", validationResult.Count);
                        if (validateExpress == ExpressValidation.ValidateAndLog)
                        {

                            foreach (var error in validationResult)
                            {
                                if (error.Details.Any())
                                {
                                    tlog?.WriteLine("Validation error: {0} ({1})", error.Item, string.Join(", ", error.Details.Select(x => x.IssueSource)));
                                }
                                else
                                    tlog?.WriteLine("Validation error: {0} - {1}", error.IssueType, error.Message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        tlog?.WriteLine("Exception thrown in validation: {0}", ex.Message);
                        return CloseAndReturn(tlog, ExitCodes.ExitCodeExceptionValidating);
                    }
                }

                if (!skipGeometry)
                {
                    var geomContext = new Xbim3DModelContext(model, loggerFactory, engineVersion: engineVer);
                    tlog?.WriteLine($"context initialised at {sCopy.ElapsedMilliseconds}msec");
                    if (singleThread)
                        geomContext.MaxThreads = 1;
                    geomContext.CreateContext(ReportProgress, adjustWcs);
                    tlog?.WriteLine($"context created at {sCopy.ElapsedMilliseconds}msec");
                }
                model.SaveAs(tempFileName, null, ReportProgress);
                tlog?.WriteLine($"model saved at {sCopy.ElapsedMilliseconds}msec");
                model.Close();
            }
            var s = Stopwatch.StartNew();
            while (!IsFileReady(tempFileName) && s.ElapsedMilliseconds < 30000)
            {
                tlog?.WriteLine($"Waited temp {s.ElapsedMilliseconds}msec");
                Thread.Sleep(1000);
            }
            // finally copy the file to the destination location
            //
            File.Move(tempFileName, xbimFileName.FullName);
            tlog?.WriteLine($"Move finished at {sCopy.ElapsedMilliseconds}msec");

            s = Stopwatch.StartNew();
            while (!IsFileReady(xbimFileName.FullName) && s.ElapsedMilliseconds < 30000)
            {
                tlog?.WriteLine($"Waited {s.ElapsedMilliseconds}msec");
                Thread.Sleep(1000);
            }
            if (!IsFileReady(xbimFileName.FullName))
            {
                tlog?.WriteLine($"Returning copy error");
                tlog?.WriteLine($"temp IsFileReady: {IsFileReady(tempFileName)}");
                tlog?.WriteLine($"xbim IsFileReady: {IsFileReady(xbimFileName.FullName)}");
                return CloseAndReturn(tlog, ExitCodes.ExitCodeErrorCopying);
            }
            tlog?.WriteLine($"Returning ok: {IsFileReady(xbimFileName.FullName)}");
            return CloseAndReturn(tlog, ExitCodes.ExitCodeOK);
        }

        private static int PrintHelp()
        {
            Console.WriteLine(
                """
                Usage: Xbim.Geometry.GeomService.exe 
                   /in:<inputfile>                                      - IFC file to process (required)
                   [/eng:V5|V6]                                         - geometry engine version (default: V6)
                   [/st:true|false]                                     - single thread mode
                   [/adjust:true|false]                                 - adjust WCS
                   [/progress:true|false]                               - show progress in console
                   [/log:true|false]                                    - enable logging 
                   [/overwrite:true|false]                              - overwrite existing xbim file if it exists
                   [/ll:Debug|Information|Warning|Error|Critical|Trace] - Defines the log level
                   [/validate:None|ValidateOnly|ValidateAndLog]         - express validation of the model (default: None)
                   [/skipgeometry:true|false]                           - skip geometry generation
                """
                );
            return 0;
        }

        private static void ReportProgress(int percentProgress, object userState)
        {
            Console.WriteLine($"{ percentProgress} {userState}");
        }

        private static int CloseAndReturn(StreamWriter? tlog, ExitCodes exitCode)
        {
            Log.Logger.Information("Closing GeomService executable with return code {exit} ============== ##", exitCode);
            if (tlog != null)
            {
                tlog.Close();
                tlog.Dispose();
            }
            return (int)exitCode;
        }

        static bool IsFileReady(string filename)
        {
            try
            {
                using (FileStream inputStream = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.None))
                    return inputStream.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Determines whether the specified argument represents a boolean parameter with the given name and parses its
        /// value if present.
        /// </summary>
        /// <remarks>If the argument matches the parameter name but does not specify a value, <paramref
        /// name="paramSetting"/> is set to <see langword="true"/> by default. Only the values "true" and "false"
        /// (case-insensitive) are recognized as explicit boolean values.</remarks>
        /// <param name="argumentToParse">The command-line argument to examine for the boolean parameter. Expected to be in the form
        /// "/paramName[:true|false]".</param>
        /// <param name="paramName">The name of the boolean parameter to search for within the argument. Comparison is case-insensitive.</param>
        /// <param name="paramSetting">When this method returns, contains the parsed boolean value if the parameter is found and valid; otherwise,
        /// <see langword="false"/>.</param>
        /// <returns>true if the argument matches the specified parameter name and contains a valid boolean value; otherwise,
        /// false.</returns>
        private static bool IsBoolParam(string argumentToParse, string paramName, out bool paramSetting)
        {
            if (argumentToParse.StartsWith($"/{paramName}", StringComparison.OrdinalIgnoreCase))
            {
                paramSetting = true;
                var tArr = argumentToParse.Split([":"], StringSplitOptions.RemoveEmptyEntries);
                if (tArr.Length > 1)
                {
                    switch (tArr[1].ToLowerInvariant())
                    {
                        case "true":
                            paramSetting = true;
                            return true;
                        case "false":
                            paramSetting = false;
                            return true;
                    }
                }
            }
            paramSetting = false; // to be ignored
            return false;
        }
    }
}
