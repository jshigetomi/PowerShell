// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace SMA
{
    /// <summary>
    /// Source Code Generator to create partial PSVersionInfo class.
    /// </summary>
    [Generator]
    public class PSVersionInfoGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// Not used.
        /// </summary>
        /// <param name="context">Generator initialization context.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValueProvider<BuildOptions> buildOptionsProvider = context.AnalyzerConfigOptionsProvider
                .Select(static (provider, _) =>
                {
                    provider.GlobalOptions.TryGetValue("build_property.ProductVersion", out var productVersion);
                    provider.GlobalOptions.TryGetValue("build_property.PSCoreBuildVersion", out var mainVersion);
                    provider.GlobalOptions.TryGetValue("build_property.PowerShellVersion", out var gitDescribe);
                    provider.GlobalOptions.TryGetValue("build_property.ReleaseTag", out var releaseTag);

                    BuildOptions options = new()
                    {
                        ProductVersion = productVersion ?? string.Empty,
                        MainVersion = mainVersion ?? string.Empty,
                        GitDescribe = gitDescribe ?? string.Empty,
                        ReleaseTag = releaseTag ?? string.Empty
                    };

                    return options;
                });

            context.RegisterSourceOutput(
                buildOptionsProvider,
                static (context, buildOptions) =>
                {
                    string gitCommitId = string.IsNullOrEmpty(buildOptions.ReleaseTag) ? buildOptions.GitDescribe : buildOptions.ReleaseTag;
                    if (gitCommitId.StartsWith("v"))
                    {
                        gitCommitId = gitCommitId.Substring(1);
                    }

                    var versions = ParsePSVersion(buildOptions.MainVersion);
                    string result = string.Format(
                        CultureInfo.InvariantCulture,
                        SourceTemplate,
                        buildOptions.ProductVersion,
                        gitCommitId,
                        versions.major,
                        versions.minor,
                        versions.patch,
                        versions.preReleaseLabel);

                    // We must use specific file name suffix (*.g.cs,*.g, *.i.cs, *.generated.cs, *.designer.cs)
                    // so that Roslyn analyzers skip the file.
                    context.AddSource("PSVersionInfo.g.cs", result);
                });
        }

        private struct BuildOptions
        {
            public string ProductVersion;
            public string MainVersion;
            public string GitDescribe;
            public string ReleaseTag;
        }

        // We must put "<auto-generated" on first line so that Roslyng analyzers skip the file.
        private const string SourceTemplate = @"// <auto-generated>
// This file is auto-generated by PSVersionInfoGenerator.
// </auto-generated>

namespace System.Management.Automation
{{
    public static partial class PSVersionInfo
    {{
        // Defined in 'PowerShell.Common.props' as 'ProductVersion'
        // Example:
        //  - when built from a commit:              ProductVersion = '7.3.0-preview.8 Commits: 29 SHA: 52c6b...'
        //  - when built from a preview release tag: ProductVersion = '7.3.0-preview.8 SHA: f1ec9...'
        //  - when built from a stable release tag:  ProductVersion = '7.3.0 SHA: f1ec9...'
        internal const string ProductVersion = ""{0}"";

        // The git commit id that the build is based off.
        // Defined in 'PowerShell.Common.props' as 'PowerShellVersion' or 'ReleaseTag',
        // depending on whether the '-ReleaseTag' is specified when building.
        // Example:
        //  - when built from a commit:              GitCommitId = '7.3.0-preview.8-29-g52c6b...'
        //  - when built from a preview release tag: GitCommitId = '7.3.0-preview.8'
        //  - when built from a stable release tag:  GitCommitId = '7.3.0'
        internal const string GitCommitId = ""{1}"";

        // The PowerShell version components.
        // The version string is defined in 'PowerShell.Common.props' as 'PSCoreBuildVersion',
        // but we break it into components to save the overhead of parsing at runtime.
        // Example:
        //  - '7.3.0-preview.8' for preview release or private build
        //  - '7.3.0' for stable release
        private const int Version_Major = {2};
        private const int Version_Minor = {3};
        private const int Version_Patch = {4};
        private const string Version_Label = ""{5}"";
    }}
}}";

        private static (int major, int minor, int patch, string preReleaseLabel) ParsePSVersion(string mainVersion)
        {
            // We only handle the pre-defined PSVersion format here, e.g. 7.x.x or 7.x.x-preview.x
            int dashIndex = mainVersion.IndexOf('-');
            bool hasLabel = dashIndex != -1;
            string preReleaseLabel = hasLabel ? mainVersion.Substring(dashIndex + 1) : string.Empty;

            if (hasLabel)
            {
                mainVersion = mainVersion.Substring(0, dashIndex);
            }

            int majorEnd = mainVersion.IndexOf('.');
            int minorEnd = mainVersion.LastIndexOf('.');

            int major = int.Parse(mainVersion.Substring(0, majorEnd), NumberStyles.Integer, CultureInfo.InvariantCulture);
            int minor = int.Parse(mainVersion.Substring(majorEnd + 1, minorEnd - majorEnd - 1), NumberStyles.Integer, CultureInfo.InvariantCulture);
            int patch = int.Parse(mainVersion.Substring(minorEnd + 1), NumberStyles.Integer, CultureInfo.InvariantCulture);

            return (major, minor, patch, preReleaseLabel);
        }
    }
}
