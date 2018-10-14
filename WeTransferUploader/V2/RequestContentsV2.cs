using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeTransferUploader.V2
{
    internal class TokenRequestContent
    {
        public TokenRequestContent(string user)
        {
            if (string.IsNullOrEmpty(user))
                throw new ArgumentNullException(nameof(user));
            User = user;
        }

        [JsonProperty("user_identifier")]
        public string User { get; set; }
   
    }


    internal class FileUploadRequestContent
    {
        public FileUploadRequestContent(string message, FileRequestContent[] files)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentNullException(nameof(message));
            if (files == null || files.Length == 0)
                throw new ArgumentNullException(nameof(files));

            Message = message;
            Files = files;
        }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("files")]
        public FileRequestContent[] Files { get; set; }
    }

    internal class FileRequestContent
    {
        public FileRequestContent(string name, int size,string fullPath)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name));
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (!System.IO.File.Exists(fullPath))
                throw new System.IO.FileNotFoundException(fullPath);

            Name = name;
            Size = size;
            FullPath = fullPath;
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("size")]
        public int Size { get; set; }

        [JsonIgnore]
        public string FullPath { get; set; }
    }

    internal class SingleFileCompletedRequestContent
    {
        public SingleFileCompletedRequestContent(int numberOfParts)
        {
            if (numberOfParts <= 0)
                throw new ArgumentOutOfRangeException(nameof(numberOfParts));

            NumberOfParts = numberOfParts;
        }

        [JsonProperty("part_numbers")]
        public int NumberOfParts { get; set; }
    }
}
