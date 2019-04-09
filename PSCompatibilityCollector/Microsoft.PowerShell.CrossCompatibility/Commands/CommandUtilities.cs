// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Management.Automation;

namespace Microsoft.PowerShell.CrossCompatibility.Commands
{
    /// <summary>
    /// Utility object to provide common useful functionality for PSCompatibility cmdlets.
    /// A static internal class does not have to be public like a common base class does.
    /// </summary>
    internal static class CommandUtilities
    {
        private const string COMPATIBILITY_ERROR_ID = "CompatibilityAnalysisError";

        public const string MODULE_PREFIX = "PSCompatibility";

        /// <summary>
        /// Writes a .NET exception as a warning, useful when
        /// collecting profiles and ignoring exceptions that occur.
        /// </summary>
        /// <param name="cmdlet">The cmdlet to write the warning from.</param>
        /// <param name="exception">The exception to write as a warning.</param>
        public static void WriteExceptionAsWarning(
            this Cmdlet cmdlet,
            Exception exception)
        {
            cmdlet.WriteWarning(exception.ToString());
        }

        /// <summary>
        /// Normalize a given path to an absolute path using PowerShell APIs
        /// available from within a cmdlet.
        /// </summary>
        /// <param name="cmdlet">The cmdlet executing this normalization.</param>
        /// <param name="path">The path to normalize.</param>
        /// <returns>The canonical absolute path referred to by the given path.</returns>
        public static string GetNormalizedAbsolutePath(this PSCmdlet cmdlet, string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return cmdlet.SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        }
    }
}