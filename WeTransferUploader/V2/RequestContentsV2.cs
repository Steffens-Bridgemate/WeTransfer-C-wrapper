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

    #region "BoardApi specific request content classes"

    internal class BoardCreationRequestContent
    {
        public BoardCreationRequestContent(string name,string description=null)
        {
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    internal class AddLinksRequestContent
    {
        public AddLinksRequestContent(IEnumerable<(string url,string title)> links):
            this(links.Select(link=>new LinkRequestContent(link.url,link.title)).ToArray())
        {}

        public AddLinksRequestContent(LinkRequestContent[] links)
        {
            Links = links ?? throw new ArgumentNullException(nameof(links));
        }
        public LinkRequestContent[] Links { get; set; }
    }

    internal class LinkRequestContent
    {
        public LinkRequestContent(string url, string title)
        {
            Url = url;
            Title = title;
        }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }
    }

    internal class AddFilesRequestContent
    {
        public AddFilesRequestContent(IEnumerable<(string name, int size,string fullPath)> files) :
            this(files.Select(file => new FileRequestContent(file.name, file.size,file.fullPath)).ToArray())
        { }

        public AddFilesRequestContent(FileRequestContent[] files)
        {
            Files = files ?? throw new ArgumentNullException(nameof(files));
        }
        public FileRequestContent[] Files { get; set; }
    }

    #endregion
}
