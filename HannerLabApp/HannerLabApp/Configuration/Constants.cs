﻿using System.IO;
using Xamarin.Essentials;

namespace HannerLabApp.Configuration
{
    public static class Constants
    {
        public const string DatabaseFilename = "data.db";

        public static readonly string MediaDirectory = Path.Combine(FileSystem.AppDataDirectory, "media/");
        public static readonly string AppDataDirectory = Path.Combine(FileSystem.AppDataDirectory, "appdata/");
        public static readonly string ExportDirectory = Path.Combine(FileSystem.AppDataDirectory, "exports/");

        public static readonly string TempDirectory = FileSystem.CacheDirectory;

        public const string AppGithubBaseRepoUrl = "HannerLab/sFish";
        public const string AppVersionString = "1.0.0";
        public const string AppDescription = "The Sample and Field data collection Information System for the Hanner Lab.";
        public const string AppCopyright = "© 2022 The Hanner Lab, University of Guelph";
        public const string AppName = "sFish";
    }
}