// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Microsoft.PowerShell.CrossCompatibility.Data.Platform;

namespace Microsoft.PowerShell.CrossCompatibility.Utility
{
    public static class PlatformNaming
    {
        public static string AnyPlatformUnionName => "anyplatform_union";

        public static string PlatformNameJoiner
        {
            get
            {
                return "_";
            }
        }

        /// <summary>
        /// Gets a unique name for the target PowerShell platform.
        /// Schema is "{os-name}_{os-arch}_{os-version}_{powershell-version}_{process-arch}_{dotnet-version}_{dotnet-edition}".
        /// os-name for Windows is "win-{sku-id}".
        /// os-name for Linux is the ID entry in /etc/os-release.
        /// os-name for Mac is "macos".
        /// </summary>
        /// <param name="platform"></param>
        /// <returns></returns>
        public static string GetPlatformName(PlatformData platform)
        {
            string psVersion = platform.PowerShell.Version?.ToString();
            string osVersion = platform.OperatingSystem.Version;
            string osArch = platform.OperatingSystem.Architecture.ToString().ToLower();
            string pArch = platform.PowerShell.ProcessArchitecture.ToString().ToLower();
            string dotnetVersion = platform.Dotnet.ClrVersion.ToString().ToLower();
            string dotnetEdition = platform.Dotnet.Runtime.ToString().ToLower();

            string[] platformNameComponents;
            switch (platform.OperatingSystem.Family)
            {
                case OSFamily.Windows:
                    if (platform.OperatingSystem.SkuId == null)
                    {
                        throw new Exception($"SkuId not set for operating system {platform.OperatingSystem.Name}");
                    }

                    uint skuId = platform.OperatingSystem.SkuId.Value;

                    platformNameComponents = new [] { $"win-{skuId}", osArch, osVersion, psVersion, pArch, dotnetVersion, dotnetEdition };
                    break;

                case OSFamily.MacOS:
                    platformNameComponents = new [] { "macos", osArch, osVersion, psVersion, pArch, dotnetVersion, dotnetEdition };
                    break;

                case OSFamily.Linux:
                    string distroId = platform.OperatingSystem.DistributionId;
                    string distroVersion = platform.OperatingSystem.DistributionVersion;

                    platformNameComponents = new [] { distroId, osArch, distroVersion, psVersion, pArch, dotnetVersion, dotnetEdition };
                    break;

                default:
                    // We shouldn't ever see anything like this
                    platformNameComponents = new [] { "unknown", osArch, osVersion ?? "?", psVersion, pArch, dotnetVersion, dotnetEdition };
                    break;
            }

            return string.Join(PlatformNameJoiner, platformNameComponents);
        }
    }
}