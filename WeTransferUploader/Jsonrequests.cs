using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeTransferUploader
{
    public class CreateTransferRequest
    {
        public CreateTransferRequest(string name)
        {
            this.Name = name;
        }

        [JsonProperty("name")]
        public string Name { get; }
    }

    public class TransferItemContainer
    {
        public TransferItemContainer(TransferItem[] items)
        {
            this.Items = items;
        }

        [JsonProperty("items")]
        public TransferItem[] Items { get; set; }
    }

    public class TransferItem
    {
        private static int _internalId;
        public static int NextInternalId()
        {
            return _internalId++;
        }

        public TransferItem(string fullPath)
        {
            if (!File.Exists(fullPath))
                throw new ArgumentException($"The file {fullPath} does not exist.");
            LocalIdentifier = NextInternalId().ToString();
            _fullPath = fullPath;
            Filename = Path.GetFileName(fullPath);
            FileSize = new FileInfo(fullPath).Length;
        }

        [JsonProperty("local_identifier")]
        public string LocalIdentifier { get; set; }

        [JsonProperty("content_identifier")]
        public string ContentIdentifier
        {
            get { return "file"; }
            set { }
        }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("filesize")]
        public long FileSize { get; set; }

        private string _fullPath;

        public string FullPath()
        {
            return _fullPath;
        }

    }

}
