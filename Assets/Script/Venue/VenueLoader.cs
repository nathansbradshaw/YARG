using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YARG.Helpers;
using YARG.Settings;
using YARG.Core.Song;
using YARG.Core.Venue;
using YARG.Core.IO;
using YARG.Core.Logging;

namespace YARG.Venue
{
    public enum VenueSource
    {
        Global,
        Song,
    }

    public static class VenueLoader
    {
        private static readonly List<string> _remainingGlobalVenues = new();
        private static readonly HashSet<string> _knownGlobalVenues = new(StringComparer.Ordinal);
        private static string _lastGlobalVenue;
        private static readonly string _venueFolder = Path.Combine(PathHelper.PersistentDataPath, "venue");
        private static readonly string _defaultVenue = Path.Combine(Application.streamingAssetsPath, "venue", "default.yarground");
        public static string VenueFolder
        {
            get
            {
                if (!Directory.Exists(_venueFolder))
                {
                    Directory.CreateDirectory(_venueFolder);
                }
                return _venueFolder;
            }
        }

#nullable enable
        public static BackgroundResult? GetVenue(SongEntry song, out VenueSource source)
        {
            BackgroundResult? result = null;
#nullable disable
            source = VenueSource.Song;
            if (!SettingsManager.Settings.DisablePerSongBackgrounds.Value)
            {
                result = song.LoadBackground();
            }

            if (!SettingsManager.Settings.DisableGlobalBackgrounds.Value && result == null)
            {
                source = VenueSource.Global;
                result = GetVenuePathFromGlobal();
            }

            if (!SettingsManager.Settings.DisableDefaultBackground.Value && result == null)
            {
                result = LoadDefaultVenue();
            }

            return result;
        }

#nullable enable
        private static BackgroundResult? GetVenuePathFromGlobal()
#nullable disable
        {
            string[] validExtensions =
            {
                "*.yarground", "*.mp4", "*.mov", "*.webm", "*.png", "*.jpg", "*.jpeg"
            };

            string venueFolder = VenueFolder;
            string launcherVenueFolder = PathHelper.VenuePath;
            var filePaths = new List<string>();
            foreach (var ext in validExtensions)
            {
                filePaths.AddRange(Directory.EnumerateFiles(venueFolder, ext, PathHelper.SafeSearchOptions));
            }

            if (launcherVenueFolder != null && Directory.Exists(launcherVenueFolder))
            {
                // We limit ourselves to yarground here because that's all that will be downloaded by the launcher
                filePaths.AddRange(Directory.EnumerateFiles(launcherVenueFolder, "*.yarground", PathHelper.SafeSearchOptions));
            }

            filePaths = filePaths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToList();

            while (filePaths.Count > 0)
            {
                var file = TakeNextGlobalVenue(filePaths);
                filePaths.Remove(file);
                try
                {
                    switch (Path.GetExtension(file).ToLowerInvariant())
                    {
                        case ".png":
                        case ".jpg":
                        case ".jpeg":
                            var image = YARGImage.Load(file);
                            if (image != null)
                            {
                                YargLogger.LogFormatInfo("Selected global background {0}", Path.GetFileName(file));
                                return new BackgroundResult(image);
                            }
                            break;
                        case ".mp4":
                        case ".mov":
                        case ".webm":
                        case ".yarground":
                            var stream = File.OpenRead(file);
                            YargLogger.LogFormatInfo("Selected global background {0}", Path.GetFileName(file));
                            return new BackgroundResult(Path.GetExtension(file).Equals(".yarground", StringComparison.OrdinalIgnoreCase)
                                ? BackgroundType.Yarground : BackgroundType.Video, stream);
                    }
                }
                catch (IOException exception)
                {
                    YargLogger.LogException(exception, "Skipping unreadable global background");
                }
                catch (UnauthorizedAccessException exception)
                {
                    YargLogger.LogException(exception, "Skipping inaccessible global background");
                }
            }
            return null;
        }

        // Visit every available background before repeating. Avoid repeating at cycle boundaries too.
        private static string TakeNextGlobalVenue(List<string> available)
        {
            if (available.Count == 0) return null;
            if (!_knownGlobalVenues.SetEquals(available))
            {
                _knownGlobalVenues.Clear();
                _knownGlobalVenues.UnionWith(available);
                _remainingGlobalVenues.Clear();
            }
            if (_remainingGlobalVenues.Count == 0) _remainingGlobalVenues.AddRange(_knownGlobalVenues);
            var choices = _remainingGlobalVenues.Where(path => path != _lastGlobalVenue).ToList();
            if (choices.Count == 0) choices.AddRange(_remainingGlobalVenues);
            var selected = choices[UnityEngine.Random.Range(0, choices.Count)];
            _remainingGlobalVenues.Remove(selected);
            _lastGlobalVenue = selected;
            return selected;
        }

#nullable enable
        private static BackgroundResult? LoadDefaultVenue()
#nullable disable
        {
            if (!File.Exists(_defaultVenue))
            {
                YargLogger.LogWarning("Default venue not found. Build error?");
                return null;
            }

            return new BackgroundResult(BackgroundType.Yarground, File.OpenRead(_defaultVenue));
        }
    }
}
