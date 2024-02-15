// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer
{
    internal enum SettingsMode { None = 0, Auto, File, Hashtable, Preset };

    /// <summary>
    /// A class to represent the settings provided to ScriptAnalyzer class.
    /// </summary>
    public class Settings
    {
        private bool recurseCustomRulePath = false;
        private bool includeDefaultRules = false;
        private string filePath;
        private List<string> includeRules;
        private List<string> excludeRules;
        private List<string> severities;
        private List<string> customRulePath;
        private Dictionary<string, Dictionary<string, object>> ruleArguments;

        public bool RecurseCustomRulePath => recurseCustomRulePath;
        public bool IncludeDefaultRules => includeDefaultRules;
        public string FilePath => filePath;
        public IEnumerable<string> IncludeRules => includeRules;
        public IEnumerable<string> ExcludeRules => excludeRules;
        public IEnumerable<string> Severities => severities;
        public IEnumerable<string> CustomRulePath => customRulePath;
        public Dictionary<string, Dictionary<string, object>> RuleArguments => ruleArguments;

        /// <summary>
        /// Create a settings object from the input object.
        /// </summary>
        /// <param name="settings">An input object of type Hashtable or string.</param>
        /// <param name="presetResolver">A function that takes in a preset and resolves it to a path.</param>
        public Settings(object settings, Func<string, string> presetResolver)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            includeRules = new List<string>();
            excludeRules = new List<string>();
            severities = new List<string>();
            ruleArguments = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            var settingsFilePath = settings as string;

            //it can either be a preset or path to a file or a hashtable
            if (settingsFilePath != null)
            {
                if (presetResolver != null)
                {
                    var resolvedFilePath = presetResolver(settingsFilePath);
                    if (resolvedFilePath != null)
                    {
                        settingsFilePath = resolvedFilePath;
                    }
                }

                if (File.Exists(settingsFilePath))
                {
                    filePath = settingsFilePath;
                    parseSettingsFile(settingsFilePath);
                }
                else
                {
                    throw new ArgumentException(
                        String.Format(
                            CultureInfo.CurrentCulture,
                            Strings.InvalidPath,
                            settingsFilePath));
                }
            }
            else
            {
                var settingsHashtable = settings as Hashtable;
                if (settingsHashtable != null)
                {
                    parseSettingsHashtable(settingsHashtable);
                }
                else
                {
                    throw new ArgumentException(Strings.SettingsInvalidType);
                }
            }
        }

        /// <summary>
        /// Create a Settings object from the input object.
        /// </summary>
        /// <param name="settings">An input object of type Hashtable or string.</param>
        public Settings(object settings) : this(settings, null)
        {
        }

        /// <summary>
        /// Retrieves the Settings directory from the Module directory structure
        /// </summary>
        public static string GetShippedSettingsDirectory()
        {
            // Find the compatibility files in Settings folder
            var path = typeof(Helper).GetTypeInfo().Assembly.Location;
            if (String.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var settingsPath = Path.Combine(Path.GetDirectoryName(path), "Settings");
            if (!Directory.Exists(settingsPath))
            {
                // try one level down as the PSScriptAnalyzer module structure is not consistent
                // CORECLR binaries are in PSScriptAnalyzer/coreclr/, PowerShell v3 binaries are in PSScriptAnalyzer/PSv3/
                // and PowerShell v5 binaries are in PSScriptAnalyzer/
                settingsPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)), "Settings");
                if (!Directory.Exists(settingsPath))
                {
                    return null;
                }
            }

            return settingsPath;
        }

        /// <summary>
        /// Returns the builtin setting presets
        ///
        /// Looks for powershell data files (*.psd1) in the PSScriptAnalyzer module settings directory
        /// and returns the names of the files without extension
        /// </summary>
        public static IEnumerable<string> GetSettingPresets()
        {
            var settingsPath = GetShippedSettingsDirectory();
            if (settingsPath != null)
            {
                foreach (var filepath in System.IO.Directory.EnumerateFiles(settingsPath, "*.psd1"))
                {
                    yield return System.IO.Path.GetFileNameWithoutExtension(filepath);
                }
            }
        }

        /// <summary>
        /// Gets the path to the settings file corresponding to the given preset.
        ///
        /// If the corresponding preset file is not found, the method returns null.
        /// </summary>
        public static string GetSettingPresetFilePath(string settingPreset)
        {
            var settingsPath = GetShippedSettingsDirectory();
            if (settingsPath != null)
            {
                if (GetSettingPresets().Contains(settingPreset, StringComparer.OrdinalIgnoreCase))
                {
                    return System.IO.Path.Combine(settingsPath, settingPreset + ".psd1");
                }
            }

            return null;
        }

        /// <summary>
        /// Create a settings object from an input object.
        /// </summary>
        /// <param name="settingsObj">An input object of type Hashtable or string.</param>
        /// <param name="cwd">The path in which to search for a settings file.</param>
        /// <param name="outputWriter">An output writer.</param>
        /// <param name="getResolvedProviderPathFromPSPathDelegate">The GetResolvedProviderPathFromPSPath method from PSCmdlet to resolve relative path including wildcard support.</param>
        /// <returns>An object of Settings type.</returns>
        internal static Settings Create(object settingsObj, string cwd, IOutputWriter outputWriter,
            PathResolver.GetResolvedProviderPathFromPSPath<string, ProviderInfo, Collection<string>> getResolvedProviderPathFromPSPathDelegate)
        {
            object settingsFound;
            var settingsMode = FindSettingsMode(settingsObj, cwd, out settingsFound);

            switch (settingsMode)
            {
                case SettingsMode.Auto:
                    outputWriter?.WriteVerbose(
                        String.Format(
                            CultureInfo.CurrentCulture,
                            Strings.SettingsNotProvided,
                            ""));
                    outputWriter?.WriteVerbose(
                        String.Format(
                            CultureInfo.CurrentCulture,
                            Strings.SettingsAutoDiscovered,
                            (string)settingsFound));
                    break;

                case SettingsMode.Preset:
                case SettingsMode.File:
                    var userProvidedSettingsString = settingsFound.ToString();
                    try
                    {
                        var resolvedPath = getResolvedProviderPathFromPSPathDelegate(userProvidedSettingsString, out ProviderInfo providerInfo).Single();
                        settingsFound = resolvedPath;
                        outputWriter?.WriteVerbose(
                            String.Format(
                                CultureInfo.CurrentCulture,
                                Strings.SettingsUsingFile,
                                resolvedPath));
                    }
                    catch
                    {
                        outputWriter?.WriteVerbose(
                            String.Format(
                                CultureInfo.CurrentCulture,
                                Strings.SettingsCannotFindFile,
                                userProvidedSettingsString));
                    }
                    break;

                case SettingsMode.Hashtable:
                    outputWriter?.WriteVerbose(
                        String.Format(
                            CultureInfo.CurrentCulture,
                            Strings.SettingsUsingHashtable));
                    break;

                default:
                    outputWriter?.WriteVerbose(
                        String.Format(
                            CultureInfo.CurrentCulture,
                            Strings.SettingsObjectCouldNotBResolved));
                    return null;
            }

            return new Settings(settingsFound);
        }

        /// <summary>
        /// Recursively convert hashtable to dictionary
        /// </summary>
        /// <param name="hashtable"></param>
        /// <returns>Dictionary that maps string to object</returns>
        private Dictionary<string, object> GetDictionaryFromHashtable(Hashtable hashtable)
        {
            var dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in hashtable.Keys)
            {
                string key = obj as string;
                if (key == null)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.KeyNotString,
                            key));
                }

                var valueHashtableObj = hashtable[obj];
                if (valueHashtableObj == null)
                {
                    throw new InvalidDataException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.WrongValueHashTable,
                            "",
                            key));
                }

                var valueHashtable = valueHashtableObj as Hashtable;
                if (valueHashtable == null)
                {
                    dictionary.Add(key, valueHashtableObj);
                }
                else
                {
                    dictionary.Add(key, GetDictionaryFromHashtable(valueHashtable));
                }
            }
            return dictionary;
        }

        private List<string> GetData(object val, string key)
        {
            // value must be either string or or an array of strings
            if (val == null)
            {
                throw new InvalidDataException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.WrongValueHashTable,
                        "",
                        key));
            }

            List<string> values = new List<string>();
            var valueStr = val as string;
            if (valueStr != null)
            {
                values.Add(valueStr);
            }
            else
            {
                var valueArr = val as object[];
                if (valueArr == null)
                {
                    // check if it is an array of strings
                    valueArr = val as string[];
                }

                if (valueArr != null)
                {
                    foreach (var item in valueArr)
                    {
                        var itemStr = item as string;
                        if (itemStr != null)
                        {
                            values.Add(itemStr);
                        }
                        else
                        {
                            throw new InvalidDataException(
                                string.Format(
                                    CultureInfo.CurrentCulture,
                                    Strings.WrongValueHashTable,
                                    val,
                                    key));
                        }
                    }
                }
                else
                {
                    throw new InvalidDataException(
                            string.Format(
                                CultureInfo.CurrentCulture,
                                Strings.WrongValueHashTable,
                                val,
                                key));
                }
            }

            return values;
        }

        /// <summary>
        /// Sets the arguments for consumption by rules
        /// </summary>
        /// <param name="ruleArgs">A hashtable with rule names as keys</param>
        private Dictionary<string, Dictionary<string, object>> ConvertToRuleArgumentType(object ruleArguments)
        {
            var ruleArgs = ruleArguments as Dictionary<string, object>;
            if (ruleArgs == null)
            {
                throw new ArgumentException(Strings.SettingsInputShouldBeDictionary, nameof(ruleArguments));
            }

            if (ruleArgs.Comparer != StringComparer.OrdinalIgnoreCase)
            {
                throw new ArgumentException(Strings.SettingsDictionaryShouldBeCaseInsesitive, nameof(ruleArguments));
            }

            var ruleArgsDict = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in ruleArgs.Keys)
            {
                var argsDict = ruleArgs[rule] as Dictionary<string, object>;
                if (argsDict == null)
                {
                    throw new InvalidDataException(Strings.SettingsInputShouldBeDictionary);
                }
                ruleArgsDict[rule] = argsDict;
            }

            return ruleArgsDict;
        }

        private void parseSettingsHashtable(Hashtable settingsHashtable)
        {
            HashSet<string> validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var settings = GetDictionaryFromHashtable(settingsHashtable);
            foreach (var settingKey in settings.Keys)
            {
                var key = settingKey.ToLowerInvariant(); // ToLowerInvariant is important to also work with turkish culture, see https://github.com/PowerShell/PSScriptAnalyzer/issues/1095
                object val = settings[key];
                switch (key)
                {
                    case "severity":
                        severities = GetData(val, key);
                        break;

                    case "includerules":
                        includeRules = GetData(val, key);
                        break;

                    case "excluderules":
                        excludeRules = GetData(val, key);
                        break;

                    case "customrulepath":
                        customRulePath = GetData(val, key);
                        break;

                    case "includedefaultrules":
                    case "recursecustomrulepath":
                        if (!(val is bool))
                        {
                            throw new InvalidDataException(string.Format(
                                CultureInfo.CurrentCulture,
                                Strings.SettingsValueTypeMustBeBool,
                                settingKey));
                        }

                        var booleanVal = (bool)val;
                        var field = this.GetType().GetField(
                            key,
                            BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.NonPublic);
                        field.SetValue(this, booleanVal);
                        break;

                    case "rules":
                        try
                        {
                            ruleArguments = ConvertToRuleArgumentType(val);
                        }
                        catch (ArgumentException argumentException)
                        {
                            throw new InvalidDataException(
                                string.Format(CultureInfo.CurrentCulture, Strings.WrongValueHashTable, "", key),
                                argumentException);
                        }

                        break;

                    default:
                        throw new InvalidDataException(
                            string.Format(
                                CultureInfo.CurrentCulture,
                                Strings.WrongKeyHashTable,
                                key));
                }
            }
        }

        private void parseSettingsFile(string settingsFilePath)
        {
            Token[] parserTokens = null;
            ParseError[] parserErrors = null;
            Ast profileAst = Parser.ParseFile(settingsFilePath, out parserTokens, out parserErrors);
            IEnumerable<Ast> hashTableAsts = profileAst.FindAll(item => item is HashtableAst, false);

            // no hashtable, raise warning
            if (hashTableAsts.Count() == 0)
            {
                throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, Strings.InvalidProfile, settingsFilePath));
            }

            HashtableAst hashTableAst = hashTableAsts.First() as HashtableAst;
            Hashtable hashtable;
            try
            {
                // ideally we should use HashtableAst.SafeGetValue() but since
                // it is not available on PSv3, we resort to our own narrow implementation.
                hashtable = Helper.GetSafeValueFromHashtableAst(hashTableAst);
            }
            catch (InvalidOperationException e)
            {
                throw new ArgumentException(Strings.InvalidProfile, e);
            }

            if (hashtable == null)
            {
                throw new ArgumentException(
                    String.Format(
                        CultureInfo.CurrentCulture,
                        Strings.InvalidProfile,
                        settingsFilePath));
            }

            parseSettingsHashtable(hashtable);
        }

        private static bool IsBuiltinSettingPreset(object settingPreset)
        {
            var preset = settingPreset as string;
            if (preset != null)
            {
                return GetSettingPresets().Contains(preset, StringComparer.OrdinalIgnoreCase);
            }

            return false;
        }

        internal static SettingsMode FindSettingsMode(object settings, string path, out object settingsFound)
        {
            var settingsMode = SettingsMode.None;

            // if the provided settings argument is wrapped in an expressions then PowerShell resolves it but it will be of type PSObject and we have to operate then on the BaseObject
            if (settings is PSObject)
            {
                settings = ((PSObject)settings).BaseObject;
            }

            settingsFound = settings;
            if (settingsFound == null)
            {
                if (path != null)
                {
                    // add a directory separator character because if there is no trailing separator character, it will return the parent
                    var directory = path.TrimEnd(System.IO.Path.DirectorySeparatorChar);
                    if (File.Exists(directory))
                    {
                        // if given path is a file, get its directory
                        directory = Path.GetDirectoryName(directory);
                    }

                    if (Directory.Exists(directory))
                    {
                        // if settings are not provided explicitly, look for it in the given path
                        // check if pssasettings.psd1 exists
                        var settingsFilename = "PSScriptAnalyzerSettings.psd1";
                        var settingsFilePath = Path.Combine(directory, settingsFilename);
                        settingsFound = settingsFilePath;
                        if (File.Exists(settingsFilePath))
                        {
                            settingsMode = SettingsMode.Auto;
                        }
                    }
                }
            }
            else
            {
                if (!TryResolveSettingForStringType(settingsFound, ref settingsMode, ref settingsFound))
                {
                    if (settingsFound is Hashtable)
                    {
                        settingsMode = SettingsMode.Hashtable;
                    }
                }
            }

            return settingsMode;
        }

        // If the settings object is a string determine wheter it is one of the settings preset or a file path and resolve the setting in the former case.
        private static bool TryResolveSettingForStringType(object settingsObject, ref SettingsMode settingsMode, ref object resolvedSettingValue)
        {
            if (settingsObject is string settingsString)
            {
                if (IsBuiltinSettingPreset(settingsString))
                {
                    settingsMode = SettingsMode.Preset;
                    resolvedSettingValue = GetSettingPresetFilePath(settingsString);
                }
                else
                {
                    settingsMode = SettingsMode.File;
                    resolvedSettingValue = settingsString;
                }
                return true;
            }

            return false;
        }
    }
}
