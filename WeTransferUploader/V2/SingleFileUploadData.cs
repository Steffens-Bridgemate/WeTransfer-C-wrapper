using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeTransferUploader.V2
{
    internal class SingleFileUploadData
    {
        private readonly SingleFileTransferResponseData _responseData;
        public SingleFileUploadData(SingleFileTransferResponseData singleFileResponseData)
        {
          
            _responseData = singleFileResponseData ?? throw new ArgumentNullException(nameof(singleFileResponseData));

            var urls = new Dictionary<int, string>();
            for (var i = 1; i < _responseData.ChunkData.NumberOfParts; i++)
                urls.Add(i, string.Empty);

            UploadUrls = urls;
        }

        public void AddUrl(int partNumber, string url)
        {
            if (partNumber <= 0 || partNumber>UploadUrls.Count)
                throw new ArgumentOutOfRangeException(nameof(partNumber));
          
            UploadUrls[partNumber] = url;
        }

        public string GetUrl(int partNumber)
        {
            if (partNumber <= 0 || partNumber > UploadUrls.Count)
                throw new ArgumentOutOfRangeException(nameof(partNumber));

            return UploadUrls[partNumber];


        }

        public string FullPath
        {
            get
            {
                return _responseData.FullPath;
            }
        }

        private Dictionary<int,string> UploadUrls { get; }
    }
}
