using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Corecii.TrackMusic
{
    public class MusicTrack : CustomData<CustomName>
    {
        public static CustomDataInfo Info = new CustomDataInfo(typeof(MusicTrack), typeof(CustomName), "MusicTrack:");

        public static string[] AllowedExtensions = new string[]
        {
            ".mp3",
            ".wav",
            ".aiff"
        };

        public static string[] AllowedDownloadSchemes = new string[]
        {
            "http",
            "https"
        };

        public override string StringFromObject(CustomName obj)
        {
            return obj.customName_;
        }
        public override void StringToObject(CustomName obj, string str)
        {
            obj.customName_ = str;
        }

        public string Name = "Unknown";
        public string FileType = ".mp3";
        public string DownloadUrl = "";
        public string Version = "";
        [NonSerialized()]
        public byte[] Embedded = new byte[0];
        [NonSerialized()]
        public string EmbedFile = null;
        [NonSerialized()]
        public string FileLocation = null;
        [NonSerialized()]
        public bool Attempted = false;
        [NonSerialized()]
        public string LastEmbeddedFile = null;
        [NonSerialized()]
        public string LastWrittenData = null;
        [NonSerialized()]
        public MusicTrack LastWritten = null;

        public string FileName { get => Regex.Replace(Name, @"[^A-Za-z0-9]", ""); }

        public override bool ReadDataString(string data)
        {
            var separatorLoc = data.IndexOf(':');
            if (separatorLoc == -1)
            {
                return false;
            }
            var numSub = data.Substring(0, separatorLoc);
            int num = -1;
            var success = int.TryParse(numSub, out num);
            if (!success || data.Length < separatorLoc + 1 + num)
            {
                return false;
            }
            var jsonStr = data.Substring(separatorLoc + 1, num);
            var embedStr = data.Substring(separatorLoc + 1 + num);
            var embedBytes = Convert.FromBase16kString(embedStr);
            JsonConvert.PopulateObject(jsonStr, this);
            Embedded = embedBytes;
            return true;
        }
        public override string WriteDataString()
        {
            var asJson = JsonConvert.SerializeObject(this);
            var embedStr = Convert.ToBase16kString(Embedded);
            return asJson.Length.ToString() + ':' + asJson + embedStr;
        }

        public void NewVersion()
        {
            Version = Guid.NewGuid().ToString();
        }

        public MusicTrack Clone()
        {
            return new MusicTrack()
            {
                Name = Name,
                DownloadUrl = DownloadUrl,
                FileType = FileType,
                Version = Version,
                Embedded = (byte[])Embedded.Clone(),
                LastEmbeddedFile = LastEmbeddedFile,
                EmbedFile = EmbedFile,
            };
        }

        public string GetError()
        {
            string Error = null;
            if (string.IsNullOrEmpty(FileName))
            {
                Error = $"FileName is empty (Name must have 1 character in A-Za-z0-9)";
            }
            else if (!AllowedExtensions.Contains(FileType))
            {
                Error = $"Bad file type {FileType}: should be .mp3, .wav, or .aiff";
            }
            else if (!string.IsNullOrEmpty(DownloadUrl))
            {
                var success = Uri.TryCreate(DownloadUrl, UriKind.Absolute, out Uri downloadUri);
                if (!success || !AllowedDownloadSchemes.Contains(downloadUri.Scheme))
                {
                    Error = $"Bad URL: should be http:// or https://";
                }
            }
            else if (string.IsNullOrEmpty(DownloadUrl) && Embedded.Length == 0)
            {
                Error = "Missing data";
            }
            return Error;
        }

        public static MusicTrack FromObject(CustomName obj)
        {
            var newThis = new MusicTrack();
            var success = newThis.ReadObject(obj);
            return success ? newThis : null;
        }
    }
}
